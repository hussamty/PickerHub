//! macOS: the system's own sampler (`NSColorSampler`), converted to sRGB. It must start on the main
//! thread; the handler runs there too and receives `nil` on Escape.
use crate::{CaptureError, Rgb};
use block2::RcBlock;
use objc2::MainThreadMarker;
use objc2_app_kit::{NSColor, NSColorSampler, NSColorSpace};

/// Starts the sampler. `done` is called once, on the main thread.
pub fn pick(done: impl FnOnce(Result<Option<Rgb>, CaptureError>) + 'static) {
    if MainThreadMarker::new().is_none() {
        done(Err(CaptureError::new("thread", "تعذّر بدء القطّارة.")));
        return;
    }
    let sampler = NSColorSampler::new();
    let done = std::cell::RefCell::new(Some(done));
    let keep = sampler.clone();
    let handler = RcBlock::new(move |color: *mut NSColor| {
        let _alive = &keep; // AppKit copies the block; the sampler stays alive until it reports.
        let result = if color.is_null() {
            Ok(None)
        } else {
            // SAFETY: AppKit passes a valid NSColor for the duration of the call.
            let color = unsafe { &*color };
            match color.colorUsingColorSpace(&NSColorSpace::sRGBColorSpace()) {
                Some(c) => Rgb::from_unit(c.redComponent(), c.greenComponent(), c.blueComponent()).map(Some),
                None => Err(CaptureError::new("invalid_color", "أعادت قطّارة النظام لوناً غير صالح.")),
            }
        };
        if let Some(f) = done.borrow_mut().take() {
            f(result);
        }
    });
    unsafe { sampler.showSamplerWithSelectionHandler(&handler) };
}
