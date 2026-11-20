//! One serial writer. The app owns the live library; this thread receives only finished snapshots.
//! Bursts (a slider drag, key repeat) coalesce into one write, and `flush` is the durability boundary.
use crate::error::StoreError;
use std::sync::{Arc, Condvar, Mutex};
use std::time::Duration;

type Save = Box<dyn Fn(&str) -> Result<(), StoreError> + Send + Sync>;

#[derive(Default)]
struct State {
    pending: Option<String>,
    latest: Option<String>,
    running: bool,
    failure: Option<String>,
}

#[derive(Clone)]
pub struct Writer {
    inner: Arc<(Mutex<State>, Condvar)>,
    save: Arc<Save>,
}

impl Writer {
    pub fn new(save: impl Fn(&str) -> Result<(), StoreError> + Send + Sync + 'static) -> Self {
        Self { inner: Arc::default(), save: Arc::new(Box::new(save)) }
    }

    pub fn enqueue(&self, snapshot: String) {
        let (lock, _) = &*self.inner;
        let mut s = lock.lock().unwrap();
        s.latest = Some(snapshot.clone());
        s.pending = Some(snapshot);
        s.failure = None;
        if s.running {
            return;
        }
        s.running = true;
        let me = self.clone();
        std::thread::spawn(move || me.drain());
    }

    fn drain(&self) {
        std::thread::sleep(Duration::from_millis(80));
        let (lock, done) = &*self.inner;
        loop {
            let snapshot = {
                let mut s = lock.lock().unwrap();
                match s.pending.take() {
                    Some(v) => v,
                    None => {
                        s.running = false;
                        done.notify_all();
                        return;
                    }
                }
            };
            if let Err(e) = (self.save)(&snapshot) {
                let mut s = lock.lock().unwrap();
                s.failure = Some(e.message());
                s.running = false;
                done.notify_all();
                return;
            }
        }
    }

    /// Waits until every queued snapshot is on disk. Reports the last failure, if any.
    pub fn flush(&self) -> Result<(), String> {
        let (lock, done) = &*self.inner;
        let mut s = lock.lock().unwrap();
        while s.running {
            s = done.wait(s).unwrap();
        }
        match &s.failure {
            Some(e) => Err(e.clone()),
            None => Ok(()),
        }
    }

    pub fn retry(&self) {
        let latest = self.inner.0.lock().unwrap().latest.clone();
        if let Some(v) = latest {
            self.enqueue(v);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};
    #[test]
    fn bursts_coalesce_and_flush_waits() {
        let writes = Arc::new(AtomicUsize::new(0));
        let last = Arc::new(Mutex::new(String::new()));
        let (w2, l2) = (writes.clone(), last.clone());
        let writer = Writer::new(move |s| {
            w2.fetch_add(1, Ordering::SeqCst);
            *l2.lock().unwrap() = s.to_string();
            Ok(())
        });
        for i in 0..50 {
            writer.enqueue(i.to_string());
        }
        writer.flush().unwrap();
        assert_eq!(*last.lock().unwrap(), "49");
        assert!(writes.load(Ordering::SeqCst) <= 2);
    }
}
