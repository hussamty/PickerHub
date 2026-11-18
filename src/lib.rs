//! PickerHub core and a native C ABI. See docs/architecture.md.
pub mod color;
pub mod daily;
pub mod names;
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
/// image are treated as noise, and nearly transparent pixels (under 16 of 255) are not counted at all.
/// Percentages are shares of the visible (opacity-weighted) image.
///
/// Each color is reported as its group's representative: the weighted median of the group in OKLab,
/// snapped to the nearest shade that really occurs in the image. A median is not pulled by the shadows,
/// highlights, compression fringes and stray pixels at a group's edges the way an average is, so the
/// reported color, and the name it is given, are those of the area a person would point at.
pub fn dominant(rgba: &[u8], count: usize) -> Vec<Value> {
    const ALIKE: f64 = 0.055;
    const SAME: f64 = 0.08;
    const NOISE: f64 = 0.0008;
    struct Group {
        sum: [u64; 3],
        weight: u64,
        lab: [f64; 3],
        /// The shades in the group: their OKLab coordinates, weight and color.
        members: Vec<([f64; 3], u64, Color)>,
    }
    impl Group {
        fn color(&self) -> Color {
            let c = |i: usize| ((self.sum[i] + self.weight / 2) / self.weight) as u8;
            Color(c(0), c(1), c(2))
        }
        fn representative(&self) -> Color {
            let total = self.members.iter().map(|m| m.1).sum::<u64>() as f64;
            let median = |k: usize| {
                let mut values: Vec<(f64, u64)> = self.members.iter().map(|m| (m.0[k], m.1)).collect();
                values.sort_by(|a, b| a.0.total_cmp(&b.0));
                let mut seen = 0.;
                for (value, weight) in &values {
                    seen += *weight as f64;
                    if seen >= total / 2. {
                        return *value;
                    }
                }
                values.last().map_or(0., |v| v.0)
            };
            let target = [median(0), median(1), median(2)];
            let apart = |m: &&([f64; 3], u64, Color)| (m.0[0] - target[0]).powi(2) + (m.0[1] - target[1]).powi(2) + (m.0[2] - target[2]).powi(2);
            self.members.iter().min_by(|a, b| apart(a).total_cmp(&apart(b))).map_or_else(|| self.color(), |m| m.2)
        }
    }
    let apart = |a: [f64; 3], b: [f64; 3]| ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2) + (a[2] - b[2]).powi(2)).sqrt();
    let mut bins: BTreeMap<u16, [u64; 4]> = BTreeMap::new();
    let mut total = 0u64;
    for px in rgba.as_chunks::<4>().0 {
        let a = px[3] as u64;
        if a < 16 {
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
        let shade = Color(((b[0] + b[3] / 2) / b[3]) as u8, ((b[1] + b[3] / 2) / b[3]) as u8, ((b[2] + b[3] / 2) / b[3]) as u8);
        let lab = shade.oklab();
        let near = groups.iter_mut().map(|g| (apart(g.lab, lab), g)).min_by(|x, y| x.0.total_cmp(&y.0));
        match near {
            Some((d, g)) if d < ALIKE => {
                for i in 0..3 {
                    g.sum[i] += b[i]
                }
                g.weight += b[3];
                g.members.push((lab, b[3], shade));
            }
            _ => groups.push(Group { sum: [b[0], b[1], b[2]], weight: b[3], lab, members: vec![(lab, b[3], shade)] }),
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
                m.members.extend(g.members);
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
        let col = g.representative();
        let name = names::name(col);
        json!({"hex":col.hex(),"name":name.arabic,"english":name.english,"family":name.family.english(),"percent":100.*g.weight as f64/total as f64})
    }).collect()
}
/// Where each color of the image lives, so it can be pointed at.
///
/// The image is segmented by nearest color: every visible pixel takes the listed color closest to it
/// in OKLab, and a 3x3 majority pass removes the speckle a photograph leaves, so regions come out
/// whole. Each color is then marked inside its largest connected region, at the pixel farthest from
/// that region's edge (a chamfer distance transform), so the mark sits deep inside the color and
/// never on a boundary it shares with another. Coordinates are pixel centres as fractions of the
/// image: `x` and `y` run from 0 to 1. `depth` is how many pixels the mark sits from the region's edge.
pub fn locate(rgba: &[u8], width: usize, height: usize, count: usize) -> Vec<Value> {
    let mut colors = dominant(rgba, count);
    let size = width * height;
    if colors.is_empty() || size == 0 || rgba.len() < size * 4 || colors.len() >= u8::MAX as usize {
        return colors;
    }
    const NONE: u8 = u8::MAX;
    let labs: Vec<[f64; 3]> = colors.iter().map(|c| hex_color(&c["hex"]).oklab()).collect();
    let nearest = |lab: [f64; 3]| -> u8 {
        let d = |l: &[f64; 3]| (l[0] - lab[0]).powi(2) + (l[1] - lab[1]).powi(2) + (l[2] - lab[2]).powi(2);
        (0..labs.len()).min_by(|a, b| d(&labs[*a]).total_cmp(&d(&labs[*b]))).unwrap() as u8
    };
    // Segment: one label per pixel. Mostly transparent pixels belong to no color.
    let mut cache: std::collections::HashMap<u32, u8> = std::collections::HashMap::new();
    let mut labels = vec![NONE; size];
    for (i, px) in rgba.as_chunks::<4>().0.iter().take(size).enumerate() {
        if px[3] < 128 {
            continue;
        }
        let key = u32::from(px[0]) << 16 | u32::from(px[1]) << 8 | u32::from(px[2]);
        labels[i] = *cache.entry(key).or_insert_with(|| nearest(Color(px[0], px[1], px[2]).oklab()));
    }
    let labels = majority(&labels, width, height, colors.len());
    for (k, color) in colors.iter_mut().enumerate() {
        let found = deepest(&labels, width, height, k as u8).unwrap_or_else(|| {
            // The region vanished in smoothing: fall back to the single pixel nearest the color.
            let target = labs[k];
            let mut best = (f64::INFINITY, 0usize);
            for (i, px) in rgba.as_chunks::<4>().0.iter().take(size).enumerate() {
                if px[3] < 128 {
                    continue;
                }
                let l = Color(px[0], px[1], px[2]).oklab();
                let d = (l[0] - target[0]).powi(2) + (l[1] - target[1]).powi(2) + (l[2] - target[2]).powi(2);
                if d < best.0 {
                    best = (d, i);
                }
            }
            (best.1 % width, best.1 / width, 0)
        });
        color["x"] = json!((found.0 as f64 + 0.5) / width as f64);
        color["y"] = json!((found.1 as f64 + 0.5) / height as f64);
        color["depth"] = json!(found.2);
    }
    colors
}
fn hex_color(hex: &Value) -> Color {
    Color::unpack(hex.as_str().and_then(|h| u32::from_str_radix(h.trim_start_matches('#'), 16).ok()).unwrap_or(0))
}
/// One pass of a 3x3 majority filter. A pixel changes only when another label outnumbers its own.
fn majority(labels: &[u8], width: usize, height: usize, kinds: usize) -> Vec<u8> {
    let mut out = labels.to_vec();
    let mut votes = vec![0u8; kinds];
    for y in 0..height {
        for x in 0..width {
            let own = labels[y * width + x];
            if own == u8::MAX {
                continue;
            }
            votes.iter_mut().for_each(|v| *v = 0);
            for ny in y.saturating_sub(1)..(y + 2).min(height) {
                for nx in x.saturating_sub(1)..(x + 2).min(width) {
                    let l = labels[ny * width + nx];
                    if l != u8::MAX {
                        votes[l as usize] += 1;
                    }
                }
            }
            let (best, count) = votes.iter().enumerate().max_by_key(|(_, v)| **v).map(|(i, v)| (i as u8, *v)).unwrap();
            if count > votes[own as usize] {
                out[y * width + x] = best;
            }
        }
    }
    out
}
/// The pixel of `label`'s largest 4-connected region that lies farthest from the region's edge.
/// The image border counts as an edge, so a region that touches it is marked away from it.
fn deepest(labels: &[u8], width: usize, height: usize, label: u8) -> Option<(usize, usize, u32)> {
    let mut seen = vec![false; labels.len()];
    let mut largest: Vec<usize> = Vec::new();
    let mut queue: Vec<usize> = Vec::new();
    for start in 0..labels.len() {
        if labels[start] != label || seen[start] {
            continue;
        }
        let mut region = Vec::new();
        seen[start] = true;
        queue.push(start);
        while let Some(i) = queue.pop() {
            region.push(i);
            let (x, y) = (i % width, i / width);
            let mut visit = |j: usize| {
                if labels[j] == label && !seen[j] {
                    seen[j] = true;
                    queue.push(j);
                }
            };
            if x > 0 { visit(i - 1) }
            if x + 1 < width { visit(i + 1) }
            if y > 0 { visit(i - width) }
            if y + 1 < height { visit(i + width) }
        }
        if region.len() > largest.len() {
            largest = region;
        }
    }
    if largest.is_empty() {
        return None;
    }
    // Chamfer (3, 4) distance transform over the region's bounding box, padded by one pixel of edge.
    let (mut x0, mut y0, mut x1, mut y1) = (usize::MAX, usize::MAX, 0, 0);
    for &i in &largest {
        let (x, y) = (i % width, i / width);
        x0 = x0.min(x); y0 = y0.min(y); x1 = x1.max(x); y1 = y1.max(y);
    }
    let (bw, bh) = (x1 - x0 + 3, y1 - y0 + 3);
    let mut dist = vec![0u32; bw * bh];
    for &i in &largest {
        dist[(i / width - y0 + 1) * bw + (i % width - x0 + 1)] = u32::MAX / 2;
    }
    for y in 1..bh - 1 {
        for x in 1..bw - 1 {
            let i = y * bw + x;
            if dist[i] == 0 { continue }
            dist[i] = dist[i].min(dist[i - 1] + 3).min(dist[i - bw] + 3).min(dist[i - bw - 1] + 4).min(dist[i - bw + 1] + 4);
        }
    }
    for y in (1..bh - 1).rev() {
        for x in (1..bw - 1).rev() {
            let i = y * bw + x;
            if dist[i] == 0 { continue }
            dist[i] = dist[i].min(dist[i + 1] + 3).min(dist[i + bw] + 3).min(dist[i + bw + 1] + 4).min(dist[i + bw - 1] + 4);
        }
    }
    // Deepest first; among equals, the one nearest the region's centre of mass, so the mark is stable.
    let (cx, cy) = largest.iter().fold((0f64, 0f64), |a, &i| (a.0 + (i % width) as f64, a.1 + (i / width) as f64));
    let (cx, cy) = (cx / largest.len() as f64, cy / largest.len() as f64);
    let best = largest.iter().map(|&i| {
        let (x, y) = (i % width, i / width);
        (dist[(y - y0 + 1) * bw + (x - x0 + 1)], -((x as f64 - cx).powi(2) + (y as f64 - cy).powi(2)), x, y)
    }).max_by(|a, b| a.0.cmp(&b.0).then(a.1.total_cmp(&b.1)))?;
    // Chamfer units are three per pixel step.
    Some((best.2, best.3, best.0 / 3))
}
#[unsafe(no_mangle)]
pub extern "C" fn extract_at(width: u32, height: u32, count: u32) -> usize {
    PIXELS.with_borrow_mut(|buf| {
        let result = locate(buf, width as usize, height as usize, count as usize);
        *buf = Vec::new();
        output(json!(result))
    })
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
    fn canvas(width: usize, height: usize, paint: impl Fn(usize, usize) -> [u8; 3]) -> Vec<u8> {
        let mut out = Vec::with_capacity(width * height * 4);
        for y in 0..height {
            for x in 0..width {
                let c = paint(x, y);
                out.extend([c[0], c[1], c[2], 255]);
            }
        }
        out
    }
    fn mark(found: &[Value], hex: &str, width: usize, height: usize) -> (f64, f64) {
        let v = found.iter().find(|v| v["hex"] == hex).unwrap_or_else(|| panic!("{hex} not found in {found:?}"));
        (v["x"].as_f64().unwrap() * width as f64, v["y"].as_f64().unwrap() * height as f64)
    }
    #[test]
    fn each_color_is_marked_deep_inside_its_own_region() {
        // Red on the left, blue on the right, and a small green square inside the blue at (90, 20).
        let (w, h) = (120, 80);
        let image = canvas(w, h, |x, y| if (85..95).contains(&x) && (15..25).contains(&y) { [30, 180, 60] } else if x < 60 { [220, 40, 40] } else { [40, 60, 210] });
        let found = locate(&image, w, h, 6);
        let (gx, gy) = mark(&found, "#1EB43C", w, h);
        assert!((gx - 90.).abs() <= 1. && (gy - 20.).abs() <= 1., "the green square's mark should sit at its centre: {gx}, {gy}");
        let (rx, ry) = mark(&found, "#DC2828", w, h);
        assert!(rx < 60. && rx > 15. && ry > 15. && ry < 65., "the red mark should be well inside the red half: {rx}, {ry}");
        let (bx, by) = mark(&found, "#283CD2", w, h);
        assert!(bx > 60. && !((80. ..100.).contains(&bx) && (10. ..30.).contains(&by)), "the blue mark should avoid the green square: {bx}, {by}");
        let depth = |hex: &str| found.iter().find(|v| v["hex"] == hex).unwrap()["depth"].as_u64().unwrap();
        assert!(depth("#1EB43C") >= 4 && depth("#DC2828") >= 20, "marks should be deep inside their regions");
    }
    #[test]
    fn the_largest_region_wins_and_speckle_is_smoothed() {
        // Two yellow patches, a big one on the right; stray yellow pixels scattered over grey.
        let (w, h) = (100, 60);
        let image = canvas(w, h, |x, y| {
            if (60..90).contains(&x) && (10..50).contains(&y) || (5..15).contains(&x) && (5..15).contains(&y) { [240, 200, 20] }
            else if (x * 7 + y * 13) % 97 == 0 { [240, 200, 20] } else { [128, 128, 128] }
        });
        let found = locate(&image, w, h, 4);
        let (x, y) = mark(&found, "#F0C814", w, h);
        assert!((70. ..80.).contains(&x) && (20. ..40.).contains(&y), "yellow should be marked in its largest patch: {x}, {y}");
        assert!(found.iter().all(|v| v["x"].is_f64() && v["y"].is_f64()), "every color gets a mark");
    }
    #[test]
    fn a_shaded_purple_area_is_named_purple() {
        // A muted purple wall lit from one side: shadows, highlights and JPEG-like speckle around one purple.
        let (w, h) = (90, 60);
        let image = canvas(w, h, |x, y| {
            let shade = (x as i32 - 45) / 3 + ((x * 7 + y * 3) % 5) as i32 - 2;
            let c = |v: i32| (v + shade).clamp(0, 255) as u8;
            if y < 40 { [c(123), c(94), c(167)] } else { [128, 128, 128] }
        });
        let found = dominant(&image, 4);
        let purple = found.iter().find(|v| v["family"] != "Neutral").unwrap_or_else(|| panic!("the purple wall was lost: {found:?}"));
        assert!(purple["family"] == "Purple" || purple["family"] == "Violet", "the wall should read as purple: {purple:?}");
        assert!(!purple["english"].as_str().unwrap().contains("Gray"), "a shaded purple is not gray: {purple:?}");
        let gray = found.iter().find(|v| v["family"] == "Neutral").expect("the gray floor is neutral");
        assert_eq!(gray["english"], "Gray");
    }
    #[test]
    fn nearly_transparent_pixels_are_not_counted() {
        // An opaque red square on a field of blue that is almost fully transparent.
        let mut image = Vec::new();
        for i in 0..400 {
            image.extend(if i < 100 { [220, 30, 30, 255] } else { [20, 40, 230, 10] });
        }
        let found = dominant(&image, 4);
        assert_eq!(found.len(), 1, "only the opaque red counts: {found:?}");
        assert_eq!(found[0]["family"], "Red");
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
