//! The local timing log (desktop/SupportLog.cs): opt-in, bounded and off the main thread. An entry
//! holds a time, the version, an operation name from a fixed list, an error code and a duration;
//! never a message, a path, a color, a name or a picture. Entries go to `diagnostics.jsonl` in the
//! library folder (256 KiB, then one previous file), and the last 64 of this session go into the
//! support report the user saves by hand.
use serde::Serialize;
use std::collections::VecDeque;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{sync_channel, SyncSender};
use std::sync::{Mutex, OnceLock};
use std::time::Instant;

static ENABLED: AtomicBool = AtomicBool::new(false);
static LAUNCH: OnceLock<Instant> = OnceLock::new();
static FOLDER: OnceLock<PathBuf> = OnceLock::new();
static RECENT: Mutex<VecDeque<Entry>> = Mutex::new(VecDeque::new());
static WRITER: OnceLock<SyncSender<Entry>> = OnceLock::new();
const KEEP: usize = 64;
const LIMIT: u64 = 256 * 1024;

/// Operations the interface may report. Anything else is ignored, so no free text reaches the log.
pub const UI_OPERATIONS: &[&str] = &["launch-to-workbench", "palette-search-to-frame", "page-to-frame", "image-to-colors", "action"];

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    time: String,
    version: &'static str,
    operation: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    milliseconds: Option<f64>,
}

/// Called as early as possible in `main`, so launch timings start from the process.
pub fn launched() {
    let _ = LAUNCH.set(Instant::now());
}
pub fn since_launch() -> Option<f64> {
    LAUNCH.get().map(|t| t.elapsed().as_secs_f64() * 1000.0)
}

/// Follows the library's setting; called whenever a snapshot is taken.
pub fn follow(enabled: bool, folder: &Path) {
    let _ = FOLDER.set(folder.to_path_buf());
    let was = ENABLED.swap(enabled, Ordering::Relaxed);
    if was && !enabled {
        RECENT.lock().unwrap().clear();
    }
}
pub fn enabled() -> bool {
    ENABLED.load(Ordering::Relaxed)
}

/// A start time when the log is on; `measured` turns it into an entry.
pub fn start() -> Option<Instant> {
    enabled().then(Instant::now)
}
pub fn measured(operation: &str, start: Option<Instant>) {
    if let Some(s) = start {
        record(operation, None, Some(s.elapsed().as_secs_f64() * 1000.0));
    }
}
/// An operation that failed, with the error's code (never its message or detail).
pub fn failed(operation: &str, code: &str) {
    record(operation, Some(code), None);
}

pub fn record(operation: &str, error: Option<&str>, milliseconds: Option<f64>) {
    if !enabled() {
        return;
    }
    let entry = Entry {
        time: now(),
        version: env!("CARGO_PKG_VERSION"),
        operation: operation.into(),
        error: error.map(str::to_string),
        milliseconds: milliseconds.map(|m| (m * 10.0).round() / 10.0),
    };
    {
        let mut recent = RECENT.lock().unwrap();
        recent.push_back(entry.clone());
        while recent.len() > KEEP {
            recent.pop_front();
        }
    }
    // A full queue drops the entry: the log must never hold up picking or saving.
    let _ = writer().try_send(entry);
}

/// This session's entries, for the support report; empty while the log is off.
pub fn recent() -> Vec<Entry> {
    if enabled() { RECENT.lock().unwrap().iter().cloned().collect() } else { Vec::new() }
}

fn writer() -> &'static SyncSender<Entry> {
    WRITER.get_or_init(|| {
        let (tx, rx) = sync_channel::<Entry>(KEEP);
        std::thread::Builder::new()
            .name("support-log".into())
            .spawn(move || {
                for entry in rx {
                    if !enabled() {
                        continue;
                    }
                    let Some(folder) = FOLDER.get() else { continue };
                    let _ = append(folder, &entry);
                }
            })
            .expect("support log thread");
        tx
    })
}

fn append(folder: &Path, entry: &Entry) -> std::io::Result<()> {
    let path = folder.join("diagnostics.jsonl");
    if std::fs::metadata(&path).map(|m| m.len() > LIMIT).unwrap_or(false) {
        std::fs::rename(&path, folder.join("diagnostics.jsonl.previous"))?;
    }
    let mut file = std::fs::OpenOptions::new().create(true).append(true).open(&path)?;
    writeln!(file, "{}", serde_json::to_string(entry).unwrap_or_default())
}

/// UTC time as RFC 3339, to the millisecond, without a date library.
fn now() -> String {
    let d = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default();
    let (secs, ms) = (d.as_secs() as i64, d.subsec_millis());
    let (days, rem) = (secs.div_euclid(86_400), secs.rem_euclid(86_400));
    // Civil date from days since 1970-01-01 (Howard Hinnant's algorithm).
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + i64::from(month <= 2);
    format!("{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}.{ms:03}Z", rem / 3600, rem % 3600 / 60, rem % 60)
}

/// The interface reports a timing it measured itself, or (with no duration) one since launch.
#[tauri::command]
pub fn log_timing(operation: String, milliseconds: Option<f64>) {
    let Some(op) = UI_OPERATIONS.iter().find(|o| **o == operation) else { return };
    let ms = milliseconds.filter(|m| m.is_finite() && *m >= 0.0 && *m < 600_000.0).or_else(|| if *op == "launch-to-workbench" { since_launch() } else { None });
    if let Some(ms) = ms {
        record(op, None, Some(ms));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dates_are_civil() {
        let s = now();
        assert_eq!(s.len(), 24);
        assert!(s.ends_with('Z') && s.as_bytes()[4] == b'-' && s.as_bytes()[10] == b'T');
    }

    #[test]
    fn off_records_nothing_and_unknown_operations_are_ignored() {
        let dir = std::env::temp_dir().join(format!("pickerhub-log-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        follow(false, &dir);
        record("capture", None, Some(1.0));
        assert!(recent().is_empty());
        follow(true, &dir);
        log_timing("/home/someone/secret.png".into(), Some(3.0));
        assert!(recent().is_empty());
        log_timing("palette-search-to-frame".into(), Some(12.34));
        failed("capture", "portal_denied");
        let r = recent();
        assert_eq!(r.len(), 2);
        assert_eq!(r[0].milliseconds, Some(12.3));
        assert_eq!(r[1].error.as_deref(), Some("portal_denied"));
        follow(false, &dir);
        assert!(recent().is_empty());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
