//! X11: grab the pointer and keyboard on the root window with a crosshair, read the pixel under a
//! click, and give everything back. Escape (or a right click) cancels.
use crate::{CaptureError, Rgb};
use x11rb::connection::Connection;
use x11rb::protocol::Event;
use x11rb::protocol::xproto::{ConnectionExt, EventMask, GrabMode, GrabStatus, ImageFormat};
use x11rb::{CURRENT_TIME, NONE};

const XC_CROSSHAIR: u16 = 34;
const ESCAPE: u32 = 0xff1b;

fn err(e: impl std::fmt::Display) -> CaptureError {
    CaptureError::new("x11", "تعذّر الالتقاط من شاشة X11.").with(e)
}

/// Reads one pixel of the root window, at root coordinates.
pub fn pixel_at<C: Connection>(conn: &C, root: u32, x: i16, y: i16) -> Result<Rgb, CaptureError> {
    let image = conn.get_image(ImageFormat::Z_PIXMAP, root, x, y, 1, 1, !0).map_err(err)?.reply().map_err(err)?;
    let setup = conn.setup();
    let depth = image.depth;
    let format = setup.pixmap_formats.iter().find(|f| f.depth == depth).ok_or_else(|| err("pixmap format"))?;
    let bytes = (format.bits_per_pixel / 8) as usize;
    if image.data.len() < bytes || bytes < 3 {
        return Err(err("unsupported visual"));
    }
    let mut v = 0u32;
    for (i, b) in image.data[..bytes.min(4)].iter().enumerate() {
        v |= (*b as u32) << if setup.image_byte_order == x11rb::protocol::xproto::ImageOrder::LSB_FIRST { 8 * i } else { 8 * (bytes - 1 - i) };
    }
    // TrueColor 24/32-bit: red, green, blue masks 0xff0000, 0xff00, 0xff.
    Ok(Rgb((v >> 16) as u8, (v >> 8) as u8, v as u8))
}

pub fn pick() -> Result<Option<Rgb>, CaptureError> {
    let (conn, screen) = x11rb::connect(None).map_err(err)?;
    let root = conn.setup().roots[screen].root;

    let font = conn.generate_id().map_err(err)?;
    conn.open_font(font, b"cursor").map_err(err)?;
    let cursor = conn.generate_id().map_err(err)?;
    conn.create_glyph_cursor(cursor, font, font, XC_CROSSHAIR, XC_CROSSHAIR + 1, 0, 0, 0, 0xffff, 0xffff, 0xffff).map_err(err)?;

    let escape = {
        let setup = conn.setup();
        let (min, max) = (setup.min_keycode, setup.max_keycode);
        let map = conn.get_keyboard_mapping(min, max - min + 1).map_err(err)?.reply().map_err(err)?;
        let per = map.keysyms_per_keycode as usize;
        map.keysyms.chunks(per.max(1)).position(|syms| syms.contains(&ESCAPE)).map(|i| min + i as u8)
    };

    let grab = conn
        .grab_pointer(false, root, EventMask::BUTTON_PRESS, GrabMode::ASYNC, GrabMode::ASYNC, NONE, cursor, CURRENT_TIME)
        .map_err(err)?
        .reply()
        .map_err(err)?;
    if grab.status != GrabStatus::SUCCESS {
        return Err(CaptureError::new("busy", "مؤشر الفأرة محجوز من تطبيق آخر. أعد المحاولة."));
    }
    let _ = conn.grab_keyboard(false, root, CURRENT_TIME, GrabMode::ASYNC, GrabMode::ASYNC).map_err(err)?.reply();
    conn.flush().map_err(err)?;

    let result = loop {
        match conn.wait_for_event().map_err(err)? {
            Event::ButtonPress(e) if e.detail == 1 => break pixel_at(&conn, root, e.root_x, e.root_y).map(Some),
            Event::ButtonPress(_) => break Ok(None),
            Event::KeyPress(k) if Some(k.detail) == escape => break Ok(None),
            _ => {}
        }
    };
    let _ = conn.ungrab_pointer(CURRENT_TIME);
    let _ = conn.ungrab_keyboard(CURRENT_TIME);
    let _ = conn.free_cursor(cursor);
    let _ = conn.close_font(font);
    let _ = conn.flush();
    result
}

