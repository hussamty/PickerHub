//! Color names, decided perceptually and in one place for the whole application.
//!
//! A name is chosen in OKLCH (lightness, chroma, hue), never by raw RGB distance:
//!
//! 1. Neutral or not. A color is gray only when its chroma is genuinely low: under 0.02 through the
//!    middle lightnesses, rising toward black and white, where a faint tint can no longer be seen.
//! 2. Tinted grays. Between that and 0.045 (0.032 for warm hues, where browns read as color sooner) a
//!    color is a gray with a visible tint, and is named for both: "Purple gray", "Taupe", "Slate gray".
//! 3. Hue families. Above that, hue, lightness and chroma place the color in one of eleven families.
//!    Purple and magenta share hues, so lightness and chroma separate them; brown is a dark, moderate
//!    orange, and a dark, low-chroma red. The name is then the nearest named reference (by OKLab
//!    distance) within that family, or within the neighbouring family when the color lies within a few
//!    degrees, or a few hundredths, of a boundary, so a borderline color takes whichever name is truly closer.
//!
//! Every reference is classified by the same rules, so a reference color is always named after itself.
use crate::color::Color;
use std::sync::OnceLock;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Family {
    Neutral,
    Red,
    Pink,
    Orange,
    Brown,
    Yellow,
    Green,
    Teal,
    Blue,
    Violet,
    Purple,
    Magenta,
}
impl Family {
    pub fn arabic(self) -> &'static str {
        match self {
            Family::Neutral => "محايد",
            Family::Red => "أحمر",
            Family::Pink => "وردي",
            Family::Orange => "برتقالي",
            Family::Brown => "بني",
            Family::Yellow => "أصفر",
            Family::Green => "أخضر",
            Family::Teal => "فيروزي",
            Family::Blue => "أزرق",
            Family::Violet => "بنفسجي",
            Family::Purple => "أرجواني",
            Family::Magenta => "ماجنتا",
        }
    }
    pub fn english(self) -> &'static str {
        match self {
            Family::Neutral => "Neutral",
            Family::Red => "Red",
            Family::Pink => "Pink",
            Family::Orange => "Orange",
            Family::Brown => "Brown",
            Family::Yellow => "Yellow",
            Family::Green => "Green",
            Family::Teal => "Teal",
            Family::Blue => "Blue",
            Family::Violet => "Violet",
            Family::Purple => "Purple",
            Family::Magenta => "Magenta",
        }
    }
}
/// How much color a color has: none to speak of, a tint, or a full hue.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Tone {
    Neutral,
    Tinted,
    Chromatic,
}
#[derive(Clone, Copy, Debug)]
pub struct Name {
    pub arabic: &'static str,
    pub english: &'static str,
    /// The color the name stands for.
    pub reference: u32,
    pub family: Family,
    pub tone: Tone,
}

