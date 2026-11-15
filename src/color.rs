//! Opaque sRGB colors. Hue is expressed in degrees, saturation and lightness in 0..1.
use serde_json::{Value, json};

#[derive(Clone, Copy, PartialEq, Debug)]
pub struct Color(pub u8, pub u8, pub u8);

impl Color {
    pub fn unpack(n: u32) -> Self {
        Self((n >> 16) as u8, (n >> 8) as u8, n as u8)
    }
    pub fn hex(self) -> String {
        format!("#{:02X}{:02X}{:02X}", self.0, self.1, self.2)
    }
    fn channels(self) -> [f64; 3] {
        [self.0, self.1, self.2].map(|c| c as f64 / 255.)
    }
    pub fn hs(self) -> (f64, f64, f64, f64, f64) {
        let [r, g, b] = self.channels();
        let hi = r.max(g).max(b);
        let lo = r.min(g).min(b);
        let d = hi - lo;
        let l = (hi + lo) / 2.;
        if d == 0. {
            return (0., 0., l, 0., hi);
        }
        let h = if hi == r {
            (g - b) / d
        } else if hi == g {
            (b - r) / d + 2.
        } else {
            (r - g) / d + 4.
        };
        (
            (h * 60.).rem_euclid(360.),
            d / (1. - (2. * l - 1.).abs()),
            l,
            d / hi,
            hi,
        )
    }
    pub fn from_hsl(h: f64, s: f64, l: f64) -> Self {
        let c = (1. - (2. * l - 1.).abs()) * s;
        let h = h.rem_euclid(360.) / 60.;
        let x = c * (1. - (h % 2. - 1.).abs());
        let channels = match h as u8 {
            0 => [c, x, 0.],
            1 => [x, c, 0.],
            2 => [0., c, x],
            3 => [0., x, c],
            4 => [x, 0., c],
            _ => [c, 0., x],
        };
        let [r, g, b] = channels.map(|v| ((v + l - c / 2.) * 255.).round() as u8);
        Self(r, g, b)
    }
    pub fn luminance(self) -> f64 {
        let [r, g, b] = self.channels().map(|v| {
            if v <= 0.04045 {
                v / 12.92
            } else {
                ((v + 0.055) / 1.055).powf(2.4)
            }
        });
        0.2126 * r + 0.7152 * g + 0.0722 * b
    }
    /// OKLab coordinates: equal distances look about equally different, which RGB distances do not.
    pub fn oklab(self) -> [f64; 3] {
        let [r, g, b] = self.channels().map(|v| if v <= 0.04045 { v / 12.92 } else { ((v + 0.055) / 1.055).powf(2.4) });
        let l = (0.412_221_470_8 * r + 0.536_332_536_3 * g + 0.051_445_992_9 * b).cbrt();
        let m = (0.211_903_498_2 * r + 0.680_699_545_1 * g + 0.107_396_956_6 * b).cbrt();
        let s = (0.088_302_461_9 * r + 0.281_718_837_6 * g + 0.629_978_700_5 * b).cbrt();
        [
            0.210_454_255_3 * l + 0.793_617_785 * m - 0.004_072_046_8 * s,
            1.977_998_495_1 * l - 2.428_592_205 * m + 0.450_593_709_9 * s,
            0.025_904_037_1 * l + 0.782_771_766_2 * m - 0.808_675_766 * s,
        ]
    }
    pub fn ratio(self, other: Self) -> f64 {
        let a = self.luminance();
        let b = other.luminance();
        (a.max(b) + 0.05) / (a.min(b) + 0.05)
    }
    pub fn ink(self) -> Self {
        if self.ratio(Self(0, 0, 0)) >= self.ratio(Self(255, 255, 255)) {
            Self(0, 0, 0)
        } else {
            Self(255, 255, 255)
        }
    }
    pub fn mix(self, other: Self, t: f64) -> Self {
        let f = |a: u8, b: u8| (a as f64 * (1. - t) + b as f64 * t).round() as u8;
        Self(f(self.0, other.0), f(self.1, other.1), f(self.2, other.2))
    }
    pub fn suggestion(self, bg: Self) -> Self {
        if self.ratio(bg) >= 4.5 {
            return self;
        }
        let end = bg.ink();
        (1..=255)
            .map(|step| self.mix(end, step as f64 / 255.))
            .find(|c| c.ratio(bg) >= 4.5)
            .unwrap_or(end)
    }
    pub fn describe(self) -> Value {
        let (h, s, l, sv, v) = self.hs();
        let h = h.round() as i32 % 360;
        let pct = |a: f64| (100. * a).round() as i32;
        let [r, g, b] = self.channels();
        let cmy = |a: f64| if v == 0. { 0 } else { pct(1. - a / v) };
        let hsv = format!("{h}°, {}%, {}%", pct(sv), pct(v));
        let name = crate::names::nearest(self);
        let base = if s < 0.10 || v < 0.07 {
            "محايد"
        } else {
            match h {
                0..=14 | 346..=359 => "أحمر",
                15..=44 => "برتقالي",
                45..=68 => "أصفر",
                69..=165 => "أخضر",
                166..=190 => "فيروزي",
                191..=254 => "أزرق",
                255..=292 => "بنفسجي",
                _ => "وردي",
            }
        };
        let shade = if l < 0.28 {
            "داكن"
        } else if l > 0.65 {
            "فاتح"
        } else {
            "متوسط السطوع"
        };
        let [ok_l, ok_a, ok_b] = self.oklab();
        let chroma = ok_a.hypot(ok_b);
        let hue = if chroma < 1e-7 { 0. } else { ok_b.atan2(ok_a).to_degrees().rem_euclid(360.) };
        let oklch = format!("oklch({:.2}% {:.5} {:.2})", ok_l * 100., chroma, hue);
        json!({"hex":self.hex(),"name":name.1,"english":name.2,"reference":Color::unpack(name.0).hex(),"base":base,"shade":shade,"hue":h,
        "ink":self.ink().hex(),"formats":{"HEX":self.hex(),"RGB":format!("rgb({}, {}, {})",self.0,self.1,self.2),
        "RGBA":format!("rgba({}, {}, {}, 1)",self.0,self.1,self.2),"HSL":format!("hsl({h}, {}%, {}%)",pct(s),pct(l)),
        "HSV":hsv,"HSB":hsv,"OKLCH":oklch,"CMYK":format!("{}%, {}%, {}%, {}%",cmy(r),cmy(g),cmy(b),pct(1.-v))}})
    }
    pub fn harmonies(self, kind: u32) -> Vec<String> {
        let (h, s, l, _, _) = self.hs();
        let rot = |angle: f64| Self::from_hsl(h + angle, s, l);
        let colors = match kind {
            0 => vec![rot(-60.), rot(-30.), self, rot(30.), rot(60.)],
            1 => vec![self, rot(180.)],
            2 => vec![self, rot(120.), rot(240.)],
            3 => vec![self, rot(150.), rot(210.)],
            4 => vec![
                self.mix(Self(0, 0, 0), 0.55),
                self.mix(Self(0, 0, 0), 0.28),
                self,
                self.mix(Self(255, 255, 255), 0.32),
                self.mix(Self(255, 255, 255), 0.65),
            ],
            _ => vec![self, self.ink(), self.suggestion(self)],
        };
        colors.into_iter().map(|c| c.hex()).collect()
    }
}
