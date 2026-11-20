//! PickerHub 2: the Tauri shell around the Rust color core and library store.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod capture;
mod commands;
mod edit;
mod images;
mod lens;
mod state;
mod system;
mod shot;
mod support;
mod update;

use state::AppState;
use tauri::{Manager, RunEvent};
use tauri_plugin_dialog::{DialogExt, MessageDialogKind};

fn main() {
    support::launched();
    let args: Vec<String> = std::env::args().collect();
    if args.iter().any(|a| a == "--version") {
        println!("PickerHub {}", env!("CARGO_PKG_VERSION"));
        return;
    }
    // --preview-envelope opens the palette-of-the-day envelope in a throwaway library.
    let preview = args.iter().any(|a| a == "--preview-envelope");
    if preview && std::env::var_os("PICKERHUB_DATA_DIR").is_none() {
        let dir = std::env::temp_dir().join(format!("pickerhub-preview-{}", std::process::id()));
        // SAFETY: set before any thread is started.
        unsafe { std::env::set_var("PICKERHUB_DATA_DIR", &dir) };
    }

    let builder = tauri::Builder::default();
    // The updater exists only in builds made with a release key (src-tauri/src/update.rs).
    #[cfg(feature = "updater")]
    let builder = match update::plugin() {
        Some(plugin) => builder.plugin(plugin).manage(update::Pending::default()),
        None => builder,
    };
    builder
        // A second launch only brings the first window forward; it never opens a second library.
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.unminimize();
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        // Only the workbench remembers its place; the lens windows follow the pointer.
        .plugin(tauri_plugin_window_state::Builder::default().with_denylist(&["lens", "overlay-0", "overlay-1", "overlay-2", "overlay-3", "overlay-4", "overlay-5"]).build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_global_shortcut::Builder::new().build())
        .manage(system::System::default())
        .manage(lens::Lens::default())
        .invoke_handler(tauri::generate_handler![
            commands::boot,
            commands::describe,
            commands::names,
            commands::frame,
            commands::from_hsv,
            commands::contrast,
            commands::vision,
            commands::edit,
            commands::undo,
            commands::redo,
            commands::copy,
            commands::export_palette,
            commands::export_colors,
            commands::export_text,
            commands::semantic,
            commands::contrast_checks,
            commands::import_palette,
            commands::backup_export,
            commands::backup_restore,
            commands::daily,
            commands::support_report,
            shot::qa_report,
            images::analyze_image,
            images::analyze_image_file,
            images::paste_image,
            capture::pick,
            capture::pick_many,
            lens::lens_sample,
            lens::lens_backdrop,
            lens::lens_done,
            lens::stop_sharing,
            lens::start_sharing,
            support::log_timing,
            update::update_supported,
            update::update_check,
            update::update_install,
            lens::capture_portal,
            lens::sharing,
            system::tray_labels,
            system::apply_shortcut,
        ])
        .setup(move |app| {
            // Full hinting (a GNOME setting) rounds every glyph advance to whole pixels, and WebKit
            // then loses most word spaces in small Latin text. Slight hinting keeps the vertical
            // snapping and the true advances; it applies to this app only.
            #[cfg(target_os = "linux")]
            if let Some(settings) = gtk::Settings::default() {
                use gtk::prelude::GtkSettingsExt;
                settings.set_gtk_xft_hinting(1);
                settings.set_gtk_xft_hintstyle(Some("hintslight"));
            }
            let folder = pickerhub_store::data_folder();
            match AppState::open(folder, preview) {
                Ok(state) => {
                    app.manage(state);
                }
                Err(e) => {
                    // 1.x holds the lock, or the library is from a newer version: say so and stop.
                    app.dialog().message(e.message()).title("PickerHub").kind(MessageDialogKind::Error).blocking_show();
                    std::process::exit(1);
                }
            }
            // A desktop without a tray host still runs the app; only the menu is missing.
            if let Err(e) = system::tray(app.handle()) {
                eprintln!("PickerHub: no tray icon ({e})");
            }
            shot::schedule(app.handle().clone());
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("PickerHub failed to start")
        .run(|app, event| {
            if let RunEvent::ExitRequested { .. } = event
                && let Some(state) = app.try_state::<AppState>()
                && let Err(e) = state.flush()
            {
                eprintln!("PickerHub could not save the library before exiting: {e}");
            }
        });
}