/// A live lens session: the pointer and keyboard held on the root window until the user picks or
/// cancels, the pointer read every frame and a square of pixels around it (desktop/native/x11.c).
pub struct LensSession {
    conn: x11rb::rust_connection::RustConnection,
    root: u32,
    width: i16,
    height: i16,
    escape: Option<u8>,
    enter: Option<u8>,
    arrows: [Option<u8>; 4],
    plus: Vec<u8>,
    minus: Vec<u8>,
    cursor: u32,
    font: u32,
}

impl crate::LensSource for LensSession {
    fn poll(&self) -> Result<Vec<crate::LensAction>, CaptureError> {
        LensSession::poll(self)
    }
    fn pointer(&self) -> Result<(i32, i32), CaptureError> {
        LensSession::pointer(self)
    }
    fn sample(&self, x: i32, y: i32) -> Result<crate::Patch, CaptureError> {
        LensSession::sample(self, x, y)
    }
    fn nudge(&self, dx: i32, dy: i32) -> Result<(), CaptureError> {
        LensSession::nudge(self, dx, dy)
    }
}

impl LensSession {
    pub fn open() -> Result<Self, CaptureError> {
        let (conn, screen) = x11rb::connect(None).map_err(err)?;
        let s = &conn.setup().roots[screen];
        let (root, width, height) = (s.root, s.width_in_pixels as i16, s.height_in_pixels as i16);
        let font = conn.generate_id().map_err(err)?;
        conn.open_font(font, b"cursor").map_err(err)?;
        let cursor = conn.generate_id().map_err(err)?;
        conn.create_glyph_cursor(cursor, font, font, XC_CROSSHAIR, XC_CROSSHAIR + 1, 0, 0, 0, 0xffff, 0xffff, 0xffff).map_err(err)?;
        let (min, max) = (conn.setup().min_keycode, conn.setup().max_keycode);
        let map = conn.get_keyboard_mapping(min, max - min + 1).map_err(err)?.reply().map_err(err)?;
        let per = (map.keysyms_per_keycode as usize).max(1);
        let find = |sym: u32| map.keysyms.chunks(per).position(|k| k.contains(&sym)).map(|i| min + i as u8);
        let all = |syms: &[u32]| map.keysyms.chunks(per).enumerate().filter(|(_, k)| syms.iter().any(|s| k.contains(s))).map(|(i, _)| min + i as u8).collect::<Vec<_>>();
        let grab = conn
            .grab_pointer(false, root, EventMask::BUTTON_PRESS, GrabMode::ASYNC, GrabMode::ASYNC, NONE, cursor, CURRENT_TIME)
            .map_err(err)?
            .reply()
            .map_err(err)?;
        if grab.status != GrabStatus::SUCCESS {
            return Err(CaptureError::new("busy", "مؤشر الفأرة محجوز من تطبيق آخر. أعد المحاولة."));
        }
        let _ = conn.grab_keyboard(false, root, CURRENT_TIME, GrabMode::ASYNC, GrabMode::ASYNC).map_err(err)?.reply();
        conn.flush().map_err(err)?;
        Ok(Self {
            escape: find(ESCAPE),
            enter: find(0xff0d),
            arrows: [find(0xff51), find(0xff53), find(0xff52), find(0xff54)],
            plus: all(&[0x2b, 0x3d, 0xffab]),
            minus: all(&[0x2d, 0xffad]),
            conn,
            root,
            width,
            height,
            cursor,
            font,
        })
    }

