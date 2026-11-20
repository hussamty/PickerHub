//! Everything the interface can ask of Rust. Each command validates its input itself.
use crate::edit::{self, Op};
use crate::state::{AppError, AppState, Snapshot};
use pickerhub::color::Color;
use pickerhub_store::model::{is_hex, parse_hex};
use pickerhub_store::{Backup, Workspace, export};
use serde::Serialize;
use serde_json::{Value, json};
use tauri::{AppHandle, State};
use tauri_plugin_clipboard_manager::ClipboardExt;
use tauri_plugin_dialog::DialogExt;

fn color(hex: &str) -> Result<Color, AppError> {
    parse_hex(hex).ok_or_else(|| AppError::new("invalid_hex", "أدخل ست خانات HEX."))
}

// ---------- Startup ----------

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Boot {
    #[serde(flatten)]
    snapshot: Snapshot,
    recovery_notice: Option<String>,
    platform: &'static str,
    session: String,
    version: &'static str,
    preview: bool,
}

#[tauri::command]
pub fn boot(state: State<'_, AppState>) -> Boot {
    Boot {
        snapshot: state.snapshot(),
        recovery_notice: state.recovery_notice.clone(),
        platform: std::env::consts::OS,
        session: std::env::var("XDG_SESSION_TYPE").unwrap_or_default(),
        version: env!("CARGO_PKG_VERSION"),
        preview: state.preview,
    }
}

// ---------- Colors ----------

/// Name, family, formats, ink, and contrast against white and black, for one color.
pub fn info(c: Color) -> Value {
    let mut v = c.describe();
    v["white"] = json!(c.ratio(Color(255, 255, 255)));
    v["black"] = json!(c.ratio(Color(0, 0, 0)));
    v
}

#[tauri::command]
pub fn describe(state: State<'_, AppState>, hex: String) -> Result<Value, AppError> {
    let c = color(&hex)?;
    Ok(state.describe_cached(c, || info(c)))
}

/// Many colors at once, for swatch grids: hex, name in both languages and ink.
#[tauri::command]
pub fn names(state: State<'_, AppState>, hexes: Vec<String>) -> Result<Vec<Value>, AppError> {
    hexes.iter().take(4096).map(|h| {
        let c = color(h)?;
        let v = state.describe_cached(c, || info(c));
        Ok(json!({"hex": v["hex"], "name": v["name"], "english": v["english"], "ink": v["ink"], "base": v["base"], "family": v["family"], "shade": v["shade"]}))
    }).collect()
}

/// Everything one frame of the picker needs, in a single round trip.
#[tauri::command]
pub fn frame(state: State<'_, AppState>, hex: String, kind: u32) -> Result<Value, AppError> {
    let c = color(&hex)?;
    let mut v = state.describe_cached(c, || info(c));
    v["harmony"] = c
        .harmonies(kind)
        .into_iter()
        .map(|h| {
            let ink = parse_hex(&h).map(|c| c.ink().hex()).unwrap_or_default();
            json!({"hex": h, "ink": ink})
        })
        .collect();
    let (h, s, l, sv, val) = c.hs();
    v["hsl"] = json!([h, s, l]);
    v["hsv"] = json!([h, sv, val]);
    Ok(v)
}

/// A color from the picker's spectrum: hue in degrees, saturation and value from 0 to 1.
#[tauri::command]
pub fn from_hsv(h: f64, s: f64, v: f64) -> String {
    let (s, v) = (s.clamp(0.0, 1.0), v.clamp(0.0, 1.0));
    let l = v * (1.0 - s / 2.0);
    let sl = if l == 0.0 || l == 1.0 { 0.0 } else { (v - l) / l.min(1.0 - l) };
    Color::from_hsl(h.rem_euclid(360.0), sl, l).hex()
}

#[tauri::command]
pub fn contrast(fg: String, bg: String) -> Result<Value, AppError> {
    let (f, b) = (color(&fg)?, color(&bg)?);
    let ratio = f.ratio(b);
    Ok(json!({"ratio": ratio, "aa": ratio >= 4.5, "large": ratio >= 3.0, "aaa": ratio >= 7.0, "aaaLarge": ratio >= 4.5, "suggestion": f.suggestion(b).hex()}))
}

/// Protanopia, deuteranopia and tritanopia previews.
#[tauri::command]
pub fn vision(hex: String) -> Result<Vec<String>, AppError> {
    Ok(pickerhub::vision::simulate(color(&hex)?).iter().map(|c| c.hex()).collect())
}

