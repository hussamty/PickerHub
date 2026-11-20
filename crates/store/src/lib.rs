//! PickerHub library storage, shared by the Tauri app and its tests.
//!
//! The formats are those of PickerHub 1.x (`desktop/Workspace.cs`, `BackupBundle.cs`,
//! `PaletteExchange.cs`, `SemanticPalette.cs`): the two versions read each other's files.
pub mod error;
pub mod export;
pub mod history;
pub mod model;
pub mod persist;
pub mod writer;

pub use error::{ErrorBody, StoreError};
pub use history::EditHistory;
pub use model::{Discovery, Gradient, Palette, Stop, Swatch, Workspace};
pub use persist::{Backup, Store, atomic_write, data_folder};
pub use writer::Writer;