/// Named colors with a full hue. Their families come from the rules below, not from this list.
const REFS: &[(u32, &str, &str)] = &[
    (0xFF0000, "أحمر", "Red"),
    (0xDC143C, "قرمزي", "Crimson"),
    (0xB22222, "أحمر ناري", "Firebrick"),
    (0x8B0000, "أحمر داكن", "Dark red"),
    (0x800000, "عنابي", "Maroon"),
    (0x800020, "خمري", "Burgundy"),
    (0x722F37, "نبيذي", "Wine"),
    (0xFF6347, "أحمر طماطمي", "Tomato"),
    (0xCD5C5C, "أحمر هندي", "Indian red"),
    (0xE34234, "زنجفري", "Vermilion"),
    (0xA52A2A, "أحمر بني", "Auburn"),
    (0xFA8072, "سلموني", "Salmon"),
    (0xE9967A, "سلموني داكن", "Dark salmon"),
    (0xC41E3A, "أحمر كاردينالي", "Cardinal"),
    (0xFFC0CB, "وردي", "Pink"),
    (0xFFB6C1, "وردي فاتح", "Light pink"),
    (0xFF69B4, "وردي زاهٍ", "Hot pink"),
    (0xFF1493, "وردي عميق", "Deep pink"),
    (0xDB7093, "وردي معتّق", "Pale violet red"),
    (0xF4C2C2, "وردي طفولي", "Baby pink"),
    (0xE75480, "وردي داكن", "Dark pink"),
    (0xFFE4E1, "وردي ضبابي", "Misty rose"),
    (0xC08081, "وردي ترابي", "Old rose"),
    (0xF88379, "مرجاني وردي", "Coral pink"),
    (0xFF00FF, "فوشيا", "Fuchsia"),
    (0xCA1F7B, "ماجنتا", "Magenta"),
    (0xC71585, "أحمر بنفسجي", "Medium violet red"),
    (0xE30B5C, "توتي", "Raspberry"),
    (0x8B008B, "ماجنتا داكن", "Dark magenta"),
    (0xDA70D6, "أوركيدي", "Orchid"),
    (0xEE82EE, "بنفسجي فاتح", "Light violet"),
    (0x800080, "أرجواني", "Purple"),
    (0x663399, "أرجواني ملكي", "Royal purple"),
    (0x6A0DAD, "أرجواني عميق", "Deep purple"),
    (0x4B0082, "نيلي", "Indigo"),
    (0x301934, "أرجواني داكن", "Dark purple"),
    (0x614051, "باذنجاني", "Eggplant"),
    (0x8E4585, "برقوقي", "Plum"),
    (0xDDA0DD, "برقوقي فاتح", "Light plum"),
    (0x9932CC, "أوركيدي داكن", "Dark orchid"),
    (0xBA55D3, "أوركيدي متوسط", "Medium orchid"),
    (0x9400D3, "بنفسجي داكن", "Dark violet"),
    (0x825F87, "أرجواني غباري", "Dusty purple"),
    (0x7851A9, "أرجواني متوسط", "Medium purple"),
    (0xB19CD9, "أرجواني فاتح", "Light purple"),
    (0xC8A2C8, "ليلكي", "Lilac"),
    (0xE0B0FF, "موف", "Mauve"),
    (0x7F00FF, "بنفسجي", "Violet"),
    (0x8A2BE2, "بنفسجي مزرق", "Blue violet"),
    (0x5A4FCF, "سوسني", "Iris"),
    (0x483D8B, "نيلي داكن", "Dark indigo"),
    (0x9370DB, "بنفسجي متوسط", "Medium violet"),
    (0xB57EDC, "بنفسجي خزامي", "Lavender violet"),
    (0xCCCCFF, "بنفسجي باهت", "Periwinkle"),
    (0x6A5ACD, "بنفسجي أردوازي", "Slate violet"),
    (0x0000FF, "أزرق", "Blue"),
    (0x000080, "كحلي", "Navy"),
    (0x191970, "أزرق ليلي", "Midnight blue"),
    (0x00008B, "أزرق داكن", "Dark blue"),
    (0x4169E1, "أزرق ملكي", "Royal blue"),
    (0x1E90FF, "أزرق زاهٍ", "Dodger blue"),
    (0x6495ED, "أزرق قنطري", "Cornflower blue"),
    (0x4682B4, "أزرق فولاذي", "Steel blue"),
    (0x87CEEB, "أزرق سماوي", "Sky blue"),
    (0xADD8E6, "أزرق فاتح", "Light blue"),
    (0x0F52BA, "ياقوتي", "Sapphire"),
    (0x003153, "أزرق بروسي", "Prussian blue"),
    (0x1E3A8A, "أزرق حبري", "Ink blue"),
    (0x007FFF, "أزرق لازوردي", "Azure"),
    (0x008080, "أخضر مزرق", "Teal"),
    (0x40E0D0, "فيروزي", "Turquoise"),
    (0x00FFFF, "سماوي ساطع", "Cyan"),
    (0xB0E0E6, "أزرق ضبابي", "Powder blue"),
    (0x20B2AA, "أخضر بحري فاتح", "Light sea green"),
    (0x5F9EA0, "أزرق عسكري", "Cadet blue"),
    (0x00CED1, "فيروزي داكن", "Dark turquoise"),
    (0x005F50, "أخضر صنوبري", "Pine green"),
    (0x006D6F, "أزرق بترولي", "Petrol"),
    (0x7FFFD4, "زبرجدي", "Aquamarine"),
    (0x008000, "أخضر", "Green"),
    (0x00FF00, "أخضر ليموني", "Lime"),
    (0x228B22, "أخضر غابي", "Forest green"),
    (0x50C878, "زمردي", "Emerald"),
    (0x8FBC8F, "أخضر مريمي", "Sage"),
    (0x98FF98, "نعناعي", "Mint"),
    (0x006400, "أخضر داكن", "Dark green"),
    (0x9ACD32, "أخضر مصفر", "Yellow green"),
    (0x32CD32, "أخضر ليموني متوسط", "Lime green"),
    (0x2E8B57, "أخضر بحري", "Sea green"),
    (0x90EE90, "أخضر فاتح", "Light green"),
    (0x556B2F, "زيتوني داكن", "Dark olive green"),
    (0x6B8E23, "زيتوني عسكري", "Olive drab"),
    (0x7CFC00, "أخضر عشبي", "Lawn green"),
    (0x4C9A3F, "أخضر عشبي داكن", "Grass green"),
    (0xACE1AF, "أخضر سيلادوني", "Celadon"),
    (0xFFFF00, "أصفر", "Yellow"),
    (0xFFD700, "ذهبي", "Gold"),
    (0xF0E68C, "كاكي", "Khaki"),
    (0x808000, "زيتوني", "Olive"),
    (0xFFFACD, "أصفر ليموني فاتح", "Lemon chiffon"),
    (0xFFF44F, "ليموني", "Lemon"),
    (0xB8860B, "ذهبي معتّق", "Dark goldenrod"),
    (0xFFDB58, "أصفر خردلي", "Mustard"),
    (0xEEE8AA, "ذهبي باهت", "Pale goldenrod"),
    (0xFFF000, "أصفر ساطع", "Bright yellow"),
    (0xFFA500, "برتقالي", "Orange"),
    (0xFF8C00, "برتقالي داكن", "Dark orange"),
    (0xFF7F50, "مرجاني", "Coral"),
    (0xFFDAB9, "خوخي", "Peach"),
    (0xF4A460, "بني رملي", "Sandy brown"),
    (0xD2691E, "شوكولاتي", "Chocolate"),
    (0xE2725B, "تيراكوتا", "Terracotta"),
    (0xFFBF00, "كهرماني", "Amber"),
    (0xCC5500, "برتقالي محروق", "Burnt orange"),
    (0xFFE5B4, "خوخي فاتح", "Light peach"),
    (0xD2B48C, "رملي", "Tan"),
    (0xDAA520, "ذهبي داكن", "Goldenrod"),
    (0xCD7F32, "برونزي", "Bronze"),
    (0xF5DEB3, "قمحي", "Wheat"),
    (0x8B4513, "بني خشبي", "Saddle brown"),
    (0xA0522D, "بني صدئي", "Sienna"),
    (0x964B00, "بني", "Brown"),
    (0x6F4E37, "بني قهوة", "Coffee"),
    (0x5C4033, "بني داكن", "Dark brown"),
    (0x7B3F00, "شوكولاتي داكن", "Dark chocolate"),
    (0x80461B, "بني محمر", "Russet"),
    (0xC04000, "ماهوجني", "Mahogany"),
    (0x3D2B1F, "بني قاتم", "Bistre"),
];
/// Grays by OKLCH lightness: the upper bound of each band.
const NEUTRAL: &[(f64, u32, &str, &str)] = &[
    (0.18, 0x000000, "أسود", "Black"),
    (0.36, 0x333333, "فحمي", "Charcoal"),
    (0.52, 0x555555, "رمادي داكن", "Dark gray"),
    (0.70, 0x808080, "رمادي", "Gray"),
    (0.84, 0xC0C0C0, "فضي", "Silver"),
    (0.93, 0xDCDCDC, "رمادي فاتح", "Light gray"),
    (0.985, 0xF5F5F5, "أبيض دخاني", "White smoke"),
    (f64::INFINITY, 0xFFFFFF, "أبيض", "White"),
];
/// Tinted grays: dark, middle, light and very light, for each kind of tint. Each reference sits inside its
/// band of chroma, between the neutral and tint limits, so it is named after itself.
const TINTED: &[(&str, [(u32, &str, &str); 4])] = &[
    ("rose", [(0x442F32, "رمادي وردي داكن", "Dark rose gray"), (0x897073, "رمادي وردي", "Rose gray"), (0xD1B6B9, "رمادي وردي فاتح", "Light rose gray"), (0xFFE4E8, "وردي ضبابي", "Misty rose")]),
    ("warm", [(0x3F332A, "رمادي بني داكن", "Dark taupe"), (0x837468, "رمادي بني", "Taupe"), (0xCBBAAE, "رمادي بني فاتح", "Light taupe"), (0xFEEADB, "كتاني", "Linen")]),
    ("yellow", [(0x383628, "رمادي زيتوني داكن", "Dark olive gray"), (0x7A7866, "رمادي زيتوني", "Olive gray"), (0xC1BFAC, "بيج", "Beige"), (0xF3EFD8, "عاجي", "Ivory")]),
    ("green", [(0x2D3A2A, "رمادي مخضر داكن", "Dark green gray"), (0x6E7C6A, "رمادي مخضر", "Green gray"), (0xB3C3B0, "رمادي مخضر فاتح", "Light green gray"), (0xE4F4E0, "أبيض مخضر", "Honeydew")]),
    ("cool", [(0x263844, "فحمي مزرق", "Charcoal blue"), (0x677A89, "رمادي أردوازي", "Slate gray"), (0xACC1D1, "رمادي مزرق فاتح", "Light blue gray"), (0xD7F3FF, "أبيض مزرق", "Ghost white")]),
    ("purple", [(0x393143, "رمادي أرجواني داكن", "Dark purple gray"), (0x7C7387, "رمادي أرجواني", "Purple gray"), (0xC2B9CF, "رمادي خزامي", "Lavender gray"), (0xF3EAFF, "خزامي", "Lavender")]),
];
/// Hue bands in OKLCH degrees; the last one wraps past 360 back to 15.
const BANDS: &[(f64, f64, Family)] = &[
    (15., 42., Family::Red),
    (42., 85., Family::Orange),
    (85., 118., Family::Yellow),
    (118., 170., Family::Green),
    (170., 222., Family::Teal),
    (222., 280., Family::Blue),
    (280., 300., Family::Violet),
    (300., 345., Family::Purple),
    (345., 375., Family::Magenta),
];
/// Within this many degrees of a band edge, the neighbouring band's names are candidates too.
const HUE_MARGIN: f64 = 8.;

