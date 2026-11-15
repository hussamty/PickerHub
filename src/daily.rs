//! Palette of the day: one nature-inspired palette per 24 hours of use, chosen and stored locally.
//!
//! Everything here is deterministic given the stored state. Time comes only from this module:
//! callers never pass a timestamp across the C boundary. See docs/daily-palette.md.
use crate::color::Color;
use serde_json::{Map, Value, json};
use std::path::{Path, PathBuf};
use std::sync::OnceLock;
use std::time::{Instant, SystemTime, UNIX_EPOCH};

pub const COOLDOWN: u64 = 86_400;
/// Small clock corrections (time sync, leap smearing) are not treated as a rollback.
pub const TOLERANCE: u64 = 120;
const SCHEMA: u64 = 1;
const FILE: &str = "daily_palette_state.json";
const RECENT: usize = 12;

pub struct Theme {
    pub id: &'static str,
    pub name: &'static str,
    pub english: &'static str,
    pub story: &'static str,
    pub moods: [&'static str; 2],
    pub primary: u32,
    pub accent: u32,
    /// Hue and saturation of the neutrals (background, surface, text).
    pub hue: f64,
    pub tint: f64,
}
const fn theme(
    id: &'static str,
    name: &'static str,
    english: &'static str,
    story: &'static str,
    moods: [&'static str; 2],
    primary: u32,
    accent: u32,
    hue: f64,
    tint: f64,
) -> Theme {
    Theme { id, name, english, story, moods, primary, accent, hue, tint }
}
#[rustfmt::skip]
pub const THEMES: &[Theme] = &[
    theme("forest-after-rain", "غابة بعد المطر", "Forest after rain", "أوراق مبتلّة وتربة داكنة يلمع فوقها ضوء أخضر صافٍ.", ["natural", "calm"], 0x2E7D5B, 0xD9A441, 150., 0.12),
    theme("desert-sunrise", "شروق الصحراء", "Desert sunrise", "رمال دافئة يلوّنها أول الضوء بالبرتقالي والوردي.", ["warm", "soft"], 0xC8693A, 0xE9B872, 28., 0.16),
    theme("ocean-depths", "أعماق المحيط", "Ocean depths", "أزرق كثيف تتخلله ومضة مرجانية بعيدة.", ["cool", "deep"], 0x1F5E8C, 0xFF7A59, 212., 0.18),
    theme("autumn-blooms", "زهور الخريف", "Autumn blooms", "بتلات نحاسية وأقحوان أصفر فوق أوراق جافة.", ["warm", "earthy"], 0xB5532E, 0xE3B23C, 22., 0.14),
    theme("glacier-peaks", "الجبال الجليدية", "Glacier peaks", "جليد أزرق باهت وسماء عالية باردة.", ["cool", "calm"], 0x4A8FB8, 0x9AD4D6, 205., 0.14),
    theme("sunset-shore", "شاطئ عند الغروب", "Shore at sunset", "رمل وردي وماء بنفسجي تحت شمس برتقالية.", ["warm", "vivid"], 0xD9603B, 0x7A5BA8, 18., 0.12),
    theme("clay-and-wildflowers", "تربة الطين والنباتات البرية", "Clay and wild plants", "طين محمّر تنبت منه أعشاب خضراء باهتة.", ["earthy", "natural"], 0xA65A3A, 0x7A9A5B, 20., 0.14),
    theme("olive-grove", "بستان الزيتون", "Olive grove", "فضّة الأوراق وخضرة الثمر في ظهيرة هادئة.", ["natural", "calm"], 0x6B7A3A, 0xC9B458, 75., 0.12),
    theme("lavender-field", "حقل الخزامى", "Lavender field", "صفوف بنفسجية تمتد حتى أفق ذهبي.", ["soft", "calm"], 0x7B5EA7, 0xE0B354, 265., 0.12),
    theme("coral-reef", "الشعاب المرجانية", "Coral reef", "مرجان وردي وأسماك فيروزية في ماء صافٍ.", ["vivid", "cool"], 0xE0605A, 0x2BA6A0, 185., 0.16),
    theme("moss-and-stone", "طحلب وحجر", "Moss and stone", "حجر رمادي مبتلّ يكسوه طحلب مخملي.", ["natural", "deep"], 0x5E7F4F, 0xA8B0A2, 110., 0.08),
    theme("night-oasis", "واحة الليل", "Oasis at night", "نخيل داكن ونجوم فوق ماء ساكن.", ["deep", "cool"], 0x2F6F73, 0xE6C36A, 190., 0.20),
    theme("pomegranate-orchard", "بستان الرمّان", "Pomegranate orchard", "قشرة قرمزية وحبّات ياقوتية بين ورق أخضر.", ["vivid", "warm"], 0xB3263C, 0x4F8A5B, 350., 0.12),
    theme("morning-mist", "ضباب الصباح", "Morning mist", "تلال رمادية زرقاء يذوب عنها الضباب.", ["calm", "soft"], 0x6F8FA6, 0xD9A88C, 210., 0.08),
    theme("sandstone-canyon", "وادي الحجر الرملي", "Sandstone canyon", "طبقات حمراء وبرتقالية نحتتها الرياح.", ["warm", "earthy"], 0xC1502E, 0xE8A565, 16., 0.16),
    theme("palm-shade", "ظل النخيل", "Palm shade", "سعف أخضر عميق وتمر ذهبي.", ["natural", "warm"], 0x2F7A4D, 0xC98A2B, 140., 0.12),
    theme("saffron-dawn", "زعفران الفجر", "Saffron dawn", "خيوط زعفران حمراء فوق أرجوان الزهرة.", ["vivid", "warm"], 0xD4622A, 0x7D4E9E, 30., 0.14),
    theme("pine-and-snow", "صنوبر وثلج", "Pine and snow", "إبر خضراء داكنة تحت ثلج نظيف.", ["cool", "natural"], 0x2D6A5A, 0xB8D8E0, 165., 0.10),
    theme("wheat-field", "حقل القمح", "Wheat field", "سنابل ذهبية تتمايل تحت سماء زرقاء.", ["warm", "calm"], 0xC99A2E, 0x4E86B5, 45., 0.16),
    theme("volcanic-rock", "صخر بركاني", "Volcanic rock", "بازلت أسود تشقّه خطوط حمم برتقالية.", ["deep", "vivid"], 0xE0562B, 0x8C8F94, 15., 0.06),
    theme("turquoise-lagoon", "البحيرة الفيروزية", "Turquoise lagoon", "ماء فيروزي ضحل فوق رمل أبيض.", ["cool", "vivid"], 0x1FA3A3, 0xF2C879, 180., 0.16),
    theme("fig-tree", "شجرة التين", "Fig tree", "ثمار بنفسجية ناضجة بين أوراق عريضة.", ["deep", "natural"], 0x6E3F6B, 0x7FA05A, 310., 0.10),
    theme("jasmine-night", "ليل الياسمين", "Jasmine night", "زهر أبيض يضيء في عتمة خضراء.", ["soft", "deep"], 0x4F7F6A, 0xE3D3A0, 155., 0.12),
    theme("fired-clay", "فخّار الطين", "Fired clay", "جرار محروقة بلون القرميد ونقوش زرقاء.", ["earthy", "warm"], 0xB4512F, 0x2F6E8E, 14., 0.14),
    theme("spring-meadow", "مرج الربيع", "Spring meadow", "عشب فتيّ وزهور صفراء صغيرة.", ["vivid", "natural"], 0x4C9A3F, 0xF0C93A, 105., 0.14),
    theme("storm-sea", "بحر العاصفة", "Storm sea", "موج رصاصي وزبد أبيض تحت غيم ثقيل.", ["deep", "cool"], 0x3D5A73, 0x9FB4C2, 215., 0.14),
    theme("cedar-mountain", "جبل الأرز", "Cedar mountain", "خشب أرز محمّر وخضرة دائمة.", ["earthy", "natural"], 0x8A4B32, 0x3F7A57, 18., 0.12),
    theme("rose-garden", "حديقة الورد", "Rose garden", "ورد جوري وأوراق خضراء ندية.", ["soft", "vivid"], 0xC7446B, 0x5E9469, 340., 0.10),
    theme("amber-honey", "عسل وكهرمان", "Amber and honey", "عسل سائل يلتقط ضوء العصر.", ["warm", "soft"], 0xC9811F, 0x7A4A21, 35., 0.18),
    theme("northern-lights", "الشفق القطبي", "Northern lights", "ستائر خضراء وبنفسجية فوق ثلج داكن.", ["vivid", "deep"], 0x2FBF8F, 0x8A63D2, 230., 0.20),
    theme("salt-flats", "السبخة الملحية", "Salt flats", "أرض بيضاء متشققة وسماء وردية.", ["soft", "calm"], 0xB77A8C, 0x6FA3B5, 340., 0.06),
    theme("mangrove", "غابة القرم", "Mangrove", "جذور متشابكة في ماء أخضر عكر.", ["natural", "earthy"], 0x4E7A54, 0xB08A4C, 130., 0.14),
    theme("peacock-feather", "ريش الطاووس", "Peacock feather", "أزرق معدني وأخضر زمردي وعين ذهبية.", ["vivid", "cool"], 0x1E6FA8, 0x1FA37A, 200., 0.16),
    theme("dunes-at-night", "كثبان الليل", "Dunes at night", "رمل بارد أزرق تحت قمر فضي.", ["calm", "deep"], 0x5A6FA0, 0xD9C9A3, 228., 0.16),
    theme("citrus-grove", "بستان الحمضيات", "Citrus grove", "برتقال وليمون بين ورق لامع.", ["vivid", "warm"], 0xE07B1F, 0x5A9A3C, 40., 0.14),
    theme("pearl-shell", "صدف اللؤلؤ", "Pearl shell", "بياض قزحي بلمسات وردية وزرقاء.", ["soft", "cool"], 0x8FA3C9, 0xE0A7B5, 220., 0.08),
];
fn mood_label(tag: &str) -> &'static str {
    match tag {
        "calm" => "هادئ",
        "warm" => "دافئ",
        "natural" => "طبيعي",
        "cool" => "بارد",
        "vivid" => "حيوي",
        "deep" => "عميق",
        "soft" => "ناعم",
        _ => "ترابي",
    }
}
pub fn find(id: &str) -> Option<&'static Theme> {
    THEMES.iter().find(|t| t.id == id)
}

