//! The live lens (desktop/Magnifier.cs). A block of brand black follows the pointer with the pixels
//! around it magnified, the chosen pixel framed in yellow and its value beneath.
//!
//! - X11: the pointer and keyboard are held on the root window (`LensSession`), and the lens is a
//!   small always-on-top window placed beside the pointer.
//! - Wayland with screen sharing: the compositor gives pointer events only to a visible surface, so a
//!   full-screen surface takes them and draws the lens itself. WebKitGTK cannot make that surface
//!   see-through on Wayland (it paints black), so the surface shows the screen itself: one fresh
//!   frame from the shared stream, taken as the eyedropper opens. The lens reads that same frame, so
//!   what is seen is exactly what is picked, and the lens can never read itself back. The sharing
//!   session (`Sharing`) is asked for once and kept for the app run.
// macOS uses the system sampler (NSColorSampler), not this lens.
#![cfg_attr(target_os = "macos", allow(dead_code))]
use crate::state::AppError;
use base64::Engine;
use pickerhub_capture::{CaptureError, Rgb};
use serde::Serialize;
use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Manager, WebviewUrl, WebviewWindowBuilder};

pub const WIDTH: f64 = 232.0;
pub const HEIGHT: f64 = 314.0;
pub const HEIGHT_MULTIPLE: f64 = 390.0;
const GAP: f64 = 24.0;

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Frame {
    /// RGB of the 25×25 square, row by row, base64.
    pixels: String,
    hex: String,
    count: usize,
    recent: Vec<String>,
    multiple: bool,
}

fn frame(patch: &pickerhub_capture::Patch, picked: &[String], multiple: bool) -> Frame {
    let rgb: Vec<u8> = patch.chunks(4).flat_map(|p| [p[0], p[1], p[2]]).collect();
    Frame {
        pixels: base64::engine::general_purpose::STANDARD.encode(rgb),
        hex: pickerhub_capture::centre(patch).hex(),
        count: picked.len(),
        recent: picked.iter().rev().take(6).rev().cloned().collect(),
        multiple,
    }
}

/// What a lens session ended with: the colors picked (one, or several in multi-pick), or nothing.
pub type Outcome = Result<Vec<Rgb>, CaptureError>;

// ---------------------------------------------------------------- X11 and Windows

