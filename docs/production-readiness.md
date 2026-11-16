# Production rollout

The existing Arabic glass UI and offline daily palette work are preserved. Each implementation phase is verified before the next begins.

## 1. Quick picking and recovery

Implemented automatic copy (HEX/RGB/HSL, HEX case and prefix), tray actions for copy/edit, optional global shortcuts with conflict reporting (X11, Win32, macOS Carbon, Wayland portal), persistent recoverable errors, 40-step undo/redo with a memory budget, atomic saves and last-known-good recovery. Shortcuts are off until enabled. F8 remains local to the window. Escape remains owned by the active picker. Normal Wayland picking still uses the system single-color picker; sharing is opt-in.

Verified locally: .NET build; 24 Rust tests; desktop persistence/recovery tests; real Xvfb capture, stationary-pixel updates, window hiding, Escape, undo/redo; shortcut conflict/activation/release; isolated system picker and PipeWire live sharing.

## 2. Release hardening

Full backup bundles include daily palette state; legacy workspace-only backups still import without replacing daily state. A durable restore journal rolls back interrupted two-file restores. Support reports omit palettes, images and file paths. Development packages include a file integrity manifest. CI runs desktop data tests on Windows and both macOS architectures, plus native Linux UI/portal tests.

`python scripts/build.py --packages --release` requires native-host signing on Windows/macOS. Windows: install the certificate into the build account's certificate store and set `PICKERHUB_WINDOWS_CERT_SHA1`. macOS: install a Developer ID Application identity, store notary credentials in the keychain, and set `PICKERHUB_APPLE_IDENTITY` and `PICKERHUB_NOTARY_PROFILE`. Builds verify signatures; macOS also notarizes, staples and assesses Gatekeeper acceptance. Credentials are never committed. Unsigned CI artifacts cannot become regular public releases; an explicitly enabled unsigned prerelease is labelled as such.

