//! Text formats PickerHub writes and reads: palette exports, semantic roles, and palette JSON imports.
//! Output matches what 1.x writes (palette formats byte for byte, JSON exports value for value),
//! so files made by either version are interchangeable.
use crate::error::StoreError;
use crate::model::{MAX_COLORS, Palette, ROLES, Swatch, is_hex, refresh_automatic_names, roles_valid};
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub const PALETTE_FORMATS: [&str; 6] = ["css", "scss", "json", "csv", "txt", "gpl"];

fn token(c: &Swatch, i: usize) -> String {
    let mut slug = String::new();
    let mut gap = false;
    for ch in c.name.chars() {
        if ch.is_alphanumeric() || ch == '-' {
            slug.push(ch);
            gap = false;
        } else if !gap {
            slug.push('-');
            gap = true;
        }
    }
    format!("color-{}-{}", i + 1, slug.trim_matches('-'))
}

/// CSV cells are quoted, and a cell a spreadsheet would run as a formula is defused with a quote mark.
fn csv(s: &str) -> String {
    let formula = s.trim_start().starts_with(['=', '+', '@', '-']) || s.starts_with(['\t', '\r', '\n']);
    let s = if formula { format!("'{s}") } else { s.to_string() };
    format!("\"{}\"", s.replace('"', "\"\""))
}

fn one_line(s: &str) -> String {
    s.replace("\r\n", " ").replace(['\r', '\n'], " ")
}

/// A file name for the palette, without characters the file systems refuse.
pub fn file_name(p: &Palette, ext: &str) -> String {
    let name: String = p.name.chars().map(|c| if "\\/:*?\"<>|".contains(c) || c.is_control() { '-' } else { c }).collect();
    format!("{name}.{ext}")
}

pub fn palette(p: &Palette, format: &str) -> Result<String, StoreError> {
    if p.colors.is_empty() {
        return Err(StoreError::invalid("empty_palette", "أضف لوناً إلى اللوحة أولاً."));
    }
    let lines = |f: &dyn Fn(usize, &Swatch) -> String| p.colors.iter().enumerate().map(|(i, c)| f(i, c)).collect::<Vec<_>>();
    Ok(match format {
        "css" => format!(":root {{\n{}\n}}\n", lines(&|i, c| format!("  --{}: {};", token(c, i), c.hex)).join("\n")),
        "scss" => lines(&|i, c| format!("${}: {};", token(c, i), c.hex)).join("\n"),
        "json" => serde_json::to_string_pretty(&json!({
            "name": p.name,
            "colors": p.colors.iter().map(|c| json!({"name": c.name, "hex": c.hex})).collect::<Vec<_>>(),
        }))
        .unwrap(),
        "csv" => format!("\u{feff}name,hex\r\n{}", lines(&|_, c| format!("{},{}", csv(&c.name), csv(&c.hex))).join("\r\n")),
        "gpl" => format!(
            "GIMP Palette\nName: {}\nColumns: 8\n#\n{}",
            one_line(&p.name),
            lines(&|_, c| {
                let v = u32::from_str_radix(&c.hex[1..], 16).unwrap_or(0);
                format!("{} {} {}\t{}", v >> 16, (v >> 8) & 255, v & 255, one_line(&c.name))
            })
            .join("\n")
        ),
        "txt" => format!("{}\n{}", p.name, lines(&|_, c| format!("{}  {}", c.name, c.hex)).join("\n")),
        _ => return Err(StoreError::invalid("unknown_format", "صيغة التصدير")),
    })
}