/// A native lens session (X11: pointer grab on the root window; Windows: low-level hooks), with the
/// lens drawn in a small always-on-top window that follows the pointer.
pub async fn native(app: &AppHandle, multiple: bool, open: fn() -> Result<Box<dyn pickerhub_capture::LensSource>, CaptureError>) -> Outcome {
    let height = if multiple { HEIGHT_MULTIPLE } else { HEIGHT };
    let started = crate::support::start();
    let lens = WebviewWindowBuilder::new(app, "lens", WebviewUrl::App("index.html#lens".into()))
        .title("PickerHub Magnifier")
        .inner_size(WIDTH, height)
        .decorations(false)
        .transparent(true)
        .always_on_top(true)
        .skip_taskbar(true)
        .resizable(false)
        .focused(false)
        .shadow(false)
        .visible(false)
        .build()
        .map_err(|e| CaptureError::new("lens", "تعذّر بدء القطّارة.").with(e))?;
    let app2 = app.clone();
    let lens2 = lens.clone();
    let result = tauri::async_runtime::spawn_blocking(move || -> Outcome {
        use pickerhub_capture::LensAction::*;
        let session = open()?;
        let mut picked: Vec<Rgb> = Vec::new();
        let mut last = (i32::MIN, i32::MIN);
        let mut shown = false;
        // Give the window a moment to load before frames are sent to it.
        std::thread::sleep(std::time::Duration::from_millis(250));
        loop {
            for action in session.poll()? {
                match action {
                    Pick(x, y) => {
                        picked.push(pickerhub_capture::centre(&session.sample(x, y)?));
                        if !multiple {
                            return Ok(picked);
                        }
                        last = (i32::MIN, i32::MIN);
                    }
                    PickHere => {
                        let (x, y) = session.pointer()?;
                        picked.push(pickerhub_capture::centre(&session.sample(x, y)?));
                        if !multiple {
                            return Ok(picked);
                        }
                        last = (i32::MIN, i32::MIN);
                    }
                    Cancel => return Ok(if multiple { picked } else { Vec::new() }),
                    Zoom(d) => {
                        let _ = app2.emit_to("lens", "lens-zoom", d);
                    }
                    Nudge(dx, dy) => session.nudge(dx, dy)?,
                }
            }
            let p = session.pointer()?;
            if p != last {
                last = p;
                // Beside the pointer, on whichever side has room within the monitor under it, so the
                // lens never covers what it reads.
                let monitor = app2.monitor_from_point(p.0 as f64, p.1 as f64).ok().flatten();
                let scale = monitor.as_ref().map(|m| m.scale_factor()).unwrap_or(1.0);
                let (mx, my, mw, mh) = monitor
                    .map(|m| (m.position().x as f64, m.position().y as f64, m.size().width as f64, m.size().height as f64))
                    .unwrap_or((0.0, 0.0, 1e5, 1e5));
                let (w, h, gap) = (WIDTH * scale, height * scale, GAP * scale);
                let place = |pos: f64, size: f64, start: f64, end: f64| {
                    if pos + gap + size <= end { pos + gap } else { (pos - gap - size).max(start) }
                };
                let _ = lens2.set_position(tauri::PhysicalPosition::new(place(p.0 as f64, w, mx, mx + mw), place(p.1 as f64, h, my, my + mh)));
            }
            let patch = session.sample(p.0, p.1)?;
            let hexes: Vec<String> = picked.iter().map(|c| c.hex()).collect();
            let _ = app2.emit_to("lens", "lens-frame", frame(&patch, &hexes, multiple));
            if !shown {
                let _ = lens2.show();
                shown = true;
                crate::support::measured("native-first-sample", started);
            }
            std::thread::sleep(std::time::Duration::from_millis(16));
        }
    })
    .await
    .map_err(|e| CaptureError::new("lens", "تعذّر بدء القطّارة.").with(e))?;
    let _ = lens.close();
    result
}

#[cfg(target_os = "linux")]
pub fn open_x11() -> Result<Box<dyn pickerhub_capture::LensSource>, CaptureError> {
    Ok(Box::new(pickerhub_capture::x11::LensSession::open()?))
}
#[cfg(windows)]
pub fn open_windows() -> Result<Box<dyn pickerhub_capture::LensSource>, CaptureError> {
    Ok(Box::new(pickerhub_capture::windows::LensSession::open()?))
}

// ---------------------------------------------------------------- Wayland, screen sharing

#[derive(Default)]
pub struct Lens {
    #[cfg(target_os = "linux")]
    sharing: tokio::sync::Mutex<Option<pickerhub_capture::screencast::Sharing>>,
    waiting: Mutex<Option<tokio::sync::oneshot::Sender<Outcome>>>,
    picked: Mutex<Vec<Rgb>>,
    multiple: Mutex<bool>,
    /// One still frame and its picture per full-screen surface, by window label.
    #[cfg(target_os = "linux")]
    frozen: Mutex<std::collections::HashMap<String, pickerhub_capture::screencast::Frozen>>,
    backdrop: Mutex<std::collections::HashMap<String, String>>,
}

