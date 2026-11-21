#!/usr/bin/env python3
"""Gives the release .deb its final package name and signs it again for the updater.

Tauri names a Debian package after the product in kebab case, so "PickerHub" becomes `picker-hub`.
1.x shipped as `pickerhub` (with /usr/bin/pickerhub), and 2.x must upgrade it in place rather than
install beside it and collide on that path. This rewrites `Package:` to `pickerhub`, keeps the
Replaces/Conflicts on the 2.0 alphas (`picker-hub-2`) that the release config adds, rebuilds the
archive as `pickerhub_<version>_<arch>.deb`, and, because the archive changed, signs it again with
the updater key (TAURI_SIGNING_PRIVATE_KEY and TAURI_SIGNING_PRIVATE_KEY_PASSWORD, as `tauri build`
uses them). No key is read from or written to the repository.

    python3 scripts/release_deb.py target/release/bundle/deb
"""
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
TAURI = ROOT / "ui/node_modules/.bin/tauri"


def run(*args: object) -> None:
    subprocess.run([str(a) for a in args], check=True)


def repackage(deb: pathlib.Path) -> pathlib.Path:
    with tempfile.TemporaryDirectory() as tmp:
        tree = pathlib.Path(tmp) / "root"
        run("dpkg-deb", "-R", deb, tree)
        control = tree / "DEBIAN/control"
        text = control.read_text(encoding="utf-8")
        version = re.search(r"^Version:\s*(\S+)", text, re.M).group(1)
        arch = re.search(r"^Architecture:\s*(\S+)", text, re.M).group(1)
        text = re.sub(r"^Package:.*$", "Package: pickerhub", text, flags=re.M)
        for field in ("Replaces", "Conflicts"):
            if not re.search(rf"^{field}:.*\bpicker-hub-2\b", text, re.M):
                sys.exit(f"{deb.name}: {field} must name picker-hub-2; build with src-tauri/tauri.release.conf.json")
        control.write_text(text, encoding="utf-8")
        out = deb.with_name(f"pickerhub_{version}_{arch}.deb")
        run("dpkg-deb", "--root-owner-group", "-Zxz", "--build", tree, out)
    return out


def main() -> None:
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / "target/release/bundle/deb")
    sources = [d for d in folder.glob("*.deb") if not d.name.startswith("pickerhub_")]
    if not sources:
        sys.exit(f"no Tauri .deb in {folder}")
    signing = bool(os.environ.get("TAURI_SIGNING_PRIVATE_KEY"))
    for deb in sources:
        out = repackage(deb)
        deb.unlink()
        deb.with_name(deb.name + ".sig").unlink(missing_ok=True)
        if signing:
            # --app-version puts the version in the signed comment: the updater requires it
            # (requireSignedVersion) and refuses an installer signed without it.
            version = subprocess.run(["dpkg-deb", "-f", out, "Version"], capture_output=True, text=True, check=True).stdout.strip()
            run(TAURI, "signer", "sign", "--app-version", version, out)
        info = subprocess.run(["dpkg-deb", "-f", out, "Package", "Version", "Replaces", "Conflicts"], capture_output=True, text=True, check=True).stdout
        print(out.name + ("  (signed)" if signing else "  (unsigned: no TAURI_SIGNING_PRIVATE_KEY)"))
        print("  " + info.strip().replace("\n", "\n  "))


if __name__ == "__main__":
    main()
