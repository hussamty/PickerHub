using System.Runtime.InteropServices;
using System.Text.Json;
namespace PickerHub;

internal static class Core
{
    // Use a distinct filename on Windows, where the managed app is also PickerHub.dll.
    const string Lib = "pickerhub_core";
    [DllImport(Lib)] static extern nint describe(uint rgb);
    [DllImport(Lib)] static extern nint contrast(uint fg, uint bg);
    [DllImport(Lib)] static extern nint harmony(uint rgb, uint kind);
    [DllImport(Lib)] static extern nuint output_len();
    [DllImport(Lib)] static extern nint pixel_buffer(uint bytes);
    [DllImport(Lib)] static extern nint extract(uint count);
    static JsonElement Read(nint ptr)
    {
        string text = Marshal.PtrToStringUTF8(ptr, checked((int)output_len()))!;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
    static uint RGB(string hex) => Convert.ToUInt32(hex[1..], 16);
    public static JsonElement Describe(string h) => Read(describe(RGB(h)));
    public static JsonElement Contrast(string a, string b) => Read(contrast(RGB(a), RGB(b)));
    public static string[] Harmony(string h, int mode) => Read(harmony(RGB(h), (uint)mode)).Deserialize<string[]>()!;
    public static JsonElement Extract(byte[] rgba)
    {
        nint ptr = pixel_buffer((uint)rgba.Length);
        if (ptr == 0) throw new InvalidDataException("تجاوزت الصورة حجم العينة المتاح.");
        Marshal.Copy(rgba, 0, ptr, rgba.Length); return Read(extract(10));
    }
    public static string Text(this JsonElement e, string key) => e.GetProperty(key).GetString()!;
}
