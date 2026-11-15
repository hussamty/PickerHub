"""A second X client receives global shortcuts without stealing ordinary input."""
import ctypes as C
from pathlib import Path
x = C.CDLL('libX11.so.6'); xt = C.CDLL('libXtst.so.6')
x.XOpenDisplay.restype = C.c_void_p
x.XDefaultRootWindow.restype = C.c_ulong
x.XKeysymToKeycode.argtypes = [C.c_void_p, C.c_ulong]
x.XStringToKeysym.restype = C.c_ulong
p = C.CDLL(str(Path('dist/native/linux-x64/PickerHub/libpicker_capture.so').resolve()))
p.pickerhub_hotkey_open.restype = C.c_void_p
p.pickerhub_hotkey_poll.argtypes = [C.c_void_p]
p.pickerhub_hotkey_close.argtypes = [C.c_void_p]
d = C.c_void_p(x.XOpenDisplay(None)); assert d.value
h = p.pickerhub_hotkey_open(0); assert h
assert not p.pickerhub_hotkey_open(0), 'Conflicting shortcut must fail cleanly'
def press(name, down):
    key = x.XKeysymToKeycode(d, x.XStringToKeysym(name.encode()))
    xt.XTestFakeKeyEvent(d, key, down, 0)
for key in ('Control_L', 'Shift_L', 'p'): press(key, 1)
for key in ('p', 'Shift_L', 'Control_L'): press(key, 0)
x.XSync(d, False)
import time
time.sleep(.1)
assert p.pickerhub_hotkey_poll(h) == 1
assert p.pickerhub_hotkey_poll(h) == 0
p.pickerhub_hotkey_close(h)
h = p.pickerhub_hotkey_open(0); assert h, 'Shortcut must release on dispose'
p.pickerhub_hotkey_close(h); x.XCloseDisplay(d)
print('PASS: global shortcut activation, conflict and cleanup')