/// OKLCH: lightness 0..1, chroma from 0 (about 0.37 at most in sRGB), hue in degrees.
pub fn oklch(c: Color) -> (f64, f64, f64) {
    let [l, a, b] = c.oklab();
    let chroma = a.hypot(b);
    // Below 1e-7 the chroma is rounding error (pure grays), so the hue is defined as 0 rather than noise.
    let hue = if chroma < 1e-7 { 0. } else { b.atan2(a).to_degrees().rem_euclid(360.) };
    (l, chroma, hue)
}
/// Below this chroma a color is gray. Faint tints vanish near black and near white, so it rises there.
pub fn neutral_limit(l: f64) -> f64 {
    0.02 + 0.03 * ((0.25 - l) / 0.13).clamp(0., 1.) + 0.02 * ((l - 0.92) / 0.07).clamp(0., 1.)
}
/// Below this chroma a colored gray is still more gray than color. Warm hues read as color sooner.
/// Below this chroma a colored gray is named by its tint (purple gray, taupe) rather than by its hue.
/// Warm tints show at less chroma than cool ones. In the dark the limit falls with lightness, so there it
/// asks for the same saturation (chroma against lightness) rather than the same chroma: a chroma that is
/// faint on a light color is plain to see on a dark one, so a dark brown door or an eggplant is named
/// brown or purple, not a tinted gray. Only colors darker than about 0.36 (warm) or 0.39 are affected.
pub fn tint_limit(l: f64, h: f64) -> f64 {
    let (chroma, saturation): (f64, f64) = if (20. ..110.).contains(&h) { (0.032, 0.09) } else { (0.045, 0.115) };
    chroma.min(saturation * l)
}
fn band(h: f64) -> Family {
    let x = if h < 15. { h + 360. } else { h };
    BANDS.iter().find(|(from, to, _)| (*from..*to).contains(&x)).map(|b| b.2).unwrap_or(Family::Red)
}
/// The family of a color, from its lightness, chroma and hue.
pub fn family(l: f64, c: f64, h: f64) -> Family {
    if c < neutral_limit(l) {
        return Family::Neutral;
    }
    let reddish = h >= 330. || h < 42.;
    if reddish && l >= 0.74 && c < 0.17 {
        return Family::Pink;
    }
    // A dark, dull magenta reads as purple (eggplant, aubergine); one only faintly tinted stays a rose gray.
    if (h >= 330. || h < 15.) && l < 0.45 && c < 0.11 && c >= tint_limit(l, h) {
        return Family::Purple;
    }
    match band(h) {
        Family::Orange if l < 0.62 && c < 0.17 => Family::Brown,
        Family::Red if l < 0.56 && c < 0.09 => Family::Brown,
        Family::Purple if l >= 0.6 && c >= 0.16 => Family::Magenta,
        f => f,
    }
}
fn tint_kind(f: Family) -> &'static str {
    match f {
        Family::Red | Family::Pink | Family::Magenta => "rose",
        Family::Orange | Family::Brown => "warm",
        Family::Yellow => "yellow",
        Family::Green => "green",
        Family::Teal | Family::Blue => "cool",
        _ => "purple",
    }
}
fn level(l: f64) -> usize {
    if l < 0.42 { 0 } else if l < 0.70 { 1 } else if l < 0.90 { 2 } else { 3 }
}
struct Reference {
    rgb: u32,
    arabic: &'static str,
    english: &'static str,
    lab: [f64; 3],
    family: Family,
}
fn references() -> &'static [Reference] {
    static ALL: OnceLock<Vec<Reference>> = OnceLock::new();
    ALL.get_or_init(|| {
        REFS.iter().map(|&(rgb, arabic, english)| {
            let c = Color::unpack(rgb);
            let (l, ch, h) = oklch(c);
            Reference { rgb, arabic, english, lab: c.oklab(), family: family(l, ch, h) }
        }).collect()
    })
}
/// The families whose names a color may take: its own, and a neighbour's when it lies close to the edge between them.
fn candidates(l: f64, c: f64, h: f64, own: Family) -> Vec<Family> {
    let mut out = vec![own];
    let mut add = |f: Family| if !out.contains(&f) { out.push(f) };
    for &(from, _, _) in BANDS {
        let edge = from.rem_euclid(360.);
        if ((h - edge + 540.) % 360. - 180.).abs() < HUE_MARGIN {
            add(band((edge - 1.).rem_euclid(360.)));
            add(band((edge + 1.).rem_euclid(360.)));
        }
    }
    let near = |value: f64, edge: f64, by: f64| (value - edge).abs() < by;
    if matches!(own, Family::Brown | Family::Orange | Family::Red) && (near(l, 0.62, 0.04) || near(c, 0.17, 0.02) || near(l, 0.56, 0.04) || near(c, 0.09, 0.02)) {
        [Family::Brown, Family::Orange, Family::Red].into_iter().for_each(&mut add);
    }
    if matches!(own, Family::Pink | Family::Red | Family::Magenta | Family::Purple | Family::Orange) && (near(l, 0.74, 0.04) || near(c, 0.17, 0.02)) {
        add(Family::Pink);
    }
    if matches!(own, Family::Purple | Family::Magenta) && (near(l, 0.6, 0.04) || near(c, 0.16, 0.02)) {
        [Family::Purple, Family::Magenta].into_iter().for_each(&mut add);
    }
    out
}
/// The name of a color. Every part of the application names colors through this function.
pub fn name(color: Color) -> Name {
    let (l, c, h) = oklch(color);
    let own = family(l, c, h);
    if own == Family::Neutral {
        let &(_, rgb, arabic, english) = NEUTRAL.iter().find(|n| l < n.0).unwrap_or(&NEUTRAL[NEUTRAL.len() - 1]);
        return Name { arabic, english, reference: rgb, family: own, tone: Tone::Neutral };
    }
    if c < tint_limit(l, h) {
        let kind = tint_kind(own);
        let set = TINTED.iter().find(|t| t.0 == kind).map(|t| t.1).unwrap_or(TINTED[TINTED.len() - 1].1);
        let (rgb, arabic, english) = set[level(l)];
        return Name { arabic, english, reference: rgb, family: own, tone: Tone::Tinted };
    }
    let lab = color.oklab();
    let families = candidates(l, c, h, own);
    let distance = |r: &Reference| (r.lab[0] - lab[0]).powi(2) + (r.lab[1] - lab[1]).powi(2) + (r.lab[2] - lab[2]).powi(2);
    let best = references().iter()
        .filter(|r| families.contains(&r.family) && {
            let (rl, rc, rh) = oklch(Color::unpack(r.rgb));
            rc >= tint_limit(rl, rh)
        })
        .min_by(|a, b| distance(a).total_cmp(&distance(b)))
        .expect("every chromatic family has named references");
    Name { arabic: best.arabic, english: best.english, reference: best.rgb, family: own, tone: Tone::Chromatic }
}

