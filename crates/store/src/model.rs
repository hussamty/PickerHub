//! The library as it is saved in `workspace.json`, schema 2.
//!
//! Field names, defaults and every validation rule follow `desktop/Workspace.cs`, so a file written
//! by PickerHub 1.x opens here and a file written here opens in 1.x. Fields this version does not
//! know are kept in `extra` and written back unchanged.
use crate::error::StoreError;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::collections::{BTreeMap, HashSet};

pub const SCHEMA: i64 = 2;
pub const MAX_PALETTES: usize = 256;
pub const MAX_COLORS: usize = 2048;
pub const MAX_GRADIENTS: usize = 512;
pub const MAX_DISCOVERIES: usize = 90;
pub const ROLES: [&str; 7] = ["background", "surface", "primary", "accent", "text", "muted", "border"];
pub const SHORTCUTS: [&str; 3] = ["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"];

pub fn new_id() -> String {
    uuid::Uuid::new_v4().simple().to_string()
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Swatch {
    pub id: String,
    pub name: String,
    pub hex: String,
    pub favorite: bool,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl Default for Swatch {
    fn default() -> Self {
        Self { id: new_id(), name: String::new(), hex: "#87CEEB".into(), favorite: false, extra: Map::new() }
    }
}
impl Swatch {
    /// A new swatch named by the perceptual classifier, in Arabic as 1.x does.
    pub fn from_hex(hex: &str) -> Self {
        let hex = hex.to_ascii_uppercase();
        let name = parse_hex(&hex).map(|c| pickerhub::names::name(c).arabic.to_string()).unwrap_or_default();
        Self { name, hex, ..Self::default() }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Palette {
    pub id: String,
    pub name: String,
    pub colors: Vec<Swatch>,
    pub collection: String,
    pub roles: BTreeMap<String, String>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl Default for Palette {
    fn default() -> Self {
        Self {
            id: new_id(),
            name: "لوحتي الأولى".into(),
            colors: Vec::new(),
            collection: String::new(),
            roles: BTreeMap::new(),
            extra: Map::new(),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Stop {
    pub hex: String,
    pub pos: i64,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl Default for Stop {
    fn default() -> Self {
        Self { hex: "#FFFFFF".into(), pos: 0, extra: Map::new() }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Gradient {
    pub id: String,
    pub name: String,
    #[serde(rename = "type")]
    pub kind: String,
    pub angle: i64,
    pub stops: Vec<Stop>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl Default for Gradient {
    fn default() -> Self {
        Self {
            id: new_id(),
            name: "تدرجي".into(),
            kind: "linear".into(),
            angle: 90,
            stops: vec![
                Stop { hex: "#2856B4".into(), pos: 0, extra: Map::new() },
                Stop { hex: "#87CEEB".into(), pos: 100, extra: Map::new() },
            ],
            extra: Map::new(),
        }
    }
}

/// A palette of the day kept in the archive: its roles in both themes.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct Discovery {
    pub id: String,
    pub name: String,
    pub created_at: String,
    pub light: BTreeMap<String, String>,
    pub dark: BTreeMap<String, String>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Workspace {
    pub version: i64,
    /// 1 is the perceptual naming of PickerHub 1.1 and later. Files without it are renamed once.
    pub name_scheme: i64,
    pub color: String,
    pub limit: i64,
    pub use_wayland_screen_sharing: bool,
    pub reduce_motion: bool,
    pub diagnostics_enabled: bool,
    pub language: String,
    pub reduce_transparency: bool,
    pub library_view: String,
    pub follow_system_theme: bool,
    pub auto_copy: bool,
    pub copy_format: String,
    pub hex_uppercase: bool,
    pub hex_prefix: bool,
    pub global_shortcut: bool,
    pub quick_pick: bool,
    pub shortcut: String,
    pub theme: String,
    pub discoveries: Vec<Discovery>,
    pub brand_locks: BTreeMap<String, String>,
    pub history: Vec<String>,
    pub palettes: Vec<Palette>,
    pub gradients: Vec<Gradient>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl Default for Workspace {
    fn default() -> Self {
        Self {
            version: SCHEMA,
            name_scheme: 1,
            color: "#87CEEB".into(),
            limit: 32,
            use_wayland_screen_sharing: false,
            reduce_motion: false,
            diagnostics_enabled: false,
            language: "ar".into(),
            reduce_transparency: false,
            library_view: "grid".into(),
            follow_system_theme: false,
            auto_copy: true,
            copy_format: "HEX".into(),
            hex_uppercase: true,
            hex_prefix: true,
            global_shortcut: false,
            quick_pick: false,
            shortcut: "Ctrl+Shift+P".into(),
            theme: "dark".into(),
            discoveries: Vec::new(),
            brand_locks: BTreeMap::new(),
            history: Vec::new(),
            palettes: vec![Palette::default()],
            gradients: Vec::new(),
            extra: Map::new(),
        }
    }
}

pub fn is_hex(s: &str) -> bool {
    s.len() == 7 && s.starts_with('#') && s[1..].bytes().all(|b| b.is_ascii_hexdigit())
}
pub fn parse_hex(s: &str) -> Option<pickerhub::color::Color> {
    is_hex(s).then(|| pickerhub::color::Color::unpack(u32::from_str_radix(&s[1..], 16).unwrap()))
}
/// A name the user can see: not blank and at most 100 UTF-16 units, as .NET counts string length.
fn is_name(s: &str) -> bool {
    !s.trim().is_empty() && s.encode_utf16().count() <= 100
}
fn ids_valid<'a>(ids: impl Iterator<Item = &'a str>) -> bool {
    let mut seen = HashSet::new();
    ids.into_iter().all(|id| {
        (1..=64).contains(&id.len()) && id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-') && seen.insert(id)
    })
}
pub fn roles_valid(map: &BTreeMap<String, String>) -> bool {
    map.len() <= ROLES.len() && map.iter().all(|(k, v)| ROLES.contains(&k.as_str()) && is_hex(v))
}
/// `DateTimeOffset.TryParse` accepts far more than this, but every value PickerHub writes is ISO 8601.
fn is_timestamp(s: &str) -> bool {
    let b = s.as_bytes();
    b.len() >= 10
        && b.len() <= 40
        && b[..4].iter().all(u8::is_ascii_digit)
        && b[4] == b'-'
        && b[5..7].iter().all(u8::is_ascii_digit)
        && b[7] == b'-'
        && b[8..10].iter().all(u8::is_ascii_digit)
}

impl Workspace {
    /// Reads and validates a workspace file, upgrading schema 1 and pre-1.1 automatic names.
    pub fn parse(text: &str) -> Result<Self, StoreError> {
        let root: Value = serde_json::from_str(text.trim_start_matches('\u{feff}')).map_err(|_| StoreError::invalid_backup())?;
        let obj = root.as_object().ok_or_else(StoreError::invalid_backup)?;
        for key in ["version", "color", "limit", "theme", "palettes", "gradients", "history"] {
            if !obj.contains_key(key) {
                return Err(StoreError::invalid_backup());
            }
        }
        let old_names = obj.get("nameScheme").and_then(Value::as_i64).is_none_or(|n| n < 1);
        let mut w: Workspace = serde_json::from_value(root).map_err(|_| StoreError::invalid_library())?;
        if w.version == 1 {
            w.version = SCHEMA;
        }
        if w.version > SCHEMA {
            return Err(StoreError::NewerVersion);
        }
        w.validate()?;
        if old_names {
            refresh_automatic_names(w.palettes.iter_mut().flat_map(|p| p.colors.iter_mut()));
            w.name_scheme = 1;
        }
        Ok(w)
    }

    pub fn validate(&self) -> Result<(), StoreError> {
        let ok = self.version == SCHEMA
            && is_hex(&self.color)
            && matches!(self.theme.as_str(), "light" | "dark")
            && matches!(self.copy_format.as_str(), "HEX" | "RGB" | "HSL")
            && matches!(self.language.as_str(), "ar" | "en")
            && matches!(self.library_view.as_str(), "grid" | "list" | "deck")
            && SHORTCUTS.contains(&self.shortcut.as_str())
            && (1..=256).contains(&self.limit)
            && roles_valid(&self.brand_locks)
            && self.discoveries.len() <= MAX_DISCOVERIES
            && self.discoveries.iter().all(|d| {
                (1..=160).contains(&d.id.len())
                    && is_name(&d.name)
                    && is_timestamp(&d.created_at)
                    && roles_valid(&d.light)
                    && roles_valid(&d.dark)
            })
            && self.discoveries.iter().map(|d| d.id.as_str()).collect::<HashSet<_>>().len() == self.discoveries.len()
            && self.history.len() as i64 <= self.limit
            && self.history.iter().all(|h| is_hex(h))
            && (1..=MAX_PALETTES).contains(&self.palettes.len())
            && self.palettes.iter().all(|p| {
                roles_valid(&p.roles)
                    && p.collection.encode_utf16().count() <= 100
                    && is_name(&p.name)
                    && p.colors.len() <= MAX_COLORS
                    && p.colors.iter().all(|c| is_name(&c.name) && is_hex(&c.hex))
                    && ids_valid(p.colors.iter().map(|c| c.id.as_str()))
            })
            && ids_valid(self.palettes.iter().map(|p| p.id.as_str()))
            && self.gradients.len() <= MAX_GRADIENTS
            && self.gradients.iter().all(|g| {
                is_name(&g.name)
                    && matches!(g.kind.as_str(), "linear" | "radial")
                    && (0..=360).contains(&g.angle)
                    && (2..=32).contains(&g.stops.len())
                    && g.stops.iter().all(|s| is_hex(&s.hex) && (0..=100).contains(&s.pos))
            })
            && ids_valid(self.gradients.iter().map(|g| g.id.as_str()));
        if ok { Ok(()) } else { Err(StoreError::invalid_library()) }
    }

    /// The JSON written to disk: indented with two spaces, as 1.x writes it.
    pub fn to_json(&self) -> String {
        serde_json::to_string_pretty(self).expect("a workspace always serializes")
    }

    /// Adds a color to the front of the history, without duplicates, within the limit.
    pub fn remember(&mut self, hex: &str) {
        let hex = hex.to_ascii_uppercase();
        self.history.retain(|h| !h.eq_ignore_ascii_case(&hex));
        self.history.insert(0, hex);
        self.history.truncate(self.limit.max(1) as usize);
    }
}

/// Before 1.1 a new swatch was named after the nearest of 51 references by RGB distance. A swatch whose
/// name is still exactly what that gave its color was never renamed by its owner, so it takes its
/// perceptual name, in the same language. Names people typed stay.
pub fn refresh_automatic_names<'a>(swatches: impl Iterator<Item = &'a mut Swatch>) {
    for swatch in swatches {
        let Some(c) = parse_hex(&swatch.hex) else { continue };
        let (old_ar, old_en) = pickerhub::names::previous(c);
        let now = pickerhub::names::name(c);
        if swatch.name == old_ar {
            swatch.name = now.arabic.to_string();
        } else if swatch.name == old_en {
            swatch.name = now.english.to_string();
        }
    }
}
