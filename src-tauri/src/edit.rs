//! Changes to the library, as small named operations. The interface never sends a whole library:
//! it says what the user did, and Rust applies it, validates the result and records it for undo.
use pickerhub_store::model::{MAX_COLORS, MAX_DISCOVERIES, MAX_PALETTES, is_hex, roles_valid};
use pickerhub_store::{Discovery, Gradient, Palette, StoreError, Swatch, Workspace};
use serde::Deserialize;
use serde_json::Value;
use std::collections::BTreeMap;

/// Settings the interface may change, by their names in `workspace.json`.
/// `fullGlass` and `minimizeToTray` are new in 2.0; they are kept with the other unknown fields and
/// ignored by 1.x.
const SETTINGS: [&str; 18] = [
    "fullGlass",
    "minimizeToTray",
    "theme",
    "language",
    "reduceMotion",
    "reduceTransparency",
    "followSystemTheme",
    "autoCopy",
    "copyFormat",
    "hexUppercase",
    "hexPrefix",
    "globalShortcut",
    "quickPick",
    "shortcut",
    "libraryView",
    "useWaylandScreenSharing",
    "diagnosticsEnabled",
    "limit",
];

#[derive(Deserialize, Debug)]
#[serde(tag = "op", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum Op {
    SetColor { hex: String },
    Remember { hex: String },
    Forget { hex: String },
    ClearHistory,
    Setting { key: String, value: Value },
    AddPalette { name: String, colors: Vec<String> },
    RenamePalette { id: String, name: String },
    DeletePalette { id: String },
    SetCollection { id: String, collection: String },
    SetRoles { id: String, roles: BTreeMap<String, String> },
    AddColors { palette: String, hexes: Vec<String> },
    RenameColor { palette: String, id: String, name: String },
    DeleteColors { palette: String, ids: Vec<String> },
    Favorite { palette: String, id: String, value: bool },
    MoveColor { palette: String, id: String, to: usize },
    MoveColorsTo { from: String, to: String, ids: Vec<String> },
    Dedupe { palette: String },
    Import { palette: Palette, into: Option<String> },
    SaveGradient { gradient: Gradient },
    DeleteGradient { id: String },
    Archive { discovery: Discovery },
    BrandLock { role: String, hex: Option<String> },
}

impl Op {
    /// As in 1.x (`MainWindow.Edit`), every change is a step Undo can walk back through: choosing a
    /// color, a setting, a palette edit. Only the palette-of-the-day archive is kept out, because it
    /// records what the user saw, not something they did.
    pub fn recorded(&self) -> bool {
        !matches!(self, Op::Archive { .. })
    }
}

fn bad() -> StoreError {
    StoreError::invalid_library()
}
fn palette_mut<'a>(w: &'a mut Workspace, id: &str) -> Result<&'a mut Palette, StoreError> {
    w.palettes.iter_mut().find(|p| p.id == id).ok_or_else(bad)
}
fn upper(hex: &str) -> Result<String, StoreError> {
    if is_hex(hex) { Ok(hex.to_ascii_uppercase()) } else { Err(StoreError::invalid("invalid_hex", "أدخل ست خانات HEX.")) }
}
fn full(p: &Palette, adding: usize) -> Result<(), StoreError> {
    if p.colors.len() + adding > MAX_COLORS {
        return Err(StoreError::invalid("palette_full", "اللوحة ممتلئة."));
    }
    Ok(())
}

