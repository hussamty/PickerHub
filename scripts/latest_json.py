#!/usr/bin/env python3
"""Writes the updater manifest (latest.json) for a release from its signed artifacts.

    python3 scripts/latest_json.py <version> <folder with the release files> [notes file] > latest.json

Each installer is paired with the minisign signature `tauri build` (or scripts/release_deb.py) wrote
beside it, and every signature must name this version, as the updater requires. The URLs point at
the GitHub release for tag v<version>, which is where the updater's endpoint (src-tauri/src/update.rs)
finds this file.
"""
import base64
import datetime
import json
import pathlib
import re
import sys

REPO = "hussamty/PickerHub"


def signed_for(name: str, signature: str, version: str) -> None:
    """Stops the release when a signature does not name this version in its signed comment.

    The app is built with requireSignedVersion, so the updater refuses such an installer: the
    release would publish an update that fails on every machine that tries it.
    """
    comment = base64.b64decode(signature).decode("utf-8", "replace").splitlines()[2]
    found = re.search(r"\bversion:(\S+)", comment)
    if not found or found.group(1).lstrip("v") != version:
        sys.exit(f"{name}: signed for {found.group(1) if found else 'no version'}, not {version}; "
                 "sign updater files with --app-version")


def main() -> None:
    version, folder = sys.argv[1].lstrip("v"), pathlib.Path(sys.argv[2])
    notes = pathlib.Path(sys.argv[3]).read_text(encoding="utf-8").strip() if len(sys.argv) > 3 else f"PickerHub {version}"
    base = f"https://github.com/{REPO}/releases/download/v{version}/"
    files = {p.name: p for p in folder.rglob("*") if p.is_file()}

    def entry(suffix: str) -> dict | None:
        names = sorted(n for n in files if n.endswith(suffix) and n + ".sig" in files)
        if not names:
            return None
        name = names[0]
        signature = files[name + ".sig"].read_text().strip()
        signed_for(name, signature, version)
        return {"signature": signature, "url": base + name}

    kinds = {
        "linux-x86_64-appimage": entry(".AppImage"),
        "linux-x86_64-deb": entry("_amd64.deb"),
        "linux-x86_64-rpm": entry(".x86_64.rpm"),
        "windows-x86_64-nsis": entry("-setup.exe"),
        "darwin-aarch64-app": entry(".app.tar.gz"),
        "darwin-x86_64-app": entry(".app.tar.gz"),   # a universal build serves both
    }
    platforms = {k: v for k, v in kinds.items() if v}
    # Installs that cannot name their bundle type fall back to the plain platform key.
    for plain, preferred in (("linux-x86_64", "linux-x86_64-appimage"), ("windows-x86_64", "windows-x86_64-nsis"),
                             ("darwin-aarch64", "darwin-aarch64-app"), ("darwin-x86_64", "darwin-x86_64-app")):
        if preferred in platforms:
            platforms[plain] = platforms[preferred]
    if not platforms:
        sys.exit(f"no signed installers in {folder}")
    pub = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    json.dump({"version": version, "notes": notes, "pub_date": pub, "platforms": platforms}, sys.stdout, indent=2)
    print()


if __name__ == "__main__":
    main()
