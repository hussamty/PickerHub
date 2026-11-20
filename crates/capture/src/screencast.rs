//! Wayland live lens: one user-approved ScreenCast session per app run (desktop/WaylandCapture.cs).
//! Each shared monitor becomes a GStreamer pipeline `pipewiresrc ! videoconvert ! appsink`; a sample
//! reads a square of the newest frame. GStreamer is loaded at run time, as 1.x did, so the app has
//! no build-time dependency on it and still runs (with the system picker) where it is missing.
use crate::{CaptureError, LENS_RADIUS, LENS_SIDE, Patch};
use ashpd::desktop::PersistMode;
use ashpd::desktop::screencast::{CursorMode, Screencast, SelectSourcesOptions, SourceType};
use std::ffi::{CString, c_char, c_int, c_uint, c_void};
use std::os::fd::{AsRawFd, OwnedFd};
use std::sync::OnceLock;

type P = *mut c_void;

#[repr(C)]
struct MapInfo {
    memory: P,
    flags: c_int,
    data: *mut u8,
    size: usize,
    maxsize: usize,
    user: [P; 4],
    reserved: [P; 4],
}

struct Gst {
    _core: libloading::Library,
    _app: libloading::Library,
    parse_launch: unsafe extern "C" fn(*const c_char, *mut P) -> P,
    bin_get_by_name: unsafe extern "C" fn(P, *const c_char) -> P,
    set_state: unsafe extern "C" fn(P, c_int) -> c_int,
    unref: unsafe extern "C" fn(P),
    sample_buffer: unsafe extern "C" fn(P) -> P,
    sample_caps: unsafe extern "C" fn(P) -> P,
    sample_unref: unsafe extern "C" fn(P),
    caps_structure: unsafe extern "C" fn(P, c_uint) -> P,
    structure_int: unsafe extern "C" fn(P, *const c_char, *mut c_int) -> c_int,
    try_pull: unsafe extern "C" fn(P, u64) -> P,
    is_eos: unsafe extern "C" fn(P) -> c_int,
    map: unsafe extern "C" fn(P, *mut MapInfo, c_int) -> c_int,
    unmap: unsafe extern "C" fn(P, *mut MapInfo),
}
// SAFETY: the function pointers are plain C entry points; GStreamer objects are used from one thread at a time.
unsafe impl Send for Gst {}
unsafe impl Sync for Gst {}

fn missing() -> CaptureError {
    CaptureError::new("gstreamer", "ثبّت GStreamer وملحق PipeWire لالتقاط الشاشة دون وميض.")
}

fn gst() -> Result<&'static Gst, CaptureError> {
    static GST: OnceLock<Option<Gst>> = OnceLock::new();
    GST.get_or_init(|| unsafe {
        let core = libloading::Library::new("libgstreamer-1.0.so.0").ok()?;
        let app = libloading::Library::new("libgstapp-1.0.so.0").ok()?;
        let init: libloading::Symbol<unsafe extern "C" fn(*mut c_int, *mut P)> = core.get(b"gst_init").ok()?;
        init(std::ptr::null_mut(), std::ptr::null_mut());
        macro_rules! sym {
            ($lib:expr, $name:literal) => {
                *$lib.get($name).ok()?
            };
        }
        Some(Gst {
            parse_launch: sym!(core, b"gst_parse_launch"),
            bin_get_by_name: sym!(core, b"gst_bin_get_by_name"),
            set_state: sym!(core, b"gst_element_set_state"),
            unref: sym!(core, b"gst_object_unref"),
            sample_buffer: sym!(core, b"gst_sample_get_buffer"),
            sample_caps: sym!(core, b"gst_sample_get_caps"),
            sample_unref: sym!(core, b"gst_sample_unref"),
            caps_structure: sym!(core, b"gst_caps_get_structure"),
            structure_int: sym!(core, b"gst_structure_get_int"),
            map: sym!(core, b"gst_buffer_map"),
            unmap: sym!(core, b"gst_buffer_unmap"),
            try_pull: sym!(app, b"gst_app_sink_try_pull_sample"),
            is_eos: sym!(app, b"gst_app_sink_is_eos"),
            _core: core,
            _app: app,
        })
    })
    .as_ref()
    .ok_or_else(missing)
}

