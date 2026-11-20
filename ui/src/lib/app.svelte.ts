// The interface's live state. The library itself is owned by Rust; this holds the latest snapshot
// Rust returned, the page on show, and the transient things only the interface knows about.
import { ipc } from "./ipc";
import type { AppError, Op, Snapshot, Workspace } from "./types";

export interface Toast { id: number; text: string; color?: string; tone?: "error" | "info"; args?: (string | number)[] }
export type Dialog =
  | { kind: "confirm"; title: string; body?: string; danger?: boolean; resolve: (v: boolean) => void }
  | { kind: "prompt"; title: string; value: string; placeholder?: string; resolve: (v: string | null) => void }
  | { kind: "choose"; title: string; options: string[]; resolve: (v: number | null) => void };

let toastId = 0;

class App {
  ws = $state<Workspace | null>(null);
  canUndo = $state(false);
  canRedo = $state(false);
  page = $state(0);
  toasts = $state<Toast[]>([]);
  dialog = $state<Dialog | null>(null);
  settingsOpen = $state(false);
  platform = $state("");
  session = $state("");
  version = $state("");
  preview = $state(false);
  /** The palette the library page and "add to palette" act on. */
  paletteId = $state("");
  /** Shared between pages: the contrast pair. */
  contrastFg = $state("#172D46");
  contrastBg = $state("#FFFFFF");
  systemDark = $state(true);
  /** A Wayland screen-sharing session is open for the live lens. */
  sharing = $state(false);
  /** The eyedropper goes through the desktop portal (Wayland), so there is a capture mode to choose. */
  portal = $state(false);
  /** The capture-mode sheet; `collect` names the palette a multiple pick was meant for. */
  captureMenu = $state<null | { collect?: string }>(null);

  get language(): "ar" | "en" {
    return this.ws?.language ?? "ar";
  }
  get theme(): "dark" | "light" {
    if (!this.ws) return "dark";
    if (this.ws.followSystemTheme) return this.systemDark ? "dark" : "light";
    return this.ws.theme;
  }
  get palette() {
    const ws = this.ws;
    if (!ws) return null;
    return ws.palettes.find((p) => p.id === this.paletteId) ?? ws.palettes[0];
  }
  get motion(): boolean {
    const reduced = typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
    return !(this.ws?.reduceMotion || reduced);
  }

  set(s: Snapshot) {
    this.ws = s.workspace;
    this.canUndo = s.canUndo;
    this.canRedo = s.canRedo;
    if (!s.workspace.palettes.some((p) => p.id === this.paletteId)) this.paletteId = s.workspace.palettes[0]?.id ?? "";
  }

  async edit(ops: Op[], done?: string): Promise<boolean> {
    try {
      this.set(await ipc.edit(ops));
      if (done) this.toast(done);
      return true;
    } catch (e) {
      this.fail(e);
      return false;
    }
  }
  /** Reports how long something took until it was drawn (two frames later), when the local log is on. */
  timed(operation: string, start = performance.now(), sinceLaunch = false) {
    if (!this.ws?.diagnosticsEnabled) return;
    requestAnimationFrame(() => requestAnimationFrame(() => ipc.logTiming(operation, sinceLaunch ? undefined : performance.now() - start).catch(() => {})));
  }
  setting(key: keyof Workspace, value: unknown) {
    return this.edit([{ op: "setting", key, value }]);
  }

  async undo() {
    if (!this.canUndo) return;
    try { this.set(await ipc.undo()); this.toast("تُراجع عن التعديل"); } catch (e) { this.fail(e); }
  }
  async redo() {
    if (!this.canRedo) return;
    try { this.set(await ipc.redo()); this.toast("أُعيد التعديل"); } catch (e) { this.fail(e); }
  }

  /** Copies a color in the format chosen in Settings (or text as it is) and says what was copied. */
  async copy(text: string, raw = false) {
    try {
      const value = await ipc.copy(text, raw);
      this.toast("نُسخ {0}", /^#[0-9a-f]{6}$/i.test(text) ? text : undefined, [value]);
    } catch (e) { this.fail(e); }
  }

  /** `text` is Arabic source text, translated when shown; `args` fill its {0}, {1}… */
  toast(text: string, color?: string, args?: (string | number)[]) {
    const id = ++toastId;
    this.toasts = [...this.toasts.slice(-2), { id, text, color, args }];
    setTimeout(() => (this.toasts = this.toasts.filter((x) => x.id !== id)), 3200);
  }
  fail(e: unknown) {
    const err = e as AppError;
    const id = ++toastId;
    const text = typeof e === "string" ? e : err?.message ?? String(e);
    this.toasts = [...this.toasts.slice(-2), { id, text, tone: "error" }];
    setTimeout(() => (this.toasts = this.toasts.filter((x) => x.id !== id)), 5200);
    if (err?.detail) console.warn(err.code, err.detail);
  }

  confirm(title: string, body?: string, danger = false) {
    return new Promise<boolean>((resolve) => (this.dialog = { kind: "confirm", title, body, danger, resolve }));
  }
  prompt(title: string, value = "", placeholder?: string) {
    return new Promise<string | null>((resolve) => (this.dialog = { kind: "prompt", title, value, placeholder, resolve }));
  }
  choose(title: string, options: string[]) {
    return new Promise<number | null>((resolve) => (this.dialog = { kind: "choose", title, options, resolve }));
  }
}

export const app = new App();
