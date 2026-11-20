//! Image analysis: decode within limits, scale to a 768 px preview, and ask the core for the colors
//! and where each one lives in the picture.
use crate::state::AppError;
use base64::Engine;
use image::{ImageReader, imageops::FilterType};
use serde_json::{Value, json};
use std::io::Cursor;
use tauri::AppHandle;
use tauri_plugin_dialog::DialogExt;

const MAX_BYTES: usize = 32 << 20;
const MAX_PIXELS: u64 = 40_000_000;
const PREVIEW: u32 = 768;

fn too_large() -> AppError {
    AppError::new("image_too_large", "PNG أو JPG أو WEBP بحجم يصل إلى 32 ميغابايت.")
}

pub fn analyze(name: &str, bytes: &[u8]) -> Result<Value, AppError> {
    if bytes.len() > MAX_BYTES {
        return Err(too_large());
    }
    let unreadable = || AppError::new("image_unreadable", "تعذّر قراءة الصورة.");
    let reader = ImageReader::new(Cursor::new(bytes)).with_guessed_format().map_err(|_| unreadable())?;
    let (w, h) = reader.into_dimensions().map_err(|_| unreadable())?;
    if w as u64 * h as u64 > MAX_PIXELS {
        return Err(too_large());
    }
    let mut reader = ImageReader::new(Cursor::new(bytes)).with_guessed_format().map_err(|_| unreadable())?;
    let mut limits = image::Limits::default();
    limits.max_alloc = Some(MAX_PIXELS * 4 + (64 << 20));
    reader.limits(limits);
    let img = reader.decode().map_err(|e| unreadable().with_detail(e))?;
    let preview = if w.max(h) > PREVIEW { img.resize(PREVIEW, PREVIEW, FilterType::Triangle) } else { img };
    let rgba = preview.to_rgba8();
    let (pw, ph) = rgba.dimensions();
    let colors = pickerhub::locate(rgba.as_raw(), pw as usize, ph as usize, 16);
    let mut png = Vec::new();
    image::DynamicImage::ImageRgba8(rgba)
        .write_to(&mut Cursor::new(&mut png), image::ImageFormat::Png)
        .map_err(|e| unreadable().with_detail(e))?;
    let stem = name.rsplit_once('.').map(|(s, _)| s).unwrap_or(name);
    Ok(json!({
        "name": if stem.trim().is_empty() { "image" } else { stem },
        "width": w, "height": h,
        "previewWidth": pw, "previewHeight": ph,
        "preview": format!("data:image/png;base64,{}", base64::engine::general_purpose::STANDARD.encode(&png)),
        "colors": colors,
    }))
}

/// Bytes sent as the raw request body (pasted or dropped images); the name rides in a header.
#[tauri::command]
pub async fn analyze_image(request: tauri::ipc::Request<'_>) -> Result<Value, AppError> {
    let tauri::ipc::InvokeBody::Raw(bytes) = request.body() else {
        return Err(AppError::new("invalid", "طلب غير صالح."));
    };
    // Header values are ASCII, so the interface percent-encodes the file name.
    let name = request.headers().get("x-name").and_then(|v| v.to_str().ok()).map(percent_decode).unwrap_or_else(|| "image".into());
    let bytes = bytes.clone();
    tauri::async_runtime::spawn_blocking(move || analyze(&name, &bytes)).await.map_err(|_| AppError::new("invalid", "طلب غير صالح."))?
}

/// The file picker path: the user chooses an image in the system dialog.
#[tauri::command]
pub async fn analyze_image_file(app: AppHandle) -> Result<Option<Value>, AppError> {
    let Some(path) = app.dialog().file().add_filter("PNG / JPG / WEBP", &["png", "jpg", "jpeg", "webp"]).blocking_pick_file() else {
        return Ok(None);
    };
    let path = path.into_path().map_err(|_| AppError::new("open_failed", "تعذّر فتح الملف"))?;
    if std::fs::metadata(&path).map(|m| m.len() as usize > MAX_BYTES).unwrap_or(true) {
        return Err(too_large());
    }
    let bytes = std::fs::read(&path).map_err(|e| AppError::new("open_failed", "تعذّر فتح الملف").with_detail(e))?;
    let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    tauri::async_runtime::spawn_blocking(move || analyze(&name, &bytes)).await.map_err(|_| AppError::new("invalid", "طلب غير صالح."))?.map(Some)
}

fn percent_decode(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' && i + 2 < b.len() {
            if let Ok(v) = u8::from_str_radix(std::str::from_utf8(&b[i + 1..(i + 3).min(b.len())]).unwrap_or(""), 16) {
                out.push(v);
                i += 3;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).chars().take(100).collect()
}

/// An image from the clipboard, as PNG bytes for the same analysis.
#[tauri::command]
pub async fn paste_image(app: AppHandle) -> Result<Value, AppError> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    let empty = || AppError::new("no_image", "لا توجد صورة في الحافظة. انسخ صورة أو ملف صورة أولاً.");
    let image = app.clipboard().read_image().map_err(|_| empty())?;
    let (w, h) = (image.width(), image.height());
    let rgba = image::RgbaImage::from_raw(w, h, image.rgba().to_vec()).ok_or_else(empty)?;
    let mut png = Vec::new();
    image::DynamicImage::ImageRgba8(rgba).write_to(&mut Cursor::new(&mut png), image::ImageFormat::Png).map_err(|_| empty())?;
    tauri::async_runtime::spawn_blocking(move || analyze("صورة ملصقة", &png)).await.map_err(|_| empty())?
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn a_two_color_picture_gives_both_colors_with_marks() {
        let mut img = image::RgbaImage::new(1600, 900);
        for (x, _, p) in img.enumerate_pixels_mut() {
            *p = if x < 1200 { image::Rgba([135, 206, 235, 255]) } else { image::Rgba([200, 85, 61, 255]) };
        }
        let mut png = Vec::new();
        image::DynamicImage::ImageRgba8(img).write_to(&mut Cursor::new(&mut png), image::ImageFormat::Png).unwrap();
        let v = analyze("meadow.png", &png).unwrap();
        assert_eq!(v["name"], "meadow");
        assert_eq!(v["previewWidth"], 768);
        let colors = v["colors"].as_array().unwrap();
        assert_eq!(colors[0]["hex"], "#87CEEB");
        assert!((colors[0]["percent"].as_f64().unwrap() - 75.0).abs() < 1.0);
        assert!(colors.iter().all(|c| c["x"].is_f64()));
        assert!(analyze("x.png", b"not an image").is_err());
        assert_eq!(percent_decode("%D8%B5%D9%88%D8%B1%D8%A9.png"), "صورة.png");
        assert_eq!(percent_decode("bad%2"), "bad%2");
    }
}
