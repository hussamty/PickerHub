using Avalonia.Controls;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace PickerHub;

[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IScreenshot : IDBusObject
{
    Task<ObjectPath> ScreenshotAsync(string parent, IDictionary<string, object> options);
}
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint code, IDictionary<string, object> values)> received, Action<Exception>? error = null);
    Task CloseAsync();
}
internal static class Picker
{
    [DllImport("picker_capture")] static extern nint pickerhub_snapshot(out int width, out int height, out int left, out int top);
    [DllImport("picker_capture")] static extern void pickerhub_snapshot_free(nint pixels);
    public static async Task<string?> Start(Window owner)
    {
        if (OperatingSystem.IsMacOS()) return await MacSampler.Start();
        DesktopFrame? frame;
        if (OperatingSystem.IsLinux() && (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))))
            frame = await PortalFrame();
        else frame = await Task.Run(() =>
        {
            var ptr = pickerhub_snapshot(out var w, out var h, out var left, out var top);
            if (ptr == 0) throw new InvalidOperationException("تعذّر التقاط سطح المكتب.");
            try
            {
                var bytes = new byte[checked(w * h * 4)]; Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return new DesktopFrame(bytes, w, h, left, top);
            }
            finally { pickerhub_snapshot_free(ptr); }
        });
        if (frame == null) return null;
        using (frame) return await MagnifierSession.Start(owner, frame);
    }
    static async Task<DesktopFrame?> PortalFrame()
    {
        const string service = "org.freedesktop.portal.Desktop";
        using var connection = new Connection(Address.Session);
        var info = await connection.ConnectAsync();
        var token = "pickerhub_" + Guid.NewGuid().ToString("N");
        var path = "/org/freedesktop/portal/desktop/request/" + info.LocalName.TrimStart(':').Replace('.', '_') + "/" + token;
        var request = connection.CreateProxy<IRequest>(service, path);
        var completion = new TaskCompletionSource<(uint code, IDictionary<string, object> values)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await request.WatchResponseAsync(r => completion.TrySetResult(r), ex => completion.TrySetException(ex));
        var portal = connection.CreateProxy<IScreenshot>(service, "/org/freedesktop/portal/desktop");
        var actual = await portal.ScreenshotAsync("", new Dictionary<string, object> { { "handle_token", token }, { "interactive", false }, { "modal", false } });
        if (actual.ToString() != path) throw new InvalidOperationException("مسار طلب التقاط الشاشة غير متوقع.");
        var result = await completion.Task;
        if (result.code == 1) return null;
        if (result.code != 0 || !result.values.TryGetValue("uri", out var value) || value is not string uri ||
            !Uri.TryCreate(uri, UriKind.Absolute, out var location) || !location.IsFile)
            throw new InvalidOperationException("تعذّر التقاط الشاشة. تحقق من أذونات بوابة سطح المكتب.");
        return DesktopFrame.FromFile(location.LocalPath);
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
        if (cls == 0) throw new InvalidOperationException("تعذّر تحميل قطّارة macOS.");
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
                if (converted == 0) throw new InvalidOperationException("تعذّر تحويل اللون إلى sRGB.");
                int C(string channel) => (int)Math.Round(Math.Clamp(Component(converted, sel_registerName(channel)), 0, 1) * 255, MidpointRounding.AwayFromZero);
                hex = $"#{C("redComponent"):X2}{C("greenComponent"):X2}{C("blueComponent"):X2}";
            }
            waiting?.TrySetResult(hex);
        }
        catch (Exception ex) { waiting?.TrySetException(ex); }
        finally { waiting = null; Send(sampler, sel_registerName("autorelease")); sampler = 0; }
    }
}
