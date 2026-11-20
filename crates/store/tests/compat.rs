//! Reading and writing libraries made by PickerHub 1.x, and recovering from damaged files.
use pickerhub_store::{Backup, Store, StoreError, Workspace};
use serde_json::Value;
use std::fs;

fn fixture(name: &str) -> String {
    fs::read_to_string(format!("{}/tests/fixtures/{name}", env!("CARGO_MANIFEST_DIR"))).unwrap()
}
fn json(text: &str) -> Value {
    serde_json::from_str(text).unwrap()
}

#[test]
fn a_1x_library_survives_a_round_trip_value_for_value() {
    let original = fixture("v12-escaped.json");
    let w = Workspace::parse(&original).unwrap();
    assert_eq!(w.palettes[0].name, "هوية TrineHub");
    assert_eq!(w.discoveries[0].light["background"], "#FAF6EF");
    assert_eq!(json(&w.to_json()), json(&original));
}

#[test]
fn unknown_fields_are_written_back() {
    let original = fixture("v12-unknown-fields.json");
    let w = Workspace::parse(&original).unwrap();
    let written = json(&w.to_json());
    assert_eq!(written["futureSetting"]["keep"][2], 3);
    assert_eq!(written["palettes"][0]["pinned"], true);
    assert_eq!(written, json(&original));
}

#[test]
fn schema_1_is_upgraded_with_defaults() {
    let w = Workspace::parse(&fixture("v1-schema.json")).unwrap();
    assert_eq!(w.version, 2);
    assert_eq!(w.name_scheme, 1);
    assert_eq!(w.copy_format, "HEX");
    assert!(w.auto_copy);
    assert_eq!(w.limit, 8);
}

#[test]
fn pre_1_1_automatic_names_are_renamed_once() {
    // A muted purple that the 1.0 table called by an RGB-nearest name.
    let c = pickerhub::color::Color(0x6E, 0x5A, 0x7E);
    let (old_ar, _) = pickerhub::names::previous(c);
    let now = pickerhub::names::name(c).arabic;
    assert_ne!(old_ar, now, "pick a color whose name changed in 1.1");
    let mut v = json(&fixture("v1-schema.json"));
    v["palettes"][0]["colors"] = serde_json::json!([
        {"id": "x1", "name": old_ar, "hex": "#6E5A7E", "favorite": false},
        {"id": "x2", "name": "اسمي الخاص", "hex": "#6E5A7E", "favorite": false}
    ]);
    let w = Workspace::parse(&v.to_string()).unwrap();
    assert_eq!(w.palettes[0].colors[0].name, now);
    assert_eq!(w.palettes[0].colors[1].name, "اسمي الخاص");
}

#[test]
fn invalid_and_newer_files_are_refused() {
    let mut v = json(&fixture("v12-escaped.json"));
    v["history"][0] = "red".into();
    assert!(matches!(Workspace::parse(&v.to_string()), Err(StoreError::Invalid { .. })));
    let mut v = json(&fixture("v12-escaped.json"));
    v["version"] = 3.into();
    assert!(matches!(Workspace::parse(&v.to_string()), Err(StoreError::NewerVersion)));
    let mut v = json(&fixture("v12-escaped.json"));
    v.as_object_mut().unwrap().remove("gradients");
    assert!(Workspace::parse(&v.to_string()).is_err());
}

#[test]
fn a_damaged_file_is_recovered_from_the_last_good_copy() {
    let dir = tempfile::tempdir().unwrap();
    let folder = dir.path().join("PickerHub");
    let mut store = Store::open(folder.clone()).unwrap();
    let mut w = Workspace::parse(&fixture("v12-escaped.json")).unwrap();
    store.save(&w).unwrap();
    w.color = "#ABCDEF".into();
    store.save(&w).unwrap(); // the first file is now the .bak
    fs::write(folder.join("workspace.json"), "{ not json").unwrap();
    let recovered = store.load().unwrap();
    assert_eq!(recovered.color, "#2E8BC0");
    assert!(store.recovery_notice.is_some());
    assert!(fs::read_dir(&folder).unwrap().any(|e| e.unwrap().file_name().to_string_lossy().starts_with("workspace.json.damaged-")));
}

#[test]
fn a_newer_library_is_never_replaced_by_an_older_backup() {
    let dir = tempfile::tempdir().unwrap();
    let folder = dir.path().join("PickerHub");
    let mut store = Store::open(folder.clone()).unwrap();
    store.save(&Workspace::default()).unwrap();
    store.save(&Workspace::default()).unwrap();
    let mut v = json(&fixture("v12-escaped.json"));
    v["version"] = 3.into();
    fs::write(folder.join("workspace.json"), v.to_string()).unwrap();
    assert!(matches!(store.load(), Err(StoreError::NewerVersion)));
    assert_eq!(json(&fs::read_to_string(folder.join("workspace.json")).unwrap())["version"], 3);
}

#[test]
fn the_lock_keeps_a_second_instance_out() {
    let dir = tempfile::tempdir().unwrap();
    let folder = dir.path().join("PickerHub");
    let first = Store::open(folder.clone()).unwrap();
    assert!(matches!(Store::open(folder.clone()), Err(StoreError::Locked)));
    drop(first);
    assert!(Store::open(folder).is_ok());
}

#[test]
fn backups_restore_both_files_and_an_interrupted_restore_rolls_back() {
    let dir = tempfile::tempdir().unwrap();
    let folder = dir.path().join("PickerHub");
    let mut store = Store::open(folder.clone()).unwrap();
    let w = Workspace::parse(&fixture("v12-escaped.json")).unwrap();
    store.save(&w).unwrap();
    fs::write(store.daily_path(), r#"{"schema_version":1,"x":1}"#).unwrap();
    let bundle = store.export_backup(&w).unwrap();

    store.save(&Workspace::default()).unwrap();
    fs::remove_file(store.daily_path()).unwrap();
    store.restore(&Backup::parse(&bundle).unwrap()).unwrap();
    assert_eq!(store.load().unwrap().palettes[0].name, "هوية TrineHub");
    assert!(fs::read_to_string(store.daily_path()).unwrap().contains("schema_version"));

    // A journal left by a crash puts the files back as they were before the restore began.
    let before = fs::read_to_string(store.workspace_path()).unwrap();
    fs::write(folder.join("restore-journal.json"), serde_json::json!({"workspace": before, "daily": null}).to_string()).unwrap();
    store.save(&Workspace::default()).unwrap();
    assert!(store.recover_interrupted().unwrap());
    assert_eq!(fs::read_to_string(store.workspace_path()).unwrap(), before);
    assert!(!store.daily_path().exists());

    // A bare 1.0 workspace file is still accepted as a backup, without touching daily state.
    let legacy = Backup::parse(&fixture("v1-schema.json")).unwrap();
    assert!(!legacy.includes_daily);
    assert!(Backup::parse(r#"{"backupVersion":2}"#).is_err());
}