// ---- Variants -------------------------------------------------------------------------------

/// The seven roles of one variant, in display order.
pub struct Variant {
    pub background: Color,
    pub surface: Color,
    pub primary: Color,
    pub accent: Color,
    pub text: Color,
    pub muted: Color,
    pub border: Color,
}
/// Walk a color's lightness away from the ground until it reaches the wanted contrast.
fn fit(h: f64, s: f64, mut l: f64, ground: Color, wanted: f64, lighter: bool) -> Color {
    for _ in 0..100 {
        let c = Color::from_hsl(h, s, l);
        if c.ratio(ground) >= wanted {
            return c;
        }
        l = if lighter { l + 0.01 } else { l - 0.01 };
        if !(0.02..=0.98).contains(&l) {
            break;
        }
    }
    Color::from_hsl(h, s, l.clamp(0.02, 0.98))
}
/// Build the dark or light variant of a theme. Both keep the theme's hues: the neutrals carry
/// its tint, and primary and accent keep their hue while lightness moves to suit the ground.
/// `light` and `vivid` are the user's adjustments, in steps from -3 to 3.
pub fn variant(t: &Theme, dark: bool, light: i64, vivid: i64) -> Variant {
    let n = |s: f64, l: f64| Color::from_hsl(t.hue, (t.tint * s).min(0.4), l);
    let (ph, ps, pl, ..) = Color::unpack(t.primary).hs();
    let (ah, asat, al, ..) = Color::unpack(t.accent).hs();
    let sat = |s: f64| (s * (1. + 0.09 * vivid as f64)).clamp(0.05, 1.);
    let shift = 0.03 * light as f64;
    if dark {
        let background = n(0.9, 0.085);
        Variant {
            background,
            surface: n(0.8, 0.145),
            primary: fit(ph, sat(ps), (pl.max(0.56) + shift).clamp(0.3, 0.9), background, 4.5, true),
            accent: fit(ah, sat(asat), (al.max(0.6) + shift).clamp(0.3, 0.9), background, 3., true),
            text: n(0.5, 0.94),
            muted: fit(t.hue, t.tint * 0.5, 0.68, n(0.8, 0.145), 4.5, true),
            border: n(0.7, 0.27),
        }
    } else {
        let background = n(1.1, 0.965);
        Variant {
            background,
            surface: n(0.6, 0.995),
            primary: fit(ph, sat(ps), (pl.min(0.42) + shift).clamp(0.1, 0.7), background, 4.5, false),
            accent: fit(ah, sat(asat), (al.min(0.5) + shift).clamp(0.1, 0.7), background, 3., false),
            text: n(1.6, 0.13),
            muted: fit(t.hue, t.tint, 0.4, background, 4.5, false),
            border: n(1.0, 0.86),
        }
    }
}
impl Variant {
    /// Every pairing the palette is meant to be used in must be readable.
    pub fn accessible(&self) -> bool {
        self.text.ratio(self.background) >= 7.
            && self.text.ratio(self.surface) >= 7.
            && self.muted.ratio(self.background) >= 4.5
            && self.muted.ratio(self.surface) >= 4.5
            && self.primary.ratio(self.background) >= 4.5
            && self.primary.ink().ratio(self.primary) >= 4.5
            && self.accent.ratio(self.background) >= 3.
            && self.accent.ink().ratio(self.accent) >= 4.5
    }
    fn json(&self) -> Value {
        let role = |key: &str, label: &str, c: Color| {
            let d = c.describe();
            json!({"role":key,"label":label,"hex":c.hex(),"rgb":d["formats"]["RGB"],"hsl":d["formats"]["HSL"],"name":d["name"],"ink":c.ink().hex()})
        };
        json!([
            role("background", "الخلفية", self.background),
            role("surface", "السطح", self.surface),
            role("primary", "الأساسي", self.primary),
            role("accent", "التمييز", self.accent),
            role("text", "النص", self.text),
            role("muted", "النص الثانوي", self.muted),
            role("border", "الحدود", self.border),
        ])
    }
}

