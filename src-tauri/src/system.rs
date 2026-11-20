//! The tray menu and the global shortcut: picking from anywhere, without opening the window first.
use crate::capture::pick_once;
use crate::state::AppError;
use std::sync::Mutex;
use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Emitter, Manager, Wry};

const ITEMS: [(&str, &str); 6] = [
    ("pick-copy", "التقاط ونسخ"),
    ("pick-edit", "التقاط وتحرير"),
    ("pick-quick", "التقاط سريع"),
    ("pick-many", "التقاط متعدد إلى اللوحة الحالية"),
    ("open", "فتح PickerHub"),
    ("quit", "خروج"),
];

#[derive(Default)]
pub struct System {
    items: Mutex<Vec<MenuItem<Wry>>>,
    shortcut: Mutex<Option<Registered>>,
}

enum Registered {
    Plugin(String),
    #[cfg(target_os = "linux")]
    Portal(tauri::async_runtime::JoinHandle<()>),
}

/// Runs a pick for the tray or the shortcut and tells the interface what happened.
pub fn run_pick(app: &AppHandle, quick: bool, copy: bool, event: &'static str) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        match pick_once(&app, quick, copy).await {
            Ok(Some(p)) => {
                let _ = app.emit(event, p);
            }
            Ok(None) => {
                let _ = app.emit("pick-cancelled", ());
            }
            Err(e) => {
                let _ = app.emit("pick-failed", e);
            }
        }
    });
}

pub fn tray(app: &AppHandle) -> tauri::Result<()> {
    let items: Vec<MenuItem<Wry>> = ITEMS.iter().map(|(id, label)| MenuItem::with_id(app, *id, *label, true, None::<&str>)).collect::<Result<_, _>>()?;
    let sep = PredefinedMenuItem::separator(app)?;
    let menu = Menu::with_items(app, &[&items[0], &items[1], &items[2], &items[3], &sep, &items[4], &items[5]])?;
    TrayIconBuilder::with_id("main")
        .icon(app.default_window_icon().cloned().expect("bundled icon"))
        .tooltip("PickerHub")
        .menu(&menu)
        .show_menu_on_left_click(true)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "pick-copy" => run_pick(app, false, true, "picked"),
            "pick-edit" => run_pick(app, false, false, "picked-edit"),
            "pick-quick" => run_pick(app, true, true, "picked"),
            "pick-many" => {
                show(app);
                let _ = app.emit("pick-many", ());
            }
            "open" => show(app),
            "quit" => app.exit(0),
            _ => {}
        })
        .build(app)?;
    *app.state::<System>().items.lock().unwrap() = items;
    Ok(())
}

pub fn show(app: &AppHandle) {
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.unminimize();
        let _ = w.show();
        let _ = w.set_focus();
    }
}

/// The interface sends the menu labels in its language whenever the language changes.
#[tauri::command]
pub fn tray_labels(app: AppHandle, labels: Vec<String>) {
    for (item, label) in app.state::<System>().items.lock().unwrap().iter().zip(labels) {
        let _ = item.set_text(label);
    }
}

fn plugin_accelerator(shortcut: &str) -> String {
    shortcut.replace("Ctrl", "CommandOrControl")
}

/// Registers (or releases) the global shortcut. Off by default; F8 stays local to the window.
/// Wayland has no global keys for clients, so there the desktop portal binds it with the user's consent.
#[tauri::command]
pub async fn apply_shortcut(app: AppHandle, enabled: bool, shortcut: String, quick: bool) -> Result<(), AppError> {
    use tauri_plugin_global_shortcut::GlobalShortcutExt;
    if !pickerhub_store::model::SHORTCUTS.contains(&shortcut.as_str()) {
        return Err(AppError::new("invalid", "طلب غير صالح."));
    }
    let system = app.state::<System>();
    match system.shortcut.lock().unwrap().take() {
        Some(Registered::Plugin(acc)) => {
            let _ = app.global_shortcut().unregister(acc.as_str());
        }
        #[cfg(target_os = "linux")]
        Some(Registered::Portal(task)) => task.abort(),
        None => {}
    }
    if !enabled {
        return Ok(());
    }
    #[cfg(target_os = "linux")]
    if pickerhub_capture::backend() == pickerhub_capture::Backend::Portal {
        let task = portal::bind(app.clone(), shortcut, quick).await?;
        *system.shortcut.lock().unwrap() = Some(Registered::Portal(task));
        return Ok(());
    }
    let acc = plugin_accelerator(&shortcut);
    let handle = app.clone();
    app.global_shortcut()
        .on_shortcut(acc.as_str(), move |_app, _shortcut, event| {
            if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                run_pick(&handle, quick, true, "picked");
            }
        })
        .map_err(|e| AppError::new("shortcut_taken", "الاختصار مستخدم أو غير متاح. اختر اختصاراً مختلفاً.").with_detail(e))?;
    *system.shortcut.lock().unwrap() = Some(Registered::Plugin(acc));
    Ok(())
}

#[cfg(target_os = "linux")]
mod portal {
    use super::run_pick;
    use crate::state::AppError;
    use ashpd::desktop::global_shortcuts::{GlobalShortcuts, NewShortcut};
    use futures_util::StreamExt;
    use tauri::AppHandle;

    pub async fn bind(app: AppHandle, shortcut: String, quick: bool) -> Result<tauri::async_runtime::JoinHandle<()>, AppError> {
        let fail = |e: ashpd::Error| AppError::new("shortcut_portal", "لم يُسمح بتفعيل الاختصار العام. يمكنك إعادة المحاولة من الإعدادات.").with_detail(e);
        let proxy = GlobalShortcuts::new().await.map_err(fail)?;
        let session = proxy.create_session(Default::default()).await.map_err(fail)?;
        let trigger = shortcut.replace("Ctrl", "CTRL").replace("Shift", "SHIFT").replace("Alt", "ALT");
        let wanted = NewShortcut::new("pick", "PickerHub: التقاط ونسخ اللون").preferred_trigger(Some(trigger.as_str()));
        proxy.bind_shortcuts(&session, &[wanted], None, Default::default()).await.map_err(fail)?.response().map_err(fail)?;
        let mut activated = proxy.receive_activated().await.map_err(fail)?;
        Ok(tauri::async_runtime::spawn(async move {
            let _keep = (proxy, session); // the binding lives as long as its session
            while let Some(a) = activated.next().await {
                if a.shortcut_id() == "pick" {
                    run_pick(&app, quick, true, "picked");
                }
            }
        }))
    }
}
