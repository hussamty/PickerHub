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


def move(x,y):
    input_api.XTestFakeMotionEvent(display,0,x,y,0)
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


def eyedropper(left, top, width):
    """Centre of the main «القطّارة» button, found by its brand yellow rather than a fixed position.
    The pill is 40 px tall and centred 36 px below the window top; 14 px above its centre it is solid
    yellow, clear of its icon and label, and wider than any yellow in the mark beside the wordmark."""
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XGetImage.restype = ctypes.c_void_p
    x11.XGetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
    x11.XGetPixel.restype = ctypes.c_ulong
    x11.XDestroyImage.argtypes = [ctypes.c_void_p]
    span = min(width, 720)
    image = x11.XGetImage(display, x11.XDefaultRootWindow(display), left, top + 22, span, 1, ctypes.c_ulong(-1), 2)
    assert image
    try:
        runs, start = [], None
        for x in range(span + 1):
            pixel = x11.XGetPixel(image, x, 0) if x < span else 0
            yellow = (pixel >> 16 & 255) > 200 and (pixel >> 8 & 255) > 190 and (pixel & 255) < 110
            if yellow and start is None:
                start = x
            elif not yellow and start is not None:
                runs.append((x - start, start))
                start = None
        length, start = max(runs, default=(0, 0))
        assert length > 60, f"No eyedropper button in the top bar: {runs}"
        return left + start + length // 2, top + 36
    finally:
        x11.XDestroyImage(image)