// ---- Time -----------------------------------------------------------------------------------

fn civil(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (yoe + era * 400 + i64::from(m <= 2), m, d)
}
/// Seconds since the Unix epoch as `2026-11-15T00:05:45Z`.
pub fn iso(t: u64) -> String {
    let (y, m, d) = civil((t / 86_400) as i64);
    let s = t % 86_400;
    format!("{y:04}-{m:02}-{d:02}T{:02}:{:02}:{:02}Z", s / 3600, s / 60 % 60, s % 60)
}
pub fn parse_iso(text: &str) -> Option<u64> {
    let b = text.as_bytes();
    if b.len() != 20 || b[4] != b'-' || b[7] != b'-' || b[10] != b'T' || b[13] != b':' || b[16] != b':' || b[19] != b'Z' {
        return None;
    }
    let n = |from: usize, to: usize| text.get(from..to)?.parse::<i64>().ok();
    let (y, m, d, h, mi, s) = (n(0, 4)?, n(5, 7)?, n(8, 10)?, n(11, 13)?, n(14, 16)?, n(17, 19)?);
    if !(1..=12).contains(&m) || !(1..=31).contains(&d) || h > 23 || mi > 59 || s > 59 {
        return None;
    }
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y.rem_euclid(400);
    let doy = (153 * (if m > 2 { m - 3 } else { m + 9 }) + 2) / 5 + d - 1;
    let days = era * 146_097 + yoe * 365 + yoe / 4 - yoe / 100 + doy - 719_468;
    u64::try_from(days * 86_400 + h * 3600 + mi * 60 + s).ok()
}
struct Clock {
    started: Instant,
    wall: u64,
}
static CLOCK: OnceLock<Clock> = OnceLock::new();
fn wall() -> Option<u64> {
    SystemTime::now().duration_since(UNIX_EPOCH).ok().map(|d| d.as_secs())
}
/// The current time, and whether the wall clock can be trusted.
///
/// While the process runs, a monotonic `Instant` gives a floor under the wall clock: setting the
/// system clock back cannot make time appear to stand still or go backwards, and is reported.
/// The wall clock may run ahead of the floor (a suspended laptop does this honestly).
pub fn now() -> (u64, bool) {
    let Some(current) = wall() else { return (0, false) };
    let clock = CLOCK.get_or_init(|| Clock { started: Instant::now(), wall: current });
    let floor = clock.wall + clock.started.elapsed().as_secs();
    (current.max(floor), current + TOLERANCE >= floor)
}

// ---- State ----------------------------------------------------------------------------------