/// One frame kept still while the user picks: what the surface shows and what the lens reads.
pub struct Frozen {
    pub width: u32,
    pub height: u32,
    /// RGBA, row by row.
    pub rgba: Vec<u8>,
    /// The monitor's logical place and size, to map pointer positions onto the frame.
    pub position: (i32, i32),
    pub size: (i32, i32),
}

impl Frozen {
    /// The square around `(x, y)` in the monitor's logical pixels.
    pub fn sample(&self, x: f64, y: f64) -> Patch {
        let (w, h) = (self.width as i64, self.height as i64);
        let (sx, sy) = (w as f64 / self.size.0.max(1) as f64, h as f64 / self.size.1.max(1) as f64);
        let (cx, cy) = ((x * sx) as i64, (y * sy) as i64);
        let mut out = vec![0u8; LENS_SIDE * LENS_SIDE * 4];
        for row in 0..LENS_SIDE as i64 {
            let fy = (cy + row - LENS_RADIUS as i64).clamp(0, h - 1) as usize;
            for col in 0..LENS_SIDE as i64 {
                let fx = (cx + col - LENS_RADIUS as i64).clamp(0, w - 1) as usize;
                let src = (fy * w as usize + fx) * 4;
                let dst = (row as usize * LENS_SIDE + col as usize) * 4;
                out[dst..dst + 3].copy_from_slice(&self.rgba[src..src + 3]);
                out[dst + 3] = 255;
            }
        }
        out
    }
}

/// One monitor's stream.
pub struct Stream {
    pipeline: P,
    sink: P,
    latest: P,
    received: bool,
    /// The monitor's place and size in the compositor's logical space.
    pub position: (i32, i32),
    pub size: (i32, i32),
}
unsafe impl Send for Stream {}

impl Stream {
    fn open(fd: i32, node: u32, position: (i32, i32), size: (i32, i32)) -> Result<Self, CaptureError> {
        let g = gst()?;
        let line = CString::new(format!("pipewiresrc fd={fd} path={node} ! videoconvert ! video/x-raw,format=RGBA ! appsink name=sample max-buffers=1 drop=true sync=false")).unwrap();
        let mut error: P = std::ptr::null_mut();
        // SAFETY: GStreamer C API with valid, NUL-terminated strings; failures are checked.
        unsafe {
            let pipeline = (g.parse_launch)(line.as_ptr(), &mut error);
            if !error.is_null() || pipeline.is_null() {
                return Err(CaptureError::new("pipewire", "تعذّر تشغيل قارئ PipeWire. تحقق من تثبيت gstreamer1.0-pipewire وملحقات الفيديو."));
            }
            let name = CString::new("sample").unwrap();
            let sink = (g.bin_get_by_name)(pipeline, name.as_ptr());
            if sink.is_null() || (g.set_state)(pipeline, 4) == 0 {
                (g.unref)(pipeline);
                return Err(CaptureError::new("stream", "تعذّر بدء تدفق الشاشة."));
            }
            Ok(Self { pipeline, sink, latest: std::ptr::null_mut(), received: false, position, size })
        }
    }

    /// Drops the frame held, so the next sample waits for a fresh one (after the lens moved away).
    pub fn drain(&mut self) {
        let Ok(g) = gst() else { return };
        unsafe {
            if !self.latest.is_null() {
                (g.sample_unref)(self.latest);
                self.latest = std::ptr::null_mut();
            }
            let queued = (g.try_pull)(self.sink, 0);
            if !queued.is_null() {
                (g.sample_unref)(queued);
            }
        }
    }

