//! PickerHub core and a native C ABI. See docs/architecture.md.
pub mod color;
pub mod daily;
mod names;
use color::Color;
use serde_json::{Value, json};
use std::cell::RefCell;
use std::collections::BTreeMap;

thread_local! {
    static OUT: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) };
    static PIXELS: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) };
}
fn output(value: Value) -> usize {
    OUT.with_borrow_mut(|out| {
        *out = value.to_string().into_bytes();
        out.as_ptr() as usize
    })
}
#[unsafe(no_mangle)]
pub extern "C" fn output_len() -> usize {
    OUT.with_borrow(|out| out.len())
}
#[unsafe(no_mangle)]
pub extern "C" fn describe(rgb: u32) -> usize {
    output(Color::unpack(rgb).describe())
}
#[unsafe(no_mangle)]
pub extern "C" fn harmony(rgb: u32, kind: u32) -> usize {
    output(json!(Color::unpack(rgb).harmonies(kind)))
}
#[unsafe(no_mangle)]
pub extern "C" fn contrast(fg: u32, bg: u32) -> usize {
    let f = Color::unpack(fg);
    let b = Color::unpack(bg);
    let ratio = f.ratio(b);
    output(
        json!({"ratio":ratio,"aa":ratio>=4.5,"large":ratio>=3.,"aaa":ratio>=7.,"aaaLarge":ratio>=4.5,"suggestion":f.suggestion(b).hex()}),
    )
}
#[unsafe(no_mangle)]
pub extern "C" fn pixel_buffer(bytes: u32) -> usize {
    if bytes == 0 || bytes > 4 * 1024 * 1024 {
        return 0;
    }
    PIXELS.with_borrow_mut(|buf| {
        buf.resize(bytes as usize, 0);
        buf.as_mut_ptr() as usize
    })
}
/// The colors of an image, as a person would list them.
///
/// Counting the most common shades alone fills the list with near-copies of the background and
/// loses a small bright detail. So pixels are first counted in fine bins, the bins are grouped
/// into colors that look alike (by OKLab distance), and the list is then chosen for both size and
/// difference: each pick lowers the standing of the colors close to it. Specks under 0.08% of the
/// image are treated as noise. Percentages are shares of the visible (opacity-weighted) image.
pub fn dominant(rgba: &[u8], count: usize) -> Vec<Value> {
    const ALIKE: f64 = 0.055;
    const SAME: f64 = 0.08;
    const NOISE: f64 = 0.0008;
    struct Group {
        sum: [u64; 3],
        weight: u64,
        lab: [f64; 3],
    }
    impl Group {
        fn color(&self) -> Color {
            let c = |i: usize| ((self.sum[i] + self.weight / 2) / self.weight) as u8;
            Color(c(0), c(1), c(2))
        }
    }
    let apart = |a: [f64; 3], b: [f64; 3]| ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2) + (a[2] - b[2]).powi(2)).sqrt();
    let mut bins: BTreeMap<u16, [u64; 4]> = BTreeMap::new();
    let mut total = 0u64;
    for px in rgba.as_chunks::<4>().0 {
        let a = px[3] as u64;
        if a == 0 {
            continue;
        }
        let key = ((px[0] as u16 >> 3) << 10) | ((px[1] as u16 >> 3) << 5) | (px[2] as u16 >> 3);
        let bin = bins.entry(key).or_default();
        for i in 0..3 {
            bin[i] += px[i] as u64 * a
        }
        bin[3] += a;
        total += a;
    }
    if total == 0 {
        return Vec::new();
    }
    let mut bins: Vec<_> = bins.into_values().collect();
    bins.sort_by_key(|b| std::cmp::Reverse(b[3]));
    // Heaviest shades first, so each group forms around a shade that is really in the image.
    let mut groups: Vec<Group> = Vec::new();
    for b in bins {
        let lab = Color(((b[0] + b[3] / 2) / b[3]) as u8, ((b[1] + b[3] / 2) / b[3]) as u8, ((b[2] + b[3] / 2) / b[3]) as u8).oklab();
        let near = groups.iter_mut().map(|g| (apart(g.lab, lab), g)).min_by(|x, y| x.0.total_cmp(&y.0));
        match near {
            Some((d, g)) if d < ALIKE => {
                for i in 0..3 {
                    g.sum[i] += b[i]
                }
                g.weight += b[3];
            }
            _ => groups.push(Group { sum: [b[0], b[1], b[2]], weight: b[3], lab }),
        }
    }
    // Averages drift as groups fill, so fold together any that ended up looking the same.
    for g in &mut groups {
        g.lab = g.color().oklab();
    }
    groups.sort_by_key(|g| std::cmp::Reverse(g.weight));
    let mut merged: Vec<Group> = Vec::new();
    for g in groups {
        match merged.iter_mut().find(|m| apart(m.lab, g.lab) < SAME) {
            Some(m) => {
                for i in 0..3 {
                    m.sum[i] += g.sum[i]
                }
                m.weight += g.weight;
            }
            None => merged.push(g),
        }
    }
    let wanted = count.min(16);
    if merged.len() > wanted {
        merged.retain(|g| g.weight as f64 / total as f64 >= NOISE);
    }
    // Size matters, saturation helps a small accent be noticed, and closeness to a color already chosen counts against.
    let standing = |g: &Group| (g.weight as f64 / total as f64).sqrt() * (0.6 + 4. * g.lab[1].hypot(g.lab[2]));
    let mut chosen: Vec<Group> = Vec::new();
    while chosen.len() < wanted && !merged.is_empty() {
        let score = |g: &Group| {
            let room = chosen.iter().map(|c| apart(c.lab, g.lab)).fold(f64::INFINITY, f64::min);
            standing(g) * (room / 0.18).min(1.)
        };
        let best = (0..merged.len()).max_by(|a, b| score(&merged[*a]).total_cmp(&score(&merged[*b]))).unwrap();
        chosen.push(merged.swap_remove(best));
    }
    chosen.sort_by_key(|g| std::cmp::Reverse(g.weight));
    chosen.into_iter().map(|g| {
        let col = g.color();
        json!({"hex":col.hex(),"name":names::nearest(col).1,"percent":100.*g.weight as f64/total as f64})
    }).collect()
}
#[unsafe(no_mangle)]
pub extern "C" fn extract(count: u32) -> usize {
    PIXELS.with_borrow_mut(|buf| {
        let result = dominant(buf, count as usize);
        *buf = Vec::new();
        output(json!(result))
    })
}

