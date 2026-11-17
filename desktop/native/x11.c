#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/keysym.h>
#include <X11/cursorfont.h>
#include <stdint.h>
#include <stdlib.h>

// Avalonia 11 looks up existing clipboard atoms during startup. Minimal X11
// desktops may not have a clipboard manager to create them first.
void pickerhub_clipboard_prepare(void) {
    XInitThreads();
    Display *d = XOpenDisplay(NULL);
    if (!d) return;
    const char *names[] = {"CLIPBOARD", "CLIPBOARD_MANAGER", "SAVE_TARGETS",
        "MULTIPLE", "TARGETS", "UTF8_STRING", "UTF16_STRING", "OEMTEXT", "ATOM_PAIR", "INCR"};
    for (unsigned i = 0; i < sizeof(names)/sizeof(names[0]); i++) XInternAtom(d, names[i], False);
    XSync(d, False);
    XCloseDisplay(d);
}

static unsigned channel(unsigned long pixel, unsigned long mask) {
    if (!mask) return 0;
    while (!(mask & 1)) { mask >>= 1; pixel >>= 1; }
    return ((pixel & mask) * 255UL + mask / 2) / mask;
}

struct picker_input { Display *display; Cursor cursor; };
static Display *active_display;
static const KeySym keys[] = {XK_Escape, XK_Return, XK_Left, XK_Right, XK_Up, XK_Down};
int pickerhub_pointer(int *x, int *y) {
    Display *d = active_display ? active_display : XOpenDisplay(NULL);
    if (!d) return 0;
    Window root, child; int relative_x, relative_y; unsigned mask;
    int ok = XQueryPointer(d, DefaultRootWindow(d), &root, &child, x, y, &relative_x, &relative_y, &mask);
    if (!active_display) XCloseDisplay(d);
    return ok;
}
static int grab_error;
static int on_grab_error(Display *d, XErrorEvent *error) {
    (void)d; (void)error; grab_error = 1; return 0;
}
void *pickerhub_input_open(void) {
    Display *d = XOpenDisplay(NULL);
    if (!d) return NULL;
    Window root = DefaultRootWindow(d);
    Cursor cursor = XCreateFontCursor(d, XC_crosshair);
    if (XGrabPointer(d, root, False, ButtonPressMask, GrabModeAsync, GrabModeAsync, None, cursor, CurrentTime) != GrabSuccess) {
        XFreeCursor(d,cursor); XCloseDisplay(d); return NULL;
    }
    // Only picker keys are grabbed. Alt+Tab remains owned by the window manager.
    XSync(d, False);
    grab_error = 0;
    XErrorHandler previous = XSetErrorHandler(on_grab_error);
    for (unsigned i = 0; i < sizeof(keys)/sizeof(keys[0]); i++) {
        KeyCode code = XKeysymToKeycode(d, keys[i]);
        unsigned modifiers[] = {0, LockMask, Mod2Mask, LockMask|Mod2Mask};
        for (unsigned j = 0; j < 4; j++) XGrabKey(d, code, modifiers[j], root, False, GrabModeAsync, GrabModeAsync);
    }
    XSync(d, False); XSetErrorHandler(previous);
    struct picker_input *in = grab_error ? NULL : calloc(1, sizeof(*in));
    if (!in) { XCloseDisplay(d); return NULL; }
    in->display = d; in->cursor = cursor; active_display = d;
    return in;
}
void pickerhub_input_close(struct picker_input *in) {
    if (!in) return;
    active_display = NULL;
    XFreeCursor(in->display,in->cursor);
    XCloseDisplay(in->display);
    free(in);
}
int pickerhub_input_poll(struct picker_input *in, int *x, int *y) {
    while (XPending(in->display)) {
        XEvent e;
        XNextEvent(in->display, &e);
        if (e.type == ButtonPress) {
            *x = e.xbutton.x_root; *y = e.xbutton.y_root;
            switch (e.xbutton.button) { case Button1:return 1; case Button3:return 2; case Button4:return 3; case Button5:return 4; }
        }
        if (e.type == KeyPress) {
            KeySym sym = XLookupKeysym(&e.xkey, 0);
            for (unsigned i = 0; i < sizeof(keys)/sizeof(keys[0]); i++)
                if (sym == keys[i]) return i == 0 ? 2 : (int)i+4;
        }
    }
    return 0;
}
static int clamp(int x, int lo, int hi) { return x < lo ? lo : x > hi ? hi : x; }
int pickerhub_sample(int x, int y, int radius, unsigned char *rgba) {
    Display *d = active_display;
    if (!d || radius < 0 || radius > 16) return 0;
    int s = DefaultScreen(d), sw = DisplayWidth(d,s), sh = DisplayHeight(d,s);
    int left = clamp(x-radius,0,sw-1), top = clamp(y-radius,0,sh-1);
    int width = clamp(x+radius,0,sw-1)-left+1, height = clamp(y+radius,0,sh-1)-top+1;
    XImage *im = XGetImage(d,DefaultRootWindow(d),left,top,width,height,AllPlanes,ZPixmap);
    if (!im) return 0;
    for (int dy=-radius; dy<=radius; dy++) for (int dx=-radius; dx<=radius; dx++) {
        unsigned long p = XGetPixel(im,clamp(x+dx-left,0,width-1),clamp(y+dy-top,0,height-1));
        *rgba++ = channel(p,im->red_mask); *rgba++ = channel(p,im->green_mask);
        *rgba++ = channel(p,im->blue_mask); *rgba++ = 255;
    }
    XDestroyImage(im);
    return 1;
}

