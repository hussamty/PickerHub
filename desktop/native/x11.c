// Capture the complete X11 desktop before the picker overlay becomes visible.
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <stdint.h>
#include <stdlib.h>
static unsigned channel(unsigned long pixel, unsigned long mask) {
    if (!mask) return 0;
    while (!(mask & 1)) { mask >>= 1; pixel >>= 1; }
    return ((pixel & mask) * 255UL + mask / 2) / mask;
}
unsigned char *pickerhub_snapshot(int *width,int *height,int *left,int *top) {
    Display *display=XOpenDisplay(NULL);
    if(!display)return NULL;
    int screen=DefaultScreen(display);
    *width=DisplayWidth(display,screen);*height=DisplayHeight(display,screen);*left=0;*top=0;
    if(*width<1 || *height<1 || (uint64_t)*width * *height>150000000){XCloseDisplay(display);return NULL;}
    XImage *image=XGetImage(display,RootWindow(display,screen),0,0,*width,*height,AllPlanes,ZPixmap);
    unsigned char *rgba=image?malloc((size_t)*width * *height * 4):NULL;
    if(rgba)for(int y=0;y<*height;y++)for(int x=0;x<*width;x++){
        unsigned long pixel=XGetPixel(image,x,y);
        size_t i=((size_t)y * *width+x)*4;
        rgba[i]=channel(pixel,image->red_mask);rgba[i+1]=channel(pixel,image->green_mask);
        rgba[i+2]=channel(pixel,image->blue_mask);rgba[i+3]=255;
    }
    if(image)XDestroyImage(image);
    XCloseDisplay(display);return rgba;
}
void pickerhub_snapshot_free(void *pixels){free(pixels);}