/// Palette of the day. `request` points to `len` bytes of UTF-8 JSON: `{dir, action, value}`.
/// The caller supplies no time: the engine reads the clock itself.
///
/// # Safety
/// `request` must be valid for reads of `len` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn daily(request: *const u8, len: usize) -> usize {
    if request.is_null() || len > 64 * 1024 {
        return output(json!({"error": "طلب غير صالح."}));
    }
    let bytes = unsafe { std::slice::from_raw_parts(request, len) };
    output(match std::str::from_utf8(bytes) {
        Ok(text) => daily::request(text),
        Err(_) => json!({"error": "طلب غير صالح."}),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn extremes() {
        let black = Color::unpack(0);
        let white = Color::unpack(0xffffff);
        assert_eq!(black.ratio(white), 21.);
        assert_eq!(black.ratio(black), 1.);
        assert_eq!(black.describe()["formats"]["CMYK"], "0%, 0%, 0%, 100%");
    }
    #[test]
    fn color_names_and_formats() {
        let v = Color::unpack(0x87ceeb).describe();
        assert_eq!(v["name"], "أزرق سماوي");
        assert_eq!(v["formats"]["RGB"], "rgb(135, 206, 235)");
        assert_eq!(v["formats"]["HSL"], "hsl(197, 71%, 73%)");
    }
    #[test]
    fn hsl_round_trip() {
        for r in (0..=255).step_by(15) {
            for g in (0..=255).step_by(15) {
                for b in (0..=255).step_by(15) {
                    let c = Color(r, g, b);
                    let (h, s, l, _, _) = c.hs();
                    assert_eq!(Color::from_hsl(h, s, l), c)
                }
            }
        }
    }
    #[test]
    fn harmonies() {
        let c = Color(255, 0, 0);
        assert_eq!(c.harmonies(1), ["#FF0000", "#00FFFF"]);
        assert_eq!(c.harmonies(2), ["#FF0000", "#00FF00", "#0000FF"]);
    }
    #[test]
    fn alternatives_meet_threshold() {
        for bg in (0..=255).step_by(5) {
            let b = Color(bg, bg, bg);
            for fg in [Color(10, 44, 91), b, Color(201, 202, 203)] {
                let s = fg.suggestion(b);
                assert!(s.ratio(b) >= 4.5);
                if fg.ratio(b) >= 4.5 {
                    assert_eq!(s, fg)
                }
            }
        }
    }
    #[test]
    fn opacity_and_frequency() {
        let v = dominant(
            &[255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 0],
            2,
        );
        assert_eq!(v.len(), 2);
        assert_eq!(v[0]["hex"], "#FF0000");
        assert!((v[0]["percent"].as_f64().unwrap() - 200. / 3.).abs() < 0.0001);
        assert!(dominant(&[1, 2, 3, 0], 6).is_empty());
    }
    fn picture(areas: &[(Color, usize, u8)]) -> Vec<u8> {
        // Each area is a color, a pixel count and a little shade variation, as a photograph has.
        let mut out = Vec::new();
        for (c, pixels, wobble) in areas {
            for i in 0..*pixels {
                let w = (i % (*wobble as usize + 1)) as u8;
                out.extend([c.0.saturating_add(w), c.1.saturating_add(w), c.2.saturating_sub(w.min(c.2)), 255]);
            }
        }
        out
    }
    #[test]
    fn small_distinct_colors_are_found() {
        // A grey wall in four close shades, with a small red sign and a smaller teal one.
        let image = picture(&[
            (Color(120, 122, 125), 40_000, 6), (Color(128, 130, 133), 30_000, 6), (Color(112, 114, 118), 20_000, 6), (Color(135, 137, 140), 8_000, 4),
            (Color(214, 40, 40), 1_500, 5), (Color(20, 160, 150), 500, 5),
        ]);
        let found = dominant(&image, 8);
        let hue = |v: &Value| Color::unpack(u32::from_str_radix(&v["hex"].as_str().unwrap()[1..], 16).unwrap()).describe()["base"].as_str().unwrap().to_owned();
        let bases: Vec<String> = found.iter().map(hue).collect();
        assert!(bases.contains(&"أحمر".to_owned()), "the red sign was missed: {bases:?}");
        assert!(bases.iter().any(|b| b == "فيروزي" || b == "أخضر"), "the teal sign was missed: {bases:?}");
        assert!(bases.iter().filter(|b| *b == "محايد").count() <= 2, "the wall should not fill the list: {found:?}");
        assert!(found.len() <= 4, "four areas were split into {}", found.len());
        assert!(found.windows(2).all(|w| w[0]["percent"].as_f64() >= w[1]["percent"].as_f64()), "largest first");
        let sum: f64 = found.iter().map(|v| v["percent"].as_f64().unwrap()).sum();
        assert!((sum - 100.).abs() < 0.5, "shares should account for the image: {sum}");
    }
    #[test]
    fn noise_is_ignored_and_the_list_is_capped() {
        // A full rainbow of 40 hues, each wide enough to count, plus a few stray pixels.
        let mut areas: Vec<_> = (0..40).map(|i| (Color::from_hsl(i as f64 * 9., 0.8, 0.5), 500, 2)).collect();
        areas.push((Color(255, 255, 255), 3, 0));
        let found = dominant(&picture(&areas), 99);
        assert_eq!(found.len(), 16, "at most sixteen colors are returned");
        assert!(!found.iter().any(|v| v["hex"] == "#FFFFFF"), "three stray pixels are noise");
        let hues: std::collections::BTreeSet<i64> = found.iter().map(|v| {
            Color::unpack(u32::from_str_radix(&v["hex"].as_str().unwrap()[1..], 16).unwrap()).describe()["hue"].as_i64().unwrap() / 45
        }).collect();
        assert!(hues.len() >= 7, "the picks should span the rainbow, got sectors {hues:?}");
    }
}
