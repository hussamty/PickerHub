import "./app.css";
import { mount } from "svelte";
import App from "./App.svelte";
import LensWindow from "./lens/LensWindow.svelte";
import Overlay from "./lens/Overlay.svelte";

// One bundle, three surfaces: the workbench, the X11 lens window and the Wayland lens overlay.
const target = document.getElementById("app")!;
const view = location.hash === "#lens" ? LensWindow : location.hash === "#overlay" ? Overlay : App;
if (view !== App) document.documentElement.classList.add("surface");
mount(view, { target });

// Test hook for scripted screenshots (tests/ui_tauri.py): page switching and state reads only.
import { app } from "./lib/app.svelte";
import { images } from "./lib/images.svelte";
import { ipc } from "./lib/ipc";
// Every Content-Security-Policy refusal, for the QA sweep (tests/ui_tauri.py --csp).
const violations: string[] = [];
document.addEventListener("securitypolicyviolation", (e) => violations.push(`${e.effectiveDirective} ${e.blockedURI || "inline"} ${e.sourceFile ? e.sourceFile.split("/").pop() + ":" + e.lineNumber : ""}`.trim()));
(window as unknown as { __pickerhub: unknown }).__pickerhub = {
  violations: () => [...violations],
  /** An accessibility audit of what is on screen (axe-core). Only QA builds (VITE_QA=1) carry it. */
  axe: async () => {
    if (import.meta.env.VITE_QA !== "1") return null;
    const axe = (await import("axe-core")).default;
    const r = await axe.run(document, { resultTypes: ["violations"] });
    return r.violations.map((v) => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.slice(0, 4).map((n) => n.target.join(" ")), count: v.nodes.length }));
  },
  page: (i: number) => (app.page = i),
  theme: (theme: "dark" | "light") => app.setting("theme", theme),
  setting: (key: string, value: unknown) => app.setting(key as never, value),
  language: (language: "ar" | "en") => app.setting("language", language),
  image: async (url: string, name = "test.png") => {
    // Decoded by hand: the CSP (connect-src) rightly refuses fetch() on data: URLs.
    const bytes = Uint8Array.from(atob(url.split(",")[1]), (c) => c.charCodeAt(0));
    images.result = await ipc.analyzeImage(bytes, name);
  },
  report: (text: string) => ipc.qaReport(text),
  app,
  /** Frame times: idle with the room at rest, then the hue slider moved on every frame. */
  bench: async () => {
    const measure = (ms: number, step?: () => void) => new Promise<number[]>((done) => {
      const gaps: number[] = []; let last = performance.now(); const start = last;
      const tick = (now: number) => { gaps.push(now - last); last = now; step?.(); if (now - start < ms) requestAnimationFrame(tick); else done(gaps.slice(1)); };
      requestAnimationFrame(tick);
    });
    const stats = (name: string, g: number[]) => {
      const s = [...g].sort((a, b) => a - b), avg = g.reduce((a, b) => a + b, 0) / g.length;
      return `${name}: ${(1000 / avg).toFixed(1)} fps, p95 ${s[Math.floor(s.length * 0.95)].toFixed(1)} ms, over 25 ms ${g.filter((x) => x > 25).length}/${g.length}`;
    };
    app.page = 0;
    await new Promise((r) => setTimeout(r, 800));
    const idle = await measure(3000);
    const hue = document.querySelector<HTMLInputElement>("input.hue")!;
    let h = 0;
    const sweep = await measure(4000, () => { h = (h + 3) % 360; hue.value = String(h); hue.dispatchEvent(new Event("input")); });
    await ipc.qaReport([stats("idle", idle), stats("hue sweep", sweep)].join("\n"));
  },
  /** Frame times while the spine, a dropdown and the settings sheet open and close over and over. */
  benchUi: async () => {
    const measure = (ms: number, step: (i: number) => void) => new Promise<number[]>((done) => {
      const gaps: number[] = []; let last = performance.now(); const start = last; let i = 0;
      const tick = (now: number) => { gaps.push(now - last); last = now; step(i++); if (now - start < ms) requestAnimationFrame(tick); else done(gaps.slice(1)); };
      requestAnimationFrame(tick);
    });
    const stats = (name: string, g: number[]) => {
      const s = [...g].sort((a, b) => a - b), avg = g.reduce((a, b) => a + b, 0) / g.length;
      return `${name}: ${(1000 / avg).toFixed(1)} fps, p95 ${s[Math.floor(s.length * 0.95)].toFixed(1)} ms, over 25 ms ${g.filter((x) => x > 25).length}/${g.length}`;
    };
    const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
    const out: string[] = [];
    for (const glass of [false, true]) {
      await app.setting("fullGlass", glass);
      app.page = 1;
      await wait(700);
      const spine = document.querySelector(".spine")!;
      out.push(stats(`${glass ? "full" : "normal"} spine`, await measure(3000, (i) => { if (i % 36 === 0) spine.dispatchEvent(new PointerEvent(i % 72 === 0 ? "pointerenter" : "pointerleave")); })));
      spine.dispatchEvent(new PointerEvent("pointerleave"));
      await wait(500);
      const trigger = document.querySelector<HTMLButtonElement>(".select .trigger")!;
      out.push(stats(`${glass ? "full" : "normal"} menu`, await measure(3000, (i) => { if (i % 24 === 0) trigger.click(); })));
      document.body.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true }));
      await wait(400);
      out.push(stats(`${glass ? "full" : "normal"} sheet`, await measure(3000, (i) => { if (i % 30 === 0) app.settingsOpen = !app.settingsOpen; })));
      app.settingsOpen = false;
      await wait(400);
      // A hover-like repaint every frame on a button inside a glass pane (the picker's formats card).
      app.page = 0;
      await wait(700);
      const rows = [...document.querySelectorAll<HTMLElement>(".format")];
      out.push(stats(`${glass ? "full" : "normal"} hover`, await measure(3000, (i) => { rows.forEach((r, k) => (r.style.background = k === i % rows.length ? "rgba(255,255,255,0.12)" : "")); })));
      rows.forEach((r) => (r.style.background = ""));
      // Moving from page to page, as the spine does.
      out.push(stats(`${glass ? "full" : "normal"} pages`, await measure(4200, (i) => { if (i % 30 === 0) app.page = (app.page + 1) % 7; })));
      const t0 = performance.now(); app.page = 1; await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
      out.push(`${glass ? "full" : "normal"} palettes page shown in ${(performance.now() - t0).toFixed(0)} ms`);
    }
    await ipc.qaReport(out.join("\n"));
  },
  state: () => ({ page: app.page, theme: app.theme, language: app.language, palettes: app.ws?.palettes.length }),
};