    /// A whole frame. With `fresh`, the held frame is dropped first and a new one awaited (up to
    /// `wait`), so a window that just left the screen is not in it; compositors send a frame on damage.
    pub fn freeze(&mut self, fresh: bool, wait: std::time::Duration) -> Result<Option<Frozen>, CaptureError> {
        let g = gst()?;
        unsafe {
            let started = std::time::Instant::now();
            if fresh && !self.latest.is_null() {
                let mut next = (g.try_pull)(self.sink, 0);
                while next.is_null() && started.elapsed() < wait {
                    std::thread::sleep(std::time::Duration::from_millis(10));
                    next = (g.try_pull)(self.sink, 0);
                }
                if !next.is_null() {
                    (g.sample_unref)(self.latest);
                    self.latest = next;
                }
            }
            while self.latest.is_null() && started.elapsed() < wait * 2 {
                let next = (g.try_pull)(self.sink, 0);
                if next.is_null() { std::thread::sleep(std::time::Duration::from_millis(10)); } else { self.latest = next; self.received = true; }
            }
            if self.latest.is_null() {
                return Ok(None);
            }
            let caps = (g.caps_structure)((g.sample_caps)(self.latest), 0);
            let (mut w, mut h) = (0, 0);
            let (wk, hk) = (CString::new("width").unwrap(), CString::new("height").unwrap());
            if (g.structure_int)(caps, wk.as_ptr(), &mut w) == 0 || (g.structure_int)(caps, hk.as_ptr(), &mut h) == 0 || w <= 0 || h <= 0 || w as i64 * h as i64 > 150_000_000 {
                return Err(CaptureError::new("stream_size", "أبعاد تدفق الشاشة غير صالحة."));
            }
            let buffer = (g.sample_buffer)(self.latest);
            let mut map: MapInfo = std::mem::zeroed();
            if (g.map)(buffer, &mut map, 1) == 0 {
                return Err(CaptureError::new("stream_read", "تعذّرت قراءة بكسلات الشاشة."));
            }
            let len = w as usize * h as usize * 4;
            if map.size < len {
                (g.unmap)(buffer, &mut map);
                return Err(CaptureError::new("stream_frame", "إطار الشاشة غير مكتمل."));
            }
            let rgba = std::slice::from_raw_parts(map.data, len).to_vec();
            (g.unmap)(buffer, &mut map);
            Ok(Some(Frozen { width: w as u32, height: h as u32, rgba, position: self.position, size: self.size }))
        }
    }

    /// Reads the square around `(x, y)`, given in the monitor's logical pixels. `None` until a frame arrives.
    pub fn sample(&mut self, x: f64, y: f64) -> Result<Option<Patch>, CaptureError> {
        let g = gst()?;
        unsafe {
            // Compositors stop sending buffers when the desktop is still: keep the newest sample.
            let next = (g.try_pull)(self.sink, 0);
            if !next.is_null() {
                if !self.latest.is_null() {
                    (g.sample_unref)(self.latest);
                }
                self.latest = next;
                self.received = true;
            }
            if self.received && (g.is_eos)(self.sink) != 0 {
                return Err(CaptureError::new("stream_ended", "توقفت مشاركة الشاشة. أعد فتح القطّارة."));
            }
            if self.latest.is_null() {
                return Ok(None);
            }
            let caps = (g.caps_structure)((g.sample_caps)(self.latest), 0);
            let (mut w, mut h) = (0, 0);
            let (wk, hk) = (CString::new("width").unwrap(), CString::new("height").unwrap());
            if (g.structure_int)(caps, wk.as_ptr(), &mut w) == 0 || (g.structure_int)(caps, hk.as_ptr(), &mut h) == 0 || w <= 0 || h <= 0 || w as i64 * h as i64 > 150_000_000 {
                return Err(CaptureError::new("stream_size", "أبعاد تدفق الشاشة غير صالحة."));
            }
            let buffer = (g.sample_buffer)(self.latest);
            let mut map: MapInfo = std::mem::zeroed();
            if (g.map)(buffer, &mut map, 1) == 0 {
                return Err(CaptureError::new("stream_read", "تعذّرت قراءة بكسلات الشاشة."));
            }
            let stride = w as usize * 4;
            if map.size < stride * h as usize {
                (g.unmap)(buffer, &mut map);
                return Err(CaptureError::new("stream_frame", "إطار الشاشة غير مكتمل."));
            }
            let data = std::slice::from_raw_parts(map.data, map.size);
            // The frame may be at a different scale from the logical monitor (HiDPI).
            let (sx, sy) = (w as f64 / self.size.0.max(1) as f64, h as f64 / self.size.1.max(1) as f64);
            let (cx, cy) = ((x * sx) as i64, (y * sy) as i64);
            let mut out = vec![0u8; LENS_SIDE * LENS_SIDE * 4];
            for row in 0..LENS_SIDE as i64 {
                let fy = (cy + row - LENS_RADIUS as i64).clamp(0, h as i64 - 1) as usize;
                for col in 0..LENS_SIDE as i64 {
                    let fx = (cx + col - LENS_RADIUS as i64).clamp(0, w as i64 - 1) as usize;
                    let src = fy * stride + fx * 4;
                    let dst = (row as usize * LENS_SIDE + col as usize) * 4;
                    out[dst..dst + 3].copy_from_slice(&data[src..src + 3]);
                    out[dst + 3] = 255;
                }
            }
            (g.unmap)(buffer, &mut map);
            Ok(Some(out))
        }
    }
}

