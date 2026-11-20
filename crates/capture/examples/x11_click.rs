//! Test fixture: shows a window of one color on the X server and clicks it through XTEST, again
//! every second, until the app's `workspace.json` has a color in its history or the timeout passes.
//! `x11_click <0xRRGGBB> <timeout ms> <workspace.json> [shortcut]`; with `shortcut`, each try presses
//! Ctrl+Shift+P before clicking.
//! Used by tests/e2e_pick.sh.
// X11 only: elsewhere this example is an empty program, so `cargo test` still builds it.
#[cfg(not(target_os = "linux"))]
fn main() {}

#[cfg(target_os = "linux")]
fn main() {
    x11::main();
}

#[cfg(target_os = "linux")]
mod x11 {
    use std::time::{Duration, Instant};
    use x11rb::connection::Connection;
    use x11rb::protocol::xproto::{ConfigureWindowAux, ConnectionExt, CreateWindowAux, StackMode, WindowClass};
    use x11rb::protocol::xtest::ConnectionExt as _;
    use x11rb::{COPY_DEPTH_FROM_PARENT, CURRENT_TIME};

    pub fn main() {
        let args: Vec<String> = std::env::args().collect();
        let color = u32::from_str_radix(args[1].trim_start_matches("0x"), 16).unwrap();
        let timeout = Duration::from_millis(args[2].parse().unwrap());
        let workspace = std::path::Path::new(&args[3]);
        let shortcut = args.get(4).map(String::as_str) == Some("shortcut");
        let (conn, screen) = x11rb::connect(None).unwrap();
        let root = conn.setup().roots[screen].root;
        let w = conn.generate_id().unwrap();
        conn.create_window(COPY_DEPTH_FROM_PARENT, w, root, 1000, 600, 200, 150, 0, WindowClass::INPUT_OUTPUT, 0,
            &CreateWindowAux::new().background_pixel(color).override_redirect(1)).unwrap();
        conn.map_window(w).unwrap();
        conn.flush().unwrap();
        let keys = shortcut.then(|| {
            let s = conn.setup();
            let map = conn.get_keyboard_mapping(s.min_keycode, s.max_keycode - s.min_keycode + 1).unwrap().reply().unwrap();
            let code = |sym: u32| s.min_keycode + map.keysyms.chunks(map.keysyms_per_keycode as usize).position(|k| k.contains(&sym)).unwrap() as u8;
            [code(0xffe3), code(0xffe1), code(0x70)]
        });
        // The app gets ready in its own time (a cold WebView on CI can take seconds longer), so rather
        // than one click at a guessed moment, try again every second until the app has saved the pick.
        // A click that comes before the eyedropper grabs the pointer lands on this window and does nothing.
        let start = Instant::now();
        std::thread::sleep(Duration::from_millis(1500));
        while !picked(workspace) {
            assert!(start.elapsed() < timeout, "no pick saved to {} after {timeout:?}", workspace.display());
            // Above the app's windows, which may have been mapped after this one.
            conn.configure_window(w, &ConfigureWindowAux::new().stack_mode(StackMode::ABOVE)).unwrap();
            if let Some(keys) = keys {
                for k in keys { conn.xtest_fake_input(2, k, CURRENT_TIME, root, 0, 0, 0).unwrap(); }
                for k in keys.iter().rev() { conn.xtest_fake_input(3, *k, CURRENT_TIME, root, 0, 0, 0).unwrap(); }
                conn.flush().unwrap();
                std::thread::sleep(Duration::from_millis(900));
            }
            conn.xtest_fake_input(6, 0, CURRENT_TIME, root, 1080, 660, 0).unwrap();
            conn.xtest_fake_input(4, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
            conn.xtest_fake_input(5, 1, CURRENT_TIME, root, 0, 0, 0).unwrap();
            conn.flush().unwrap();
            std::thread::sleep(Duration::from_millis(1000));
        }
    }

    /// Whether the saved library has a color in its history: `"history"`, a colon, `[`, then not `]`.
    /// Other edits (turning the shortcut on) save the file too, so its existence alone is not enough.
    fn picked(workspace: &std::path::Path) -> bool {
        let Ok(text) = std::fs::read_to_string(workspace) else { return false };
        let Some(at) = text.find("\"history\"") else { return false };
        let mut rest = text[at + 9..].chars().filter(|c| !c.is_whitespace());
        rest.next() == Some(':') && rest.next() == Some('[') && !matches!(rest.next(), Some(']') | None)
    }
}
