//! Screen color sampling for PickerHub, one backend per platform:
//!
//! | Platform      | Backend                                                                 |
//! |---------------|-------------------------------------------------------------------------|
//! | Linux/X11     | grab the pointer on the root window, read the clicked pixel (`x11rb`)   |
//! | Linux/Wayland | GNOME Color Picker extension if active, else the XDG `PickColor` portal |
//! | Windows       | low-level mouse and keyboard hooks, then GDI `GetPixel`                 |
//! | macOS         | `NSColorSampler`, converted to sRGB                                     |
//!
//! Every backend returns `Ok(None)` when the user cancels (Escape, or the system dialog's cancel).
#[cfg(target_os = "linux")]
pub mod screencast;
#[cfg(target_os = "linux")]
pub mod wayland;
#[cfg(target_os = "linux")]
pub mod x11;
#[cfg(windows)]
pub mod windows;
#[cfg(target_os = "macos")]
pub mod macos;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rgb(pub u8, pub u8, pub u8);
impl Rgb {
    pub fn hex(self) -> String {
        format!("#{:02X}{:02X}{:02X}", self.0, self.1, self.2)
    }
    /// From channels in 0..1, as portals and AppKit report them.
    pub fn from_unit(r: f64, g: f64, b: f64) -> Result<Self, CaptureError> {
        let c = |v: f64| {
            if v.is_finite() && (0.0..=1.0).contains(&v) {
                Ok((v * 255.0).round() as u8)
            } else {
                Err(CaptureError::new("invalid_color", "قيمة قناة اللون غير صالحة."))
            }
        };
        Ok(Self(c(r)?, c(g)?, c(b)?))
    }
}

/// A failure the interface can show: `message` is Arabic source text (the English catalog's key).
#[derive(Debug, Clone)]
pub struct CaptureError {
    pub code: &'static str,
    pub message: &'static str,
    pub detail: Option<String>,
}
impl CaptureError {
    pub fn new(code: &'static str, message: &'static str) -> Self {
        Self { code, message, detail: None }
    }
    pub fn with(mut self, detail: impl std::fmt::Display) -> Self {
        self.detail = Some(detail.to_string());
        self
    }
}
impl std::fmt::Display for CaptureError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}: {}", self.code, self.message)?;
        if let Some(d) = &self.detail {
            write!(f, " ({d})")?;
        }
        Ok(())
    }
}
impl std::error::Error for CaptureError {}

/// The magnifier's view: a square of pixels around the pointer, RGBA, row by row.
pub const LENS_RADIUS: i32 = 12;
pub const LENS_SIDE: usize = (2 * LENS_RADIUS + 1) as usize;
pub type Patch = Vec<u8>;
/// The color at the centre of a patch.
pub fn centre(p: &Patch) -> Rgb {
    let i = (LENS_RADIUS as usize * LENS_SIDE + LENS_RADIUS as usize) * 4;
    Rgb(p[i], p[i + 1], p[i + 2])
}

/// What the user did during a lens session (desktop/native/x11.c, pickerhub_input_poll).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LensAction {
    Pick(i32, i32),
    PickHere,
    Cancel,
    Zoom(i32),
    Nudge(i32, i32),
}

/// A native live-lens session: the platform holds the pointer and keyboard for picking while the app
/// reads the pointer and the pixels around it every frame (X11 and Windows).
pub trait LensSource {
    /// Everything the user did since the last call.
    fn poll(&self) -> Result<Vec<LensAction>, CaptureError>;
    fn pointer(&self) -> Result<(i32, i32), CaptureError>;
    fn sample(&self, x: i32, y: i32) -> Result<Patch, CaptureError>;
    /// Moves the pointer by a pixel (the arrow keys).
    fn nudge(&self, dx: i32, dy: i32) -> Result<(), CaptureError>;
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Backend {
    X11,
    Portal,
    Windows,
    MacOs,
    Unsupported,
}

/// An explicit session type is authoritative: native libraries can probe or populate
/// `WAYLAND_DISPLAY` even in an X11 session, and that must not switch capture.
pub fn wayland_session(session: Option<&str>, display: Option<&str>) -> bool {
    match session.map(str::to_ascii_lowercase).as_deref() {
        Some("wayland") => true,
        Some("x11") => false,
        _ => display.is_some_and(|d| !d.trim().is_empty()),
    }
}

pub fn backend() -> Backend {
    if cfg!(windows) {
        Backend::Windows
    } else if cfg!(target_os = "macos") {
        Backend::MacOs
    } else if cfg!(target_os = "linux") {
        let session = std::env::var("XDG_SESSION_TYPE").ok();
        let display = std::env::var("WAYLAND_DISPLAY").ok();
        if wayland_session(session.as_deref(), display.as_deref()) { Backend::Portal } else { Backend::X11 }
    } else {
        Backend::Unsupported
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn session_detection_follows_1x() {
        assert!(wayland_session(Some("wayland"), None));
        assert!(!wayland_session(Some("x11"), Some("wayland-0")));
        assert!(wayland_session(None, Some("wayland-0")));
        assert!(!wayland_session(Some(""), Some(" ")));
    }
    #[test]
    fn unit_channels() {
        assert_eq!(Rgb::from_unit(1.0, 0.5, 0.0).unwrap().hex(), "#FF8000");
        assert!(Rgb::from_unit(f64::NAN, 0.0, 0.0).is_err());
        assert!(Rgb::from_unit(1.2, 0.0, 0.0).is_err());
    }
}
