// Grab only picker actions during a session. The shell keeps its normal Alt+Tab
// handling and the foreground application keeps all unrelated keyboard input.
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>

struct picker_input {
    HHOOK mouse, keyboard;
    int action, x, y;
    int held_key, held_button;
};
static struct picker_input *active;

static LRESULT CALLBACK on_mouse(int code, WPARAM message, LPARAM data) {
    if (code == HC_ACTION && active) {
        MSLLHOOKSTRUCT *event = (MSLLHOOKSTRUCT *)data;
        int action = 0;
        switch (message) {
        case WM_LBUTTONDOWN: action = 1; active->held_button = VK_LBUTTON; break;
        case WM_RBUTTONDOWN: action = 2; active->held_button = VK_RBUTTON; break;
        case WM_LBUTTONUP: case WM_RBUTTONUP:
            if (active->held_button) { active->held_button = 0; return 1; }
            break;
        case WM_MOUSEWHEEL: action = (short)HIWORD(event->mouseData) > 0 ? 3 : 4; break;
        }
        if (action) {
            if (!active->action || action == 2) {
                active->action = action; active->x = event->pt.x; active->y = event->pt.y;
            }
            return 1;
        }
    }
    return CallNextHookEx(NULL, code, message, data);
}
static LRESULT CALLBACK key_event(int code, WPARAM message, LPARAM data) {
    if (code == HC_ACTION && active) {
        int key = (int)((KBDLLHOOKSTRUCT *)data)->vkCode;
        if ((message == WM_KEYUP || message == WM_SYSKEYUP) && active->held_key == key) {
            active->held_key = 0; return 1;
        }
        if (message == WM_KEYDOWN && !(GetAsyncKeyState(VK_CONTROL)&0x8000)
            && !(GetAsyncKeyState(VK_LWIN)&0x8000) && !(GetAsyncKeyState(VK_RWIN)&0x8000)) {
            int action = 0;
            switch (key) {
            case VK_ESCAPE:action=2;break; case VK_RETURN:action=5;break;
            case VK_LEFT:action=6;break; case VK_RIGHT:action=7;break;
            case VK_UP:action=8;break; case VK_DOWN:action=9;break;
            }
            if (action) {
                if (!active->action || action == 2) active->action = action;
                active->held_key = key; return 1;
            }
        }
    }
    return CallNextHookEx(NULL, code, message, data);
}
__declspec(dllexport) void pickerhub_input_close(struct picker_input *in) {
    if (!in) return;
    active = NULL;
    if (in->mouse) UnhookWindowsHookEx(in->mouse);
    if (in->keyboard) UnhookWindowsHookEx(in->keyboard);
    free(in);
}
__declspec(dllexport) void *pickerhub_input_open(void) {
    struct picker_input *in = calloc(1,sizeof(*in));
    if (!in) return NULL;
    HMODULE module = NULL;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCWSTR)(uintptr_t)&pickerhub_input_open, &module);
    in->mouse = SetWindowsHookExW(WH_MOUSE_LL,on_mouse,module,0);
    in->keyboard = SetWindowsHookExW(WH_KEYBOARD_LL,key_event,module,0);
    if (!in->mouse || !in->keyboard) { pickerhub_input_close(in); return NULL; }
    active = in;
    return in;
}
__declspec(dllexport) int pickerhub_input_poll(struct picker_input *in,int *x,int *y) {
    *x=in->x; *y=in->y; int action=in->action; in->action=0; return action;
}
__declspec(dllexport) int pickerhub_pointer(int *x,int *y) {
    POINT point;
    if (!GetPhysicalCursorPos(&point)) return 0;
    *x=point.x; *y=point.y; return 1;
}
static int clamp(int x,int lo,int hi) { return x<lo ? lo : x>hi ? hi : x; }
__declspec(dllexport) int pickerhub_sample(int x,int y,int radius,unsigned char *rgba) {
    if (radius<0 || radius>16) return 0;
    DPI_AWARENESS_CONTEXT previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    int xmin=GetSystemMetrics(SM_XVIRTUALSCREEN), ymin=GetSystemMetrics(SM_YVIRTUALSCREEN);
    int xmax=xmin+GetSystemMetrics(SM_CXVIRTUALSCREEN)-1, ymax=ymin+GetSystemMetrics(SM_CYVIRTUALSCREEN)-1;
    int left=clamp(x-radius,xmin,xmax), top=clamp(y-radius,ymin,ymax);
    int width=clamp(x+radius,xmin,xmax)-left+1, height=clamp(y+radius,ymin,ymax)-top+1;
    BITMAPINFO info={0};
    info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth=width; info.bmiHeader.biHeight=-height;
    info.bmiHeader.biPlanes=1; info.bmiHeader.biBitCount=32; info.bmiHeader.biCompression=BI_RGB;
    HDC screen=GetDC(NULL), mem=CreateCompatibleDC(screen);
    void *bits=NULL;
    HBITMAP bitmap=CreateDIBSection(screen,&info,DIB_RGB_COLORS,&bits,NULL,0);
    int ok=0;
    if (mem && bitmap) {
        HGDIOBJ old=SelectObject(mem,bitmap);
        if (BitBlt(mem,0,0,width,height,screen,left,top,SRCCOPY|CAPTUREBLT)) {
            GdiFlush();
            for (int dy=-radius;dy<=radius;dy++) for (int dx=-radius;dx<=radius;dx++) {
                int offset=(clamp(y+dy-top,0,height-1)*width+clamp(x+dx-left,0,width-1))*4;
                unsigned char *p=(unsigned char *)bits+offset;
                *rgba++=p[2]; *rgba++=p[1]; *rgba++=p[0]; *rgba++=255;
            }
            ok=1;
        }
        SelectObject(mem,old);
    }
    if (bitmap) DeleteObject(bitmap);
    if (mem) DeleteDC(mem);
    if (screen) ReleaseDC(NULL,screen);
    if (previous) SetThreadDpiAwarenessContext(previous);
    return ok;
}

static int shortcut_pending;
static LRESULT CALLBACK shortcut_proc(HWND w, UINT message, WPARAM a, LPARAM b) {
    if (message == WM_HOTKEY) { shortcut_pending = 1; return 0; }
    return DefWindowProcW(w, message, a, b);
}
__declspec(dllexport) void *pickerhub_hotkey_open(int choice) {
    HINSTANCE module = GetModuleHandleW(NULL);
    WNDCLASSW wc = {0}; wc.lpfnWndProc = shortcut_proc; wc.hInstance = module; wc.lpszClassName = L"PickerHubShortcut";
    RegisterClassW(&wc);
    HWND w = CreateWindowW(wc.lpszClassName, L"", 0, 0,0,0,0, HWND_MESSAGE, NULL, module, NULL);
    if (!w) return NULL;
    if (!RegisterHotKey(w, 1, MOD_NOREPEAT | MOD_CONTROL | (choice == 1 ? MOD_ALT : MOD_SHIFT), choice == 2 ? 'C' : 'P')) { DestroyWindow(w); return NULL; }
    shortcut_pending = 0; return w;
}
__declspec(dllexport) int pickerhub_hotkey_poll(void *handle) { (void)handle; int hit = shortcut_pending; shortcut_pending = 0; return hit; }
__declspec(dllexport) void pickerhub_hotkey_close(void *handle) { if (handle) { UnregisterHotKey(handle, 1); DestroyWindow(handle); } }