/// The automatic names PickerHub 0.5 to 1.0 gave: the nearest of these 51 references by a weighted RGB
/// distance. It called a muted purple such as #7B5EA7 "Gray" and a dark brown "Charcoal". They are kept
/// only so that a saved swatch still carrying its old automatic name can be recognized and renamed;
/// nothing names a color with them any more.
const PREVIOUS: &[(u32, &str, &str)] = &[
    (0x000000, "أسود", "Black"),
    (0xFFFFFF, "أبيض", "White"),
    (0x808080, "رمادي", "Gray"),
    (0xC0C0C0, "فضي", "Silver"),
    (0x36454F, "فحمي", "Charcoal"),
    (0xF5F5F5, "أبيض دخاني", "White smoke"),
    (0xFFFFF0, "عاجي", "Ivory"),
    (0xF5F5DC, "بيج", "Beige"),
    (0xD2B48C, "رملي", "Tan"),
    (0x8B4513, "بني خشبي", "Saddle brown"),
    (0xA52A2A, "بني محمر", "Brown"),
    (0x800000, "عنابي", "Maroon"),
    (0xFF0000, "أحمر", "Red"),
    (0xDC143C, "قرمزي", "Crimson"),
    (0xFF6347, "أحمر طماطمي", "Tomato"),
    (0xFF7F50, "مرجاني", "Coral"),
    (0xFFA500, "برتقالي", "Orange"),
    (0xFFDAB9, "خوخي", "Peach"),
    (0xFFD700, "ذهبي", "Gold"),
    (0xFFFF00, "أصفر", "Yellow"),
    (0xF0E68C, "كاكي", "Khaki"),
    (0x808000, "زيتوني", "Olive"),
    (0x9ACD32, "أخضر مصفر", "Yellow green"),
    (0x00FF00, "أخضر ليموني", "Lime"),
    (0x008000, "أخضر", "Green"),
    (0x228B22, "أخضر غابي", "Forest green"),
    (0x50C878, "زمردي", "Emerald"),
    (0x8FBC8F, "أخضر مريمي", "Sage"),
    (0x98FF98, "نعناعي", "Mint"),
    (0x005F50, "أخضر صنوبرى", "Pine green"),
    (0x008080, "أخضر مزرق", "Teal"),
    (0x40E0D0, "فيروزي", "Turquoise"),
    (0x00FFFF, "سماوي ساطع", "Cyan"),
    (0x87CEEB, "أزرق سماوي", "Sky blue"),
    (0xB0E0E6, "أزرق ضبابي", "Powder blue"),
    (0x4682B4, "أزرق فولاذي", "Steel blue"),
    (0x6495ED, "أزرق قنطري", "Cornflower blue"),
    (0x1E90FF, "أزرق زاهٍ", "Dodger blue"),
    (0x4169E1, "أزرق ملكي", "Royal blue"),
    (0x0000FF, "أزرق", "Blue"),
    (0x000080, "كحلي", "Navy"),
    (0x4B0082, "نيلي", "Indigo"),
    (0x800080, "أرجواني", "Purple"),
    (0x8A2BE2, "بنفسجي", "Blue violet"),
    (0xC8A2C8, "ليلكي", "Lilac"),
    (0xE6E6FA, "خزامي", "Lavender"),
    (0xFF00FF, "فوشيا", "Fuchsia"),
    (0xFF69B4, "وردي زاهٍ", "Hot pink"),
    (0xFFC0CB, "وردي", "Pink"),
    (0xDB7093, "وردي معتّق", "Pale violet red"),
    (0xFA8072, "سلموني", "Salmon"),
];
/// The Arabic and English name PickerHub 0.5 to 1.0 gave this color automatically.
pub fn previous(c: Color) -> (&'static str, &'static str) {
    let near = PREVIOUS.iter().min_by_key(|e| {
        let n = Color::unpack(e.0);
        let (dr, dg, db) = (c.0 as i32 - n.0 as i32, c.1 as i32 - n.1 as i32, c.2 as i32 - n.2 as i32);
        let mean = (c.0 as i32 + n.0 as i32) / 2;
        (512 + mean) * dr * dr + 1024 * dg * dg + (767 - mean) * db * db
    });
    near.map_or(("", ""), |e| (e.1, e.2))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn named(hex: u32) -> Name {
        name(Color::unpack(hex))
    }
    #[test]
    fn every_reference_is_named_after_itself() {
        for &(rgb, arabic, english) in REFS {
            let n = named(rgb);
            assert_eq!((n.arabic, n.english), (arabic, english), "{rgb:06X} is named {} ({:?})", n.english, n.family);
        }
        for &(_, rgb, _, english) in NEUTRAL {
            assert_eq!(named(rgb).english, english);
        }
    }
    // The sample set: purple, violet, muted purple, dark purple, bluish purple, magenta, gray purple,
    // neutral gray, brown and blue, several of each.
    const PURPLES: &[u32] = &[0x800080, 0x7B5EA7, 0x6A0DAD, 0x663399, 0x9370DB, 0x8F00FF, 0x7F00FF, 0x8A2BE2, 0x9400D3];
    const MUTED_PURPLES: &[u32] = &[0x8E7CC3, 0x9C89B8, 0x866F91, 0x7D5A8C, 0xA08AB0];
    const DARK_PURPLES: &[u32] = &[0x301934, 0x3C1E5A, 0x2E1A47, 0x4B0082, 0x2A1B3D];
    const BLUISH_PURPLES: &[u32] = &[0x6A5ACD, 0x5D3FD3, 0x7B68EE, 0x483D8B, 0x5A4FCF];
    const MAGENTAS: &[u32] = &[0xFF00FF, 0xC0007A, 0xD6006F, 0xCA1F7B, 0xE040FB];
    const GRAY_PURPLES: &[u32] = &[0x8B8197, 0x7D7387, 0x9A8FA3, 0x6E6577, 0xA49BAD];
    const GRAYS: &[u32] = &[0x808080, 0x8A8A8A, 0x6E6E6E, 0xB5B5B5, 0x7F7F80, 0x404040, 0xD3D3D3, 0x7E8081, 0x787A7D];
    const BROWNS: &[u32] = &[0x8B4513, 0x6F4E37, 0x964B00, 0x5C4033, 0xA0522D, 0x7B3F00, 0x4E342E];
    const BLUES: &[u32] = &[0x0000FF, 0x1E3A8A, 0x4682B4, 0x2C5AA0, 0x4169E1, 0x87CEEB, 0x000080, 0x1E90FF];
    fn purple_word(n: &Name) -> bool {
        let english = n.english.to_lowercase();
        ["purple", "violet", "indigo", "iris", "plum", "orchid", "lavender", "mauve", "lilac"].iter().any(|w| english.contains(w))
    }
    #[test]
    fn dark_browns_and_eggplants_are_named_by_their_hue() {
        // The same chroma looks far more colorful on a dark color: these read as brown and purple.
        for hex in [0x3B2A20, 0x3C2A21, 0x2B1D14, 0x362419] {
            let n = named(hex);
            assert!(n.family == Family::Brown && n.tone == Tone::Chromatic, "{hex:06X} should be a brown, not {} ({:?})", n.english, n.tone);
            assert!(!n.arabic.contains("رمادي") && !n.english.to_lowercase().contains("taupe"), "{hex:06X} is named {} / {}", n.english, n.arabic);
        }
        let eggplant = named(0x2E1F2E);
        assert!(eggplant.family == Family::Purple && eggplant.tone == Tone::Chromatic, "an eggplant is a dark purple, not {}", eggplant.english);
        // Grayish darks keep their tint: taupe, umber, charcoal and a navy gray.
        for hex in [0x483C32, 0x635147, 0x36454F, 0x1F2A36, 0x393143] {
            assert_eq!(named(hex).tone, Tone::Tinted, "{hex:06X} is a tinted gray, named {}", named(hex).english);
        }
    }
    #[test]
    fn every_tinted_reference_is_named_after_itself() {
        for (kind, set) in TINTED {
            for &(rgb, arabic, english) in set {
                let n = named(rgb);
                assert!(n.tone == Tone::Tinted && n.english == english && n.arabic == arabic, "{kind} {rgb:06X} {english} is named {} ({:?})", n.english, n.tone);
            }
        }
    }
    #[test]
    fn previous_names_are_those_the_old_engine_gave() {
        // These are what saved swatches carry from 0.5 to 1.0, so they must not drift.
        assert_eq!(previous(Color::unpack(0x7B5EA7)), ("رمادي", "Gray"));
        assert_eq!(previous(Color::unpack(0x5C4033)), ("فحمي", "Charcoal"));
        assert_eq!(previous(Color::unpack(0x87CEEB)), ("أزرق سماوي", "Sky blue"));
        assert_eq!(previous(Color::unpack(0xFF0000)), ("أحمر", "Red"));
    }
    #[test]
    fn purples_are_never_gray() {
        for &hex in PURPLES.iter().chain(MUTED_PURPLES).chain(DARK_PURPLES).chain(BLUISH_PURPLES) {
            let n = named(hex);
            assert!(matches!(n.family, Family::Purple | Family::Violet), "{hex:06X} fell into {:?} as {}", n.family, n.english);
            assert!(n.tone == Tone::Chromatic && purple_word(&n) && !n.english.to_lowercase().contains("gray"), "{hex:06X} is named {}", n.english);
        }
        // The purple in the guide's test picture, which used to be called Gray.
        assert_eq!(named(0x7B5EA7).english, "Medium purple");
    }
    #[test]
    fn gray_purples_keep_their_tint_and_true_grays_stay_gray() {
        for &hex in GRAY_PURPLES {
            let n = named(hex);
            assert!(n.tone == Tone::Tinted && n.family == Family::Purple, "{hex:06X}: {:?} {:?}", n.tone, n.family);
            assert!(n.english == "Purple gray" || n.english == "Lavender gray", "{hex:06X} is named {}", n.english);
        }
        for &hex in GRAYS {
            let n = named(hex);
            assert_eq!(n.family, Family::Neutral, "{hex:06X} should be neutral, not {}", n.english);
            assert!(["Gray", "Dark gray", "Silver", "Light gray"].contains(&n.english), "{hex:06X} is named {}", n.english);
        }
    }
    #[test]
    fn magenta_brown_and_blue_keep_their_families() {
        for &hex in MAGENTAS {
            let n = named(hex);
            assert_eq!(n.family, Family::Magenta, "{hex:06X} is {:?} {}", n.family, n.english);
            assert!(["Fuchsia", "Magenta", "Medium violet red", "Raspberry"].contains(&n.english), "{hex:06X} is named {}", n.english);
        }
        for &hex in BROWNS {
            assert_eq!(named(hex).family, Family::Brown, "{hex:06X} is named {}", named(hex).english);
        }
        for &hex in BLUES {
            let n = named(hex);
            assert_eq!(n.family, Family::Blue, "{hex:06X} is named {}", n.english);
            assert!(!n.english.contains("Gray"));
        }
    }
    #[test]
    fn a_dimmed_purple_stays_purple_until_it_is_nearly_black() {
        // The same purple hue at every lightness from shadow to highlight, at a muted chroma.
        for step in 0..=60 {
            let l = 0.2 + step as f64 * 0.012;
            let c = Color::from_oklch(l, 0.07, 305.);
            let n = name(c);
            if l >= 0.26 {
                assert!(matches!(n.family, Family::Purple | Family::Violet), "L {l:.3} ({}) fell into {:?} as {}", c.hex(), n.family, n.english);
            }
        }
    }
    #[test]
    fn names_change_only_at_real_boundaries() {
        // Along a sweep of hue at fixed lightness and chroma, the family changes only between neighbours.
        let order = [Family::Red, Family::Orange, Family::Yellow, Family::Green, Family::Teal, Family::Blue, Family::Violet, Family::Purple, Family::Magenta];
        let mut last = None;
        for step in 0..360 {
            let f = family(0.55, 0.12, step as f64);
            let f = if f == Family::Brown { Family::Orange } else if f == Family::Pink { Family::Red } else { f };
            if let Some(prev) = last {
                if prev != f {
                    let (a, b) = (order.iter().position(|x| *x == prev).unwrap(), order.iter().position(|x| *x == f).unwrap());
                    assert!((a + 1) % order.len() == b || (b + 1) % order.len() == a, "{prev:?} jumped to {f:?} at {step} degrees");
                }
            }
            last = Some(f);
        }
    }
}
