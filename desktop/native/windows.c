// Capture physical pixels across all monitors, including negative desktop origins.
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
__declspec(dllexport) unsigned char *pickerhub_snapshot(int *width,int *height,int *left,int *top) {
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    *left=GetSystemMetrics(SM_XVIRTUALSCREEN);*top=GetSystemMetrics(SM_YVIRTUALSCREEN);
    *width=GetSystemMetrics(SM_CXVIRTUALSCREEN);*height=GetSystemMetrics(SM_CYVIRTUALSCREEN);
    if(*width<1 || *height<1 || (uint64_t)*width * *height>150000000)return NULL;
    BITMAPINFO info={0};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth=*width;info.bmiHeader.biHeight=-*height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
    HDC screen=GetDC(NULL),memory=CreateCompatibleDC(screen);
    void *bits=NULL;
    HBITMAP bitmap=CreateDIBSection(screen,&info,DIB_RGB_COLORS,&bits,NULL,0);
    unsigned char *rgba=NULL;
    if(memory && bitmap){
        HGDIOBJ old=SelectObject(memory,bitmap);
        if(BitBlt(memory,0,0,*width,*height,screen,*left,*top,SRCCOPY|CAPTUREBLT)){
            rgba=malloc((size_t)*width * *height * 4);
            if(rgba)for(size_t i=0;i<(size_t)*width * *height *4;i+=4){
                unsigned char *bgra=bits;
                rgba[i]=bgra[i+2];rgba[i+1]=bgra[i+1];rgba[i+2]=bgra[i];rgba[i+3]=255;
            }
        }
        SelectObject(memory,old);
    }
    if(bitmap)DeleteObject(bitmap);if(memory)DeleteDC(memory);ReleaseDC(NULL,screen);
    return rgba;
}
__declspec(dllexport) void pickerhub_snapshot_free(void *pixels){free(pixels);}