pub fn apply(w: &mut Workspace, op: Op) -> Result<(), StoreError> {
    match op {
        // 1.x `Select`: the chosen color also goes to the front of the history.
        Op::SetColor { hex } => {
            let hex = upper(&hex)?;
            w.remember(&hex);
            w.color = hex;
        }
        Op::Remember { hex } => w.remember(&upper(&hex)?),
        Op::Forget { hex } => w.history.retain(|h| !h.eq_ignore_ascii_case(&hex)),
        Op::ClearHistory => w.history.clear(),
        Op::Setting { key, value } => {
            if !SETTINGS.contains(&key.as_str()) {
                return Err(bad());
            }
            let mut v = serde_json::to_value(&*w).map_err(|_| bad())?;
            v[&key] = value;
            let mut next: Workspace = serde_json::from_value(v).map_err(|_| bad())?;
            if key == "limit" {
                next.history.truncate(next.limit.clamp(1, 256) as usize);
            }
            *w = next;
        }
        Op::AddPalette { name, colors } => {
            if w.palettes.len() >= MAX_PALETTES {
                return Err(StoreError::invalid("too_many_palettes", "وصلت إلى الحد الأقصى للوحات."));
            }
            let colors = colors.iter().map(|h| upper(h).map(|h| Swatch::from_hex(&h))).collect::<Result<Vec<_>, _>>()?;
            w.palettes.push(Palette { name: name.trim().to_string(), colors, ..Palette::default() });
        }
        Op::RenamePalette { id, name } => palette_mut(w, &id)?.name = name.trim().to_string(),
        Op::DeletePalette { id } => {
            if w.palettes.len() <= 1 {
                return Err(StoreError::invalid("last_palette", "لا يمكن حذف آخر لوحة."));
            }
            w.palettes.retain(|p| p.id != id);
        }
        Op::SetCollection { id, collection } => palette_mut(w, &id)?.collection = collection.trim().to_string(),
        Op::SetRoles { id, roles } => {
            if !roles_valid(&roles) {
                return Err(bad());
            }
            palette_mut(w, &id)?.roles = roles.into_iter().map(|(k, v)| (k, v.to_ascii_uppercase())).collect();
        }
        Op::AddColors { palette, hexes } => {
            let p = palette_mut(w, &palette)?;
            full(p, hexes.len())?;
            for h in hexes {
                p.colors.push(Swatch::from_hex(&upper(&h)?));
            }
        }
        Op::RenameColor { palette, id, name } => {
            palette_mut(w, &palette)?.colors.iter_mut().find(|c| c.id == id).ok_or_else(bad)?.name = name.trim().to_string()
        }
        Op::DeleteColors { palette, ids } => palette_mut(w, &palette)?.colors.retain(|c| !ids.contains(&c.id)),
        Op::Favorite { palette, id, value } => {
            palette_mut(w, &palette)?.colors.iter_mut().find(|c| c.id == id).ok_or_else(bad)?.favorite = value
        }
        Op::MoveColor { palette, id, to } => {
            let p = palette_mut(w, &palette)?;
            let from = p.colors.iter().position(|c| c.id == id).ok_or_else(bad)?;
            let c = p.colors.remove(from);
            p.colors.insert(to.min(p.colors.len()), c);
        }
        Op::MoveColorsTo { from, to, ids } => {
            if from == to {
                return Ok(());
            }
            let moving: Vec<Swatch> = palette_mut(w, &from)?.colors.iter().filter(|c| ids.contains(&c.id)).cloned().collect();
            let target = palette_mut(w, &to)?;
            full(target, moving.len())?;
            target.colors.extend(moving);
            palette_mut(w, &from)?.colors.retain(|c| !ids.contains(&c.id));
        }
        Op::Dedupe { palette } => {
            let mut seen = std::collections::HashSet::new();
            palette_mut(w, &palette)?.colors.retain(|c| seen.insert(c.hex.to_ascii_uppercase()));
        }
        Op::Import { palette, into } => match into {
            None => {
                if w.palettes.len() >= MAX_PALETTES {
                    return Err(StoreError::invalid("too_many_palettes", "وصلت إلى الحد الأقصى للوحات."));
                }
                w.palettes.push(Palette { id: pickerhub_store::model::new_id(), ..palette });
            }
            Some(id) => {
                let target = palette_mut(w, &id)?;
                let unique = pickerhub_store::export::unique_against(&palette, &target.colors);
                full(target, unique.len())?;
                target.colors.extend(unique.into_iter().map(|c| Swatch { id: pickerhub_store::model::new_id(), ..c }));
            }
        },
        Op::SaveGradient { gradient } => match w.gradients.iter_mut().find(|g| g.id == gradient.id) {
            Some(g) => *g = gradient,
            None => w.gradients.push(gradient),
        },
        Op::DeleteGradient { id } => w.gradients.retain(|g| g.id != id),
        Op::Archive { discovery } => {
            if !w.discoveries.iter().any(|d| d.id == discovery.id) {
                w.discoveries.insert(0, discovery);
                w.discoveries.truncate(MAX_DISCOVERIES);
            }
        }
        Op::BrandLock { role, hex } => match hex {
            Some(h) => {
                w.brand_locks.insert(role, upper(&h)?);
            }
            None => {
                w.brand_locks.remove(&role);
            }
        },
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn operations_keep_the_library_valid() {
        let mut w = Workspace::default();
        let id = w.palettes[0].id.clone();
        apply(&mut w, Op::AddColors { palette: id.clone(), hexes: vec!["#ff0000".into(), "#FF0000".into(), "#00ff00".into()] }).unwrap();
        assert_eq!(w.palettes[0].colors[0].hex, "#FF0000");
        assert_eq!(w.palettes[0].colors[0].name, "أحمر");
        apply(&mut w, Op::Dedupe { palette: id.clone() }).unwrap();
        assert_eq!(w.palettes[0].colors.len(), 2);
        let second = w.palettes[0].colors[1].id.clone();
        apply(&mut w, Op::MoveColor { palette: id.clone(), id: second, to: 0 }).unwrap();
        assert_eq!(w.palettes[0].colors[0].hex, "#00FF00");
        apply(&mut w, Op::Setting { key: "theme".into(), value: "light".into() }).unwrap();
        assert_eq!(w.theme, "light");
        assert!(apply(&mut w, Op::Setting { key: "version".into(), value: 9.into() }).is_err());
        assert!(apply(&mut w, Op::DeletePalette { id: id.clone() }).is_err());
        apply(&mut w, Op::Setting { key: "limit".into(), value: 1.into() }).unwrap();
        apply(&mut w, Op::Remember { hex: "#123456".into() }).unwrap();
        apply(&mut w, Op::Remember { hex: "#654321".into() }).unwrap();
        assert_eq!(w.history, ["#654321"]);
        w.validate().unwrap();
    }
}
