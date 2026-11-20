// The eyedropper (F8, the title bar button, the tray and the global shortcut).
import { app } from "./app.svelte";
import { ipc } from "./ipc";

let busy = false;

export async function pick() {
  if (busy) return;
  busy = true;
  try {
    const r = await ipc.pick();
    if (!r) { app.toast("أُلغي الالتقاط"); return; }
    app.set(r.snapshot);
    if (r.copied) app.toast("نُسخ {0}", r.hex, [r.copied]);
    else app.toast("{0}", r.hex, [r.hex]);
  } catch (e) {
    app.fail(e);
  } finally {
    busy = false;
  }
}

/** Picks again and again until Escape, adding each color to a palette. */
export async function pickMany(palette: string) {
  if (busy) return;
  busy = true;
  try {
    const before = app.ws?.palettes.find((p) => p.id === palette)?.colors.length ?? 0;
    const s = await ipc.pickMany(palette);
    app.set(s);
    const after = s.workspace.palettes.find((p) => p.id === palette)?.colors.length ?? 0;
    app.toast(after > before ? "أُضيفت الألوان إلى اللوحة" : "أُلغي الالتقاط");
  } catch (e) {
    // Multiple picks need the live lens: offer it, as 1.x did, rather than only reporting the refusal.
    if ((e as { code?: string })?.code === "multi_unavailable") app.captureMenu = { collect: palette };
    else app.fail(e);
  } finally {
    busy = false;
  }
}
