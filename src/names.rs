use crate::color::Color;
type Entry = (u32, &'static str, &'static str);
const NAMES: &[Entry] = &[
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
// Red-mean weighted distance in sRGB. Exact reference colors have distance zero.
pub fn nearest(c: Color) -> Entry {
    *NAMES
        .iter()
        .min_by_key(|e| {
            let n = Color::unpack(e.0);
            let dr = c.0 as i32 - n.0 as i32;
            let dg = c.1 as i32 - n.1 as i32;
            let db = c.2 as i32 - n.2 as i32;
            let mean = (c.0 as i32 + n.0 as i32) / 2;
            (512 + mean) * dr * dr + 1024 * dg * dg + (767 - mean) * db * db
        })
        .unwrap()
}