#[cfg(target_os = "linux")]
pub async fn shared(app: &AppHandle, multiple: bool) -> Outcome {
    let lens = app.state::<Lens>();
    let started = crate::support::start();
    let mut timing = "live-warm-first-sample";
    {
        let mut sharing = lens.sharing.lock().await;
        if sharing.is_none() {
            timing = "live-fresh-first-sample";
            match pickerhub_capture::screencast::Sharing::start().await? {
                Some(s) => *sharing = Some(s),
                None => return Ok(Vec::new()),
            }
            let _ = app.emit("sharing", true);
        }
    }
    // Every monitor gets a surface of its own, placed with GTK's fullscreen_on_monitor (Wayland lets a
    // client choose the output of a full-screen window, though not a free position). Monitors are
    // matched to shared streams by their place in the compositor's logical space.
    let monitors = gdk_monitors(app).await;
    let mut surfaces: Vec<(String, i32)> = Vec::new();
    {
        let mut sharing = lens.sharing.lock().await;
        let Some(sharing) = sharing.as_mut() else { return Ok(Vec::new()) };
        for (n, stream) in sharing.streams.iter_mut().enumerate() {
            let index = monitors
                .iter()
                .position(|m| (m.0 - stream.position.0).abs() <= 2 && (m.1 - stream.position.1).abs() <= 2)
                .unwrap_or(n.min(monitors.len().saturating_sub(1)));
            let Some(frozen) = stream.freeze(true, std::time::Duration::from_millis(400))? else { continue };
            let label = format!("overlay-{n}");
            let (w, h) = (frozen.width, frozen.height);
            let rgb: Vec<u8> = frozen.rgba.chunks(4).flat_map(|p| [p[0], p[1], p[2]]).collect();
            // The picture the surface shows: JPEG is quick to make and to draw; the lens reads the exact frame.
            let jpeg = tauri::async_runtime::spawn_blocking(move || {
                let mut out = Vec::new();
                image::codecs::jpeg::JpegEncoder::new_with_quality(&mut out, 92).encode(&rgb, w, h, image::ExtendedColorType::Rgb8).map(|_| out)
            })
            .await
            .map_err(|e| CaptureError::new("lens", "تعذّر بدء القطّارة.").with(e))?
            .map_err(|e| CaptureError::new("lens", "تعذّر بدء القطّارة.").with(e))?;
            lens.backdrop.lock().unwrap().insert(label.clone(), format!("data:image/jpeg;base64,{}", base64::engine::general_purpose::STANDARD.encode(jpeg)));
            lens.frozen.lock().unwrap().insert(label.clone(), frozen);
            surfaces.push((label, index as i32));
        }
    }
    if surfaces.is_empty() {
        return Err(CaptureError::new("stream_empty", "لم يصل إطار من الشاشة. أعد فتح القطّارة أو استخدم منتقي النظام."));
    }
    let (tx, rx) = tokio::sync::oneshot::channel();
    *lens.waiting.lock().unwrap() = Some(tx);
    *lens.picked.lock().unwrap() = Vec::new();
    *lens.multiple.lock().unwrap() = multiple;
    let mut windows = Vec::new();
    for (i, (label, monitor)) in surfaces.iter().enumerate() {
        let w = WebviewWindowBuilder::new(app, label, WebviewUrl::App("index.html#overlay".into()))
            .title("PickerHub Magnifier")
            .decorations(false)
            .always_on_top(true)
            .skip_taskbar(true)
            .resizable(false)
            .shadow(false)
            .focused(i == 0)
            .build()
            .map_err(|e| CaptureError::new("lens", "تعذّر بدء القطّارة.").with(e))?;
        let target = w.clone();
        let monitor = *monitor;
        let _ = app.run_on_main_thread(move || {
            use gtk::prelude::GtkWindowExt;
            if let Ok(gw) = target.gtk_window() {
                match GtkWindowExt::screen(&gw) {
                    Some(screen) => gw.fullscreen_on_monitor(&screen, monitor),
                    None => gw.fullscreen(),
                }
            }
        });
        windows.push(w);
    }
    crate::support::measured(timing, started);
    let outcome = rx.await.unwrap_or(Ok(Vec::new()));
    for w in windows {
        let _ = w.close();
    }
    lens.frozen.lock().unwrap().clear();
    lens.backdrop.lock().unwrap().clear();
    outcome
}