/// The value a copy action puts on the clipboard, in the format chosen in Settings.
pub fn copy_value(w: &Workspace, hex: &str) -> String {
    let Some(c) = parse_hex(hex) else { return hex.to_string() };
    match w.copy_format.as_str() {
        "RGB" => format!("rgb({}, {}, {})", c.0, c.1, c.2),
        "HSL" => c.describe()["formats"]["HSL"].as_str().unwrap_or(hex).to_string(),
        _ => {
            let digits = &hex[1..];
            let digits = if w.hex_uppercase { digits.to_ascii_uppercase() } else { digits.to_ascii_lowercase() };
            if w.hex_prefix { format!("#{digits}") } else { digits }
        }
    }
}

// ---------- Library ----------

#[tauri::command]
pub fn edit(state: State<'_, AppState>, ops: Vec<Op>) -> Result<Snapshot, AppError> {
    let record = ops.iter().any(Op::recorded);
    state.edit(record, |w| {
        for op in ops {
            edit::apply(w, op)?;
        }
        Ok(())
    })
}

#[tauri::command]
pub fn undo(state: State<'_, AppState>) -> Result<Snapshot, AppError> {
    state.travel(false)
}

#[tauri::command]
pub fn redo(state: State<'_, AppState>) -> Result<Snapshot, AppError> {
    state.travel(true)
}

#[tauri::command]
pub fn copy(app: AppHandle, state: State<'_, AppState>, text: String, raw: Option<bool>) -> Result<String, AppError> {
    let value = if raw.unwrap_or(false) || !is_hex(&text) { text } else { copy_value(&state.workspace(), &text) };
    app.clipboard().write_text(value.clone()).map_err(|e| AppError::new("clipboard", "تعذّر النسخ").with_detail(e))?;
    Ok(value)
}

// ---------- Files: every read and write goes through a system dialog the user answers ----------

fn save_dialog(app: &AppHandle, name: &str, text: &str) -> Result<Option<String>, AppError> {
    let ext = name.rsplit('.').next().unwrap_or("txt").to_string();
    let Some(path) = app.dialog().file().set_file_name(name).add_filter(ext.to_uppercase(), &[&ext]).blocking_save_file() else {
        return Ok(None);
    };
    let path = path.into_path().map_err(|_| AppError::new("save_failed", "تعذّر حفظ الملف"))?;
    pickerhub_store::atomic_write(&path, text.as_bytes()).map_err(|e| AppError::new("save_failed", "تعذّر حفظ الملف").with_detail(e))?;
    Ok(path.file_name().map(|n| n.to_string_lossy().into_owned()))
}

fn open_dialog(app: &AppHandle, title: &str, exts: &[&str], max: u64) -> Result<Option<(String, Vec<u8>)>, AppError> {
    let Some(path) = app.dialog().file().set_title(title).add_filter("", exts).blocking_pick_file() else { return Ok(None) };
    let path = path.into_path().map_err(|_| AppError::new("open_failed", "تعذّر فتح الملف"))?;
    let size = std::fs::metadata(&path).map(|m| m.len()).unwrap_or(u64::MAX);
    if size > max {
        return Err(AppError::new("too_large", "الملف أكبر من الحد المسموح."));
    }
    let bytes = std::fs::read(&path).map_err(|e| AppError::new("open_failed", "تعذّر فتح الملف").with_detail(e))?;
    Ok(Some((path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(), bytes)))
}

#[tauri::command]
pub async fn export_palette(app: AppHandle, state: State<'_, AppState>, id: String, format: String) -> Result<Option<String>, AppError> {
    let (name, text) = {
        let w = state.workspace();
        let p = w.palettes.iter().find(|p| p.id == id).ok_or_else(|| AppError::from(&pickerhub_store::StoreError::invalid_library()))?;
        (export::file_name(p, &format), export::palette(p, &format)?)
    };
    save_dialog(&app, &name, &text)
}

/// Colors that are not a saved palette (the picker's color, a harmony, an image's colors) in any
/// palette format. Each entry is `[name, hex]`.
#[tauri::command]
pub async fn export_colors(app: AppHandle, name: String, colors: Vec<(String, String)>, format: String) -> Result<Option<String>, AppError> {
    let mut p = pickerhub_store::Palette { name: name.trim().chars().take(100).collect(), ..Default::default() };
    if p.name.trim().is_empty() {
        p.name = "PickerHub".into();
    }
    for (n, hex) in colors.into_iter().take(pickerhub_store::model::MAX_COLORS) {
        color(&hex)?;
        p.colors.push(pickerhub_store::Swatch { name: n, hex: hex.to_ascii_uppercase(), ..Default::default() });
    }
    save_dialog(&app, &export::file_name(&p, &format), &export::palette(&p, &format)?)
}

/// Any text the interface built from Rust-made content (semantic CSS, tokens, gradient CSS).
#[tauri::command]
pub async fn export_text(app: AppHandle, name: String, text: String) -> Result<Option<String>, AppError> {
    if text.len() > 16 << 20 || name.contains(['/', '\\']) {
        return Err(AppError::new("invalid", "طلب غير صالح."));
    }
    save_dialog(&app, &name, &text)
}