fn fnv(seed: u64, bytes: &[u8]) -> u64 {
    bytes.iter().fold(seed ^ 0xcbf2_9ce4_8422_2325, |h, b| (h ^ u64::from(*b)).wrapping_mul(0x0000_0100_0000_01b3))
}
/// A stable random identifier for this installation, made without a randomness dependency.
fn new_id() -> String {
    use std::hash::{BuildHasher, Hasher};
    let part = || std::collections::hash_map::RandomState::new().build_hasher().finish();
    let t = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_nanos() as u64).unwrap_or(0);
    format!("{:016x}{:016x}", part() ^ t, part() ^ u64::from(std::process::id()))
}
/// A keyed checksum over the state. It catches casual edits of the file; it cannot stop
/// someone who reads this source or controls the machine, and does not claim to.
fn signature(state: &Value) -> String {
    let mut body = state.clone();
    if let Some(map) = body.as_object_mut() {
        map.remove("signature");
    }
    let text = body.to_string();
    let id = state["installation_id"].as_str().unwrap_or("");
    let a = fnv(fnv(0x5069_636b_6572_4875, id.as_bytes()), text.as_bytes());
    let b = fnv(a.rotate_left(29) ^ 0x6461_696c_795f_7061, text.as_bytes());
    format!("{a:016x}{b:016x}")
}
fn fresh_state() -> Value {
    json!({
        "schema_version": SCHEMA, "installation_id": new_id(), "usage_index": 0,
        "last_used_at_utc": null, "last_seen_at_utc": null, "next_available_at_utc": null,
        "current_palette_id": null, "current_palette_created_at_utc": null,
        "current_adjust": {"light": 0, "vivid": 0},
        "current_flags": {"saved": false, "applied": false, "dismissed": false},
        "history": [], "saved_palette_ids": [],
        "preferences": {"moods": {}, "light": 0, "vivid": 0}
    })
}
/// Bring an older file up to the current schema. A file from a newer build is left alone.
pub fn migrate(mut state: Value) -> Result<Value, &'static str> {
    let version = state["schema_version"].as_u64().unwrap_or(0);
    if version > SCHEMA {
        return Err("حُفظت حالة باقة اليوم بإصدار أحدث من PickerHub.");
    }
    if !state.is_object() || state["installation_id"].as_str().is_none_or(str::is_empty) {
        return Err("ملف حالة باقة اليوم غير صالح.");
    }
    // Version 0 predates usage_index, history, flags and adjustments: fill what is missing.
    let defaults = fresh_state();
    let map = state.as_object_mut().unwrap();
    for (key, value) in defaults.as_object().unwrap() {
        if key != "installation_id" && map.get(key).is_none_or(Value::is_null) && !value.is_null() {
            map.insert(key.clone(), value.clone());
        }
    }
    for key in ["last_used_at_utc", "last_seen_at_utc", "next_available_at_utc", "current_palette_id", "current_palette_created_at_utc"] {
        map.entry(key).or_insert(Value::Null);
    }
    map.insert("schema_version".into(), json!(SCHEMA));
    Ok(state)
}
pub struct Store {
    path: PathBuf,
}
pub enum Loaded {
    Missing,
    Good(Value),
    /// The file exists but cannot be trusted: unreadable, edited by hand, or damaged.
    Altered,
    Newer(&'static str),
}
impl Store {
    pub fn new(dir: &Path) -> Self {
        Self { path: dir.join(FILE) }
    }
    pub fn load(&self) -> Loaded {
        let Ok(text) = std::fs::read_to_string(&self.path) else {
            return if self.path.exists() { Loaded::Altered } else { Loaded::Missing };
        };
        let Ok(state) = serde_json::from_str::<Value>(&text) else { return Loaded::Altered };
        if state["signature"].as_str() != Some(signature(&state).as_str()) {
            return Loaded::Altered;
        }
        match migrate(state) {
            Ok(state) => Loaded::Good(state),
            Err(reason) if reason.contains("أحدث") => Loaded::Newer(reason),
            Err(_) => Loaded::Altered,
        }
    }
    /// Write to a temporary file, flush it to disk, then rename it over the old one. A crash at
    /// any point leaves either the old state or the new one, never a partial file.
    pub fn save(&self, state: &mut Value) -> std::io::Result<()> {
        use std::io::Write;
        let sign = signature(state);
        state["signature"] = json!(sign);
        if let Some(dir) = self.path.parent() {
            std::fs::create_dir_all(dir)?;
        }
        let temporary = self.path.with_extension(format!("json.{}.tmp", std::process::id()));
        let result = (|| {
            let mut file = std::fs::File::create(&temporary)?;
            file.write_all(serde_json::to_string_pretty(state)?.as_bytes())?;
            file.sync_all()?;
            std::fs::rename(&temporary, &self.path)
        })();
        if result.is_err() {
            let _ = std::fs::remove_file(&temporary);
        }
        result
    }
}

// ---- Selection ------------------------------------------------------------------------------

fn time(state: &Value, key: &str) -> Option<u64> {
    state[key].as_str().and_then(parse_iso)
}
fn mood_score(state: &Value, tag: &str) -> f64 {
    state["preferences"]["moods"][tag].as_f64().unwrap_or(0.).clamp(-6., 6.)
}
/// Choose the theme for the next reveal. The result depends only on the installation, the
/// number of reveals so far, the recent history and the mood scores.
///
/// Recently shown themes are excluded. Among the rest, mood scores pull toward what the user
/// saves and applies; a per-reveal jitter keeps equal candidates from always landing the same
/// way; and one reveal in five ignores the scores entirely, so preference never ends variety.
pub fn select(state: &Value) -> &'static Theme {
    let id = state["installation_id"].as_str().unwrap_or("");
    let index = state["usage_index"].as_u64().unwrap_or(0) + 1;
    let history: Vec<&str> = state["history"].as_array().map(|a| a.iter().filter_map(Value::as_str).collect()).unwrap_or_default();
    let recent = &history[history.len().saturating_sub(RECENT.min(THEMES.len() - 1))..];
    let seed = fnv(fnv(0, id.as_bytes()), &index.to_le_bytes());
    let explore = seed % 5 == 0;
    let mut best: Option<(f64, &Theme)> = None;
    for theme in THEMES.iter().filter(|t| !recent.contains(&t.id)) {
        let jitter = (fnv(seed, theme.id.as_bytes()) >> 11) as f64 / (1u64 << 53) as f64;
        let liking = if explore { 0. } else { theme.moods.iter().map(|m| mood_score(state, m)).sum() };
        let score = liking + 3. * jitter;
        if best.is_none_or(|(top, _)| score > top) {
            best = Some((score, theme));
        }
    }
    best.map(|(_, theme)| theme).unwrap_or(&THEMES[0])
}

