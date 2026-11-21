#!/usr/bin/env python3
"""Checks the Tauri interface in the real WebView, on a fixture library (never the user's).

    cargo build -p pickerhub-app --features custom-protocol,qa
    python3 tests/ui_tauri.py [--pages] [--bench]

1. Final yeh: «بني» and «بنى» are drawn large in Noto Kufi at 400 and 700. When WebKit loses the dots
   (newer Kufi builds, docs/tauri-migration-plan.md 5.4) the two render pixel for pixel the same.
2. --pages: every page in Arabic and English, dark and light, saved to dist/qa-tauri.
3. Content security policy: every page, sheet, menu and animation runs without a refusal.
4. --a11y: an axe-core audit of every page and state. It needs a QA interface and no frozen prototypes:
       VITE_QA=1 npm --prefix ui run build
       TAURI_CONFIG='{"app":{"security":{"freezePrototype":false}}}' cargo build -p pickerhub-app --features custom-protocol,qa
5. --bench: frame times on the picker, idle and while the hue slider moves every frame.
"""
import argparse, os, pathlib, shutil, subprocess, sys, tempfile

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "target/debug/pickerhub"
FIXTURE = ROOT / "crates/store/tests/fixtures/v12-escaped.json"


def run(script: str, shot: pathlib.Path | None = None, wait: int = 1500, timeout: int = 60) -> str:
    with tempfile.TemporaryDirectory() as data:
        (pathlib.Path(data) / "PickerHub").mkdir()
        shutil.copy(FIXTURE, pathlib.Path(data) / "PickerHub/workspace.json")
        # X11 only: under a Wayland session GTK would open the app on the real desktop, not on Xvfb.
        # Its own config, cache and data folders: the window state and WebKit's files of the PickerHub
        # installed on this machine (same identifier) are never read or overwritten by a test.
        home = pathlib.Path(data) / "xdg"
        env = dict(os.environ, PICKERHUB_DATA_DIR=data, PICKERHUB_EVAL=script, PICKERHUB_WAIT=str(wait), GDK_BACKEND="x11", XDG_SESSION_TYPE="x11",
                   XDG_CONFIG_HOME=str(home / "config"), XDG_CACHE_HOME=str(home / "cache"), XDG_DATA_HOME=str(home / "data"))
        env.pop("WAYLAND_DISPLAY", None)
        if shot:
            env["PICKERHUB_SHOT"] = str(shot)
        # A private session bus: the single-instance guard never sees (or raises) a PickerHub the user has open.
        out = subprocess.run(["dbus-run-session", "--", str(APP)], env=env, capture_output=True, text=True, timeout=timeout)
        return out.stdout


def differing(a: Image.Image, b: Image.Image) -> int:
    pa, pb = a.convert("L").tobytes(), b.convert("L").tobytes()
    return sum(1 for x, y in zip(pa, pb) if abs(x - y) > 96)


