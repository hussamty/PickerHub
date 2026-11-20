//! Files on disk: the data folder, the library lock, atomic writes, recovery and backups.
use crate::error::StoreError;
use crate::model::Workspace;
use serde_json::{Value, json};
use std::fs::{self, File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

/// `<local data>/PickerHub`, the folder 1.x uses: `$XDG_DATA_HOME` (or `~/.local/share`) on Linux,
/// `%LOCALAPPDATA%` on Windows and `~/Library/Application Support` on macOS.
/// `PICKERHUB_DATA_DIR` replaces the root, for tests and the envelope preview.
pub fn data_folder() -> PathBuf {
    let root = std::env::var_os("PICKERHUB_DATA_DIR")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    root.join("PickerHub")
}

/// Writes through a uniquely named temporary file, flushes it to disk and moves it over `path`.
pub fn atomic_write(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    let tmp = PathBuf::from(format!("{}.{}", path.display(), crate::model::new_id()));
    let result = (|| {
        let mut f = OpenOptions::new().write(true).create_new(true).open(&tmp)?;
        f.write_all(bytes)?;
        f.sync_all()?;
        fs::rename(&tmp, path)?;
        #[cfg(unix)]
        if let Some(dir) = path.parent() {
            // The rename itself is durable only once the directory entry is.
            let _ = File::open(dir).and_then(|d| d.sync_all());
        }
        Ok(())
    })();
    let _ = fs::remove_file(&tmp);
    result
}

/// The open library: its folder and the lock that keeps a second PickerHub (1.x or 2.x) out.
pub struct Store {
    folder: PathBuf,
    _lock: Option<File>,
    pub recovery_notice: Option<&'static str>,
}

impl Store {
    /// Takes `desktop.lock` exclusively, the same file and the same kind of lock (`flock` on Unix,
    /// a share-none handle on Windows) as 1.x, so the two versions never edit the library together.
    pub fn open(folder: PathBuf) -> Result<Self, StoreError> {
        fs::create_dir_all(&folder)?;
        let lock = OpenOptions::new().read(true).write(true).create(true).truncate(false).open(folder.join("desktop.lock"))?;
        match lock.try_lock() {
            Ok(()) => {}
            Err(fs::TryLockError::WouldBlock) => return Err(StoreError::Locked),
            Err(fs::TryLockError::Error(e)) => return Err(e.into()),
        }
        Ok(Self { folder, _lock: Some(lock), recovery_notice: None })
    }
    /// A store without the lock, for reading in tests and the self-test.
    pub fn unlocked(folder: PathBuf) -> Self {
        Self { folder, _lock: None, recovery_notice: None }
    }
    pub fn folder(&self) -> &Path {
        &self.folder
    }
    pub fn workspace_path(&self) -> PathBuf {
        self.folder.join("workspace.json")
    }
    fn backup_path(&self) -> PathBuf {
        self.folder.join("workspace.json.bak")
    }
    pub fn daily_path(&self) -> PathBuf {
        self.folder.join("daily_palette_state.json")
    }
    fn journal_path(&self) -> PathBuf {
        self.folder.join("restore-journal.json")
    }
    /// The copy taken before 2.0 first writes, so a return to 1.x always has its own file.
    fn first_run_copy(&self) -> PathBuf {
        self.folder.join("workspace.v1-backup.json")
    }

    /// Loads the library. A damaged file is set aside and the last good copy restored; a file from a
    /// newer PickerHub is never replaced by an older backup.
    pub fn load(&mut self) -> Result<Workspace, StoreError> {
        self.recovery_notice = None;
        self.recover_interrupted()?;
        let path = self.workspace_path();
        if !path.exists() {
            return Ok(Workspace::default());
        }
        let text = fs::read_to_string(&path)?;
        match Workspace::parse(&text) {
            Ok(w) => {
                if !self.first_run_copy().exists() {
                    atomic_write(&self.first_run_copy(), text.as_bytes())?;
                }
                Ok(w)
            }
            Err(StoreError::NewerVersion) => Err(StoreError::NewerVersion),
            Err(StoreError::Invalid { .. }) => {
                if let Ok(v) = serde_json::from_str::<Value>(&text)
                    && v["version"].as_i64().is_some_and(|n| n > crate::model::SCHEMA)
                {
                    return Err(StoreError::NewerVersion);
                }
                let recovered = Workspace::parse(&fs::read_to_string(self.backup_path())?)?;
                let stamp = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_millis();
                fs::copy(&path, self.folder.join(format!("workspace.json.damaged-{stamp}")))?;
                atomic_write(&path, recovered.to_json().as_bytes())?;
                self.recovery_notice = Some("استُعيدت آخر نسخة سليمة. حُفظ الملف التالف للمراجعة ولم تُحذف بياناته.");
                Ok(recovered)
            }
            Err(e) => Err(e),
        }
    }

    /// Validates and saves a snapshot, keeping the previous valid file as `.bak`.
    pub fn save_snapshot(&self, json: &str) -> Result<(), StoreError> {
        Workspace::parse(json)?;
        fs::create_dir_all(&self.folder)?;
        let path = self.workspace_path();
        if let Ok(old) = fs::read_to_string(&path)
            && Workspace::parse(&old).is_ok()
        {
            atomic_write(&self.backup_path(), old.as_bytes())?;
        }
        atomic_write(&path, json.as_bytes())?;
        Ok(())
    }
    pub fn save(&self, w: &Workspace) -> Result<(), StoreError> {
        self.save_snapshot(&w.to_json())
    }

    // ---- Full backups: the library together with the palette-of-the-day state. ----

    pub fn export_backup(&self, w: &Workspace) -> Result<String, StoreError> {
        let daily = fs::read_to_string(self.daily_path()).ok();
        validate_daily(daily.as_deref())?;
        let workspace: Value = serde_json::to_value(w).expect("a workspace always serializes");
        Ok(serde_json::to_string_pretty(&json!({"backupVersion": 1, "workspace": workspace, "dailyState": daily})).unwrap())
    }

    /// Restores a backup. A journal of both files is written first, so a crash halfway through
    /// rolls back to the library as it was on the next start.
    pub fn restore(&self, backup: &Backup) -> Result<(), StoreError> {
        fs::create_dir_all(&self.folder)?;
        self.recover_interrupted()?;
        let before = json!({
            "workspace": fs::read_to_string(self.workspace_path()).ok(),
            "daily": fs::read_to_string(self.daily_path()).ok(),
        });
        atomic_write(&self.journal_path(), before.to_string().as_bytes())?;
        let result = (|| {
            self.save(&backup.workspace)?;
            if backup.includes_daily {
                set(&self.daily_path(), backup.daily.as_deref())?;
            }
            fs::remove_file(self.journal_path())?;
            Ok(())
        })();
        if result.is_err() {
            self.recover_interrupted()?;
        }
        result
    }

    pub fn recover_interrupted(&self) -> Result<bool, StoreError> {
        let Ok(text) = fs::read_to_string(self.journal_path()) else { return Ok(false) };
        let before: Value = serde_json::from_str(&text).map_err(|_| StoreError::invalid_backup())?;
        set(&self.workspace_path(), before["workspace"].as_str())?;
        set(&self.daily_path(), before["daily"].as_str())?;
        fs::remove_file(self.journal_path())?;
        Ok(true)
    }
}

fn set(path: &Path, data: Option<&str>) -> std::io::Result<()> {
    match data {
        Some(text) => atomic_write(path, text.as_bytes()),
        None => match fs::remove_file(path) {
            Err(e) if e.kind() != std::io::ErrorKind::NotFound => Err(e),
            _ => Ok(()),
        },
    }
}

fn validate_daily(daily: Option<&str>) -> Result<(), StoreError> {
    let Some(text) = daily else { return Ok(()) };
    if text.len() > 1024 * 1024 {
        return Err(StoreError::invalid("daily_too_large", "حالة الباقة اليومية أكبر من الحد المسموح."));
    }
    let v: Value = serde_json::from_str(text).map_err(|_| StoreError::invalid_backup())?;
    if v.get("schema_version").and_then(Value::as_i64) != Some(1) {
        return Err(StoreError::invalid("daily_unsupported", "نسخة حالة الباقة اليومية غير مدعومة. حدّث PickerHub قبل الاستعادة."));
    }
    Ok(())
}

/// A parsed backup: a full bundle, or a bare workspace file from before bundles existed.
pub struct Backup {
    pub workspace: Workspace,
    pub daily: Option<String>,
    pub includes_daily: bool,
}
impl Backup {
    pub fn parse(text: &str) -> Result<Self, StoreError> {
        let text = text.trim_start_matches('\u{feff}');
        let root: Value = serde_json::from_str(text).map_err(|_| StoreError::invalid_backup())?;
        let Some(version) = root.get("backupVersion") else {
            return Ok(Self { workspace: Workspace::parse(text)?, daily: None, includes_daily: false });
        };
        if version.as_i64() != Some(1) {
            return Err(StoreError::invalid("backup_newer", "صيغة النسخة الاحتياطية أحدث من التطبيق."));
        }
        let workspace = Workspace::parse(&root["workspace"].to_string())?;
        let daily = root["dailyState"].as_str().map(str::to_string);
        validate_daily(daily.as_deref())?;
        Ok(Self { workspace, daily, includes_daily: true })
    }
}