// ---- Requests -------------------------------------------------------------------------------

fn palette(state: &Value) -> Value {
    let Some(theme) = state["current_palette_id"].as_str().and_then(find) else { return Value::Null };
    let light = state["current_adjust"]["light"].as_i64().unwrap_or(0).clamp(-3, 3);
    let vivid = state["current_adjust"]["vivid"].as_i64().unwrap_or(0).clamp(-3, 3);
    let (dark, pale) = (variant(theme, true, light, vivid), variant(theme, false, light, vivid));
    json!({
        "id": theme.id, "name": theme.name, "english": theme.english, "story": theme.story,
        "serial": state["usage_index"], "createdAt": state["current_palette_created_at_utc"],
        "moods": theme.moods.iter().map(|m| json!({"tag": m, "label": mood_label(m)})).collect::<Vec<_>>(),
        "dark": dark.json(), "light": pale.json(), "accessible": dark.accessible() && pale.accessible(),
        "adjust": {"light": light, "vivid": vivid}
    })
}
fn answer(state: &Value, now: u64, trusted: bool, altered: bool, fresh: bool) -> Value {
    let next = time(state, "next_available_at_utc");
    let seen = time(state, "last_seen_at_utc");
    // The wall clock is behind what this file has already seen: do not count that time twice.
    let behind = !trusted || seen.is_some_and(|seen| now + TOLERANCE < seen);
    let has = state["current_palette_id"].is_string();
    let remaining = next.map_or(0, |next| next.saturating_sub(now));
    let phase = if behind {
        "clock"
    } else if remaining == 0 {
        "sealed"
    } else if has {
        "revealed"
    } else {
        "waiting"
    };
    let retry = next.unwrap_or(0).max(seen.unwrap_or(0));
    json!({
        "state": phase, "fresh": fresh, "altered": altered, "remaining": remaining,
        "nextAt": next.map(iso), "retryAt": if behind { json!(iso(retry)) } else { Value::Null },
        "palette": palette(state), "flags": state["current_flags"], "serial": state["usage_index"]
    })
}
fn bump(state: &mut Value, theme: &Theme, by: f64) {
    for mood in theme.moods {
        let now = mood_score(state, mood);
        state["preferences"]["moods"][mood] = json!((now + by).clamp(-6., 6.));
    }
}
/// Apply one request to the stored state. `now` and `trusted` come from [`now`]; tests pass their own.
pub fn handle(store: &Store, action: &str, value: &str, now: u64, trusted: bool) -> Value {
    let (mut state, mut altered) = match store.load() {
        Loaded::Good(state) => (state, false),
        Loaded::Missing => (fresh_state(), false),
        Loaded::Newer(reason) => return json!({"error": reason}),
        // A file that fails its check earns no reveal: start over, locked for a full cooldown.
        Loaded::Altered => {
            let mut state = fresh_state();
            state["last_seen_at_utc"] = json!(iso(now));
            state["next_available_at_utc"] = json!(iso(now + COOLDOWN));
            if let Err(e) = store.save(&mut state) {
                return json!({"error": format!("تعذّر حفظ حالة باقة اليوم: {e}")});
            }
            (state, true)
        }
    };
    let seen = time(&state, "last_seen_at_utc");
    let behind = !trusted || seen.is_some_and(|seen| now + TOLERANCE < seen);
    let mut changed = false;
    let mut fresh = false;
    // Remember the latest time this file has been open at, at most once a minute.
    if !behind && seen.is_none_or(|seen| now >= seen + 60) {
        state["last_seen_at_utc"] = json!(iso(now));
        changed = true;
    }
    let current = state["current_palette_id"].as_str().and_then(find);
    match action {
        "status" => {}
        "reveal" => {
            let due = time(&state, "next_available_at_utc").is_none_or(|next| now >= next);
            if !behind && due {
                let theme = select(&state);
                let index = state["usage_index"].as_u64().unwrap_or(0) + 1;
                let half = |key: &str| state["preferences"][key].as_i64().unwrap_or(0).clamp(-3, 3) / 2;
                let adjust = json!({"light": half("light"), "vivid": half("vivid")});
                state["usage_index"] = json!(index);
                state["last_used_at_utc"] = json!(iso(now));
                state["last_seen_at_utc"] = json!(iso(now));
                state["next_available_at_utc"] = json!(iso(now + COOLDOWN));
                state["current_palette_id"] = json!(theme.id);
                state["current_palette_created_at_utc"] = json!(iso(now));
                state["current_adjust"] = adjust;
                state["current_flags"] = json!({"saved": false, "applied": false, "dismissed": false});
                if let Some(history) = state["history"].as_array_mut() {
                    history.push(json!(theme.id));
                    let extra = history.len().saturating_sub(THEMES.len());
                    history.drain(..extra);
                }
                (changed, fresh, altered) = (true, true, false);
            }
        }
        "save" | "apply" | "dismiss" => {
            if let Some(theme) = current {
                let (flag, weight) = match action {
                    "save" => ("saved", 2.),
                    "apply" => ("applied", 3.),
                    _ => ("dismissed", -2.),
                };
                // Each kind of feedback counts once per palette.
                if state["current_flags"][flag] != json!(true) {
                    state["current_flags"][flag] = json!(true);
                    bump(&mut state, theme, weight);
                    if action == "save" {
                        if let Some(saved) = state["saved_palette_ids"].as_array_mut() {
                            if !saved.contains(&json!(theme.id)) {
                                saved.push(json!(theme.id));
                            }
                        }
                    }
                    changed = true;
                }
            }
        }
        "adjust" => {
            let (key, step) = match value {
                "lighter" => ("light", 1),
                "darker" => ("light", -1),
                "vivid" => ("vivid", 1),
                "muted" => ("vivid", -1),
                _ => return json!({"error": "تعديل غير معروف."}),
            };
            if current.is_some() {
                for scope in ["current_adjust", "preferences"] {
                    let now = state[scope][key].as_i64().unwrap_or(0);
                    state[scope][key] = json!((now + step).clamp(-3, 3));
                }
                changed = true;
            }
        }
        _ => return json!({"error": "طلب غير معروف."}),
    }
    if changed {
        if let Err(e) = store.save(&mut state) {
            return json!({"error": format!("تعذّر حفظ حالة باقة اليوم: {e}")});
        }
    }
    answer(&state, now, trusted, altered, fresh)
}
/// Entry point for the C boundary: a JSON request `{dir, action, value}`.
pub fn request(text: &str) -> Value {
    let Ok(Value::Object(request)) = serde_json::from_str::<Value>(text) else { return json!({"error": "طلب غير صالح."}) };
    let field = |map: &Map<String, Value>, key: &str| map.get(key).and_then(Value::as_str).unwrap_or("").to_owned();
    let dir = field(&request, "dir");
    if dir.is_empty() {
        return json!({"error": "مجلد البيانات غير محدد."});
    }
    let (now, trusted) = now();
    handle(&Store::new(Path::new(&dir)), &field(&request, "action"), &field(&request, "value"), now, trusted)
}