def main():
    with tempfile.TemporaryDirectory() as home:
        env = dict(os.environ, HOME=home, XDG_DATA_HOME=home+"/data", XDG_CONFIG_HOME=home+"/config",
                   XDG_SESSION_TYPE="x11", WAYLAND_DISPLAY="")
        # If an independent test portal is supplied, exercise the Wayland request and response.
        portal = os.environ.get("PICKERHUB_TEST_PORTAL") == "1"
        if portal:
            env["XDG_SESSION_TYPE"] = "wayland"
        single_color = portal and os.environ.get("PICKERHUB_TEST_LIVE") != "1"
        if portal and not single_color:
            # Existing libraries migrate to system picking by default. Only this
            # explicit preference should route the button into ScreenCast.
            folder = Path(env["XDG_DATA_HOME"]) / "PickerHub"
            folder.mkdir(parents=True)
            (folder / "workspace.json").write_text(json.dumps({
                "version": 1, "color": "#87CEEB", "limit": 32, "theme": "light",
                "palettes": [{"id":"test", "name":"Test", "colors":[]}], "gradients":[],
                "history": [], "useWaylandScreenSharing": True
            }))
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
                click(*eyedropper(left, top, width))  # The main «القطّارة» button.
                if single_color:
                    def is_visible():
                        return "Map State: IsViewable" in subprocess.check_output(["xwininfo","-id",window], text=True)
                    until(lambda: not is_visible())
                    until(is_visible)
                    files = list(Path(home).rglob("workspace.json"))
                    if os.environ.get("PICKERHUB_PORTAL_RESULT", "0") == "0":
                        # Return to the UI is independent of the coalesced disk write.
                        until(lambda: len(list(Path(home).rglob("workspace.json"))) == 1)
                        files = list(Path(home).rglob("workspace.json"))
                        until(lambda: json.loads(files[0].read_text())["history"] == ["#3175B3"])
                    else:
                        assert not files
                    print("PASS: system color request completed and the workbench returned")
                    return
                if portal and os.environ.get("PICKERHUB_PORTAL_RESULT","0")!="0":
                    until(lambda:"= 0" in subprocess.check_output(["xprop","-id",window,"_NET_WM_WINDOW_OPACITY"],text=True))
                    until(lambda:"= 0" not in subprocess.check_output(["xprop","-id",window,"_NET_WM_WINDOW_OPACITY"],text=True))
                    assert not json.loads((folder / "workspace.json").read_text())["history"]
                    print("PASS: main window restored after portal cancellation or failure")
                    return
                until(lambda: re.search(r'"PickerHub Magnifier"',windows()))
                def mapped():
                    return "Map State: IsViewable" in subprocess.check_output(["xwininfo","-id",window],text=True)
                assert not mapped(), "PickerHub must hide while the magnifier is active"
                move(1350,850)
                time.sleep(.48)
                # The lens itself shows the sampled color while moving, before selection. The lens is 232x314 and sits
                # up-left of the pointer here, at (1094,512); the swatch is a disc centred 33 across and 253 down.
                x11.XGetImage.restype=ctypes.c_void_p
                x11.XGetPixel.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_int]
                x11.XGetPixel.restype=ctypes.c_ulong
                x11.XDestroyImage.argtypes=[ctypes.c_void_p]
                def first_swatch():
                    image=x11.XGetImage(display,x11.XDefaultRootWindow(display),1127,765,1,1,ctypes.c_ulong(-1),2)
                    assert image
                    try: return x11.XGetPixel(image,0,0)&0xffffff == (0x336699 if portal else 0x745AC3)
                    finally:x11.XDestroyImage(image)
                # The input window can map before PipeWire delivers its first frame.
                # Wait for the actual expected color rather than a fixed GPU deadline.
                try:
                    until(first_swatch)
                except AssertionError:
                    print(windows(), flush=True)
                    if portal: print(subprocess.check_output(["gdbus","call","--session","--dest","org.freedesktop.portal.Desktop","--object-path","/org/freedesktop/portal/desktop","--method","org.pickerhub.Test.Frames"], text=True), flush=True)
                    trace = subprocess.run(["sudo", "gdb", "-batch", "-ex", "set pagination off", "-ex", "thread apply all bt", "-p", str(process.pid)], capture_output=True, text=True, timeout=20)
                    print(trace.stdout + trace.stderr, flush=True)
                    print(subprocess.check_output(["pw-dump"], text=True) if portal else "X11", flush=True)
                    from PIL import Image
                    class ImageInfo(ctypes.Structure):
                        _fields_ = [(n,t) for n,t in [('width',ctypes.c_int),('height',ctypes.c_int),('offset',ctypes.c_int),('format',ctypes.c_int),('data',ctypes.c_void_p),('byte_order',ctypes.c_int),('unit',ctypes.c_int),('bit_order',ctypes.c_int),('pad',ctypes.c_int),('depth',ctypes.c_int),('stride',ctypes.c_int),('bits',ctypes.c_int)]]
                    shot=x11.XGetImage(display,x11.XDefaultRootWindow(display),0,0,1400,900,ctypes.c_ulong(-1),2)
                    info=ctypes.cast(shot,ctypes.POINTER(ImageInfo)).contents
                    out=ROOT/'dist/qa';out.mkdir(parents=True,exist_ok=True)
                    Image.frombytes('RGB',(info.width,info.height),ctypes.string_at(info.data,info.stride*info.height),'raw','BGRX',info.stride).save(out/'portal-failure.png')
                    x11.XDestroyImage(shot)
                    raise
                # Change the source under a stationary pointer, with the lens still open.
                if portal:
                    change = ["gdbus","call","--session","--dest","org.freedesktop.portal.Desktop","--object-path",
                              "/org/freedesktop/portal/desktop","--method","org.pickerhub.Test.Color"]
                    assert "uint32 1" in subprocess.check_output([*change, str(0x297351)], text=True)
                else:
                    other = x11.XCreateSimpleWindow(display, root, 1300, 800, 95, 90, 0, 0, 0x297351)
                    x11.XMapRaised(display, other)
                    x11.XSetInputFocus(display, other, 1, 0)
                    x11.XSync(display, False)
                def live_swatch():
                    image = x11.XGetImage(display,x11.XDefaultRootWindow(display),1127,765,1,1,ctypes.c_ulong(-1),2)
                    try: return x11.XGetPixel(image,0,0)&0xffffff == 0x297351
                    finally: x11.XDestroyImage(image)
                until(live_swatch)
                click(1350,850)
                until(mapped)
                file=until(lambda:next(Path(home).rglob("workspace.json"),None))
                until(lambda: json.loads(file.read_text())["history"] == ["#297351"])
                original=json.loads(file.read_text())
                if portal:
                    result=subprocess.check_output(["gdbus","call","--session","--dest","org.freedesktop.portal.Desktop","--object-path","/org/freedesktop/portal/desktop","--method","org.pickerhub.Test.Color","2113632"],text=True)
                    assert "uint32 1" in result, "Only one sharing session should be started"
                x11.XSetInputFocus(display,int(window,16),1,0)
                x11.XFlush(display)
                key("F8")
                until(lambda:re.search(r'"PickerHub Magnifier"',windows()))
                assert not mapped()
                if portal:
                    move(1350,850)
                    time.sleep(.48)
                    image=x11.XGetImage(display,x11.XDefaultRootWindow(display),1127,765,1,1,ctypes.c_ulong(-1),2)
                    try:assert x11.XGetPixel(image,0,0)&0xffffff==0x204060,"Second capture must show the updated stream, not an old screenshot"
                    finally:x11.XDestroyImage(image)
                if not portal:
                    x11.XSetInputFocus(display, other, 1, 0)
                    x11.XSync(display, False)
                key("Escape")
                until(lambda:not re.search(r'"PickerHub Magnifier"',windows()))
                until(mapped)
                assert json.loads(file.read_text())==original
                if portal:
                    result = subprocess.check_output([*change, str(0x204060)], text=True)
                    assert "uint32 1" in result, "A later capture must reuse the active screen-sharing session"
                    assert "not found" in subprocess.check_output(["xprop", "-id", window, "_NET_WM_WINDOW_OPACITY"], text=True), +                        "A warm capture must leave the workbench opacity untouched"
                if not portal:
                    x11.XSetInputFocus(display,int(window,16),1,0)
                    ctrl = x11.XKeysymToKeycode(display, x11.XStringToKeysym(b"Control_L"))
                    input_api.XTestFakeKeyEvent(display, ctrl, 1, 0); key("z")
                    input_api.XTestFakeKeyEvent(display, ctrl, 0, 0); x11.XFlush(display)
                    until(lambda: json.loads(file.read_text())["history"] == [])
                    input_api.XTestFakeKeyEvent(display, ctrl, 1, 0); key("y")
                    input_api.XTestFakeKeyEvent(display, ctrl, 0, 0); x11.XFlush(display)
                    until(lambda: json.loads(file.read_text())["history"] == original["history"])
                assert process.poll() is None
                print("PASS: desktop eyedropper and persisted selected color", original["color"])
            finally:
                process.terminate()


if __name__ == "__main__":
    main()
