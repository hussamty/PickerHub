"""Capture isolated native UI pages and exercise the in-process UI test runner."""
from contextlib import contextmanager
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
    try: tree = subprocess.check_output(['xwininfo','-root','-tree'], text=True, stderr=subprocess.DEVNULL)
    except subprocess.CalledProcessError: return None  # A native window closed while X11 traversed the tree.
    match = re.search(r'(0x[0-9a-f]+) "PickerHub Magnifier"', tree)
    return match[1] if match else None

@contextmanager
def application(args, env):
    process = subprocess.Popen(args, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
    try: yield process
    finally:
        if process.poll() is None:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill(); process.wait()
        process.stdin.close(); process.stdout.close()

d = C.c_void_p(x.XOpenDisplay(None)); assert d.value
x.XCreateSimpleWindow.restype = C.c_ulong
class XWindowAttributes(C.Structure):
    _fields_ = [(n,t) for n,t in [('background_pixmap',C.c_ulong),('background_pixel',C.c_ulong),('border_pixmap',C.c_ulong),('border_pixel',C.c_ulong),('bit_gravity',C.c_int),('win_gravity',C.c_int),('backing_store',C.c_int),('backing_planes',C.c_ulong),('backing_pixel',C.c_ulong),('save_under',C.c_int),('event_mask',C.c_long),('do_not_propagate_mask',C.c_long),('override_redirect',C.c_int),('colormap',C.c_ulong),('cursor',C.c_ulong)]]
reference = x.XCreateSimpleWindow(d, x.XDefaultRootWindow(d), 5, 5, 35, 35, 0, 0, 0x112233)
attributes = XWindowAttributes(); attributes.override_redirect = 1
x.XChangeWindowAttributes(d, reference, 1 << 9, C.byref(attributes))
x.XMapRaised(d, reference); x.XSync(d, False)
screen_width = x.XDisplayWidth(d, 0); screen_height = x.XDisplayHeight(d, 0)
for language in ('ar','en'):
    env = dict(os.environ, XDG_SESSION_TYPE='x11', WAYLAND_DISPLAY='')
    args = [str(ROOT/'dist/native/linux-x64/PickerHub/PickerHub'), '--ui-test'] + (['--english'] if language == 'en' else [])
    with application(args, env) as process:
        passed = False
        for line in process.stdout:
            if line.startswith('FRAME '):
                time.sleep(.4)
                frame = x.XGetImage(d, x.XDefaultRootWindow(d), 0,0,screen_width,screen_height,C.c_ulong(-1),2)
                assert frame and frame.contents.bits == 32
                im = frame.contents
                image = Image.frombytes('RGB',(im.width,im.height),C.string_at(im.data,im.stride*im.height),'raw','BGRX',im.stride)
                parts = line.strip().split()
                if len(parts) == 6:
                    left, top, width, height = map(int, parts[2:])
                    image = image.crop((left, top, left + width, top + height))
                image.save(OUT/(parts[1]+'.png')); x.XDestroyImage(frame)
                process.stdin.write('\n'); process.stdin.flush()
            elif line.startswith('PASTE'):
                tree = subprocess.check_output(['xwininfo','-root','-tree'], text=True)
                window = re.search(r'(0x[0-9a-f]+) "PickerHub"', tree)
                assert window, 'Application window must exist before pasting'
                x.XSetInputFocus(d, C.c_ulong(int(window[1], 16)), 1, 0); x.XSync(d, False)
                ctrl = x.XKeysymToKeycode(d, x.XStringToKeysym(b'Control_L'))
                key = x.XKeysymToKeycode(d, x.XStringToKeysym(b'v'))
                xt.XTestFakeKeyEvent(d, ctrl, 1, 0); xt.XTestFakeKeyEvent(d, key, 1, 0)
                xt.XTestFakeKeyEvent(d, key, 0, 0); xt.XTestFakeKeyEvent(d, ctrl, 0, 0); x.XFlush(d)
                time.sleep(.3); process.stdin.write('\n'); process.stdin.flush()
            elif line.startswith('DRAG '):
                _, x0, y0, x1, y1 = line.split(); x0,y0,x1,y1 = map(int,(x0,y0,x1,y1))
                xt.XTestFakeMotionEvent(d, 0, x0, y0, 0); x.XFlush(d); time.sleep(.1)
                xt.XTestFakeButtonEvent(d, 1, 1, 0); x.XFlush(d)
                for step in range(1, 13):
                    xt.XTestFakeMotionEvent(d, 0, round(x0+(x1-x0)*step/12), round(y0+(y1-y0)*step/12), 0); x.XFlush(d); time.sleep(.015)
                xt.XTestFakeButtonEvent(d, 1, 0, 0); x.XFlush(d)
                time.sleep(.1); process.stdin.write('\n'); process.stdin.flush()
            elif line.startswith('SCROLL '):
                _, mx, my, steps = line.split()
                xt.XTestFakeMotionEvent(d, 0, int(mx), int(my), 0); x.XFlush(d); time.sleep(.1)
                for _ in range(int(steps)):
                    xt.XTestFakeButtonEvent(d, 5, 1, 0); xt.XTestFakeButtonEvent(d, 5, 0, 0)
                x.XFlush(d); time.sleep(.3)
                process.stdin.write('\n'); process.stdin.flush()
            elif line.startswith('HOVER '):
                _, mx, my = line.split()
                xt.XTestFakeMotionEvent(d, 0, int(mx), int(my), 0); x.XFlush(d)
                time.sleep(.1)
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
                frame = x.XGetImage(d, x.XDefaultRootWindow(d), 0,0,screen_width,screen_height,C.c_ulong(-1),2)
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
