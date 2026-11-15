using Avalonia.Controls;
using Tmds.DBus;

namespace PickerHub;

[DBusInterface("org.gnome.Shell.Extensions.ColorPicker")]
public interface IGnomeColorPicker : IDBusObject
{
    Task<IDictionary<string, object>> PickAsync();
}

[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IColorPortal : IDBusObject
{
    Task<ObjectPath> PickColorAsync(string parent, IDictionary<string, object> options);
}

internal static class WaylandColorPicker
{
    const string Extension = "org.gnome.Shell.Extensions.ColorPicker";
    const string Portal = "org.freedesktop.portal.Desktop";

    public static async Task<string?> Pick(Window owner)
    {
        using var bus = new Connection(Address.Session);
        var peer = (await bus.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5))).LocalName;
        if (await bus.IsServiceActiveAsync(Extension))
        {
            try
            {
                var picker = bus.CreateProxy<IGnomeColorPicker>(Extension, "/org/gnome/Shell/Extensions/ColorPicker");
                return ToHex(await picker.PickAsync().WaitAsync(TimeSpan.FromMinutes(2)));
            }
            catch (DBusException e) when (e.ErrorName == "org.gtk.GDBus.UnmappedGError.Quark._g_2dio_2derror_2dquark.Code19")
            {
                return null; // G_IO_ERROR_CANCELLED from the extension
            }
            catch (DBusException e) when (Unavailable(e))
            {
                // The extension may be disabled between discovering it and calling Pick.
            }
        }

        var pickerPortal = bus.CreateProxy<IColorPortal>(Portal, "/org/freedesktop/portal/desktop");
        var token = "pickerhub_" + Guid.NewGuid().ToString("N");
        var path = "/org/freedesktop/portal/desktop/request/" + peer.TrimStart(':').Replace('.', '_') + "/" + token;
        var request = bus.CreateProxy<IRequest>(Portal, path);
        var response = new TaskCompletionSource<(uint code, IDictionary<string, object> values)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await request.WatchResponseAsync(r => response.TrySetResult(r), e => response.TrySetException(e));
        var handle = owner.TryGetPlatformHandle();
        string parent = handle?.HandleDescriptor == "XID" ? $"x11:{handle.Handle:x}" : "";
        try
        {
            var actual = await pickerPortal.PickColorAsync(parent, new Dictionary<string, object> { ["handle_token"] = token }).WaitAsync(TimeSpan.FromSeconds(10));
            if (actual.ToString() != path) throw new IOException(L.T("تعذّر تتبّع طلب قطّارة النظام."));
        }
        catch (DBusException e) when (Unavailable(e))
        {
            throw new InvalidOperationException(L.T("قطّارة النظام غير متاحة. تحقق من تثبيت بوابة سطح المكتب الخاصة ببيئتك."), e);
        }
        (uint code, IDictionary<string, object> values) reply;
        try { reply = await response.Task.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch { try { await request.CloseAsync().WaitAsync(TimeSpan.FromSeconds(1)); } catch { } throw new IOException(L.T("انتهت مهلة قطّارة النظام. أعد المحاولة أو افتح إعدادات القطّارة.")); }
        return reply.code switch
        {
            0 => ToHex(reply.values),
            1 => null,
            _ => throw new InvalidOperationException(L.T("تعذّر إتمام اختيار اللون في قطّارة النظام."))
        };
    }

    static bool Unavailable(DBusException e) => e.ErrorName is
        "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" or
        "org.freedesktop.DBus.Error.UnknownObject" or "org.freedesktop.DBus.Error.UnknownMethod" or
        "org.freedesktop.DBus.Error.UnknownInterface";

    static string ToHex(IDictionary<string, object> reply)
    {
        if (!reply.TryGetValue("color", out var color) || color is not ValueTuple<double, double, double> rgb)
            throw new InvalidDataException(L.T("أعادت قطّارة النظام لوناً غير صالح."));
        static int Channel(double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > 1) throw new InvalidDataException(L.T("قيمة قناة اللون غير صالحة."));
            return (int)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        }
        return $"#{Channel(rgb.Item1):X2}{Channel(rgb.Item2):X2}{Channel(rgb.Item3):X2}";
    }
}
