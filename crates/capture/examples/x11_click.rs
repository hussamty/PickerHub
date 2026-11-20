//! Test fixture: shows a window of one color on the X server, waits, then clicks it through XTEST.
//! `x11_click <0xRRGGBB> <delay ms> [shortcut]`; with `shortcut`, presses Ctrl+Shift+P first.
//! Used by tests/e2e_pick.sh.
use std::time::Duration;
use x11rb::connection::Connection;
use x11rb::protocol::xproto::{ConnectionExt, CreateWindowAux, WindowClass};
use x11rb::protocol::xtest::ConnectionExt as _;
use x11rb::{COPY_DEPTH_FROM_PARENT, CURRENT_TIME};

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let color = u32::from_str_radix(args[1].trim_start_matches("0x"), 16).unwrap();
    let delay: u64 = args[2].parse().unwrap();
    let (conn, screen) = x11rb::connect(None).unwrap();
    let root = conn.setup().roots[screen].root;
    let w = conn.generate_id().unwrap();
    conn.create_window(COPY_DEPTH_FROM_PARENT, w, root, 1000, 600, 200, 150, 0, WindowClass::INPUT_OUTPUT, 0,
        &CreateWindowAux::new().background_pixel(color).override_redirect(1)).unwrap();
    conn.map_window(w).unwrap();
    conn.flush().unwrap();
    std::thread::sleep(Duration::from_millis(delay));
    if args.get(3).map(String::as_str) == Some("shortcut") {
        let s = conn.setup();
        let map = conn.get_keyboard_mapping(s.min_keycode, s.max_keycode - s.min_keycode + 1).unwrap().reply().unwrap();
        let code = |sym: u32| s.min_keycode + map.keysyms.chunks(map.keysyms_per_keycode as usize).position(|k| k.contains(&sym)).unwrap() as u8;
        let keys = [code(0xffe3), code(0xffe1), code(0x70)];
        for k in keys { conn.xtest_fake_input(2, k, CURRENT_TIME, root, 0, 0, 0).unwrap(); }
        for k in keys.iter().rev() { conn.xtest_fake_input(3, *k, CURRENT_TIME, root, 0, 0, 0).unwrap(); }
        conn.flush().unwrap();
        std::thread::sleep(Duration::from_millis(900));
    }
    conn.xtest_fake_input(6, 0, CURRENT_TIME, root, 1080, 660, 0).unwrap();
    conn.xtest_fake_input(4, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.xtest_fake_input(5, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.flush().unwrap();
    std::thread::sleep(Duration::from_millis(1500));
}
