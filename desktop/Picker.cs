using Avalonia.Controls;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace PickerHub;

internal enum PickAction { Default, Copy, Edit, Quick, Collect }

[DBusInterface("org.freedesktop.host.portal.Registry")]
public interface IPortalRegistry : IDBusObject
{
    Task RegisterAsync(string appId,IDictionary<string,object> options);
}
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint code, IDictionary<string, object> values)> received, Action<Exception>? error = null);
    Task CloseAsync();
}
internal static class Picker
{
    [DllImport("picker_capture")] static extern int pickerhub_window_transparent(nuint window,int transparent);
    public static void SetPortalParentTransparent(Window owner,bool transparent)
    {
        var handle=owner.TryGetPlatformHandle();
        if(handle?.HandleDescriptor=="XID")
        {
            if(pickerhub_window_transparent((nuint)handle.Handle,transparent?1:0)==0 && transparent)
                throw new InvalidOperationException(L.T("تعذّر تجهيز نافذة التطبيق لطلب إذن الشاشة."));
        }
        else owner.Opacity=transparent?0:1;
    }
    // An explicit session type is authoritative. Native libraries can probe or
    // populate WAYLAND_DISPLAY even in an X11 session; that must not switch capture.
    internal static bool WaylandSession(string? session, string? display) => session?.ToLowerInvariant() switch
    {
        "wayland" => true,
        "x11" => false,
        _ => !string.IsNullOrWhiteSpace(display)
    };
    public static bool UsesPortal => OperatingSystem.IsLinux() && WaylandSession(
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public static async Task<string?> Start(Window owner, bool useScreenSharing, Action<string>? collected = null, Action? firstSample = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            string? selected;
            do { selected = await MacSampler.Start(); if (selected != null) collected?.Invoke(selected); }
            while (collected != null && selected != null);
            return selected;
        }
        if (UsesPortal && !useScreenSharing) return await WaylandColorPicker.Pick(owner);
        return await MagnifierSession.Start(owner, collected, firstSample);
    }

}

// AppKit copies the completion block before this call returns. Keep the sampler alive
// until AppKit reports a selection (or nil on Escape). AppKit invokes it on the UI thread.
internal static unsafe class MacSampler
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] static extern nint objc_getClass(string name);
    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendArg(nint obj, nint sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern double Component(nint obj, nint sel);
    [StructLayout(LayoutKind.Sequential)] struct Block { public nint isa; public int flags, reserved; public nint invoke, descriptor; }
    static TaskCompletionSource<string?>? waiting;
    static nint sampler;
    static readonly nint descriptor = MakeDescriptor();
    static nint MakeDescriptor()
    {
        nint p = Marshal.AllocHGlobal(2 * IntPtr.Size);
        Marshal.WriteIntPtr(p, 0, 0); Marshal.WriteIntPtr(p, IntPtr.Size, sizeof(Block)); return p;
    }
    public static Task<string?> Start()
    {
        if (waiting != null) return waiting.Task;
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cls = objc_getClass("NSColorSampler");
        if (cls == 0) throw new InvalidOperationException(L.T("تعذّر تحميل قطّارة macOS."));
        var stackClass = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteStackBlock");
        Block block = new() { isa = stackClass, invoke = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&Complete, descriptor = descriptor };
        sampler = Send(cls, sel_registerName("new")); waiting = result;
        SendArg(sampler, sel_registerName("showSamplerWithSelectionHandler:"), (nint)(&block));
        return result.Task;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    static void Complete(nint block, nint color)
    {
        try
        {
            string? hex = null;
            if (color != 0)
            {
                nint space = Send(objc_getClass("NSColorSpace"), sel_registerName("sRGBColorSpace"));
                var converted = SendArg(color, sel_registerName("colorUsingColorSpace:"), space);
                if (converted == 0) throw new InvalidOperationException(L.T("تعذّر تحويل اللون إلى sRGB."));
                int C(string channel) => (int)Math.Round(Math.Clamp(Component(converted, sel_registerName(channel)), 0, 1) * 255, MidpointRounding.AwayFromZero);
                hex = $"#{C("redComponent"):X2}{C("greenComponent"):X2}{C("blueComponent"):X2}";
            }
            waiting?.TrySetResult(hex);
        }
        catch (Exception ex) { waiting?.TrySetException(ex); }
        finally { waiting = null; Send(sampler, sel_registerName("autorelease")); sampler = 0; }
    }
}
