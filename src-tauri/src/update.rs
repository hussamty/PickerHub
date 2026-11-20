//! Signed updates. A build carries the updater only when it was made with the `updater` feature and
//! a public key in `PICKERHUB_UPDATER_PUBKEY` (docs/release.md); every other build says so and never
//! touches the network. The release manifest is fetched over TLS from the project's latest GitHub
//! release, and an artifact is installed only when its minisign signature matches the built-in key
//! and names the version the manifest announced (no downgrade by a crafted manifest).
use crate::state::AppError;
use serde::Serialize;
use tauri::AppHandle;

#[cfg_attr(not(feature = "updater"), allow(dead_code))]
pub const ENDPOINT: &str = "https://github.com/hussamty/PickerHub/releases/latest/download/latest.json";

/// The public key this build was made with, if any.
pub fn pubkey() -> Option<&'static str> {
    option_env!("PICKERHUB_UPDATER_PUBKEY").map(str::trim).filter(|k| !k.is_empty())
}

#[cfg(feature = "updater")]
pub fn plugin<R: tauri::Runtime>() -> Option<tauri::plugin::TauriPlugin<R, tauri_plugin_updater::Config>> {
    pubkey().map(|key| tauri_plugin_updater::Builder::new().pubkey(key).build())
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Offer {
    version: String,
    current: String,
    notes: Option<String>,
    date: Option<String>,
}

#[cfg(feature = "updater")]
#[derive(Default)]
pub struct Pending(pub std::sync::Mutex<Option<tauri_plugin_updater::Update>>);

/// Whether this build can update itself.
#[tauri::command]
pub fn update_supported() -> bool {
    cfg!(feature = "updater") && pubkey().is_some()
}

fn unavailable() -> AppError {
    AppError::new("update_unavailable", "هذه النسخة لا تحدّث نفسها. نزّل الإصدار الجديد من صفحة المشروع.")
}

/// Asks the release channel for a newer version. None when this one is current.
#[tauri::command]
pub async fn update_check(app: AppHandle) -> Result<Option<Offer>, AppError> {
    #[cfg(feature = "updater")]
    if update_supported() {
        use tauri::Manager;
        use tauri_plugin_updater::UpdaterExt;
        let fail = |e: tauri_plugin_updater::Error| AppError::new("update_check", "تعذّر التحقق من التحديثات. تحقق من الاتصال وحاول لاحقاً.").with_detail(e);
        let url = ENDPOINT.parse().expect("update endpoint");
        let update = app.updater_builder().endpoints(vec![url]).map_err(fail)?.build().map_err(fail)?.check().await.map_err(fail)?;
        let offer = update.as_ref().map(|u| Offer { version: u.version.clone(), current: u.current_version.clone(), notes: u.body.clone(), date: u.date.map(|d| d.to_string()) });
        *app.state::<Pending>().0.lock().unwrap() = update;
        return Ok(offer);
    }
    let _ = app;
    Err(unavailable())
}

/// Downloads the offered version, verifies its signature, installs it and restarts.
#[tauri::command]
pub async fn update_install(app: AppHandle) -> Result<(), AppError> {
    #[cfg(feature = "updater")]
    if update_supported() {
        use tauri::Manager;
        let Some(update) = app.state::<Pending>().0.lock().unwrap().take() else {
            return Err(AppError::new("update_none", "لا يوجد تحديث جاهز. تحقق من التحديثات أولاً."));
        };
        // The library is on disk before the installer replaces the program.
        let _ = app.state::<crate::state::AppState>().flush();
        update
            .download_and_install(|_, _| {}, || {})
            .await
            .map_err(|e| AppError::new("update_install", "تعذّر تثبيت التحديث. لم يتغير شيء في نسختك الحالية.").with_detail(e))?;
        app.restart();
    }
    let _ = app;
    Err(unavailable())
}