/// The monitors as GTK sees them, in logical pixels: (x, y, width, height).
#[cfg(target_os = "linux")]
async fn gdk_monitors(app: &AppHandle) -> Vec<(i32, i32, i32, i32)> {
    let (tx, rx) = tokio::sync::oneshot::channel();
    let _ = app.run_on_main_thread(move || {
        use gtk::gdk::prelude::MonitorExt;
        let list = gtk::gdk::Display::default()
            .map(|d| (0..d.n_monitors()).filter_map(|i| d.monitor(i)).map(|m| { let g = m.geometry(); (g.x(), g.y(), g.width(), g.height()) }).collect())
            .unwrap_or_default();
        let _ = tx.send(list);
    });
    rx.await.unwrap_or_default()
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Sampled {
    frame: Option<Frame>,
}

/// The picture a full-screen surface shows while the user picks: its own monitor's frame.
#[tauri::command]
pub fn lens_backdrop(app: AppHandle, window: tauri::WebviewWindow) -> Option<String> {
    app.state::<Lens>().backdrop.lock().unwrap().get(window.label()).cloned()
}

/// The overlay asks for the pixels under the pointer, as fractions of its width and height, which
/// map onto the frozen frame whatever the scale.
#[tauri::command]
pub fn lens_sample(app: AppHandle, window: tauri::WebviewWindow, fx: f64, fy: f64) -> Result<Sampled, AppError> {
    #[cfg(target_os = "linux")]
    {
        let lens = app.state::<Lens>();
        let frozen = lens.frozen.lock().unwrap();
        let Some(f) = frozen.get(window.label()) else { return Ok(Sampled { frame: None }) };
        let patch = f.sample(fx.clamp(0.0, 1.0) * f.size.0 as f64, fy.clamp(0.0, 1.0) * f.size.1 as f64);
        let picked: Vec<String> = lens.picked.lock().unwrap().iter().map(|c| c.hex()).collect();
        let multiple = *lens.multiple.lock().unwrap();
        return Ok(Sampled { frame: Some(frame(&patch, &picked, multiple)) });
    }
    #[cfg(not(target_os = "linux"))]
    {
        let _ = (app, window, fx, fy);
        Ok(Sampled { frame: None })
    }
}

/// The overlay reports a pick (the centre color it shows) or, with `hex` empty, the end.
#[tauri::command]
pub fn lens_done(app: AppHandle, hex: Option<String>) {
    let lens = app.state::<Lens>();
    let multiple = *lens.multiple.lock().unwrap();
    if let Some(h) = hex.as_deref().and_then(|h| u32::from_str_radix(h.trim_start_matches('#'), 16).ok()) {
        lens.picked.lock().unwrap().push(Rgb((h >> 16) as u8, (h >> 8) as u8, h as u8));
        if multiple {
            return;
        }
    }
    let picked = std::mem::take(&mut *lens.picked.lock().unwrap());
    let outcome = if hex.is_none() && !multiple { Vec::new() } else { picked };
    if let Some(tx) = lens.waiting.lock().unwrap().take() {
        let _ = tx.send(Ok(outcome));
    }
}

/// Whether the eyedropper goes through the desktop portal (Wayland): only there is there a capture mode to choose.
#[tauri::command]
pub fn capture_portal() -> bool {
    #[cfg(target_os = "linux")]
    {
        pickerhub_capture::backend() == pickerhub_capture::Backend::Portal
    }
    #[cfg(not(target_os = "linux"))]
    {
        false
    }
}

/// Opens screen sharing ahead of the first pick (desktop/CaptureOptions.cs, EnableLiveSharing): the
/// compositor asks for consent now, so the next F8 shows the live lens at once. False when the user
/// declined.
#[tauri::command]
pub async fn start_sharing(app: AppHandle) -> Result<bool, AppError> {
    #[cfg(target_os = "linux")]
    {
        let lens = app.state::<Lens>();
        let mut sharing = lens.sharing.lock().await;
        if sharing.is_none() {
            match pickerhub_capture::screencast::Sharing::start().await? {
                Some(s) => *sharing = Some(s),
                None => return Ok(false),
            }
            let _ = app.emit("sharing", true);
        }
        Ok(true)
    }
    #[cfg(not(target_os = "linux"))]
    {
        let _ = app;
        Ok(false)
    }
}

/// Ends screen sharing (Settings, or the tray).
#[tauri::command]
pub async fn stop_sharing(app: AppHandle) {
    #[cfg(target_os = "linux")]
    {
        *app.state::<Lens>().sharing.lock().await = None;
    }
    let _ = app.emit("sharing", false);
}

#[tauri::command]
pub async fn sharing(app: AppHandle) -> bool {
    #[cfg(target_os = "linux")]
    {
        return app.state::<Lens>().sharing.lock().await.is_some();
    }
    #[cfg(not(target_os = "linux"))]
    {
        let _ = app;
        false
    }
}
