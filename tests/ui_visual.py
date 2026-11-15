"""Capture isolated native UI pages and exercise the in-process UI test runner."""
import ctypes as C
import os
import subprocess
import time
import re
from pathlib import Path
from PIL import Image
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'dist/qa'; OUT.mkdir(parents=True, exist_ok=True)
x = C.CDLL('libX11.so.6'); x.XOpenDisplay.restype = C.c_void_p
x.XDefaultRootWindow.restype = C.c_ulong
class XImage(C.Structure):
    _fields_ = [(n,t) for n,t in [('width',C.c_int),('height',C.c_int),('offset',C.c_int),('format',C.c_int),('data',C.c_void_p),('byte_order',C.c_int),('unit',C.c_int),('bit_order',C.c_int),('pad',C.c_int),('depth',C.c_int),('stride',C.c_int),('bits',C.c_int)]]
x.XGetImage.restype = C.POINTER(XImage); x.XDestroyImage.argtypes = [C.POINTER(XImage)]
xt = C.CDLL('libXtst.so.6')
x.XStringToKeysym.restype = C.c_ulong
x.XKeysymToKeycode.argtypes = [C.c_void_p, C.c_ulong]
def until(predicate):
    for _ in range(100):
        result = predicate()
        if result: return result
        time.sleep(.05)
    raise AssertionError('Native picker timed out')
def lens():
    tree = subprocess.check_output(['xwininfo','-root','-tree'], text=True)
    match = re.search(r'(0x[0-9a-f]+) "PickerHub Magnifier"', tree)
    return match[1] if match else None

d = C.c_void_p(x.XOpenDisplay(None)); assert d.value
x.XCreateSimpleWindow.restype = C.c_ulong
reference = x.XCreateSimpleWindow(d, x.XDefaultRootWindow(d), 5, 5, 35, 35, 0, 0, 0x112233)
x.XMapRaised(d, reference); x.XSync(d, False)
for language in ('ar','en'):
    env = dict(os.environ, XDG_SESSION_TYPE='x11', WAYLAND_DISPLAY='')
    args = [str(ROOT/'dist/native/linux-x64/PickerHub/PickerHub'), '--ui-test'] + (['--english'] if language == 'en' else [])
    with subprocess.Popen(args, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True) as process:
        passed = False
        for line in process.stdout:
            if line.startswith('FRAME '):
                time.sleep(.4)
                frame = x.XGetImage(d, x.XDefaultRootWindow(d), 0,0,1400,900,C.c_ulong(-1),2)
                assert frame and frame.contents.bits == 32
                im = frame.contents
                image = Image.frombytes('RGB',(im.width,im.height),C.string_at(im.data,im.stride*im.height),'raw','BGRX',im.stride)
                image.save(OUT/(line.strip().split(' ',1)[1]+'.png')); x.XDestroyImage(frame)
                process.stdin.write('\n'); process.stdin.flush()
            elif line.startswith('SINGLEPICK'):
                until(lens)
                xt.XTestFakeMotionEvent(d, 0, 20, 20, 0); x.XFlush(d); time.sleep(.2)
                xt.XTestFakeButtonEvent(d, 1, 1, 0); xt.XTestFakeButtonEvent(d, 1, 0, 0); x.XFlush(d)
                until(lambda: not lens())
            elif line.startswith('MULTIPICK'):
                old = until(lens)
                xt.XTestFakeMotionEvent(d, 0, 20, 20, 0); x.XFlush(d); time.sleep(.2)
                for color in (0x112233, 0x223344, 0x334455, 0x445566, 0x556677, 0x667788, 0x778899, 0x8899AA):
                    x.XSetWindowBackground(d, reference, color); x.XClearWindow(d, reference); x.XSync(d, False)
                    time.sleep(.08)
                    # Repeated clicks without moving must keep the same live session.
                    xt.XTestFakeButtonEvent(d, 1, 1, 0); xt.XTestFakeButtonEvent(d, 1, 0, 0); x.XFlush(d)
                    time.sleep(.15)
                    assert lens() == old, 'Multi-pick must not recreate the lens'
                frame = x.XGetImage(d, x.XDefaultRootWindow(d), 0,0,1400,900,C.c_ulong(-1),2)
                im = frame.contents
                Image.frombytes('RGB',(im.width,im.height),C.string_at(im.data,im.stride*im.height),'raw','BGRX',im.stride).save(OUT/(language+'-multipick.png'))
                x.XDestroyImage(frame)
                escape = x.XKeysymToKeycode(d, x.XStringToKeysym(b'Escape'))
                xt.XTestFakeKeyEvent(d, escape, 1, 0); xt.XTestFakeKeyEvent(d, escape, 0, 0); x.XFlush(d)
            elif line.startswith('TIMING '):
                print(language, line.strip())
            elif line.startswith('PASS:'):
                print(language,line.strip()); passed=True
        assert process.wait(timeout=10) == 0 and passed
x.XCloseDisplay(d)
