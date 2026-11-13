// Direct X11 sampling. The grab routes clicks to the picker without activating the target.
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/cursorfont.h>
#include <X11/keysym.h>
#include <stdint.h>
#include <stdio.h>
#include <time.h>
#include <stdlib.h>

static unsigned channel(unsigned long pixel, unsigned long mask) {
    if (!mask) return 0;
    while (!(mask & 1)) { mask >>= 1; pixel >>= 1; }
    return ((pixel & mask) * 255UL + mask / 2) / mask;
}
static int clamp(int n, int low, int high) { return n < low ? low : n > high ? high : n; }
// -1 cancel, -2 display unavailable, -3 device grab refused. Nonnegative is RRGGBB.
int pickerhub_x11(void) {
    Display *d = XOpenDisplay(NULL);
    if (!d) return -2;
    int screen = DefaultScreen(d), result = -1;
    Window root = RootWindow(d, screen), child, reply;
    Cursor cursor = XCreateFontCursor(d, XC_crosshair);
    if (XGrabPointer(d, root, False, ButtonPressMask | ButtonReleaseMask | PointerMotionMask,
                     GrabModeAsync, GrabModeAsync, None, cursor, CurrentTime) != GrabSuccess) {
        XFreeCursor(d,cursor); XCloseDisplay(d); return -3;
    }
    if (XGrabKeyboard(d, root, False, GrabModeAsync, GrabModeAsync, CurrentTime) != GrabSuccess) {
        XUngrabPointer(d,CurrentTime); XFreeCursor(d,cursor); XCloseDisplay(d); return -3;
    }
    const int side=11, panel=146, height=153;
    XSetWindowAttributes attrs = { .override_redirect=True, .background_pixel=BlackPixel(d,screen) };
    Window lens = XCreateWindow(d, root, 0,0,panel,height,0,CopyFromParent,InputOutput,
                                CopyFromParent,CWOverrideRedirect|CWBackPixel,&attrs);
    GC gc=XCreateGC(d,lens,0,NULL);
    int zoom=10, done=0, ox=-999, oy=-999, current=-1, accept=0;
    while (!done) {
        int x,y,localx,localy; unsigned mask;
        if (!XQueryPointer(d,root,&reply,&child,&x,&y,&localx,&localy,&mask)) {result=-2;break;}
        int w=DisplayWidth(d,screen), h=DisplayHeight(d,screen);
        int left=clamp(x+22+panel<=w ? x+22 : x-22-panel,0,w-panel);
        int top=clamp(y+22+height<=h ? y+22 : y-22-height,0,h-height);
        if(left!=ox || top!=oy) {XMoveWindow(d,lens,left,top);XMapRaised(d,lens);ox=left;oy=top;}
        int sx=clamp(x-side/2,0,w-side), sy=clamp(y-side/2,0,h-side);
        XImage *im=XGetImage(d,root,sx,sy,side,side,AllPlanes,ZPixmap);
        if(!im) {result=-2;break;}
        unsigned long px=XGetPixel(im,x-sx,y-sy);
        current=(channel(px,im->red_mask)<<16)|(channel(px,im->green_mask)<<8)|channel(px,im->blue_mask);
        XSetForeground(d,gc,BlackPixel(d,screen));XFillRectangle(d,lens,gc,0,0,panel,height);
        int off=(panel-side*zoom)/2;
        for(int j=0;j<side;j++) for(int i=0;i<side;i++) {
            XSetForeground(d,gc,XGetPixel(im,i,j));
            XFillRectangle(d,lens,gc,off+i*zoom,4+j*zoom,zoom-1,zoom-1);
        }
        XDestroyImage(im);
        XSetForeground(d,gc,WhitePixel(d,screen));
        XDrawRectangle(d,lens,gc,off+(x-sx)*zoom-1,3+(y-sy)*zoom,zoom+1,zoom+1);
        char label[32];snprintf(label,sizeof label,"#%06X   %dx",current,zoom);
        XDrawString(d,lens,gc,9,height-9,label,(int)__builtin_strlen(label));XFlush(d);
        if(accept){result=current;break;}
        while(XPending(d)&&!done&&!accept) {
            XEvent ev;XNextEvent(d,&ev);
            if(ev.type==KeyPress) {
                KeySym key=XLookupKeysym(&ev.xkey,0);
                if(key==XK_Escape) done=1;
                if(key==XK_Return || key==XK_KP_Enter) accept=1;
                int dx=(key==XK_Right)-(key==XK_Left), dy=(key==XK_Down)-(key==XK_Up);
                if(dx || dy) XWarpPointer(d,None,None,0,0,0,0,dx,dy);
            }
            if(ev.type==ButtonRelease && ev.xbutton.button==Button1) {
                // Read at the event coordinate, even if the mouse moves immediately afterwards.
                XUnmapWindow(d,lens);
                im=XGetImage(d,root,ev.xbutton.x_root,ev.xbutton.y_root,1,1,AllPlanes,ZPixmap);
                if(im){px=XGetPixel(im,0,0);result=(channel(px,im->red_mask)<<16)|(channel(px,im->green_mask)<<8)|channel(px,im->blue_mask);XDestroyImage(im);}else result=-2;
                done=1;
            }
            if(ev.type==ButtonPress) {
                if(ev.xbutton.button==Button3)done=1;
                if(ev.xbutton.button==Button4)zoom=clamp(zoom+1,5,12);
                if(ev.xbutton.button==Button5)zoom=clamp(zoom-1,5,12);
            }
        }
        struct timespec delay={0,18000000};nanosleep(&delay,NULL);
    }
    XUnmapWindow(d,lens);XUngrabKeyboard(d,CurrentTime);XUngrabPointer(d,CurrentTime);
    XFreeGC(d,gc);XDestroyWindow(d,lens);XFreeCursor(d,cursor);XCloseDisplay(d);return result;
}
