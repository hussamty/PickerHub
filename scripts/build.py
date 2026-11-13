"""Build the offline application and portable installers using Rust and Python's stdlib."""
import base64
import hashlib
import json
import plistlib
import shutil
import subprocess
import sys
import tarfile
import zipfile
from html import escape
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
UI = ROOT / "ui"
VERSION = "0.1.0"

def encoded(path):
    return base64.b64encode(path.read_bytes()).decode("ascii")

def build():
    subprocess.run(["cargo", "build", "--locked", "--release", "--target", "wasm32-unknown-unknown"], cwd=ROOT, check=True)
    target = json.loads(subprocess.check_output(["cargo", "metadata", "--no-deps", "--format-version", "1"], cwd=ROOT))["target_directory"]
    wasm = Path(target) / "wasm32-unknown-unknown/release/pickerhub.wasm"
    style = (UI / "style.css").read_text(encoding="utf-8")
    for token, weight in [("REGULAR", "Regular"), ("BOLD", "Bold")]:
        style = style.replace("/*" + token + "*/", encoded(UI / "fonts" / f"NotoSansArabic-{weight}.ttf"))
    code = "\n".join((UI / f"{name}.js").read_text(encoding="utf-8") for name in ["core", "library", "images", "capture", "app"])
    page = (UI / "index.html").read_text(encoding="utf-8").replace("/*WASM*/", encoded(wasm)).replace("/*STYLE*/", style).replace("/*CODE*/", code)
    notices = [(ROOT / "LICENSE").read_text(encoding="utf-8")]
    notices.extend(p.read_text(encoding="utf-8") for p in sorted((ROOT / "third_party").glob("*.txt")))
    page = page.replace("</body>", "<template id=\"licenses\"><pre>" + escape("\n\n".join(notices)) + "</pre></template></body>")
    DIST.mkdir(exist_ok=True)
    output = DIST / "PickerHub.html"
    output.write_text(page, encoding="utf-8")
    return output

def bundle(output):
    stage = DIST / "packaging"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir()
    def common(directory):
        directory.mkdir(parents=True, exist_ok=True)
        shutil.copy2(output, directory)
        for name in ["LICENSE", "README.md", "CONTRIBUTING.md", "CHANGELOG.md", "third_party", "docs"]:
            source = ROOT / name
            if source.is_dir():
                shutil.copytree(source, directory / name)
            else:
                shutil.copy2(source, directory)
    def executable(path, text):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        path.chmod(0o755)
    def archive(source, name):
        path = DIST / name
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            for item in sorted(source.rglob("*")):
                if item.is_file():
                    archive.write(item, item.relative_to(source))
        return path

    windows = stage / "windows/PickerHub"
    common(windows)
    archive(windows.parent, f"PickerHub-{VERSION}-Windows.zip")
    app = stage / "mac/PickerHub.app/Contents"
    common(app / "Resources")
    executable(app / "MacOS/PickerHub", '#!/bin/sh\nexec /usr/bin/open "$(dirname "$0")/../Resources/PickerHub.html"\n')
    (app / "Info.plist").write_bytes(plistlib.dumps(dict(CFBundleExecutable="PickerHub", CFBundleIdentifier="org.pickerhub.app", CFBundleName="PickerHub", CFBundlePackageType="APPL", CFBundleShortVersionString=VERSION, CFBundleVersion="1")))
    archive(app.parent.parent, f"PickerHub-{VERSION}-macOS.zip")
    linux = stage / "linux/PickerHub"
    common(linux)
    executable(linux / "pickerhub", '#!/bin/sh\nexec xdg-open "$(dirname "$(readlink -f "$0")")/PickerHub.html"\n')
    with tarfile.open(DIST / f"PickerHub-{VERSION}-Linux.tar.gz", "w:gz") as archive_file:
        archive_file.add(linux, arcname="PickerHub")
    if shutil.which("dpkg-deb"):
        deb = stage / "deb"
        common(deb / "usr/share/pickerhub")
        executable(deb / "usr/bin/pickerhub", '#!/bin/sh\nexec xdg-open /usr/share/pickerhub/PickerHub.html\n')
        desktop = deb / "usr/share/applications"
        desktop.mkdir(parents=True)
        (desktop / "pickerhub.desktop").write_text("[Desktop Entry]\nType=Application\nName=PickerHub\nComment=Offline color workbench\nExec=pickerhub\nIcon=applications-graphics\nTerminal=false\nCategories=Graphics;Utility;\n", encoding="utf-8")
        (deb / "DEBIAN").mkdir()
        (deb / "DEBIAN/control").write_text(f"Package: pickerhub\nVersion: {VERSION}\nArchitecture: all\nMaintainer: PickerHub contributors\nDepends: xdg-utils\nSection: graphics\nPriority: optional\nDescription: Offline Arabic color workbench powered by Rust and WebAssembly\n", encoding="utf-8")
        subprocess.run(["dpkg-deb", "--root-owner-group", "--build", deb, DIST / f"pickerhub_{VERSION}_all.deb"], check=True)
    artifacts = [output, *DIST.glob(f"PickerHub-{VERSION}-*"), *DIST.glob(f"pickerhub_{VERSION}_*.deb")]
    (DIST / "SHA256SUMS").write_text("".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}\n" for p in sorted(artifacts)), encoding="ascii")
    shutil.rmtree(stage)

if __name__ == "__main__":
    output = build()
    if "--packages" in sys.argv:
        bundle(output)
    print(output)