/// Reads a palette JSON exported by PickerHub (any version) as a new palette.
pub fn parse_palette(bytes: &[u8]) -> Result<Palette, StoreError> {
    let invalid = || StoreError::invalid("invalid_palette", "ملف اللوحة غير صالح. اختر ملف JSON صدّرته من PickerHub.");
    if bytes.len() > 2 << 20 {
        return Err(invalid());
    }
    let text = std::str::from_utf8(bytes).map_err(|_| invalid())?.trim_start_matches('\u{feff}');
    let root: Value = serde_json::from_str(text).map_err(|_| invalid())?;
    let name_of = |v: &Value| -> Result<String, StoreError> {
        match v.get("name").and_then(Value::as_str) {
            Some(s) if !s.trim().is_empty() && s.encode_utf16().count() <= 100 => Ok(s.to_string()),
            _ => Err(invalid()),
        }
    };
    let name = name_of(&root)?;
    let colors = root.get("colors").and_then(Value::as_array).ok_or_else(invalid)?;
    if colors.is_empty() || colors.len() > MAX_COLORS {
        return Err(invalid());
    }
    let mut p = Palette { name, ..Palette::default() };
    for item in colors {
        let hex = item.get("hex").and_then(Value::as_str).filter(|h| is_hex(h)).ok_or_else(invalid)?;
        p.colors.push(Swatch { name: name_of(item)?, hex: hex.to_ascii_uppercase(), ..Swatch::default() });
    }
    refresh_automatic_names(p.colors.iter_mut());
    Ok(p)
}

/// The incoming colors that `existing` does not already hold, compared case-insensitively.
pub fn unique_against(incoming: &Palette, existing: &[Swatch]) -> Vec<Swatch> {
    let mut seen: std::collections::HashSet<String> = existing.iter().map(|c| c.hex.to_ascii_uppercase()).collect();
    incoming.colors.iter().filter(|c| seen.insert(c.hex.to_ascii_uppercase())).cloned().collect()
}

pub type Themes = BTreeMap<String, BTreeMap<String, String>>;

fn validate(themes: &Themes) -> Result<(), StoreError> {
    if themes.is_empty() || themes.iter().any(|(k, v)| !matches!(k.as_str(), "light" | "dark") || !roles_valid(v) || v.is_empty()) {
        return Err(StoreError::invalid("roles_missing", "Assign valid semantic roles before export."));
    }
    Ok(())
}

pub fn semantic_css(themes: &Themes) -> Result<String, StoreError> {
    validate(themes)?;
    let block = |selector: &str, roles: &BTreeMap<String, String>| {
        let body: Vec<String> = ROLES
            .iter()
            .filter_map(|r| roles.get(*r).map(|hex| format!("  --color-{r}: {};", hex.to_ascii_uppercase())))
            .collect();
        format!("{selector} {{\n{}\n}}\n", body.join("\n"))
    };
    let mut css = String::from("/* PickerHub: sRGB semantic colors. Check contrast for your intended use. */\n");
    // 1.x builds the themes light first, then dark, and writes them in that order.
    for mode in ["light", "dark"] {
        let Some(roles) = themes.get(mode) else { continue };
        let selector = if mode == "light" {
            ":root, [data-theme=\"light\"]"
        } else if themes.len() == 1 {
            ":root, [data-theme=\"dark\"]"
        } else {
            "[data-theme=\"dark\"]"
        };
        css += &block(selector, roles);
    }
    if let Some(dark) = themes.get("dark") {
        css += &format!("@media (prefers-color-scheme: dark) {{\n{}}}\n", block(":root:not([data-theme=\"light\"])", dark));
    }
    Ok(css)
}

/// Typed DTCG color tokens (Color Module 2025.10).
pub fn semantic_tokens(themes: &Themes) -> Result<String, StoreError> {
    validate(themes)?;
    let mut out: Vec<(String, Value)> = Vec::new();
    for (mode, roles) in ordered(themes) {
        let mut group = serde_json::Map::new();
        for (role, hex) in roles {
            let v = u32::from_str_radix(&hex[1..], 16).unwrap();
            let comps: Vec<f64> = [v >> 16, (v >> 8) & 255, v & 255].iter().map(|c| *c as f64 / 255.0).collect();
            group.insert(
                role.clone(),
                json!({"$type": "color", "$value": {"colorSpace": "srgb", "components": comps, "alpha": 1, "hex": hex.to_ascii_lowercase()}}),
            );
        }
        out.push((mode.clone(), Value::Object(group)));
    }
    let body: Vec<String> = out.iter().map(|(k, v)| format!("{}: {}", Value::from(k.as_str()), indent_by(&serde_json::to_string_pretty(v).unwrap(), 2))).collect();
    Ok(format!("{{\n  {}\n}}", body.join(",\n  ")))
}

