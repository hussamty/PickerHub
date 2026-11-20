//! Color vision deficiency previews: Machado et al. (2009), severity 1.0, applied in linear sRGB.
//! Reference: https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html
use crate::color::Color;

/// Protanopia, deuteranopia, tritanopia.
const MATRICES: [[f64; 9]; 3] = [
    [0.152286, 1.052583, -0.204868, 0.114503, 0.786281, 0.099216, -0.003882, -0.048116, 1.051998],
    [0.367322, 0.860646, -0.227968, 0.280085, 0.672501, 0.047413, -0.011820, 0.042940, 0.968881],
    [1.255528, -0.076749, -0.178779, -0.078411, 0.930809, 0.147602, 0.004733, 0.691367, 0.303900],
];

/// How the color looks with each of the three dichromacies, in the order above.
pub fn simulate(c: Color) -> [Color; 3] {
    let lin = [c.0, c.1, c.2].map(|v| {
        let v = v as f64 / 255.0;
        if v <= 0.04045 { v / 12.92 } else { ((v + 0.055) / 1.055).powf(2.4) }
    });
    MATRICES.map(|m| {
        let out: [u8; 3] = std::array::from_fn(|i| {
            let v = (m[i * 3] * lin[0] + m[i * 3 + 1] * lin[1] + m[i * 3 + 2] * lin[2]).clamp(0.0, 1.0);
            let e = if v <= 0.0031308 { 12.92 * v } else { 1.055 * v.powf(1.0 / 2.4) - 0.055 };
            (255.0 * e).round() as u8
        });
        Color(out[0], out[1], out[2])
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn grays_are_unchanged_and_red_shifts() {
        for g in [0u8, 128, 255] {
            for s in simulate(Color(g, g, g)) {
                assert!((s.0 as i32 - g as i32).abs() <= 1 && (s.2 as i32 - g as i32).abs() <= 1);
            }
        }
        let [p, d, _] = simulate(Color(255, 0, 0));
        assert!(p.0 < 200 && d.0 < 255);
    }
}
