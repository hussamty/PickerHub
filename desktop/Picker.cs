using Avalonia.Controls;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace PickerHub;

[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IScreenshot : IDBusObject
{
    Task<ObjectPath> PickColorAsync(string parent, IDictionary<string, object> options);
}
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint code, IDictionary<string, object> values)> received, Action<Exception>? error = null);
    Task CloseAsync();
}
internal static class Picker
{
    [DllImport("picker_capture")] static extern int pickerhub_x11();
    [DllImport("picker_capture")] static extern int pickerhub_windows();

    public static async Task<string?> Start(Window owner)
    {
        // Determine the desktop session, independent of the toolkit's XWayland window.
        if (OperatingSystem.IsMacOS()) return await MacSampler.Start();
        if (OperatingSystem.IsLinux() && (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))))
            return await Portal(owner);

        // Win32 runs its private message loop on a dedicated thread.
        int pixel = await Task.Factory.StartNew<int>(OperatingSystem.IsWindows() ? pickerhub_windows : pickerhub_x11,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return pixel switch
        {
            -1 => null,
            < 0 => throw new InvalidOperationException("تعذّر بدء القطّارة. أغلق أي أداة التقاط أخرى وأعد المحاولة."),
            _ => $"#{pixel:X6}"
        };
    }

    static async Task<string?> Portal(Window owner)
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
        var handle = owner.TryGetPlatformHandle();
        string parent = handle?.HandleDescriptor == "XID" ? $"x11:{handle.Handle:x}" : "";
        var actual = await portal.PickColorAsync(parent, new Dictionary<string, object> { { "handle_token", token } });
        if (actual.ToString() != path) throw new InvalidOperationException("مسار طلب بوابة الألوان غير متوقع.");
        var result = await completion.Task;
        if (result.code == 1) return null;
        if (result.code != 0 || !result.values.TryGetValue("color", out var value) || value is not ValueTuple<double, double, double> rgb)
            throw new InvalidOperationException("تعذّر اختيار اللون. تحقق من دعم PickColor في بوابة سطح المكتب.");
        int component(double n) => (int)Math.Round(Math.Clamp(n, 0, 1) * 255, MidpointRounding.AwayFromZero);
        return $"#{component(rgb.Item1):X2}{component(rgb.Item2):X2}{component(rgb.Item3):X2}";
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
