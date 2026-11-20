// Actions shared by several pages.
import { app } from "./app.svelte";
import { ipc } from "./ipc";

export const FORMATS = ["css", "scss", "json", "csv", "txt", "gpl"] as const;

/** Adds colors to the palette selected on the library page. */
export async function addToPalette(hexes: string[]) {
  const p = app.palette;
  if (!p || hexes.length === 0) return;
  await app.edit([{ op: "addColors", palette: p.id, hexes }], hexes.length > 1 ? "أُضيفت الألوان إلى اللوحة" : "أُضيفت إلى اللوحة الحالية");
}

/** Asks for a format and saves colors that are not a saved palette. */
export async function exportColors(name: string, colors: [string, string][]) {
  const choice = await app.choose("صيغة التصدير", [...FORMATS]);
  if (choice == null) return;
  try {
    if (await ipc.exportColors(name, colors, FORMATS[choice])) app.toast("حُفظ الملف");
  } catch (e) { app.fail(e); }
}

/** Sets a color as the contrast pair's text or background and opens the contrast page. */
export function useInContrast(hex: string, role: "fg" | "bg") {
  if (role === "fg") app.contrastFg = hex; else app.contrastBg = hex;
  app.page = 2;
}

/** The latest request wins, at most one per frame: for drags that ask Rust for every position. */
export function perFrame<A extends unknown[], R>(fn: (...a: A) => Promise<R>, done: (r: R) => void) {
  let pending: A | null = null, busy = false;
  const run = async () => {
    const args = pending!; pending = null;
    try { done(await fn(...args)); } catch (e) { app.fail(e); }
    busy = false;
    if (pending) { busy = true; requestAnimationFrame(run); }
  };
  return (...args: A) => {
    pending = args;
    if (!busy) { busy = true; requestAnimationFrame(run); }
  };
}

export const isHex = (s: string) => /^#?[0-9a-f]{6}$/i.test(s.trim());
export const normalHex = (s: string) => ("#" + s.trim().replace(/^#/, "")).toUpperCase();
