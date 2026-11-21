//! The tray menu and the global shortcut: picking from anywhere, without opening the window first,
//! and the tray as the window's home when "Minimize to tray on close" is on.
use crate::capture::pick_once;
use crate::state::{AppError, AppState};
use serde::Deserialize;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, Ordering};
use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, WebviewWindow, Wry};

/// The tray's words; the interface sends them in its language (`tray_labels`).
#[derive(Deserialize, Clone)]
pub struct TrayLabels {
    pick: String,
    show: String,
    hide: String,
    settings: String,
    quit: String,
}
impl Default for TrayLabels {
    fn default() -> Self {
        Self { pick: "التقاط لون".into(), show: "إظهار PickerHub".into(), hide: "إخفاء PickerHub".into(), settings: "الإعدادات".into(), quit: "إنهاء PickerHub".into() }
    }
}

struct TrayItems {
    pick: MenuItem<Wry>,
    toggle: MenuItem<Wry>,
    settings: MenuItem<Wry>,
    quit: MenuItem<Wry>,
}

#[derive(Default)]
pub struct System {
    items: Mutex<Option<TrayItems>>,
    labels: Mutex<TrayLabels>,
    /// Set once the tray icon exists: only then may closing hide the window instead of quitting.
    tray: AtomicBool,
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

/// The tray: one pick, the window (show or hide, settings) and quit. On Windows a double click
/// opens the window and the menu is on the right button; macOS keeps its click-to-open menu, and
/// Linux trays (AppIndicator) send no clicks, so there the menu's Show item reopens the window.
pub fn tray(app: &AppHandle) -> tauri::Result<()> {
    let labels = app.state::<System>().labels.lock().unwrap().clone();
    let items = TrayItems {
        pick: MenuItem::with_id(app, "pick", &labels.pick, true, None::<&str>)?,
        toggle: MenuItem::with_id(app, "toggle", &labels.hide, true, None::<&str>)?,
        settings: MenuItem::with_id(app, "settings", &labels.settings, true, None::<&str>)?,
        quit: MenuItem::with_id(app, "quit", &labels.quit, true, None::<&str>)?,
    };
    let (a, b) = (PredefinedMenuItem::separator(app)?, PredefinedMenuItem::separator(app)?);
    let menu = Menu::with_items(app, &[&items.pick, &a, &items.toggle, &items.settings, &b, &items.quit])?;
    TrayIconBuilder::with_id("main")
        .icon(app.default_window_icon().cloned().expect("bundled icon"))
        .tooltip("PickerHub")
        .menu(&menu)
        .show_menu_on_left_click(cfg!(target_os = "macos"))
        .on_menu_event(|app, event| match event.id().as_ref() {
            "pick" => run_pick(app, false, true, "picked"),
            "toggle" => {
                if main_window(app).is_some_and(|w| w.is_visible().unwrap_or(false)) { hide(app) } else { show(app) }
            }
            "settings" => {
                show(app);
                let _ = app.emit("open-settings", ());
            }
            "quit" => quit(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::DoubleClick { button: MouseButton::Left, .. } = event {
                show(tray.app_handle());
            }
        })
        .build(app)?;
    let system = app.state::<System>();
    *system.items.lock().unwrap() = Some(items);
    system.tray.store(true, Ordering::Relaxed);
    sync(app);
    Ok(())
}

fn main_window(app: &AppHandle) -> Option<WebviewWindow> {
    app.get_webview_window("main")
}

/// Brings the window back: from the tray, a second launch or the end of a pick.
pub fn show(app: &AppHandle) {
    if let Some(w) = main_window(app) {
        let _ = w.unminimize();
        let _ = w.show();
        let _ = w.set_focus();
    }
    sync(app);
}

/// Puts the window away in the tray; the app keeps running. Its place and size are saved first.
pub fn hide(app: &AppHandle) {
    save_window(app);
    if let Some(w) = main_window(app) {
        let _ = w.hide();
    }
    sync(app);
}

/// Saves and quits for good: the tray's Quit, or closing with the tray option off.
pub fn quit(app: &AppHandle) {
    save_window(app);
    app.exit(0);
}

fn save_window(app: &AppHandle) {
    use tauri_plugin_window_state::AppHandleExt;
    let _ = app.save_window_state(crate::WINDOW_STATE);
}

/// Whether closing the window should hide it in the tray: the setting is on and a tray exists to
/// bring it back from. Without a tray, closing still quits, so the app can never become unreachable.
pub fn closes_to_tray(app: &AppHandle) -> bool {
    app.state::<System>().tray.load(Ordering::Relaxed)
        && app.try_state::<AppState>().is_some_and(|s| s.workspace().extra.get("minimizeToTray").and_then(|v| v.as_bool()).unwrap_or(false))
}

/// The Show/Hide item names the action that applies now.
pub fn sync(app: &AppHandle) {
    let system = app.state::<System>();
    let visible = main_window(app).is_some_and(|w| w.is_visible().unwrap_or(false));
    if let Some(items) = system.items.lock().unwrap().as_ref() {
        let labels = system.labels.lock().unwrap();
        let _ = items.toggle.set_text(if visible { &labels.hide } else { &labels.show });
    }
}

/// The interface sends the tray's words in its language whenever the language changes.
#[tauri::command]
pub fn tray_labels(app: AppHandle, labels: TrayLabels) {
    let system = app.state::<System>();
    if let Some(items) = system.items.lock().unwrap().as_ref() {
        let _ = items.pick.set_text(&labels.pick);
        let _ = items.settings.set_text(&labels.settings);
        let _ = items.quit.set_text(&labels.quit);
    }
    *system.labels.lock().unwrap() = labels;
    sync(&app);
}

/// Whether this desktop has a tray icon, so the interface offers the close-to-tray setting.
#[tauri::command]
pub fn tray_available(app: AppHandle) -> bool {
    app.state::<System>().tray.load(Ordering::Relaxed)
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
