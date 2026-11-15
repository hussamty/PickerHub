using Avalonia.Controls;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace PickerHub;

[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
public interface IShortcutPortal : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> BindShortcutsAsync(ObjectPath session, (string, IDictionary<string, object>)[] shortcuts, string parent, IDictionary<string, object> options);
    Task<IDisposable> WatchActivatedAsync(Action<(ObjectPath session, string id, ulong timestamp, IDictionary<string, object> options)> handler);
}

// Owns all registrations; disposing closes the portal session or releases the OS shortcut.
internal sealed class GlobalShortcut : IDisposable
{
    [DllImport("picker_capture")] static extern nint pickerhub_hotkey_open(int choice);
    [DllImport("picker_capture")] static extern int pickerhub_hotkey_poll(nint handle);
    [DllImport("picker_capture")] static extern void pickerhub_hotkey_close(nint handle);
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    [StructLayout(LayoutKind.Sequential)] struct EventType { public uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] struct HotKeyID { public uint Signature, Id; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Handler(nint next, nint ev, nint data);
    [DllImport(Carbon)] static extern nint GetApplicationEventTarget();
    [DllImport(Carbon)] static extern int InstallEventHandler(nint target, Handler handler, uint count, ref EventType type, nint data, out nint reference);
    [DllImport(Carbon)] static extern int RegisterEventHotKey(uint key, uint modifiers, HotKeyID id, nint target, uint options, out nint reference);
    [DllImport(Carbon)] static extern int UnregisterEventHotKey(nint reference);
    [DllImport(Carbon)] static extern int RemoveEventHandler(nint reference);
    nint native, handlerRef;
    Handler? handler;
    DispatcherTimer? timer;
    Connection? bus;
    IDisposable? watch;
    ICastSession? session;
    bool disposed;
    public string Description { get; private set; } = "";
    public async Task Start(Window owner, string keys, Action action)
    {
        int choice = keys == "Ctrl+Alt+P" ? 1 : keys == "Ctrl+Shift+C" ? 2 : 0;
        if (Picker.UsesPortal)
        {
            bus = new Connection(Address.Session);
            string peer = (await bus.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5))).LocalName;
            try { await bus.CreateProxy<IPortalRegistry>("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop").RegisterAsync("pickerhub", new Dictionary<string, object>()).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (DBusException e) when (e.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface") { }
            var portal = bus.CreateProxy<IShortcutPortal>("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop");
            var created = await Request(peer, opts => { opts["session_handle_token"] = "ph_" + Guid.NewGuid().ToString("N"); return portal.CreateSessionAsync(opts); });
            var path = new ObjectPath((string)created["session_handle"]);
            session = bus.CreateProxy<ICastSession>("org.freedesktop.portal.Desktop", path);
            watch = await portal.WatchActivatedAsync(e => { if (e.session == path && e.id == "pick") Dispatcher.UIThread.Post(() => { if (!disposed) action(); }); });
            var handle = owner.TryGetPlatformHandle(); string parent = handle?.HandleDescriptor == "XID" ? $"x11:{handle.Handle:x}" : "";
            string trigger = choice == 1 ? "CTRL+ALT+p" : choice == 2 ? "CTRL+SHIFT+c" : "CTRL+SHIFT+p";
            var bound = await Request(peer, opts => portal.BindShortcutsAsync(path, [("pick", new Dictionary<string, object> { ["description"] = L.T("PickerHub: التقاط لون"), ["preferred_trigger"] = trigger })], parent, opts));
            if (!bound.TryGetValue("shortcuts", out var value) || value is not (string, IDictionary<string, object>)[] entries || entries.Length == 0)
                throw new InvalidOperationException(L.T("لم يُربط اختصار. افتح إعدادات اختصارات النظام وأعد المحاولة."));
            Description = entries[0].Item2.TryGetValue("trigger_description", out var label) ? label.ToString()! : keys;
        }
        else if (OperatingSystem.IsMacOS())
        {
            handler = (_, _, _) => { if (!disposed) Dispatcher.UIThread.Post(action); return 0; };
            var type = new EventType { Class = 0x6B657962, Kind = 6 }; // kEventClassKeyboard / kEventHotKeyPressed
            if (InstallEventHandler(GetApplicationEventTarget(), handler, 1, ref type, 0, out handlerRef) != 0 ||
                RegisterEventHotKey(choice == 2 ? 8u : 35u, 4096u | (choice == 1 ? 2048u : 512u), new HotKeyID { Signature = 0x50484350, Id = 1 }, GetApplicationEventTarget(), 0, out native) != 0)
                throw new InvalidOperationException(L.T("الاختصار مستخدم من تطبيق آخر. اختر اختصاراً مختلفاً."));
            Description = keys;
        }
        else
        {
            native = pickerhub_hotkey_open(choice);
            if (native == 0) throw new InvalidOperationException(L.T("الاختصار مستخدم أو غير متاح. اختر اختصاراً مختلفاً."));
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            timer.Tick += (_, _) => { if (native != 0 && pickerhub_hotkey_poll(native) != 0) action(); }; timer.Start();
            Description = keys;
        }
    }
    async Task<IDictionary<string, object>> Request(string peer, Func<Dictionary<string, object>, Task<ObjectPath>> start)
    {
        string token = "ph_" + Guid.NewGuid().ToString("N");
        var path = new ObjectPath("/org/freedesktop/portal/desktop/request/" + peer.TrimStart(':').Replace('.', '_') + "/" + token);
        var request = bus!.CreateProxy<IRequest>("org.freedesktop.portal.Desktop", path);
        var response = new TaskCompletionSource<(uint code, IDictionary<string, object> values)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = await request.WatchResponseAsync(v => response.TrySetResult(v), e => response.TrySetException(e));
        try
        {
            if (await start(new() { ["handle_token"] = token }).WaitAsync(TimeSpan.FromSeconds(10)) != path) throw new IOException(L.T("تعذّر تتبّع طلب الاختصار."));
            var reply = await response.Task.WaitAsync(TimeSpan.FromMinutes(2));
            if (reply.code != 0) throw new InvalidOperationException(L.T("لم يُسمح بتفعيل الاختصار العام. يمكنك إعادة المحاولة من الإعدادات."));
            return reply.values;
        }
        catch { try { await request.CloseAsync().WaitAsync(TimeSpan.FromSeconds(1)); } catch { } throw; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer?.Stop(); watch?.Dispose();
        // Disconnection also closes every portal session owned by this connection.
        bus?.Dispose(); session = null;
        if (OperatingSystem.IsMacOS()) { if (native != 0) UnregisterEventHotKey(native); if (handlerRef != 0) RemoveEventHandler(handlerRef); }
        else if (native != 0) pickerhub_hotkey_close(native);
        native = handlerRef = 0;
    }
}