def final_yeh() -> bool:
    script = """
      const box = document.createElement('div');
      box.style.cssText = 'position:fixed;inset:0;z-index:999;background:#fff;color:#000;display:grid;grid-template-columns:repeat(4,1fr);align-items:center;justify-items:center;font-family:"Noto Kufi Arabic";font-size:120px;line-height:2';
      for (const w of [400, 700]) for (const word of ['\u0628\u0646\u064A', '\u0628\u0646\u0649']) { const s = document.createElement('span'); s.style.fontWeight = w; s.textContent = word; box.append(s); }
      document.body.append(box);
    """
    with tempfile.TemporaryDirectory() as tmp:
        path = pathlib.Path(tmp) / "yeh.png"
        run(script, path)
        img = Image.open(path)
    w, h = img.size
    cells = [img.crop((i * w // 4, 0, (i + 1) * w // 4, h)) for i in range(4)]
    ok = True
    for weight, (dotted, dotless) in zip((400, 700), ((cells[0], cells[1]), (cells[2], cells[3]))):
        diff = differing(dotted, dotless)
        good = diff > 200
        ok &= good
        print(f"{'ok  ' if good else 'FAIL'} final yeh at {weight}: «بني» differs from «بنى» in {diff} px")
    return ok


def pages(out: pathlib.Path) -> bool:
    out.mkdir(parents=True, exist_ok=True)
    for lang in ("ar", "en"):
        for theme in ("dark", "light"):
            for page in range(7):
                path = out / f"{lang}-{theme}-{page}.png"
                run(f'__pickerhub.language("{lang}"); __pickerhub.theme("{theme}"); __pickerhub.page({page});', path, wait=1800)
                print(("ok   " if path.exists() else "FAIL ") + str(path.relative_to(ROOT)))
    return all((out / f"{l}-{t}-{p}.png").exists() for l in ("ar", "en") for t in ("dark", "light") for p in range(7))


# Walks every page in both themes and both languages, opens the sheets, menus and dialogs, plays the
# envelope and the image guide, and reports every Content-Security-Policy refusal on the way.
CSP_SWEEP = """
(async () => {
  const h = __pickerhub, wait = (ms) => new Promise((r) => setTimeout(r, ms));
  await h.setting("reduceMotion", false);
  for (const lang of ["ar", "en"]) for (const theme of ["dark", "light"]) {
    await h.language(lang); await h.theme(theme);
    for (let p = 0; p < 7; p++) {
      h.page(p); await wait(250);
      const select = document.querySelector(".select button, button[aria-haspopup]");
      if (select) { select.click(); await wait(150); document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" })); }
    }
  }
  h.app.settingsOpen = true; await wait(300); h.app.settingsOpen = false;
  h.app.captureMenu = {}; await wait(200); h.app.captureMenu = null;
  h.app.choose("x", ["a", "b"]); await wait(200); h.app.dialog = null;
  const c = document.createElement("canvas"); c.width = 64; c.height = 48;
  const g = c.getContext("2d"); g.fillStyle = "#d33"; g.fillRect(0, 0, 32, 48); g.fillStyle = "#3a6"; g.fillRect(32, 0, 32, 48);
  h.page(3); await h.image(c.toDataURL("image/png")); await wait(900);
  h.page(6); await wait(400);
  [...document.querySelectorAll("button.open")][0]?.click(); await wait(3200);
  h.report(JSON.stringify(h.violations()));
})();
"""


def csp() -> bool:
    text = run(CSP_SWEEP, None, wait=0, timeout=120).strip().splitlines()
    found = text[-1] if text else ""
    good = found == "[]"
    print(f"{'ok  ' if good else 'FAIL'} content security policy: {'no refusals' if good else found or 'no report'}")
    return good


# axe-core on every page in Arabic dark and English light, the sheets, an analysed image and the opened
# envelope. axe ships only in QA interface builds (VITE_QA=1), and it cannot run under Tauri's frozen
# prototypes, so this check needs its own build (see --a11y in the docstring).
A11Y_SWEEP = """
(async () => {
  const h = __pickerhub, wait = (ms) => new Promise((r) => setTimeout(r, ms)), out = {};
  const add = (where, list) => { for (const v of list || []) { const k = v.id; (out[k] ??= { impact: v.impact, help: v.help, where: [], nodes: new Set() }); out[k].where.push(where); v.nodes.forEach((n) => out[k].nodes.add(n)); } };
  for (const [lang, theme] of [["ar", "dark"], ["en", "light"]]) {
    await h.language(lang); await h.theme(theme);
    for (let p = 0; p < 7; p++) { h.page(p); await wait(400); add(`${lang}-${theme}-${p}`, await h.axe()); }
    h.app.settingsOpen = true; await wait(500); add(`${lang}-${theme}-settings`, await h.axe()); h.app.settingsOpen = false; await wait(300);
    h.app.captureMenu = {}; await wait(400); add(`${lang}-${theme}-capture`, await h.axe()); h.app.captureMenu = null; await wait(300);
  }
  const c = document.createElement("canvas"); c.width = 64; c.height = 48; const g = c.getContext("2d"); g.fillStyle = "#d33"; g.fillRect(0,0,32,48); g.fillStyle = "#3a6"; g.fillRect(32,0,32,48);
  h.page(3); await h.image(c.toDataURL("image/png")); await wait(1500); add("image-result", await h.axe());
  h.page(6); await wait(300); [...document.querySelectorAll("button.open")][0]?.click(); await wait(4000); add("daily-open", await h.axe());
  h.report(JSON.stringify(Object.fromEntries(Object.entries(out).map(([k, v]) => [k, { ...v, nodes: [...v.nodes].slice(0, 8), where: [...new Set(v.where)] }]))));
})().catch((e) => __pickerhub.report("ERR " + e));
"""


def a11y() -> bool:
    import json
    text = run(A11Y_SWEEP, None, wait=0, timeout=180).strip().splitlines()
    try:
        found = json.loads(text[-1])
    except (ValueError, IndexError):
        print("FAIL accessibility: " + (text[-1] if text else "no report"))
        return False
    for k, v in found.items():
        print(f"FAIL accessibility {k} [{v['impact']}] {v['help']}: {', '.join(v['where'][:6])} · {'; '.join(v['nodes'][:3])}")
    if not found:
        print("ok   accessibility: no axe violations on any page, sheet or state")
    return not found


def bench() -> bool:
    text = run("__pickerhub.bench()", None, wait=0, timeout=90).strip()
    print(text or "FAIL no benchmark output")
    return bool(text)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", action="store_true")
    ap.add_argument("--bench", action="store_true")
    ap.add_argument("--a11y", action="store_true")
    args = ap.parse_args()
    if not APP.exists():
        sys.exit("build first: cargo build -p pickerhub-app --features custom-protocol,qa")
    ok = final_yeh()
    ok &= csp()
    if args.pages:
        ok &= pages(ROOT / "dist/qa-tauri")
    if args.bench:
        ok &= bench()
    if args.a11y:
        ok &= a11y()
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