#[cfg(test)]
mod tests {
    use super::*;
    const T0: u64 = 1_794_700_000;
    struct Folder(PathBuf);
    impl Folder {
        fn new(name: &str) -> Self {
            let dir = std::env::temp_dir().join(format!("pickerhub-daily-{name}-{}", std::process::id()));
            let _ = std::fs::remove_dir_all(&dir);
            std::fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
        fn store(&self) -> Store {
            Store::new(&self.0)
        }
        fn raw(&self) -> Value {
            serde_json::from_str(&std::fs::read_to_string(self.0.join(FILE)).unwrap()).unwrap()
        }
    }
    impl Drop for Folder {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }
    fn id(answer: &Value) -> String {
        answer["palette"]["id"].as_str().unwrap_or("").to_owned()
    }

    #[test]
    fn catalog_is_complete_and_accessible() {
        assert!((30..=60).contains(&THEMES.len()));
        let mut ids: Vec<_> = THEMES.iter().map(|t| t.id).collect();
        ids.sort_unstable();
        ids.dedup();
        assert_eq!(ids.len(), THEMES.len(), "theme ids must be unique");
        for theme in THEMES {
            for light in -3..=3 {
                for vivid in -3..=3 {
                    for dark in [true, false] {
                        assert!(variant(theme, dark, light, vivid).accessible(), "{} dark={dark} light={light} vivid={vivid}", theme.id);
                    }
                }
            }
        }
    }
    #[test]
    fn variants_keep_the_theme_identity() {
        for theme in THEMES {
            let (dark, light) = (variant(theme, true, 0, 0), variant(theme, false, 0, 0));
            let hue = |c: Color| c.hs().0;
            let apart = |a: f64, b: f64| ((a - b + 540.) % 360. - 180.).abs();
            assert!(apart(hue(dark.primary), hue(light.primary)) < 8., "{}", theme.id);
            assert!(apart(hue(dark.accent), hue(light.accent)) < 8., "{}", theme.id);
            // A real pair of variants, not an inversion: the dark ground is dark and the light one light.
            assert!(dark.background.luminance() < 0.05 && light.background.luminance() > 0.8);
        }
    }
    #[test]
    fn iso_round_trip() {
        assert_eq!(iso(0), "1970-01-01T00:00:00Z");
        assert_eq!(iso(1_794_700_800), "2026-11-15T00:00:00Z");
        for t in [0, 951_782_400, 1_709_251_199, T0, 4_102_444_799] {
            assert_eq!(parse_iso(&iso(t)), Some(t));
        }
        assert_eq!(parse_iso("2026-13-01T00:00:00Z"), None);
        assert_eq!(parse_iso("not a time"), None);
    }
    #[test]
    fn first_reveal_creates_and_records() {
        let f = Folder::new("first");
        let before = handle(&f.store(), "status", "", T0, true);
        assert_eq!(before["state"], "sealed");
        assert!(before["palette"].is_null());
        // Looking at the screen is not a use.
        assert!(!f.0.join(FILE).exists() || f.raw()["last_used_at_utc"].is_null());
        let opened = handle(&f.store(), "reveal", "", T0, true);
        assert_eq!(opened["state"], "revealed");
        assert_eq!(opened["fresh"], true);
        assert_eq!(opened["remaining"], COOLDOWN);
        let raw = f.raw();
        assert_eq!(raw["last_used_at_utc"], iso(T0));
        assert_eq!(raw["next_available_at_utc"], iso(T0 + COOLDOWN));
        assert_eq!(raw["current_palette_id"], opened["palette"]["id"]);
    }
    #[test]
    fn same_palette_until_a_full_day_has_passed() {
        let f = Folder::new("cooldown");
        let first = id(&handle(&f.store(), "reveal", "", T0, true));
        for later in [1, 3600, COOLDOWN - 1] {
            // A new Store each time stands for restarting the application.
            let again = handle(&f.store(), "reveal", "", T0 + later, true);
            assert_eq!(id(&again), first);
            assert_eq!(again["fresh"], false);
            assert_eq!(again["remaining"], COOLDOWN - later);
            assert_eq!(f.raw()["usage_index"], 1);
        }
        assert_eq!(handle(&f.store(), "status", "", T0 + COOLDOWN, true)["state"], "sealed");
        let second = handle(&f.store(), "reveal", "", T0 + COOLDOWN, true);
        assert_eq!(second["fresh"], true);
        assert_ne!(id(&second), first);
        assert_eq!(f.raw()["usage_index"], 2);
        assert_eq!(f.raw()["last_used_at_utc"], iso(T0 + COOLDOWN));
    }
    #[test]
    fn previous_palette_stays_available_when_a_new_one_is_due() {
        let f = Folder::new("previous");
        let first = id(&handle(&f.store(), "reveal", "", T0, true));
        let due = handle(&f.store(), "status", "", T0 + COOLDOWN + 5, true);
        assert_eq!(due["state"], "sealed");
        assert_eq!(id(&due), first);
        // Saving it does not spend the new day's reveal.
        handle(&f.store(), "save", "", T0 + COOLDOWN + 6, true);
        assert_eq!(handle(&f.store(), "status", "", T0 + COOLDOWN + 7, true)["state"], "sealed");
        assert_eq!(f.raw()["usage_index"], 1);
    }
    #[test]
    fn clock_set_back_blocks_the_reveal() {
        let f = Folder::new("rollback");
        let first = id(&handle(&f.store(), "reveal", "", T0, true));
        handle(&f.store(), "status", "", T0 + 7200, true);
        let back = handle(&f.store(), "reveal", "", T0 - 3600, true);
        assert_eq!(back["state"], "clock");
        assert_eq!(back["fresh"], false);
        assert_eq!(id(&back), first);
        assert_eq!(back["retryAt"], iso(T0 + COOLDOWN));
        assert_eq!(f.raw()["last_seen_at_utc"], iso(T0 + 7200), "a clock set back must not lower the high-water mark");
        // The running process saw the wall clock fall behind its own monotonic count.
        assert_eq!(handle(&f.store(), "reveal", "", T0 + COOLDOWN + 1, false)["state"], "clock");
        assert_eq!(f.raw()["usage_index"], 1);
    }
    #[test]
    fn clock_set_forward_cannot_be_repeated() {
        let f = Folder::new("forward");
        handle(&f.store(), "reveal", "", T0, true);
        // Jumping a year ahead does open a pack: a local clock is all there is to go by.
        let jumped = handle(&f.store(), "reveal", "", T0 + 365 * COOLDOWN, true);
        assert_eq!(jumped["fresh"], true);
        // Returning to the real time is then a rollback, and stays one until the faked time is reached.
        for real in [T0 + 60, T0 + COOLDOWN, T0 + 30 * COOLDOWN] {
            let back = handle(&f.store(), "reveal", "", real, true);
            assert_eq!(back["state"], "clock");
            assert_eq!(back["retryAt"], iso(T0 + 366 * COOLDOWN));
        }
        assert_eq!(f.raw()["usage_index"], 2);
    }
    #[test]
    fn selection_is_deterministic_and_does_not_repeat_soon() {
        let mut state = fresh_state();
        state["installation_id"] = json!("fixed-installation");
        let mut seen = Vec::new();
        for index in 0..THEMES.len() {
            state["usage_index"] = json!(index);
            let (a, b) = (select(&state), select(&state.clone()));
            assert_eq!(a.id, b.id);
            let recent = &seen[seen.len().saturating_sub(RECENT)..];
            assert!(!recent.contains(&a.id), "{} repeated within {RECENT} reveals", a.id);
            seen.push(a.id);
            state["history"].as_array_mut().unwrap().push(json!(a.id));
        }
        let mut other = fresh_state();
        other["installation_id"] = json!("another-installation");
        let first: Vec<_> = (0..8).map(|i| { other["usage_index"] = json!(i); select(&other).id }).collect();
        let mut base = fresh_state();
        base["installation_id"] = json!("fixed-installation");
        let second: Vec<_> = (0..8).map(|i| { base["usage_index"] = json!(i); select(&base).id }).collect();
        assert_ne!(first, second, "installations should not all see the same order");
    }
    #[test]
    fn preferences_steer_without_ending_variety() {
        let mut state = fresh_state();
        state["installation_id"] = json!("taste");
        state["preferences"]["moods"] = json!({"calm": 6.0, "vivid": -6.0});
        let (mut calm, mut vivid, mut ids) = (0, 0, std::collections::BTreeSet::new());
        for index in 0..200 {
            state["usage_index"] = json!(index);
            let theme = select(&state);
            calm += usize::from(theme.moods.contains(&"calm"));
            vivid += usize::from(theme.moods.contains(&"vivid"));
            ids.insert(theme.id);
        }
        let share = THEMES.iter().filter(|t| t.moods.contains(&"calm")).count() as f64 / THEMES.len() as f64;
        assert!(calm as f64 / 200. > share * 2., "liked moods should come up more often: {calm}");
        assert!(vivid > 0, "disliked moods must still appear sometimes");
        assert!(ids.len() >= 12, "variety collapsed to {} themes", ids.len());
    }
    #[test]
    fn feedback_scores_once_per_palette() {
        let f = Folder::new("feedback");
        let opened = handle(&f.store(), "reveal", "", T0, true);
        let moods: Vec<String> = opened["palette"]["moods"].as_array().unwrap().iter().map(|m| m["tag"].as_str().unwrap().to_owned()).collect();
        handle(&f.store(), "save", "", T0 + 1, true);
        handle(&f.store(), "save", "", T0 + 2, true);
        let applied = handle(&f.store(), "apply", "", T0 + 3, true);
        assert_eq!(applied["flags"]["saved"], true);
        assert_eq!(applied["flags"]["applied"], true);
        let raw = f.raw();
        for mood in &moods {
            assert_eq!(raw["preferences"]["moods"][mood], 5.0, "save 2 + apply 3, each once");
        }
        assert_eq!(raw["saved_palette_ids"], json!([opened["palette"]["id"]]));
        handle(&f.store(), "dismiss", "", T0 + 4, true);
        assert_eq!(f.raw()["preferences"]["moods"][&moods[0]], 3.0);
        // Feedback never consumes or replaces the palette.
        assert_eq!(id(&handle(&f.store(), "status", "", T0 + 5, true)), id(&opened));
    }
    #[test]
    fn adjustments_reshape_the_palette_and_are_remembered() {
        let f = Folder::new("adjust");
        let opened = handle(&f.store(), "reveal", "", T0, true);
        let lighter = handle(&f.store(), "adjust", "lighter", T0 + 1, true);
        assert_eq!(id(&lighter), id(&opened));
        assert_ne!(lighter["palette"]["dark"][2]["hex"], opened["palette"]["dark"][2]["hex"]);
        for _ in 0..6 {
            handle(&f.store(), "adjust", "lighter", T0 + 2, true);
        }
        assert_eq!(f.raw()["current_adjust"]["light"], 3);
        assert_eq!(f.raw()["preferences"]["light"], 3);
        assert_eq!(handle(&f.store(), "adjust", "sideways", T0 + 3, true)["error"].is_string(), true);
        // The next palette starts from half of the learned direction.
        let next = handle(&f.store(), "reveal", "", T0 + COOLDOWN, true);
        assert_eq!(next["palette"]["adjust"]["light"], 1);
    }
    #[test]
    fn hand_edited_state_earns_nothing() {
        let f = Folder::new("edited");
        handle(&f.store(), "reveal", "", T0, true);
        let path = f.0.join(FILE);
        let edited = std::fs::read_to_string(&path).unwrap().replace(&iso(T0 + COOLDOWN), &iso(T0));
        std::fs::write(&path, edited).unwrap();
        let after = handle(&f.store(), "reveal", "", T0 + 10, true);
        assert_eq!(after["altered"], true);
        assert_eq!(after["state"], "waiting");
        assert!(after["palette"].is_null());
        assert_eq!(after["remaining"], COOLDOWN);
        std::fs::write(&path, "{ not json").unwrap();
        assert_eq!(handle(&f.store(), "reveal", "", T0 + 20, true)["altered"], true);
    }
    #[test]
    fn saves_are_atomic() {
        let f = Folder::new("atomic");
        handle(&f.store(), "reveal", "", T0, true);
        let good = std::fs::read_to_string(f.0.join(FILE)).unwrap();
        // A crash mid-write leaves a temporary file beside the real one: it must be ignored.
        std::fs::write(f.0.join("daily_palette_state.json.999.tmp"), "{\"half\":").unwrap();
        let again = handle(&f.store(), "status", "", T0 + 5, true);
        assert_eq!(again["state"], "revealed");
        assert_eq!(std::fs::read_to_string(f.0.join(FILE)).unwrap(), good);
        handle(&f.store(), "save", "", T0 + 90, true);
        let leftovers: Vec<_> = std::fs::read_dir(&f.0).unwrap().filter_map(Result::ok).map(|e| e.file_name().into_string().unwrap())
            .filter(|n| n.ends_with(".tmp") && !n.contains(".999.")).collect();
        assert!(leftovers.is_empty(), "{leftovers:?}");
        assert!(matches!(f.store().load(), Loaded::Good(_)));
    }
    #[test]
    fn migrates_old_state_and_refuses_newer() {
        let old = json!({"installation_id": "abc", "last_used_at_utc": iso(T0), "next_available_at_utc": iso(T0 + COOLDOWN), "current_palette_id": "olive-grove"});
        let state = migrate(old).unwrap();
        assert_eq!(state["schema_version"], SCHEMA);
        assert_eq!(state["installation_id"], "abc");
        assert_eq!(state["current_palette_id"], "olive-grove");
        assert_eq!(state["usage_index"], 0);
        assert_eq!(state["current_flags"]["saved"], false);
        assert!(state["history"].is_array());
        assert!(migrate(json!({"schema_version": SCHEMA + 1, "installation_id": "abc"})).is_err());
        assert!(migrate(json!({"schema_version": 1})).is_err());
        let f = Folder::new("newer");
        let mut future = fresh_state();
        future["schema_version"] = json!(SCHEMA + 1);
        f.store().save(&mut future).unwrap();
        assert!(handle(&f.store(), "reveal", "", T0, true)["error"].is_string());
        assert_eq!(f.raw()["schema_version"], SCHEMA + 1, "a newer file must not be overwritten");
    }
    #[test]
    fn running_clock_never_goes_backwards() {
        let (first, trusted) = now();
        assert!(trusted && first > 1_700_000_000);
        let (second, _) = now();
        assert!(second >= first);
    }
}
