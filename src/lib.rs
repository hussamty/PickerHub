//! PickerHub core and a native C ABI. See docs/architecture.md.
pub mod color;
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
/// Quantize into 4-bit RGB bins. Weight by opacity; percentages use the entire visible sample.
pub fn dominant(rgba: &[u8], count: usize) -> Vec<Value> {
    let mut bins: BTreeMap<u16, [u64; 4]> = BTreeMap::new();
    let mut total = 0;
    for px in rgba.as_chunks::<4>().0 {
        let a = px[3] as u64;
        if a == 0 {
            continue;
        }
        let key = ((px[0] as u16 >> 4) << 8) | ((px[1] as u16 >> 4) << 4) | (px[2] as u16 >> 4);
        let bin = bins.entry(key).or_default();
        for i in 0..3 {
            bin[i] += px[i] as u64 * a
        }
        bin[3] += a;
        total += a;
    }
    let mut bins: Vec<_> = bins.into_values().collect();
    bins.sort_by_key(|b| std::cmp::Reverse(b[3]));
    bins.into_iter().take(count.min(16)).map(|b|{
        let c=|i:usize|((b[i]+b[3]/2)/b[3]) as u8; let col=Color(c(0),c(1),c(2));
        json!({"hex":col.hex(),"name":names::nearest(col).1,"percent":100.*b[3] as f64/total as f64})
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
}
