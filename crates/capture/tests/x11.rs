//! Real X11 picking under Xvfb: `xvfb-run -a cargo test -p pickerhub-capture --test x11 -- --ignored`
#![cfg(target_os = "linux")]
use std::time::Duration;
use x11rb::connection::Connection;
use x11rb::protocol::xproto::{ConnectionExt, CreateWindowAux, WindowClass};
use x11rb::protocol::xtest::ConnectionExt as _;
use x11rb::{COPY_DEPTH_FROM_PARENT, CURRENT_TIME};

const MOTION: u8 = 6;
const PRESS: u8 = 4;
const RELEASE: u8 = 5;
const KEY_PRESS: u8 = 2;
const KEY_RELEASE: u8 = 3;

fn window(conn: &impl Connection, root: u32, pixel: u32) -> u32 {
    let w = conn.generate_id().unwrap();
    conn.create_window(COPY_DEPTH_FROM_PARENT, w, root, 100, 100, 200, 150, 0, WindowClass::INPUT_OUTPUT, 0, &CreateWindowAux::new().background_pixel(pixel).override_redirect(1))
        .unwrap();
    conn.map_window(w).unwrap();
    conn.flush().unwrap();
    std::thread::sleep(Duration::from_millis(200));
    w
}

#[test]
#[ignore = "needs an X server; run under xvfb-run"]
fn a_click_returns_the_pixel_and_escape_cancels() {
    let (conn, screen) = x11rb::connect(None).unwrap();
    let root = conn.setup().roots[screen].root;
    window(&conn, root, 0x336699);

    let picking = std::thread::spawn(pickerhub_capture::x11::pick);
    std::thread::sleep(Duration::from_millis(400));
    conn.xtest_fake_input(MOTION, 0, CURRENT_TIME, root, 180, 160, 0).unwrap();
    conn.xtest_fake_input(PRESS, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.xtest_fake_input(RELEASE, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.flush().unwrap();
    assert_eq!(picking.join().unwrap().unwrap().map(|c| c.hex()), Some("#336699".into()));

    // Escape gives the screen back with no color.
    let escape = {
        let s = conn.setup();
        let map = conn.get_keyboard_mapping(s.min_keycode, s.max_keycode - s.min_keycode + 1).unwrap().reply().unwrap();
        s.min_keycode + map.keysyms.chunks(map.keysyms_per_keycode as usize).position(|k| k.contains(&0xff1b)).unwrap() as u8
    };
    let picking = std::thread::spawn(pickerhub_capture::x11::pick);
    std::thread::sleep(Duration::from_millis(400));
    conn.xtest_fake_input(KEY_PRESS, escape, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.xtest_fake_input(KEY_RELEASE, escape, CURRENT_TIME, root, 0, 0, 0).unwrap();
    conn.flush().unwrap();
    assert_eq!(picking.join().unwrap().unwrap(), None);

    // A pixel outside every window reads the root background, not an error.
    let root_pixel = pickerhub_capture::x11::pixel_at(&conn, root, 5, 5).unwrap();
    let _ = root_pixel;
}
