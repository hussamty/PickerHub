//! Loads the library in `<folder>/PickerHub` the way the app does and saves it again.
//! Used by tests/compat_1x.py to hand a 2.0-written file to PickerHub 1.x.
fn main() {
    let folder = std::path::PathBuf::from(std::env::args().nth(1).expect("usage: roundtrip <data root>")).join("PickerHub");
    let mut store = pickerhub_store::Store::open(folder).expect("open");
    let w = store.load().expect("load");
    store.save(&w).expect("save");
    println!("saved {} palettes, {} history entries", w.palettes.len(), w.history.len());
}
