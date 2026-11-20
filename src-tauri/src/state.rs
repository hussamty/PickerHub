//! The app's live state: the open library, its undo history and its writer.
use pickerhub::color::Color;
use pickerhub_store::{Backup, EditHistory, ErrorBody, Store, StoreError, Workspace, Writer};
use serde::Serialize;
use serde_json::Value;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex, MutexGuard};

/// An error as the interface receives it. `message` is Arabic source text, which the interface
/// translates through the English catalog; `detail` is diagnostic text from the system, if any.
#[derive(Serialize, Debug, Clone)]
pub struct AppError {
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub detail: Option<String>,
}
impl AppError {
    pub fn new(code: &str, message: &str) -> Self {
        Self { code: code.into(), message: message.into(), detail: None }
    }
    pub fn with_detail(mut self, e: impl std::fmt::Display) -> Self {
        self.detail = Some(e.to_string());
        self
    }
    pub fn with_message(mut self, m: &str) -> Self {
        self.message = m.into();
        self
    }
}
impl From<&StoreError> for AppError {
    fn from(e: &StoreError) -> Self {
        let b = ErrorBody::from(e);
        Self { code: b.code, message: b.message, detail: None }
    }
}
impl From<StoreError> for AppError {
    fn from(e: StoreError) -> Self {
        (&e).into()
    }
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    pub workspace: Workspace,
    pub can_undo: bool,
    pub can_redo: bool,
}

pub struct AppState {
    store: Arc<Mutex<Store>>,
    workspace: Mutex<Workspace>,
    history: Mutex<EditHistory>,
    writer: Writer,
    cache: Mutex<HashMap<(u8, u8, u8), Value>>,
    folder: PathBuf,
    pub recovery_notice: Option<String>,
    pub preview: bool,
}

impl AppState {
    pub fn open(folder: PathBuf, preview: bool) -> Result<Self, StoreError> {
        let mut store = Store::open(folder.clone())?;
        let workspace = store.load()?;
        let recovery_notice = store.recovery_notice.map(str::to_string);
        let store = Arc::new(Mutex::new(store));
        let saver = store.clone();
        let writer = Writer::new(move |json| saver.lock().unwrap().save_snapshot(json));
        Ok(Self {
            store,
            workspace: Mutex::new(workspace),
            history: Mutex::default(),
            writer,
            cache: Mutex::default(),
            folder,
            recovery_notice,
            preview,
        })
    }
    pub fn folder(&self) -> &Path {
        &self.folder
    }
    pub fn workspace(&self) -> MutexGuard<'_, Workspace> {
        self.workspace.lock().unwrap()
    }
    pub fn snapshot(&self) -> Snapshot {
        let h = self.history.lock().unwrap();
        let workspace = self.workspace().clone();
        // The timing log follows its setting from every change that reaches the interface.
        crate::support::follow(workspace.diagnostics_enabled, &self.folder);
        Snapshot { workspace, can_undo: h.can_undo(), can_redo: h.can_redo() }
    }

    /// Applies a change to a copy, validates it, then makes it current, records it and saves it.
    pub fn edit(&self, record: bool, change: impl FnOnce(&mut Workspace) -> Result<(), StoreError>) -> Result<Snapshot, AppError> {
        {
            let mut w = self.workspace();
            let mut next = w.clone();
            change(&mut next)?;
            next.validate()?;
            if next == *w {
                drop(w);
                return Ok(self.snapshot());
            }
            let after = next.to_json();
            if record {
                self.history.lock().unwrap().commit(w.to_json(), &after);
            }
            *w = next;
            self.writer.enqueue(after);
        }
        Ok(self.snapshot())
    }

    pub fn travel(&self, forward: bool) -> Result<Snapshot, AppError> {
        {
            let mut w = self.workspace();
            let current = w.to_json();
            let Some(next) = self.history.lock().unwrap().travel(current, forward) else { drop(w); return Ok(self.snapshot()) };
            let parsed = Workspace::parse(&next)?;
            *w = parsed;
            self.writer.enqueue(next);
        }
        Ok(self.snapshot())
    }

    pub fn export_backup(&self) -> Result<String, AppError> {
        let w = self.workspace().clone();
        Ok(self.store.lock().unwrap().export_backup(&w)?)
    }

    pub fn restore(&self, backup: &Backup) -> Result<Snapshot, AppError> {
        self.writer.flush().map_err(|m| AppError::new("io", "تعذّر حفظ المكتبة").with_detail(m))?;
        self.store.lock().unwrap().restore(backup)?;
        *self.workspace() = backup.workspace.clone();
        self.history.lock().unwrap().clear();
        Ok(self.snapshot())
    }

    /// Waits for the last change to reach the disk. Called before the app exits.
    pub fn flush(&self) -> Result<(), String> {
        self.writer.flush()
    }

    /// Color descriptions are pure; a bounded cache keeps grids and drags cheap.
    pub fn describe_cached(&self, c: Color, make: impl FnOnce() -> Value) -> Value {
        let key = (c.0, c.1, c.2);
        let mut cache = self.cache.lock().unwrap();
        if let Some(v) = cache.get(&key) {
            return v.clone();
        }
        let v = make();
        if cache.len() >= 512 {
            cache.clear();
        }
        cache.insert(key, v.clone());
        v
    }
}
