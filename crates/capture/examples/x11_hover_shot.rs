//! Test fixture: shows a striped window, moves the pointer over it, captures the whole X screen to
//! a PNG (to see the live lens), then presses Escape. `x11_hover_shot <delay ms> <out.png>`
// X11 only: elsewhere this example is an empty program, so `cargo test` still builds it.
#[cfg(not(target_os = "linux"))]
fn main() {}

#[cfg(target_os = "linux")]
fn main() {
    x11::main();
}

#[cfg(target_os = "linux")]
mod x11 {
    use std::time::Duration;
    use x11rb::connection::Connection;
    use x11rb::protocol::xproto::{ConnectionExt, CreateWindowAux, ImageFormat, Rectangle, WindowClass, CreateGCAux};
    use x11rb::protocol::xtest::ConnectionExt as _;
    use x11rb::{COPY_DEPTH_FROM_PARENT, CURRENT_TIME};

    pub fn main() {
        let args: Vec<String> = std::env::args().collect();
        let delay: u64 = args[1].parse().unwrap();
        let (conn, screen) = x11rb::connect(None).unwrap();
        let s = &conn.setup().roots[screen];
        let (root, sw, sh) = (s.root, s.width_in_pixels, s.height_in_pixels);
        let w = conn.generate_id().unwrap();
        conn.create_window(COPY_DEPTH_FROM_PARENT, w, root, 300, 200, 600, 400, 0, WindowClass::INPUT_OUTPUT, 0,
            &CreateWindowAux::new().background_pixel(0x2E8BC0).override_redirect(1)).unwrap();
        conn.map_window(w).unwrap();
        let gc = conn.generate_id().unwrap();
        conn.create_gc(gc, w, &CreateGCAux::new().foreground(0xFFF000)).unwrap();
        conn.flush().unwrap();
        std::thread::sleep(Duration::from_millis(300));
        let stripes: Vec<Rectangle> = (0..30).map(|i| Rectangle { x: i * 20, y: 0, width: 10, height: 400 }).collect();
        conn.poly_fill_rectangle(w, gc, &stripes).unwrap();
        conn.flush().unwrap();
        std::thread::sleep(Duration::from_millis(delay));
        conn.xtest_fake_input(6, 0, CURRENT_TIME, root, 455, 390, 0).unwrap();
        conn.flush().unwrap();
        std::thread::sleep(Duration::from_millis(1200));
        let img = conn.get_image(ImageFormat::Z_PIXMAP, root, 0, 0, sw, sh, !0).unwrap().reply().unwrap();
        let mut rgb = Vec::with_capacity(sw as usize * sh as usize * 3);
        for px in img.data.chunks(4) { rgb.extend_from_slice(&[px[2], px[1], px[0]]); }
        let file = std::io::BufWriter::new(std::fs::File::create(&args[2]).unwrap());
        let mut enc = png::Encoder::new(file, sw as u32, sh as u32);
        enc.set_color(png::ColorType::Rgb);
        enc.write_header().unwrap().write_image_data(&rgb).unwrap();
        let s = conn.setup();
        let map = conn.get_keyboard_mapping(s.min_keycode, s.max_keycode - s.min_keycode + 1).unwrap().reply().unwrap();
        let esc = s.min_keycode + map.keysyms.chunks(map.keysyms_per_keycode as usize).position(|k| k.contains(&0xff1b)).unwrap() as u8;
        conn.xtest_fake_input(2, esc, CURRENT_TIME, root, 0, 0, 0).unwrap();
        conn.xtest_fake_input(3, esc, CURRENT_TIME, root, 0, 0, 0).unwrap();
        conn.flush().unwrap();
        std::thread::sleep(Duration::from_millis(500));
    }
}
