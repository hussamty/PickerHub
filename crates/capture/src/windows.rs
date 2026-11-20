//! Windows: low-level mouse and keyboard hooks for the length of one pick (the shell keeps Alt+Tab),
//! a crosshair while it lasts, and GDI's `GetPixel` on the screen at the click.
use crate::{CaptureError, Rgb};
use std::cell::Cell;
use windows_sys::Win32::Foundation::{LPARAM, LRESULT, POINT, WPARAM};
use windows_sys::Win32::Graphics::Gdi::{CLR_INVALID, GetDC, GetPixel, ReleaseDC};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::VK_ESCAPE;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CallNextHookEx, DispatchMessageW, GetCursorPos, GetMessageW, HC_ACTION, IDC_CROSS, KBDLLHOOKSTRUCT, LoadCursorW, MSG,
    PostQuitMessage, SPI_SETCURSORS, SetCursor, SetWindowsHookExW, SystemParametersInfoW, TranslateMessage,
    UnhookWindowsHookEx, WH_KEYBOARD_LL, WH_MOUSE_LL, WM_KEYDOWN, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEMOVE, WM_RBUTTONDOWN,
};

thread_local! {
    static RESULT: Cell<Option<Option<Rgb>>> = const { Cell::new(None) };
}

fn sample(p: POINT) -> Option<Rgb> {
    // SAFETY: a screen DC is obtained and released on this thread around one GetPixel call.
    unsafe {
        let dc = GetDC(std::ptr::null_mut());
        let c = GetPixel(dc, p.x, p.y);
        ReleaseDC(std::ptr::null_mut(), dc);
        (c != CLR_INVALID).then(|| Rgb((c & 0xff) as u8, ((c >> 8) & 0xff) as u8, ((c >> 16) & 0xff) as u8))
    }
}

