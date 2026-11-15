using Avalonia.Media;
using System.Text.Json;

namespace PickerHub;
internal static class L
{
    public static string Language = "ar";
    public static FlowDirection Flow => Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    static readonly Dictionary<string, string> English = Load();
    static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("PickerHub.Localization.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string T(string text) => Language == "en" && English.TryGetValue(text, out var translation) ? translation : text;
}