#[tauri::command]
pub fn semantic(kind: String, themes: export::Themes) -> Result<(String, String), AppError> {
    Ok(match kind.as_str() {
        "css" => ("palette.css".into(), export::semantic_css(&themes)?),
        "tokens" => ("palette.tokens.json".into(), export::semantic_tokens(&themes)?),
        "report" => ("palette.contrast.json".into(), export::contrast_report(&themes)?),
        _ => return Err(AppError::new("invalid", "طلب غير صالح.")),
    })
}

#[tauri::command]
pub fn contrast_checks(roles: std::collections::BTreeMap<String, String>) -> Vec<Value> {
    export::contrast_checks(&roles)
}

#[derive(Serialize)]
pub struct ImportPreview {
    palette: pickerhub_store::Palette,
    unique: usize,
    duplicates: usize,
}

#[tauri::command]
pub async fn import_palette(app: AppHandle, state: State<'_, AppState>, into: String) -> Result<Option<ImportPreview>, AppError> {
    let Some((_, bytes)) = open_dialog(&app, "استيراد لوحة JSON", &["json"], 2 << 20)? else { return Ok(None) };
    let palette = export::parse_palette(&bytes)?;
    let w = state.workspace();
    let existing = w.palettes.iter().find(|p| p.id == into).map(|p| p.colors.as_slice()).unwrap_or(&[]);
    let unique = export::unique_against(&palette, existing).len();
    Ok(Some(ImportPreview { duplicates: palette.colors.len() - unique, unique, palette }))
}

#[tauri::command]
pub async fn backup_export(app: AppHandle, state: State<'_, AppState>) -> Result<Option<String>, AppError> {
    let text = state.export_backup()?;
    save_dialog(&app, "PickerHub-backup.json", &text)
}

#[tauri::command]
pub async fn backup_restore(app: AppHandle, state: State<'_, AppState>) -> Result<Option<Snapshot>, AppError> {
    let Some((_, bytes)) = open_dialog(&app, "نسخة PickerHub", &["json"], 16 << 20)? else { return Ok(None) };
    let text = String::from_utf8(bytes).map_err(|_| AppError::from(&pickerhub_store::StoreError::invalid_backup()))?;
    let backup = Backup::parse(&text)?;
    state.restore(&backup).map(Some)
}

// ---------- Palette of the day ----------

#[tauri::command]
pub fn daily(state: State<'_, AppState>, action: String, value: Option<String>) -> Result<Value, AppError> {
    // The envelope preview runs in a throwaway library and may seal the envelope again.
    if action == "reset" {
        if !state.preview {
            return Err(AppError::new("invalid", "طلب غير صالح."));
        }
        let _ = std::fs::remove_file(state.folder().join("daily_palette_state.json"));
        return Ok(json!({"state": "sealed"}));
    }
    let request = json!({"dir": state.folder().to_string_lossy(), "action": action, "value": value.unwrap_or_default()});
    let answer = pickerhub::daily::request(&request.to_string());
    if let Some(e) = answer.get("error").and_then(Value::as_str) {
        return Err(AppError::new("daily", "باقة اليوم").with_message(e));
    }
    Ok(answer)
}

// ---------- Support ----------

/// A report a user can attach to a bug: the build, the platform and the shape of the library.
/// It never contains colors, names, images or file paths.
#[tauri::command]
pub async fn support_report(app: AppHandle, state: State<'_, AppState>) -> Result<Option<String>, AppError> {
    let w = state.workspace().clone();
    let report = json!({
        "app": "PickerHub",
        "version": env!("CARGO_PKG_VERSION"),
        "os": std::env::consts::OS,
        "arch": std::env::consts::ARCH,
        "session": std::env::var("XDG_SESSION_TYPE").unwrap_or_default(),
        "capture": format!("{:?}", pickerhub_capture::backend()),
        "library": {
            "schema": w.version,
            "palettes": w.palettes.len(),
            "colors": w.palettes.iter().map(|p| p.colors.len()).sum::<usize>(),
            "gradients": w.gradients.len(),
            "history": w.history.len(),
            "discoveries": w.discoveries.len(),
        },
        "settings": {
            "language": w.language, "theme": w.theme, "followSystemTheme": w.follow_system_theme,
            "reduceMotion": w.reduce_motion, "reduceTransparency": w.reduce_transparency,
            "autoCopy": w.auto_copy, "copyFormat": w.copy_format, "globalShortcut": w.global_shortcut,
            "quickPick": w.quick_pick, "useWaylandScreenSharing": w.use_wayland_screen_sharing,
        },
        "recovered": state.recovery_notice.is_some(),
        // This session's timings and error codes, when the user turned the local log on.
        "log": crate::support::recent(),
    });
    save_dialog(&app, "PickerHub-diagnostics.json", &serde_json::to_string_pretty(&report).unwrap())
}