    /// Everything the user did since the last call.
    pub fn poll(&self) -> Result<Vec<crate::LensAction>, CaptureError> {
        use crate::LensAction::*;
        let mut out = Vec::new();
        while let Some(e) = self.conn.poll_for_event().map_err(err)? {
            match e {
                Event::ButtonPress(b) => match b.detail {
                    1 => out.push(Pick(b.root_x as i32, b.root_y as i32)),
                    3 => out.push(Cancel),
                    4 => out.push(Zoom(1)),
                    5 => out.push(Zoom(-1)),
                    _ => {}
                },
                Event::KeyPress(k) => {
                    let c = Some(k.detail);
                    if c == self.escape {
                        out.push(Cancel)
                    } else if c == self.enter {
                        out.push(PickHere)
                    } else if c == self.arrows[0] {
                        out.push(Nudge(-1, 0))
                    } else if c == self.arrows[1] {
                        out.push(Nudge(1, 0))
                    } else if c == self.arrows[2] {
                        out.push(Nudge(0, -1))
                    } else if c == self.arrows[3] {
                        out.push(Nudge(0, 1))
                    } else if self.plus.contains(&k.detail) {
                        out.push(Zoom(1))
                    } else if self.minus.contains(&k.detail) {
                        out.push(Zoom(-1))
                    }
                }
                _ => {}
            }
        }
        Ok(out)
    }

    pub fn pointer(&self) -> Result<(i32, i32), CaptureError> {
        let p = self.conn.query_pointer(self.root).map_err(err)?.reply().map_err(err)?;
        Ok((p.root_x as i32, p.root_y as i32))
    }

    /// Moves the pointer by a pixel (the arrow keys).
    pub fn nudge(&self, dx: i32, dy: i32) -> Result<(), CaptureError> {
        self.conn.warp_pointer(NONE, NONE, 0, 0, 0, 0, dx as i16, dy as i16).map_err(err)?;
        self.conn.flush().map_err(err)
    }

    /// The square around `(x, y)`, clamped to the screen at its edges.
    pub fn sample(&self, x: i32, y: i32) -> Result<crate::Patch, CaptureError> {
        let r = crate::LENS_RADIUS;
        let side = crate::LENS_SIDE;
        let x0 = (x - r).clamp(0, (self.width as i32 - side as i32).max(0));
        let y0 = (y - r).clamp(0, (self.height as i32 - side as i32).max(0));
        let image = self
            .conn
            .get_image(ImageFormat::Z_PIXMAP, self.root, x0 as i16, y0 as i16, side as u16, side as u16, !0)
            .map_err(err)?
            .reply()
            .map_err(err)?;
        let setup = self.conn.setup();
        let bpp = setup.pixmap_formats.iter().find(|f| f.depth == image.depth).map(|f| f.bits_per_pixel as usize / 8).unwrap_or(4);
        let lsb = setup.image_byte_order == x11rb::protocol::xproto::ImageOrder::LSB_FIRST;
        let stride = image.data.len() / side;
        let mut out = vec![0u8; side * side * 4];
        // Re-centre: near an edge the read square is shifted, the lens still centres on the pointer.
        let (ox, oy) = (x - r - x0, y - r - y0);
        for row in 0..side as i32 {
            for col in 0..side as i32 {
                let (sx, sy) = ((col + ox).clamp(0, side as i32 - 1) as usize, (row + oy).clamp(0, side as i32 - 1) as usize);
                let px = &image.data[sy * stride + sx * bpp..][..bpp.min(4)];
                let v = px.iter().enumerate().fold(0u32, |a, (i, b)| a | (*b as u32) << if lsb { 8 * i } else { 8 * (bpp - 1 - i) });
                let d = (row as usize * side + col as usize) * 4;
                out[d] = (v >> 16) as u8;
                out[d + 1] = (v >> 8) as u8;
                out[d + 2] = v as u8;
                out[d + 3] = 255;
            }
        }
        Ok(out)
    }
}

impl Drop for LensSession {
    fn drop(&mut self) {
        let _ = self.conn.ungrab_pointer(CURRENT_TIME);
        let _ = self.conn.ungrab_keyboard(CURRENT_TIME);
        let _ = self.conn.free_cursor(self.cursor);
        let _ = self.conn.close_font(self.font);
        let _ = self.conn.flush();
    }
}