Signing follows [Microsoft's SignTool guidance](https://learn.microsoft.com/windows/win32/seccrypto/signtool), [.NET macOS publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos), and [Apple notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).

External release gates still require actual Windows/macOS hosts and signing identities. A hash manifest detects corruption but is not an authenticated update channel. Automatic updating is not enabled without a trusted, signed release feed.

## Verification commands

```sh
cargo test --locked
python3 scripts/build.py --packages
./dist/native/linux-x64/PickerHub/PickerHub --self-test
python3 tests/release_checks.py
xvfb-run -a -s '-screen 0 1400x900x24 -noreset' python3 tests/native_x11.py
xvfb-run -a python3 tests/hotkey_x11.py
# Requires openbox; the window manager runs only inside this Xvfb session.
xvfb-run -a -s '-screen 0 1920x1200x24 -noreset' sh tests/run_ui_test.sh
dbus-run-session -- xvfb-run -a -s '-screen 0 1400x900x24 -noreset' sh tests/run_portal_test.sh
```

Before public release, test real mixed-DPI monitors, negative display origins, unplug/replug, suspend/resume, GNOME and KDE, native Windows and macOS, keyboard/screen-reader navigation and actual installation/upgrade. Automated Xvfb tests do not establish those results.

## 3. Library efficiency and performance

Implemented a searchable grid/list plus the existing card deck, favorites, project labels, multi-selection, copy/export/move/delete selection and duplicate removal. Rendering is bounded to 48 colors per page. Color descriptions use a thread-safe 512-entry cache. Glass surface bookkeeping retains attached keys only; surfaces remain disposed on the render thread. Spectrum editing preserves its control and keyboard focus; keyboard adjustments commit on release. Image decode, downsampling and extraction run on a cancellable worker, with a 32 MB input limit, 40 MP decode limit and 768 px preview. JPEG codecs can downsample during decoding.

Arabic and English interface catalogs are bundled; language changes apply immediately. Existing user names are preserved. Some curated daily-palette story content remains in Arabic. System theme, reduced motion and reduced transparency are available. Accessibility names, live error announcements and keyboard undo are included; a full screen-reader audit still requires real platform testing.

Verified locally: 2,048-color filtering and bounded UI creation, bulk operations, concurrent native color requests, cancellation, image color/size correctness, editor focus, repeated page mounting, all seven pages in Arabic and English, compact light layouts, settings and semantic dialogs. Native screenshots are generated in `dist/qa`. Local observations were roughly 40–55 ms for 1,500 cached/uncached metadata requests and 38–40 ms for a generated 1920×1080 image; these are not cross-device latency guarantees.

## 4. Semantic exports and discovery

Implemented independently saved semantic roles, CSS variables and typed DTCG color tokens, explicit light/dark exports for daily palettes, WCAG contrast reports, a contrast matrix capped at 12 colors, and approximate protanopia/deuteranopia/tritanopia previews in linear sRGB. Daily palettes are archived locally (up to 90 entries), with export and save-to-library actions. Brand primary/accent locks affect daily exports, with contrast recalculated. Existing daily cooldown rules are preserved. Multi-pick is available from the tray and palette page on live-lens backends; Escape returns to the workbench.

OKLCH is available as an output format for the existing 8-bit sRGB model. CMYK is explicitly labelled approximate. This does not add wide-gamut/HDR capture or an ICC print pipeline. Semantic roles are independent saved color values, so editing a regular swatch does not silently alter an exported role.

Token structure follows the [DTCG Color Module 2025.10](https://www.w3.org/community/reports/design-tokens/CG-FINAL-color-20251028/). OKLCH derives from [Björn Ottosson's Oklab model](https://bottosson.github.io/posts/oklab/). Vision previews use Machado et al. severity-1 matrices, also documented in the [QGIS preview implementation](https://api.qgis.org/api/master/qgsprevieweffect_8cpp_source.html).

## Release candidate 0.4.0

Workspace schema 2 reads and upgrades schema 1. Older application versions reject schema 2 instead of silently deleting newer settings. Full backups and recovery copies should be retained before rolling back to 0.3.x. No user workspace was modified during these isolated tests.

Additional launch gates: provision signing accounts, test native Windows/macOS packages and real monitor/desktop combinations, finish English editorial translations and screen-reader review, then prepare public release/store metadata. A signed automatic-update feed, wide-gamut color editing and drag-and-drop ordering remain future roadmap work. Existing up/down controls in card view still support ordering. No analytics or account service was added.


## 0.4.1 stabilization

The recorded 0.4.0 SIGABRT was an unhandled Avalonia visual-parent exception: `ShowError` reused a `TextBlock` still owned by the previous banner. Each banner now creates its own message control. The UI regression test raises consecutive errors before exercising the rest of the application.

Multi-pick retains one magnifier and one input registration. The test changes an external source window eight times under a stationary pointer and checks every collected color, the unchanged lens window ID, clipboard format, Escape, collapsed navigation, persistence, and one-step undo/redo. Single-pick checks toggle automatic copying without restarting. Settings changes no longer rebuild hidden pages unless appearance requires it. Language refreshes the shell and tray in place.

Workspace edits validate and schedule immutable snapshots on one background writer. Pending bursts coalesce. Atomic writes and the recovery backup remain in use. Normal close flushes outstanding work; failed writes retain the open snapshot, show a retry action and keep the window open. A force kill or power loss during the short pending-write interval can still lose the latest unflushed edit. Tests cover a blocked writer, ordering, coalescing, failure and retry. Restore flushes the writer before replacing the library.

The magnifier redraws only when pixels or zoom change. Hover effects no longer animate control width/margins. Spine transitions are reused. Skia shader/filter wrappers are disposed deterministically. Undo/redo coalesces page reconstruction on the dispatcher: the local 2,048-swatch benchmark dropped from approximately 1,008 ms to 175–185 ms for 24 consecutive actions (a burst benchmark, not an end-to-end frame-rate guarantee).

Release 0.4.1 displays its version in Settings; the package name and desktop-entry metadata derive from the desktop project version. The installed launcher must resolve to `/opt/pickerhub/PickerHub`, matching `/usr/bin/pickerhub`. Legacy per-user launchers can override system entries and should be backed up before removing an identified stale override.

Remaining release work: signed Windows/macOS distribution; real mixed-DPI/multi-monitor hardware coverage on GNOME/KDE, Windows and macOS; long-session memory/frame-time profiling on real GPUs; and a signed update mechanism with rollback. The opt-in diagnostic log was delivered in 0.5.0 as detailed below.


## 0.5.0 preview delivery

The public repository is https://github.com/hussamty/PickerHub (MIT). Native CI builds all four targets; only a successful complete matrix publishes an explicitly unsigned prerelease. No signing certificate is available. A real Windows 11, mixed-DPI and long-session checklist is included in `windows-test.md`; unexecuted hardware cases are not represented as passes.

A per-workspace single-instance activation pipe now owns the file lock before opening UI. An independent-process regression verifies activation. The lens shows each palette addition immediately with a count and the last six swatches. Clipboard acknowledgement runs independently of the picker lifetime. Static Wayland streams retain their cached frame between picks. Extra formats expand on demand, history wraps without clipping, paired controls change layout only at the breakpoint, and the rail compacts for short windows.

Diagnostics are opt-in, local, bounded (64 in-memory events; two 256 KiB disk segments), and written on a background worker. Exception types and operation names are recorded without messages, paths, colors or screen images. Manual support exports include this session's events. Abrupt termination may lose pending diagnostic records.

Still not shipped: publisher-signed automatic installation with authenticated update metadata and binary rollback. Until a Windows certificate and Apple signing/notarization credentials exist, use the documented backup plus side-by-side manual upgrade/rollback. The repository build workflow and signing script are prepared; a hash alone is not treated as a signature. Wide-gamut/HDR fidelity, full English editorial translations and a screen-reader audit remain separate product improvements.


## 0.5.1 visual consistency

Liquid action groups use one unioned path with explicit curved bridges instead of a blur threshold. Connections remain visible with the pointer or keyboard focus, in both themes, without an offscreen blur pass per group. Glass menus and sheets share theme tokens; fields, selected menu rows and tooltip surfaces follow the same material family. Native popup overlays stay inside the app to avoid rectangular transparency artifacts. Color swatches retain their exact color fill.

Automated Arabic and English UI runs now move a real pointer onto action groups, capture both themes, and check menu bounds. The full capture, settings, multi-pick and delayed-clipboard regressions still run before release.


## 0.6.0 workbench and image input

Image Analysis accepts clipboard pixels, copied image files and drag/drop through the same bounded, cancellable import pipeline. The previous result survives cancellation and invalid input. In-process clipboard bitmaps are borrowed, so repeated paste does not dispose the clipboard owner's data. Text fields retain their normal paste behavior.

Color fronts and library cards use exact color fills. Deck animation revisions prevent delayed callbacks from replacing a newly selected card; navigation can interrupt a flip. Tooltip glass uses the existing in-window popup layer. Sheets and menus recompute bounds when the window changes size.

The clipping regression was a ScrollViewer padding/viewport mismatch: the reported scroll end could still leave the footer below the client area. The bounded shell and bottom content margin fix this. UI regressions send real mouse-wheel events and drag the custom scrollbar, then check the actual last button's bounds at 680×600, 1120×700, 1360×860 and 1800×1040 in Arabic and English. The scrollbar does not interpolate offsets or allocate blur surfaces.

Linux/Windows use default custom chrome with native move/resize operations and window state actions. CI's isolated Openbox session checks minimize, maximize, restore, titlebar drag and corner resize. These checks do not prove Windows Snap, real mixed-DPI behavior or macOS clipboard integration; those remain explicit cases in the hardware test guide. macOS retains its native window controls.


## 0.6.1 compatibility and chrome review

The icon-only update is included in this release. The tray previously used a generated yellow disc while other surfaces used the mascot; the tray and titlebar now use the small-size mascot. CI checks ICO source sizes and the actual Windows executable icon resources, ICNS bundle metadata/content, and all eight Debian hicolor assets plus the desktop entry. The application window and tray icons are checked in the native UI run. OS icon caches and dock integration still need observation on real desktops.

The titlebar hides the maximize button in maximized/fullscreen states and restores it in the normal state. Double-click remains available to restore. Native move begins after a four-pixel drag threshold so the first click cannot consume the double-click gesture. The isolated window-manager suite checks the real double-click, drag, corner resize, minimize and restore. The sidebar suite checks intermediate opening width, settled width, closing grace and re-entry, as well as reduced motion. Window-state and pointer behavior on real Windows 11/macOS remains in the manual guide; successful builds are not hardware interaction coverage.
