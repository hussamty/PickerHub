"""Run inside Xvfb with a 1400x900 screen. Exercises the real desktop window and native picker."""
import ctypes
import json
import os
import re
import subprocess
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "dist/native/linux-x64/PickerHub/PickerHub"
x11 = ctypes.CDLL("libX11.so.6")
input_api = ctypes.CDLL("libXtst.so.6")
x11.XOpenDisplay.restype = ctypes.c_void_p
display = ctypes.c_void_p(x11.XOpenDisplay(None))
assert display.value, "Start xvfb-run first"


def click(x, y):
    input_api.XTestFakeMotionEvent(display, 0, x, y, 0)
    for pressed in (1, 0):
        input_api.XTestFakeButtonEvent(display, 1, pressed, 0)
    x11.XFlush(display)


def key(name):
    symbol = x11.XStringToKeysym(name.encode())
    code = x11.XKeysymToKeycode(display, symbol)
    for pressed in (1, 0):
        input_api.XTestFakeKeyEvent(display, code, pressed, 0)
    x11.XFlush(display)


def until(predicate):
    for _ in range(100):
        value = predicate()
        if value:
            return value
        time.sleep(.08)
    raise AssertionError("Timed out waiting for " + predicate.__name__)


def windows():
    return subprocess.check_output(["xwininfo", "-root", "-tree"], text=True)


def main():
    with tempfile.TemporaryDirectory() as home:
        env = dict(os.environ, HOME=home, XDG_DATA_HOME=home+"/data", XDG_CONFIG_HOME=home+"/config",
                   XDG_SESSION_TYPE="x11", WAYLAND_DISPLAY="")
        # If an independent test portal is supplied, exercise the Wayland request and response.
        portal = os.environ.get("PICKERHUB_TEST_PORTAL") == "1"
        if portal:
            env["XDG_SESSION_TYPE"] = "wayland"
        with subprocess.Popen([APP], env=env) as process:
            try:
                window = until(lambda: re.search(r'(0x[0-9a-f]+) "PickerHub"', windows()))[1]
                # The window moves from its initial origin to CenterScreen during startup.
                time.sleep(1)
                info = subprocess.check_output(["xwininfo", "-id", window], text=True)
                left = int(re.search(r"Absolute upper-left X:\s+(-?\d+)", info)[1])
                top = int(re.search(r"Absolute upper-left Y:\s+(-?\d+)", info)[1])
                width = int(re.search(r"Width:\s+(\d+)", info)[1])
                x11.XSetInputFocus(display, int(window, 16), 1, 0)
                x11.XFlush(display)
                subprocess.run(["xsetroot", "-solid", "#37A6D4"], check=True)
                if not portal:
                    # A separate X client owns this colored window, outside the workbench.
                    x11.XDefaultRootWindow.restype = ctypes.c_ulong
                    x11.XCreateSimpleWindow.restype = ctypes.c_ulong
                    root = x11.XDefaultRootWindow(display)
                    target = x11.XCreateSimpleWindow(display, root, 1300, 800, 95, 90, 0, 0, 0x745AC3)
                    x11.XStoreName(display, target, b"External color reference")
                    x11.XMapRaised(display, target)
                    x11.XSync(display, False)
                click(left+width-58, top+42)  # The main «القطّارة» button.
                if not portal:
                    until(lambda: re.search(r"146x153[+-]", windows()))
                    click(1350, 850)
                file = until(lambda: next(Path(home).rglob("workspace.json"), None))
                original = json.loads(file.read_text())
                assert original["history"] == (["#336699"] if portal else ["#745AC3"])
                if not portal:
                    key("F8")
                    until(lambda: re.search(r"146x153[+-]", windows()))
                    click(1230, 850)  # Desktop background, outside both windows.
                    until(lambda: json.loads(file.read_text())["color"] == "#37A6D4")
                    original = json.loads(file.read_text())
                    key("F8")
                    until(lambda: re.search(r"146x153[+-]", windows()))
                    key("Escape")
                    until(lambda: not re.search(r"146x153[+-]", windows()))
                    assert json.loads(file.read_text()) == original
                assert process.poll() is None
                print("PASS: desktop eyedropper and persisted selected color", original["color"])
            finally:
                process.terminate()


if __name__ == "__main__":
    main()
