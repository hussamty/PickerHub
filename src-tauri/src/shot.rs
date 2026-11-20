//! QA hooks, compiled only with the `qa` feature: run a script in the page, then save a frame of
//! the WebView and quit. `PICKERHUB_EVAL='…' PICKERHUB_SHOT=out.png pickerhub`
#[cfg(all(feature = "qa", target_os = "linux"))]
pub fn schedule(app: tauri::AppHandle) {
    use tauri::Manager;
    use webkit2gtk::{SnapshotOptions, SnapshotRegion, WebViewExt};
    let shot = std::env::var("PICKERHUB_SHOT").ok();
    let script = std::env::var("PICKERHUB_EVAL").ok();
    if shot.is_none() && script.is_none() {
        return;
    }
    let wait = std::env::var("PICKERHUB_WAIT").ok().and_then(|v| v.parse().ok()).unwrap_or(1200u64);
    std::thread::spawn(move || {
        std::thread::sleep(std::time::Duration::from_millis(2500));
        let window = app.get_webview_window("main").expect("main window");
        if let Some(js) = script {
            window.eval(&js).unwrap();
        }
        std::thread::sleep(std::time::Duration::from_millis(wait));
        let Some(path) = shot else { return };
        let handle = app.clone();
        window
            .with_webview(move |webview| {
                webview.inner().snapshot(SnapshotRegion::Visible, SnapshotOptions::INCLUDE_SELECTION_HIGHLIGHTING, None::<&gio::Cancellable>, move |result| {
                    let image = cairo::ImageSurface::try_from(result.expect("snapshot")).expect("image surface");
                    image.write_to_png(&mut std::fs::File::create(&path).unwrap()).unwrap();
                    handle.exit(0);
                });
            })
            .unwrap();
    });
}
#[cfg(not(all(feature = "qa", target_os = "linux")))]
pub fn schedule(_app: tauri::AppHandle) {}

/// QA builds print what a scripted page measured and quit; other builds refuse.
#[tauri::command]
pub fn qa_report(app: tauri::AppHandle, text: String) -> Result<(), String> {
    if cfg!(feature = "qa") {
        println!("{text}");
        app.exit(0);
        Ok(())
    } else {
        let _ = app;
        Err("not a QA build".into())
    }
}
