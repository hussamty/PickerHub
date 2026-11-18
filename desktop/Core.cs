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
    [DllImport(Lib)] static extern nint extract_at(uint width, uint height, uint count);
    [DllImport(Lib)] static extern nint daily(byte[] request, nuint len);
    static JsonElement Read(nint ptr)
    {
        string text = Marshal.PtrToStringUTF8(ptr, checked((int)output_len()))!;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
    static uint RGB(string hex) => Convert.ToUInt32(hex[1..], 16);
    static readonly Dictionary<uint, JsonElement> descriptions = [];
    static readonly Queue<uint> descriptionOrder = [];
    internal static int CachedDescriptions { get { lock (descriptions) return descriptions.Count; } }
    public static JsonElement Describe(string h)
    {
        uint rgb = RGB(h);
        lock (descriptions)
        {
            if (descriptions.TryGetValue(rgb, out var cached)) return cached;
            var result = Read(describe(rgb));
            if (descriptions.Count >= 512) descriptions.Remove(descriptionOrder.Dequeue());
            descriptions.Add(rgb, result); descriptionOrder.Enqueue(rgb); return result;
        }
    }
    public static JsonElement Contrast(string a, string b) => Read(contrast(RGB(a), RGB(b)));
    public static string[] Harmony(string h, int mode) => Read(harmony(RGB(h), (uint)mode)).Deserialize<string[]>()!;
    public static JsonElement Extract(byte[] rgba)
    {
        nint ptr = pixel_buffer((uint)rgba.Length);
        if (ptr == 0) throw new InvalidDataException(L.T("تجاوزت الصورة حجم العينة المتاح."));
        Marshal.Copy(rgba, 0, ptr, rgba.Length); return Read(extract(16));
    }
    // The same colors, each with where it lives in the image: x and y as fractions of the width and height.
    public static JsonElement ExtractAt(byte[] rgba, int width, int height)
    {
        nint ptr = pixel_buffer((uint)rgba.Length);
        if (ptr == 0) throw new InvalidDataException(L.T("تجاوزت الصورة حجم العينة المتاح."));
        Marshal.Copy(rgba, 0, ptr, rgba.Length); return Read(extract_at((uint)width, (uint)height, 16));
    }
    // Palette of the day. The core reads the clock and the state file itself; only the folder and the action cross over.
    public static JsonElement Daily(string action, string value = "")
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new { dir = Workspace.Folder, action, value });
        var answer = Read(daily(request, (nuint)request.Length));
        if (answer.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        return answer;
    }
    public static string Text(this JsonElement e, string key)
    {
        if (L.Language == "en" && key == "name" && e.TryGetProperty("english", out var english)) return english.GetString()!;
        // The engine names the hue family in both languages, so English never depends on a second word list.
        if (L.Language == "en" && key == "base" && e.TryGetProperty("family", out var family)) return family.GetString()!;
        if (L.Language == "en" && key == "story" && e.TryGetProperty("storyEnglish", out var story)) return story.GetString()!;
        return L.T(e.GetProperty(key).GetString()!);
    }
}