// Preserve focus for the portal's consent dialog while hiding the application's
// complete managed window (including its frame) from the compositor's capture.
int pickerhub_window_transparent(unsigned long window,int transparent) {
    Display *display=XOpenDisplay(NULL);
    if(!display)return 0;
    Atom property=XInternAtom(display,"_NET_WM_WINDOW_OPACITY",False);
    if(transparent){
        unsigned long value=0;
        XChangeProperty(display,window,property,XA_CARDINAL,32,PropModeReplace,(unsigned char*)&value,1);
    }else XDeleteProperty(display,window,property);
    XSync(display,False);XCloseDisplay(display);return 1;
}

unsigned long pickerhub_active_window(void) {
    Display *d = XOpenDisplay(NULL);
    if (!d) return None;
    Window window = None;
    Atom type;
    int format, revert;
    unsigned long count, remaining;
    unsigned char *value = NULL;
    Atom active = XInternAtom(d, "_NET_ACTIVE_WINDOW", True);
    if (active != None && XGetWindowProperty(d, DefaultRootWindow(d), active, 0, 1, False,
            XA_WINDOW, &type, &format, &count, &remaining, &value) == Success &&
            type == XA_WINDOW && format == 32 && count == 1)
        window = *(unsigned long *)value;
    if (value) XFree(value);
    if (window == None) XGetInputFocus(d, &window, &revert);
    if (window == PointerRoot || window == DefaultRootWindow(d)) window = None;
    XCloseDisplay(d);
    return window;
}

// EWMH activation with a fresh server timestamp. An input surface may have no
// recent X event timestamp after the compositor handles a window switch.
void pickerhub_focus_window(unsigned long window) {
    Display *d = XOpenDisplay(NULL);
    if (!d) return;
    Window root = DefaultRootWindow(d);
    Window probe = XCreateSimpleWindow(d,root,0,0,1,1,0,0,0);
    XSelectInput(d,probe,PropertyChangeMask);
    Atom stamp = XInternAtom(d,"_PICKERHUB_TIME",False);
    unsigned char byte = 0;
    XChangeProperty(d,probe,stamp,XA_INTEGER,8,PropModeReplace,&byte,1);
    XEvent time_event;
    XWindowEvent(d,probe,PropertyChangeMask,&time_event);
    XEvent request = {0};
    request.xclient.type = ClientMessage;
    request.xclient.window = window;
    request.xclient.message_type = XInternAtom(d,"_NET_ACTIVE_WINDOW",False);
    request.xclient.format = 32;
    request.xclient.data.l[0] = 1;
    request.xclient.data.l[1] = time_event.xproperty.time;
    XSendEvent(d,root,False,SubstructureRedirectMask|SubstructureNotifyMask,&request);
    XDestroyWindow(d,probe);
    XCloseDisplay(d);
}

// A separate connection owns only the user's configured shortcut, never other input.
void *pickerhub_hotkey_open(int choice) {
    Display *d = XOpenDisplay(NULL); if (!d) return NULL;
    XSync(d, False); grab_error = 0;
    XErrorHandler previous = XSetErrorHandler(on_grab_error);
    KeyCode code = XKeysymToKeycode(d, choice == 2 ? XK_c : XK_p);
    unsigned base = ControlMask | (choice == 1 ? Mod1Mask : ShiftMask);
    unsigned locks[] = {0, LockMask, Mod2Mask, LockMask | Mod2Mask};
    for (unsigned i = 0; i < 4; i++) XGrabKey(d, code, base | locks[i], DefaultRootWindow(d), False, GrabModeAsync, GrabModeAsync);
    XSync(d, False); XSetErrorHandler(previous);
    if (grab_error) { XCloseDisplay(d); return NULL; }
    return d;
}
int pickerhub_hotkey_poll(Display *d) {
    int hit = 0;
    while (XPending(d)) { XEvent e; XNextEvent(d, &e); if (e.type == KeyPress) hit = 1; }
    return hit;
}
void pickerhub_hotkey_close(Display *d) { if (d) XCloseDisplay(d); }