unsafe extern "system" fn mouse(code: i32, w: WPARAM, l: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        match w as u32 {
            WM_MOUSEMOVE => unsafe {
                SetCursor(LoadCursorW(std::ptr::null_mut(), IDC_CROSS));
            },
            WM_LBUTTONDOWN => {
                let mut p = POINT { x: 0, y: 0 };
                // SAFETY: p is a valid out-pointer.
                unsafe { GetCursorPos(&mut p) };
                RESULT.set(Some(sample(p)));
                unsafe { PostQuitMessage(0) };
                return 1; // the click picks; it does not reach the window beneath
            }
            WM_LBUTTONUP => return 1,
            WM_RBUTTONDOWN => {
                RESULT.set(Some(None));
                unsafe { PostQuitMessage(0) };
                return 1;
            }
            _ => {}
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

unsafe extern "system" fn keyboard(code: i32, w: WPARAM, l: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 && w as u32 == WM_KEYDOWN {
        // SAFETY: for WH_KEYBOARD_LL, lParam points at a KBDLLHOOKSTRUCT.
        let k = unsafe { &*(l as *const KBDLLHOOKSTRUCT) };
        if k.vkCode == VK_ESCAPE as u32 {
            RESULT.set(Some(None));
            unsafe { PostQuitMessage(0) };
            return 1;
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

/// Runs one pick on the calling thread, which must not own windows the user needs meanwhile.
pub fn pick() -> Result<Option<Rgb>, CaptureError> {
    RESULT.set(None);
    // SAFETY: hooks are installed for this thread's message loop and always removed before returning.
    unsafe {
        let module = GetModuleHandleW(std::ptr::null());
        let m = SetWindowsHookExW(WH_MOUSE_LL, Some(mouse), module, 0);
        let k = SetWindowsHookExW(WH_KEYBOARD_LL, Some(keyboard), module, 0);
        if m.is_null() || k.is_null() {
            if !m.is_null() {
                UnhookWindowsHookEx(m);
            }
            if !k.is_null() {
                UnhookWindowsHookEx(k);
            }
            return Err(CaptureError::new("hook", "تعذّر بدء القطّارة."));
        }
        let mut msg: MSG = std::mem::zeroed();
        while RESULT.get().is_none() && GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        UnhookWindowsHookEx(m);
        UnhookWindowsHookEx(k);
        // Restore the system cursors in case the crosshair outlived the hook.
        SystemParametersInfoW(SPI_SETCURSORS, 0, std::ptr::null_mut(), 0);
    }
    Ok(RESULT.get().flatten())
}

// ---------------------------------------------------------------- live lens

use crate::{LENS_RADIUS, LENS_SIDE, LensAction, Patch};
use std::collections::VecDeque;
use std::sync::Mutex;
use windows_sys::Win32::Graphics::Gdi::{
    BI_RGB, BITMAPINFO, BITMAPINFOHEADER, BitBlt, CAPTUREBLT, CreateCompatibleDC, CreateDIBSection, DIB_RGB_COLORS, DeleteDC, DeleteObject,
    SRCCOPY, SelectObject,
};
use windows_sys::Win32::System::Threading::GetCurrentThreadId;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{GetAsyncKeyState, VK_CONTROL, VK_DOWN, VK_LEFT, VK_LWIN, VK_RETURN, VK_RIGHT, VK_RWIN, VK_UP};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    GetSystemMetrics, MSLLHOOKSTRUCT, PostThreadMessageW, SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN, SetCursorPos, WM_KEYUP,
    WM_MOUSEWHEEL, WM_QUIT, WM_RBUTTONUP, WM_SYSKEYUP,
};

/// What the hooks saw, and which key or button they are still swallowing until it is released
/// (desktop/native/windows.c): only picking actions are taken; Alt+Tab and every other key pass.
struct Held {
    actions: VecDeque<LensAction>,
    key: u32,
    button: bool,
}
static HELD: Mutex<Held> = Mutex::new(Held { actions: VecDeque::new(), key: 0, button: false });

unsafe extern "system" fn lens_mouse(code: i32, w: WPARAM, l: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        // SAFETY: for WH_MOUSE_LL, lParam points at an MSLLHOOKSTRUCT.
        let e = unsafe { &*(l as *const MSLLHOOKSTRUCT) };
        let mut held = HELD.lock().unwrap();
        let action = match w as u32 {
            WM_LBUTTONDOWN => Some(LensAction::Pick(e.pt.x, e.pt.y)),
            WM_RBUTTONDOWN => Some(LensAction::Cancel),
            WM_MOUSEWHEEL => Some(LensAction::Zoom(if ((e.mouseData >> 16) as i16) > 0 { 1 } else { -1 })),
            WM_LBUTTONUP | WM_RBUTTONUP if held.button => {
                held.button = false;
                return 1;
            }
            _ => None,
        };
        if let Some(a) = action {
            if matches!(a, LensAction::Pick(..) | LensAction::Cancel) {
                held.button = true;
            }
            held.actions.push_back(a);
            return 1;
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

unsafe extern "system" fn lens_keyboard(code: i32, w: WPARAM, l: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        // SAFETY: for WH_KEYBOARD_LL, lParam points at a KBDLLHOOKSTRUCT.
        let key = unsafe { &*(l as *const KBDLLHOOKSTRUCT) }.vkCode;
        let mut held = HELD.lock().unwrap();
        if (w as u32 == WM_KEYUP || w as u32 == WM_SYSKEYUP) && held.key == key {
            held.key = 0;
            return 1;
        }
        let modified = unsafe { GetAsyncKeyState(VK_CONTROL as i32) as u16 & 0x8000 != 0 || GetAsyncKeyState(VK_LWIN as i32) as u16 & 0x8000 != 0 || GetAsyncKeyState(VK_RWIN as i32) as u16 & 0x8000 != 0 };
        if w as u32 == WM_KEYDOWN && !modified {
            let action = match key as u16 {
                k if k == VK_ESCAPE => Some(LensAction::Cancel),
                k if k == VK_RETURN => Some(LensAction::PickHere),
                k if k == VK_LEFT => Some(LensAction::Nudge(-1, 0)),
                k if k == VK_RIGHT => Some(LensAction::Nudge(1, 0)),
                k if k == VK_UP => Some(LensAction::Nudge(0, -1)),
                k if k == VK_DOWN => Some(LensAction::Nudge(0, 1)),
                0xBB | 0x6B => Some(LensAction::Zoom(1)),  // = + and keypad +
                0xBD | 0x6D => Some(LensAction::Zoom(-1)), // - and keypad -
                _ => None,
            };
            if let Some(a) = action {
                held.key = key;
                held.actions.push_back(a);
                return 1;
            }
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

/// The hooks live on a thread of their own with a message loop, for the length of one session.
pub struct LensSession {
    thread: u32,
    join: Option<std::thread::JoinHandle<()>>,
}

impl LensSession {
    pub fn open() -> Result<Self, CaptureError> {
        {
            let mut held = HELD.lock().unwrap();
            held.actions.clear();
            held.key = 0;
            held.button = false;
        }
        let (tx, rx) = std::sync::mpsc::channel();
        let join = std::thread::spawn(move || unsafe {
            let module = GetModuleHandleW(std::ptr::null());
            let m = SetWindowsHookExW(WH_MOUSE_LL, Some(lens_mouse), module, 0);
            let k = SetWindowsHookExW(WH_KEYBOARD_LL, Some(lens_keyboard), module, 0);
            let _ = tx.send(if m.is_null() || k.is_null() { 0 } else { GetCurrentThreadId() });
            if !m.is_null() && !k.is_null() {
                let mut msg: MSG = std::mem::zeroed();
                while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }
            if !m.is_null() {
                UnhookWindowsHookEx(m);
            }
            if !k.is_null() {
                UnhookWindowsHookEx(k);
            }
            SystemParametersInfoW(SPI_SETCURSORS, 0, std::ptr::null_mut(), 0);
        });
        let thread = rx.recv().unwrap_or(0);
        if thread == 0 {
            let _ = join.join();
            return Err(CaptureError::new("hook", "تعذّر بدء القطّارة. أغلق أداة الالتقاط الأخرى وأعد المحاولة."));
        }
        Ok(Self { thread, join: Some(join) })
    }
}

impl crate::LensSource for LensSession {
    fn poll(&self) -> Result<Vec<LensAction>, CaptureError> {
        Ok(HELD.lock().unwrap().actions.drain(..).collect())
    }
    fn pointer(&self) -> Result<(i32, i32), CaptureError> {
        let mut p = POINT { x: 0, y: 0 };
        // SAFETY: p is a valid out-pointer; the crosshair is re-applied as the pointer moves.
        unsafe {
            GetCursorPos(&mut p);
            SetCursor(LoadCursorW(std::ptr::null_mut(), IDC_CROSS));
        }
        Ok((p.x, p.y))
    }
    fn nudge(&self, dx: i32, dy: i32) -> Result<(), CaptureError> {
        let (x, y) = self.pointer()?;
        unsafe { SetCursorPos(x + dx, y + dy) };
        Ok(())
    }
    /// One BitBlt of the square around the pointer into a 32-bit DIB, clamped to the virtual screen.
    fn sample(&self, x: i32, y: i32) -> Result<Patch, CaptureError> {
        let side = LENS_SIDE as i32;
        let fail = || CaptureError::new("gdi", "تعذّرت قراءة البكسل من الشاشة.");
        // SAFETY: GDI objects are created, used and released on this thread within the call.
        unsafe {
            let (vx, vy) = (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN));
            let (vw, vh) = (GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
            let x0 = (x - LENS_RADIUS).clamp(vx, vx + (vw - side).max(0));
            let y0 = (y - LENS_RADIUS).clamp(vy, vy + (vh - side).max(0));
            let screen = GetDC(std::ptr::null_mut());
            let memory = CreateCompatibleDC(screen);
            let mut info: BITMAPINFO = std::mem::zeroed();
            info.bmiHeader = BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: side,
                biHeight: -side, // top-down rows
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB,
                ..std::mem::zeroed()
            };
            let mut bits: *mut core::ffi::c_void = std::ptr::null_mut();
            let dib = CreateDIBSection(memory, &info, DIB_RGB_COLORS, &mut bits, std::ptr::null_mut(), 0);
            if dib.is_null() || bits.is_null() {
                DeleteDC(memory);
                ReleaseDC(std::ptr::null_mut(), screen);
                return Err(fail());
            }
            let old = SelectObject(memory, dib);
            let ok = BitBlt(memory, 0, 0, side, side, screen, x0, y0, SRCCOPY | CAPTUREBLT) != 0;
            let data = std::slice::from_raw_parts(bits as *const u8, (side * side * 4) as usize);
            let (ox, oy) = (x - LENS_RADIUS - x0, y - LENS_RADIUS - y0);
            let mut out = vec![0u8; LENS_SIDE * LENS_SIDE * 4];
            for row in 0..side {
                for col in 0..side {
                    let (sx, sy) = ((col + ox).clamp(0, side - 1), (row + oy).clamp(0, side - 1));
                    let s = ((sy * side + sx) * 4) as usize;
                    let d = ((row * side + col) * 4) as usize;
                    out[d] = data[s + 2];
                    out[d + 1] = data[s + 1];
                    out[d + 2] = data[s];
                    out[d + 3] = 255;
                }
            }
            SelectObject(memory, old);
            DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(std::ptr::null_mut(), screen);
            if ok { Ok(out) } else { Err(fail()) }
        }
    }
}

impl Drop for LensSession {
    fn drop(&mut self) {
        unsafe { PostThreadMessageW(self.thread, WM_QUIT, 0, 0) };
        if let Some(j) = self.join.take() {
            let _ = j.join();
        }
    }
}
