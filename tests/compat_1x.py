#!/usr/bin/env python3
"""Hands libraries written by PickerHub 2.0 to PickerHub 1.x and checks that 1.x opens them intact.

For each library: 2.0 loads and saves it (crates/store, example `roundtrip`), then the 1.x desktop
app starts on that folder under Xvfb. The test fails if 1.x exits early, sets the file aside as
damaged, or if any value differs between what 2.0 wrote and what is on disk after 1.x ran.

    python3 tests/compat_1x.py --app dist/native/linux-x64/PickerHub/PickerHub [extra workspace.json ...]
"""
import argparse, json, os, pathlib, shutil, subprocess, sys, tempfile, time

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURES = sorted((ROOT / "crates/store/tests/fixtures").glob("*.json"))

def check(source: pathlib.Path, app: str) -> bool:
    with tempfile.TemporaryDirectory() as tmp:
        folder = pathlib.Path(tmp) / "PickerHub"
        folder.mkdir()
        shutil.copy(source, folder / "workspace.json")
        subprocess.run(["cargo", "run", "-q", "-p", "pickerhub-store", "--example", "roundtrip", "--", tmp], cwd=ROOT, check=True)
        written = json.loads((folder / "workspace.json").read_text())
        env = dict(os.environ, XDG_DATA_HOME=tmp)
        env.pop("WAYLAND_DISPLAY", None)
        proc = subprocess.Popen(["xvfb-run", "-a", "-s", "-screen 0 1400x900x24", app], env=env,
                                stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        time.sleep(8)
        alive = proc.poll() is None
        proc.terminate()
        try:
            proc.wait(10)
        except subprocess.TimeoutExpired:
            proc.kill()
        damaged = [p.name for p in folder.iterdir() if p.name.startswith("workspace.json.damaged")]
        after = json.loads((folder / "workspace.json").read_text())
        ok = alive and not damaged and after == written
        print(f"{'ok  ' if ok else 'FAIL'} {source.name}: 1.x running={alive} damaged={damaged} identical={after == written}")
        if not ok and not alive:
            print(proc.stderr.read().decode(errors="replace")[-2000:])
        return ok

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--app", required=True, help="PickerHub 1.x executable")
    ap.add_argument("extra", nargs="*", type=pathlib.Path, help="more workspace.json files (copied, never modified)")
    args = ap.parse_args()
    results = [check(f, args.app) for f in FIXTURES + args.extra]
    sys.exit(0 if all(results) else 1)

if __name__ == "__main__":
    main()
