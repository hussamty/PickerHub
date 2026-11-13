"""Build a self-contained native desktop package. Default: current host and architecture."""
import argparse
import hashlib
import json
import os
import platform
import plistlib
import shutil
import subprocess
import tarfile
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIST = ROOT / "dist" / "native"
VERSION = "0.3.1"
HOST = {"Linux": "linux", "Windows": "win", "Darwin": "osx"}[platform.system()]
ARCH = "arm64" if platform.machine().lower() in ("aarch64", "arm64") else "x64"


def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


def native(rid, work):
    osname, arch = rid.split("-")
    if (osname, arch) != (HOST, ARCH):
        raise ValueError("Cross-publishing requires --core and, on Linux/Windows, --capture.")
    run("cargo", "build", "--release", "--locked")
    lib = {"linux": "libpickerhub.so", "win": "pickerhub.dll", "osx": "libpickerhub.dylib"}[HOST]
    lib = ROOT / "target" / "release" / lib
    src = ROOT / "desktop" / "native"
    capture = None
    if osname == "linux":
        capture = work / "libpicker_capture.so"
        run("cc", "-shared", "-fPIC", "-O2", src / "x11.c", "-o", capture, "-lX11")
    if osname == "win":
        capture = work / "picker_capture.dll"
        run("cl", "/nologo", "/LD", "/O2", src / "windows.c", f"/Fo{work / 'capture.obj'}", "/link",
            f"/OUT:{capture}", "gdi32.lib", "user32.lib")
    return lib, capture


def zip_folder(folder, path):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=5) as z:
        for item in sorted(folder.rglob("*")):
            if item.is_file():
                z.write(item, item.relative_to(folder.parent))


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--rid", default=f"{HOST}-{ARCH}", choices=["linux-x64", "win-x64", "osx-arm64", "osx-x64"])
    ap.add_argument("--core", type=Path, help="Prebuilt Rust dynamic library matching the target")
    ap.add_argument("--capture", type=Path, help="Prebuilt X11 or Win32 capture library")
    ap.add_argument("--packages", action="store_true", help="Also create distribution archives")
    args = ap.parse_args()
    rid = args.rid
    osname = rid.split("-")[0]
    DIST.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=DIST) as temporary:
        work = Path(temporary)
        core, capture = (args.core, args.capture) if args.core else native(rid, work)
        if not core.is_file() or (osname != "osx" and not (capture and capture.is_file())):
            raise SystemExit("Missing target-specific native library.")
        bundle = work / ("PickerHub.app" if osname == "osx" else "PickerHub")
        runtime = bundle / "Contents/MacOS" if osname == "osx" else bundle
        run("dotnet", "publish", ROOT / "desktop/PickerHub.csproj", "-c", "Release", "-r", rid,
            "--self-contained", "true", "-p:RestoreLockedMode=true", "-p:DebugType=None", "-o", runtime)
        # The managed UI assembly and Rust library have distinct names on case-insensitive volumes.
        core_name = {"linux": "libpickerhub_core.so", "win": "pickerhub_core.dll", "osx": "libpickerhub_core.dylib"}[osname]
        shutil.copy2(core, runtime / core_name)
        if capture:
            shutil.copy2(capture, runtime / ("picker_capture.dll" if osname == "win" else "libpicker_capture.so"))
        notes = bundle / "Contents/Resources" if osname == "osx" else bundle / "documentation"
        notes.mkdir(parents=True)
        for f in ["README.md", "LICENSE", "CHANGELOG.md", "CONTRIBUTING.md"]:
            shutil.copy2(ROOT / f, notes / f)
        for directory in ["docs", "third_party"]:
            shutil.copytree(ROOT / directory, notes / directory)
        # Ship license files from the exact NuGet packages resolved for this build.
        assets = json.loads((ROOT / "desktop/obj/project.assets.json").read_text(encoding="utf-8-sig"))
        package_dirs = [Path(d) for d in assets["packageFolders"]]
        packages = {data["path"] for data in assets["libraries"].values() if data.get("type") == "package"}
        for base in package_dirs:
            for runtime_package in base.glob("microsoft.netcore.app.runtime." + rid + "/*"):
                packages.add(str(runtime_package.relative_to(base)))
            for package in packages:
                folder = base / package
                if folder.is_dir():
                    for f in folder.iterdir():
                        if f.is_file() and f.name.lower().startswith(("license", "third-party-notices", "copying")):
                            license_dir = notes / "third_party" / "runtime" / folder.parent.name
                            license_dir.mkdir(parents=True, exist_ok=True)
                            shutil.copy2(f, license_dir / f.name)
        if osname == "osx":
            (bundle / "Contents/Info.plist").write_bytes(plistlib.dumps({
                "CFBundleExecutable": "PickerHub", "CFBundleName": "PickerHub", "CFBundleIdentifier": "org.pickerhub.app",
                "CFBundlePackageType": "APPL", "CFBundleShortVersionString": VERSION, "CFBundleVersion": "4",
                "NSHighResolutionCapable": True, "LSMinimumSystemVersion": "14.0"}))
            (runtime / "PickerHub").chmod(0o755)
            if HOST == "osx":
                run("codesign", "--force", "--deep", "--sign", "-", bundle)
            elif shutil.which("rcodesign"):
                run("rcodesign", "sign", bundle)
            else:
                raise SystemExit("Cross-packaging a macOS app requires rcodesign for ad hoc signing.")
        dest = DIST / rid / bundle.name
        dest.parent.mkdir(exist_ok=True)
        if dest.exists():
            shutil.rmtree(dest)
        shutil.move(bundle, dest)
        if not args.packages:
            print(dest)
            return
        stem = f"PickerHub-{VERSION}-{rid}"
        if osname == "linux":
            archive = DIST / (stem + ".tar.gz")
            with tarfile.open(archive, "w:gz", compresslevel=5) as t:
                t.add(dest, arcname="PickerHub")
            if shutil.which("dpkg-deb"):
                deb = work / "deb"
                shutil.copytree(dest, deb / "opt/pickerhub")
                for path in ["usr/bin", "usr/share/applications", "DEBIAN"]:
                    (deb / path).mkdir(parents=True)
                (deb / "usr/bin/pickerhub").symlink_to("/opt/pickerhub/PickerHub")
                (deb / "usr/share/applications/pickerhub.desktop").write_text(
                    "[Desktop Entry]\nType=Application\nName=PickerHub\nExec=/opt/pickerhub/PickerHub\n"
                    "Icon=applications-graphics\nTerminal=false\nCategories=Graphics;Utility;\nStartupWMClass=PickerHub\n")
                (deb / "DEBIAN/control").write_text(
                    f"Package: pickerhub\nVersion: {VERSION}\nArchitecture: amd64\nMaintainer: PickerHub contributors\nSection: graphics\n"
                    "Priority: optional\nDepends: libc6, libgcc-s1, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3\n"
                    "Recommends: xdg-desktop-portal\nDescription: Native Arabic color workbench and desktop eyedropper\n")
                run("dpkg-deb", "--root-owner-group", "-Zgzip", "-z5", "--build", deb, DIST / f"pickerhub_{VERSION}_amd64.deb")
        else:
            archive = DIST / (stem + ".zip")
            zip_folder(dest, archive)
        for path in [archive, *([DIST / f"pickerhub_{VERSION}_amd64.deb"] if osname=="linux" and shutil.which("dpkg-deb") else [])]:
            path.with_name(path.name+".sha256").write_text(hashlib.sha256(path.read_bytes()).hexdigest()+"  "+path.name+"\n")
        print(dest, archive)


if __name__ == "__main__":
    main()