impl Drop for Stream {
    fn drop(&mut self) {
        let Ok(g) = gst() else { return };
        unsafe {
            if !self.latest.is_null() {
                (g.sample_unref)(self.latest);
            }
            (g.set_state)(self.pipeline, 1);
            (g.unref)(self.sink);
            (g.unref)(self.pipeline);
        }
    }
}

/// The shared screens, kept for the app run so later picks do not ask again.
pub struct Sharing {
    pub streams: Vec<Stream>,
    _fd: OwnedFd,
    _proxy: Screencast,
}

impl Sharing {
    /// Asks the user (through the portal) to share every monitor. `Ok(None)` when they decline.
    pub async fn start() -> Result<Option<Self>, CaptureError> {
        gst()?; // fail before asking the user to share if the frame reader is unavailable
        let refused = || CaptureError::new("share_refused", "لم يُسمح بمشاركة الشاشة. أعد فتح القطّارة واسمح بمشاركة الشاشات المطلوبة.");
        let fail = |e: ashpd::Error| CaptureError::new("share_failed", "لم تبدأ المشاركة. يمكنك اختيار منتقي النظام من وضع الالتقاط.").with(e);
        let proxy = Screencast::new().await.map_err(fail)?;
        let session = proxy.create_session(Default::default()).await.map_err(fail)?;
        let options = SelectSourcesOptions::default()
            .set_sources(SourceType::MONITOR)
            .set_multiple(true)
            .set_cursor_mode(CursorMode::HIDDEN)
            .set_persist_mode(PersistMode::DoNot);
        match proxy.select_sources(&session, options).await.map_err(fail)?.response() {
            Ok(()) => {}
            Err(ashpd::Error::Response(ashpd::desktop::ResponseError::Cancelled)) => return Ok(None),
            Err(_) => return Err(refused()),
        }
        let streams = match proxy.start(&session, None, Default::default()).await.map_err(fail)?.response() {
            Ok(s) => s,
            Err(ashpd::Error::Response(ashpd::desktop::ResponseError::Cancelled)) => return Ok(None),
            Err(_) => return Err(refused()),
        };
        if streams.streams().is_empty() {
            return Err(CaptureError::new("share_none", "اختر شاشة واحدة على الأقل في نافذة المشاركة."));
        }
        let fd = proxy.open_pipe_wire_remote(&session, Default::default()).await.map_err(fail)?;
        let mut opened = Vec::new();
        for s in streams.streams() {
            opened.push(Stream::open(fd.as_raw_fd(), s.pipe_wire_node_id(), s.position().unwrap_or((0, 0)), s.size().unwrap_or((1, 1)))?);
        }
        std::mem::forget(session); // the session lives as long as the fd and the app
        Ok(Some(Self { streams: opened, _fd: fd, _proxy: proxy }))
    }

    /// The stream that shows a point of the compositor's logical space.
    pub fn at(&mut self, x: f64, y: f64) -> Option<&mut Stream> {
        self.streams.iter_mut().find(|s| {
            let (px, py, w, h) = (s.position.0 as f64, s.position.1 as f64, s.size.0 as f64, s.size.1 as f64);
            x >= px && y >= py && x < px + w && y < py + h
        })
    }
}
