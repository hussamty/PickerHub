// Freeze the complete virtual desktop in physical pixels, including negative monitor origins.
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdio.h>

typedef struct { HDC image; int w,h,left,top,zoom,result; POINT pos; } Picker;
static LRESULT CALLBACK overlay(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp) {
    Picker *p=(Picker*)GetWindowLongPtr(hwnd,GWLP_USERDATA);
    if(msg==WM_NCCREATE) {p=(Picker*)((CREATESTRUCT*)lp)->lpCreateParams;SetWindowLongPtr(hwnd,GWLP_USERDATA,(LONG_PTR)p);}
    if(!p)return DefWindowProc(hwnd,msg,wp,lp);
    POINT mouse; GetCursorPos(&mouse);ScreenToClient(hwnd,&mouse);
    switch(msg) {
    case WM_MOUSEMOVE: case WM_MOUSEWHEEL:
        p->pos=mouse;
        if(msg==WM_MOUSEWHEEL) {p->zoom+=(short)HIWORD(wp)>0?1:-1;if(p->zoom<5)p->zoom=5;if(p->zoom>16)p->zoom=16;}
        InvalidateRect(hwnd,NULL,FALSE);return 0;
    case WM_KEYDOWN:
        if(wp==VK_ESCAPE){DestroyWindow(hwnd);return 0;}
        if(wp==VK_LEFT||wp==VK_RIGHT||wp==VK_UP||wp==VK_DOWN) {
            SetCursorPos(mouse.x+p->left+(wp==VK_RIGHT)-(wp==VK_LEFT),mouse.y+p->top+(wp==VK_DOWN)-(wp==VK_UP));return 0;
        }
        if(wp!=VK_RETURN)return 0;
        /* Enter and click sample the same frozen pixel. */
        /* fall through */
    case WM_LBUTTONUP: {
        /* GetCursorPos above retains full physical coordinates on wide desktops. */
        COLORREF c=GetPixel(p->image,mouse.x,mouse.y);
        p->result=c==CLR_INVALID?-2:(int)((GetRValue(c)<<16)|(GetGValue(c)<<8)|GetBValue(c));
        DestroyWindow(hwnd);return 0;
    }
    case WM_RBUTTONUP:case WM_CLOSE: DestroyWindow(hwnd);return 0;
    case WM_ACTIVATEAPP: if(!wp)PostMessage(hwnd,WM_CLOSE,0,0);return 0;
    case WM_PAINT: {
        PAINTSTRUCT ps;HDC dc=BeginPaint(hwnd,&ps);
        BitBlt(dc,ps.rcPaint.left,ps.rcPaint.top,ps.rcPaint.right-ps.rcPaint.left,ps.rcPaint.bottom-ps.rcPaint.top,
               p->image,ps.rcPaint.left,ps.rcPaint.top,SRCCOPY);
        int x=p->pos.x,y=p->pos.y,z=p->zoom,width=11*z;
        POINT physical={x+p->left,y+p->top};MONITORINFO mi={sizeof(mi)};
        GetMonitorInfo(MonitorFromPoint(physical,MONITOR_DEFAULTTONEAREST),&mi);
        int lx=(physical.x+width+28<mi.rcMonitor.right)?x+20:x-width-20;
        int ly=(physical.y+width+52<mi.rcMonitor.bottom)?y+20:y-width-48;
        int sx=max(0,min(x-5,p->w-11)),sy=max(0,min(y-5,p->h-11));
        SetStretchBltMode(dc,COLORONCOLOR);StretchBlt(dc,lx,ly,width,width,p->image,sx,sy,11,11,SRCCOPY);
        for(int i=0;i<=11;i++){MoveToEx(dc,lx+i*z,ly,NULL);LineTo(dc,lx+i*z,ly+width);MoveToEx(dc,lx,ly+i*z,NULL);LineTo(dc,lx+width,ly+i*z);}
        RECT center={lx+(x-sx)*z,ly+(y-sy)*z,lx+(x-sx+1)*z+1,ly+(y-sy+1)*z+1};
        FrameRect(dc,&center,(HBRUSH)GetStockObject(WHITE_BRUSH));
        RECT foot={lx,ly+width,lx+width,ly+width+22};FillRect(dc,&foot,(HBRUSH)GetStockObject(BLACK_BRUSH));
        COLORREF c=GetPixel(p->image,x,y);wchar_t text[24];swprintf(text,24,L"#%02X%02X%02X",GetRValue(c),GetGValue(c),GetBValue(c));
        SetBkMode(dc,TRANSPARENT);SetTextColor(dc,RGB(255,255,255));DrawText(dc,text,-1,&foot,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
        EndPaint(hwnd,&ps);return 0;
    }
    case WM_DESTROY:PostQuitMessage(0);return 0;
    }
    return DefWindowProc(hwnd,msg,wp,lp);
}
__declspec(dllexport) int pickerhub_windows(void) {
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    Picker p={0};p.result=-1;p.zoom=10;p.left=GetSystemMetrics(SM_XVIRTUALSCREEN);p.top=GetSystemMetrics(SM_YVIRTUALSCREEN);
    p.w=GetSystemMetrics(SM_CXVIRTUALSCREEN);p.h=GetSystemMetrics(SM_CYVIRTUALSCREEN);
    HDC screen=GetDC(NULL);p.image=CreateCompatibleDC(screen);HBITMAP bmp=CreateCompatibleBitmap(screen,p.w,p.h);
    HGDIOBJ old=SelectObject(p.image,bmp);BOOL ok=bmp && p.image && BitBlt(p.image,0,0,p.w,p.h,screen,p.left,p.top,SRCCOPY|CAPTUREBLT);
    ReleaseDC(NULL,screen);
    HINSTANCE module=GetModuleHandle(NULL);const wchar_t *name=L"PickerHubPixelSelector";
    WNDCLASS wc={0};wc.lpfnWndProc=overlay;wc.hInstance=module;wc.lpszClassName=name;wc.hCursor=LoadCursor(NULL,IDC_CROSS);
    if(ok && RegisterClass(&wc)){
        HWND win=CreateWindowEx(WS_EX_TOPMOST|WS_EX_TOOLWINDOW,name,L"PickerHub",WS_POPUP,p.left,p.top,p.w,p.h,NULL,NULL,module,&p);
        if(win){GetCursorPos(&p.pos);ScreenToClient(win,&p.pos);ShowWindow(win,SW_SHOW);SetForegroundWindow(win);SetFocus(win);SetCapture(win);
          MSG msg;while(GetMessage(&msg,NULL,0,0)>0){TranslateMessage(&msg);DispatchMessage(&msg);}ReleaseCapture();
        }else p.result=-2;
        UnregisterClass(name,module);
    }else p.result=-2;
    SelectObject(p.image,old);DeleteObject(bmp);DeleteDC(p.image);return p.result;
}
