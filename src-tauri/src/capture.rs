//! The eyedropper: hide the window, let the platform sampler pick, then record the color.
use crate::state::{AppError, AppState, Snapshot};
use pickerhub_capture::{Backend, CaptureError, Rgb};
use serde::Serialize;
use tauri::{AppHandle, Manager, State};
use tauri_plugin_clipboard_manager::ClipboardExt;

impl From<CaptureError> for AppError {
    fn from(e: CaptureError) -> Self {
        let mut a = AppError::new(e.code, e.message);
        a.detail = e.detail;
        a
    }
}

/// Runs the platform's eyedropper. `multiple` keeps picking until Escape (the lens backends only).
async fn sample(app: &AppHandle, multiple: bool) -> Result<Vec<Rgb>, CaptureError> {
    let one = |r: Option<Rgb>| r.into_iter().collect::<Vec<_>>();
    match pickerhub_capture::backend() {
        #[cfg(target_os = "linux")]
        Backend::Portal => {
            let sharing = app.state::<AppState>().workspace().use_wayland_screen_sharing;
            if sharing {
                crate::lens::shared(app, multiple).await
            } else if multiple {
                Err(CaptureError::new("multi_unavailable", "يتطلب الالتقاط المتعدد العدسة الحية ومشاركة الشاشة."))
            } else {
                pickerhub_capture::wayland::pick().await.map(one)
            }
        }
        #[cfg(target_os = "linux")]
        Backend::X11 => crate::lens::native(app, multiple, crate::lens::open_x11).await,
        #[cfg(windows)]
        Backend::Windows => crate::lens::native(app, multiple, crate::lens::open_windows).await,
        #[cfg(target_os = "macos")]
        Backend::MacOs => {
            let mut all = Vec::new();
            loop {
                let (tx, rx) = std::sync::mpsc::channel();
                app.run_on_main_thread(move || pickerhub_capture::macos::pick(move |r| { let _ = tx.send(r); }))
                    .map_err(|e| CaptureError::new("thread", "تعذّر بدء القطّارة.").with(e))?;
                let r = tauri::async_runtime::spawn_blocking(move || rx.recv().unwrap_or(Ok(None)))
                    .await
                    .map_err(|e| CaptureError::new("thread", "تعذّر بدء القطّارة.").with(e))??;
                match r {
                    Some(c) => all.push(c),
                    None => break,
                }
                if !multiple {
                    break;
                }
            }
            Ok(all)
        }
        _ => {
            let _ = (app, one, multiple);
            Err(CaptureError::new("unsupported", "القطّارة غير متاحة على هذا النظام."))
        }
    }
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Picked {
    hex: String,
    copied: Option<String>,
    snapshot: Snapshot,
}

/// One pick. `stay_hidden` keeps the window away afterwards (the quick pick of the tray and
/// shortcut); `copy` false leaves the clipboard as it was (the tray's "pick and edit").
pub async fn pick_once(app: &AppHandle, stay_hidden: bool, copy: bool) -> Result<Option<Picked>, AppError> {
    let window = app.get_webview_window("main");
    let portal = pickerhub_capture::backend() == Backend::Portal && !app.state::<AppState>().workspace().use_wayland_screen_sharing;
    // The window leaves the screen so the click lands on what is beneath it. The system picker
    // draws its own lens over everything, so with it the window may stay.
    if let Some(w) = &window
        && !portal
    {
        let _ = w.hide();
        tokio::time::sleep(std::time::Duration::from_millis(180)).await;
    }
    let started = crate::support::start();
    let result = sample(app, false).await.map(|v| v.into_iter().next());
    match &result {
        Err(e) => crate::support::failed(if portal { "capture" } else { "capture-lens" }, &e.code),
        Ok(Some(_)) if portal => crate::support::measured("system-picker-selection", started),
        _ => {}
    }
    if let Some(w) = &window
        && !stay_hidden
    {
        let _ = w.show();
        let _ = w.set_focus();
    }
    let Some(rgb) = result? else { return Ok(None) };
    let hex = rgb.hex();
    let state = app.state::<AppState>();
    let snapshot = state.edit(true, |w| {
        w.remember(&hex);
        w.color = hex.clone();
        Ok(())
    })?;
    let copied = if copy && snapshot.workspace.auto_copy {
        let value = crate::commands::copy_value(&snapshot.workspace, &hex);
        let copying = crate::support::start();
        let done = app.clipboard().write_text(value.clone()).ok().map(|_| value);
        crate::support::measured("pick-to-clipboard", copying);
        done
    } else {
        None
    };
    Ok(Some(Picked { hex, copied, snapshot }))
}

#[tauri::command]
pub async fn pick(app: AppHandle) -> Result<Option<Picked>, AppError> {
    pick_once(&app, false, true).await
}

/// Picks until the user cancels, adding every color to a palette.
#[tauri::command]
pub async fn pick_many(app: AppHandle, state: State<'_, AppState>, palette: String) -> Result<Snapshot, AppError> {
    let window = app.get_webview_window("main");
    if let Some(w) = &window {
        let _ = w.hide();
        tokio::time::sleep(std::time::Duration::from_millis(180)).await;
    }
    let outcome = sample(&app, true).await;
    if let Err(e) = &outcome {
        crate::support::failed("capture-many", &e.code);
    }
    if let Some(w) = &window {
        let _ = w.show();
        let _ = w.set_focus();
    }
    let picked: Vec<String> = outcome?.into_iter().map(|c| c.hex()).collect();
    if picked.is_empty() {
        return Ok(state.snapshot());
    }
    state.edit(true, |w| {
        for hex in &picked {
            w.remember(hex);
        }
        pickerhub_edit_add(w, &palette, &picked)
    })
}

fn pickerhub_edit_add(w: &mut pickerhub_store::Workspace, palette: &str, hexes: &[String]) -> Result<(), pickerhub_store::StoreError> {
    crate::edit::apply(w, crate::edit::Op::AddColors { palette: palette.to_string(), hexes: hexes.to_vec() })
}