/// Light first, then dark: the order 1.x writes them in.
fn ordered(themes: &Themes) -> impl Iterator<Item = (&String, &BTreeMap<String, String>)> {
    ["light", "dark"].into_iter().filter_map(|m| themes.get_key_value(m))
}
fn indent(text: &str) -> String {
    indent_by(text, 4)
}
fn indent_by(text: &str, n: usize) -> String {
    text.replace('\n', &format!("\n{}", " ".repeat(n)))
}

/// Text and accent roles checked against both backgrounds.
pub fn contrast_checks(colors: &BTreeMap<String, String>) -> Vec<Value> {
    let mut rows = Vec::new();
    for fg in ["text", "muted", "primary", "accent"] {
        for bg in ["background", "surface"] {
            if let (Some(f), Some(b)) = (colors.get(fg), colors.get(bg)) {
                let (Some(f), Some(b)) = (crate::model::parse_hex(f), crate::model::parse_hex(b)) else { continue };
                let ratio = f.ratio(b);
                rows.push(json!({"foreground": fg, "background": bg, "ratio": ratio, "aa": ratio >= 4.5, "large": ratio >= 3.0, "aaa": ratio >= 7.0}));
            }
        }
    }
    rows
}

pub fn contrast_report(themes: &Themes) -> Result<String, StoreError> {
    validate(themes)?;
    // Written by hand to keep light before dark, as 1.x does; serde_json's map would sort the keys.
    let body: Vec<String> = ordered(themes)
        .map(|(k, v)| format!("{}: {}", Value::from(k.as_str()), indent(&serde_json::to_string_pretty(&contrast_checks(v)).unwrap())))
        .collect();
    Ok(format!("{{\n  \"standard\": \"WCAG 2 contrast (opaque sRGB)\",\n  \"themes\": {{\n    {}\n  }}\n}}", body.join(",\n    ")))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn sample() -> Palette {
        Palette {
            name: "Brand".into(),
            colors: vec![
                Swatch { name: "أزرق سماوي".into(), hex: "#87CEEB".into(), ..Swatch::default() },
                Swatch { name: "=SUM(A1)".into(), hex: "#111111".into(), ..Swatch::default() },
            ],
            ..Palette::default()
        }
    }
    #[test]
    fn formats_match_1x() {
        let p = sample();
        assert_eq!(palette(&p, "css").unwrap(), ":root {\n  --color-1-أزرق-سماوي: #87CEEB;\n  --color-2-SUM-A1: #111111;\n}\n");
        assert_eq!(palette(&p, "scss").unwrap(), "$color-1-أزرق-سماوي: #87CEEB;\n$color-2-SUM-A1: #111111;");
        assert_eq!(palette(&p, "csv").unwrap(), "\u{feff}name,hex\r\n\"أزرق سماوي\",\"#87CEEB\"\r\n\"'=SUM(A1)\",\"#111111\"");
        assert_eq!(palette(&p, "gpl").unwrap(), "GIMP Palette\nName: Brand\nColumns: 8\n#\n135 206 235\tأزرق سماوي\n17 17 17\t=SUM(A1)");
        assert_eq!(palette(&p, "txt").unwrap(), "Brand\nأزرق سماوي  #87CEEB\n=SUM(A1)  #111111");
    }
    #[test]
    fn json_round_trip_and_dedupe() {
        let p = sample();
        let back = parse_palette(palette(&p, "json").unwrap().as_bytes()).unwrap();
        assert_eq!(back.name, "Brand");
        assert_eq!(back.colors.len(), 2);
        assert_eq!(unique_against(&back, &p.colors[..1]).len(), 1);
        assert!(parse_palette(b"{\"name\":\"x\",\"colors\":[]}").is_err());
    }
    #[test]
    fn semantic_exports() {
        let mut themes = Themes::new();
        themes.insert("dark".into(), [("text".to_string(), "#ffffff".to_string()), ("background".to_string(), "#111111".to_string())].into());
        let css = semantic_css(&themes).unwrap();
        assert!(css.contains(":root, [data-theme=\"dark\"] {\n  --color-background: #111111;\n  --color-text: #FFFFFF;\n}"));
        assert!(semantic_tokens(&themes).unwrap().contains("\"hex\": \"#ffffff\""));
        assert!(contrast_report(&themes).unwrap().contains("\"aaa\": true"));
    }
}
