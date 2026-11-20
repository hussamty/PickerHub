//! Undo and redo over whole-library snapshots, bounded like 1.x: 40 steps and 8 MiB.
#[derive(Default)]
pub struct EditHistory {
    undo: Vec<String>,
    redo: Vec<String>,
}

const MAX_ENTRIES: usize = 40;
const MAX_BYTES: usize = 8 * 1024 * 1024;

fn push(list: &mut Vec<String>, value: String) {
    if value.len() > MAX_BYTES {
        list.clear();
        return;
    }
    list.push(value);
    while list.len() > MAX_ENTRIES || (list.len() > 1 && list.iter().map(String::len).sum::<usize>() > MAX_BYTES) {
        list.remove(0);
    }
}

impl EditHistory {
    pub fn can_undo(&self) -> bool {
        !self.undo.is_empty()
    }
    pub fn can_redo(&self) -> bool {
        !self.redo.is_empty()
    }
    /// Records that the library went from `before` to `after`. A change clears the redo list.
    pub fn commit(&mut self, before: String, after: &str) {
        if before == after {
            return;
        }
        push(&mut self.undo, before);
        self.redo.clear();
    }
    /// Steps back (or forward). Returns the snapshot to apply, if there is one.
    pub fn travel(&mut self, current: String, forward: bool) -> Option<String> {
        let (source, target) = if forward { (&mut self.redo, &mut self.undo) } else { (&mut self.undo, &mut self.redo) };
        let next = source.pop()?;
        push(target, current);
        Some(next)
    }
    pub fn clear(&mut self) {
        self.undo.clear();
        self.redo.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn undo_redo_and_bounds() {
        let mut h = EditHistory::default();
        h.commit("a".into(), "b");
        h.commit("b".into(), "c");
        assert_eq!(h.travel("c".into(), false).as_deref(), Some("b"));
        assert_eq!(h.travel("b".into(), false).as_deref(), Some("a"));
        assert_eq!(h.travel("a".into(), false), None);
        assert_eq!(h.travel("a".into(), true).as_deref(), Some("b"));
        h.commit("b".into(), "z");
        assert!(!h.can_redo());
        for i in 0..100 {
            h.commit(i.to_string(), "x");
        }
        assert_eq!(h.undo.len(), MAX_ENTRIES);
    }
}
