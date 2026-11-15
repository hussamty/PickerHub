using System.Globalization;
using System.Text.Json;

namespace PickerHub;

internal sealed class Discovery
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public Dictionary<string, string> Light { get; set; } = [];
    public Dictionary<string, string> Dark { get; set; } = [];
}
internal static class SemanticPalette
{
    public static readonly string[] Roles = ["background", "surface", "primary", "accent", "text", "muted", "border"];
    public static readonly string[] Labels = ["الخلفية", "السطح", "الأساسي", "التمييز", "النص", "النص الثانوي", "الحدود"];
    public static bool Valid(Dictionary<string, string>? map) => map != null && map.Count <= Roles.Length && map.All(p => Roles.Contains(p.Key) && Workspace.Hex(p.Value));
    public static Dictionary<string, string> Read(JsonElement p, string mode) => p.GetProperty(mode).EnumerateArray().ToDictionary(r => r.Text("role"), r => r.Text("hex"));
    public static Dictionary<string, string> WithLocks(Dictionary<string, string> colors, Dictionary<string, string> locks)
    { var result = new Dictionary<string, string>(colors); foreach (var p in locks) if (result.ContainsKey(p.Key)) result[p.Key] = p.Value; return result; }
    public static string Css(Dictionary<string, Dictionary<string, string>> themes)
    {
        Validate(themes);
        string Block(string selector, Dictionary<string, string> roles) => selector + " {\n" + string.Join("\n", Roles.Where(roles.ContainsKey).Select(r => "  --color-" + r + ": " + roles[r].ToUpperInvariant() + ";")) + "\n}\n";
        var css = "/* PickerHub: sRGB semantic colors. Check contrast for your intended use. */\n";
        foreach (var (mode, roles) in themes)
            css += Block(mode == "light" ? ":root, [data-theme=\"light\"]" : themes.Count == 1 ? ":root, [data-theme=\"dark\"]" : "[data-theme=\"dark\"]", roles);
        if (themes.TryGetValue("dark", out var dark)) css += "@media (prefers-color-scheme: dark) {\n" + Block(":root:not([data-theme=\"light\"])", dark) + "}\n";
        return css;
    }
    public static string Tokens(Dictionary<string, Dictionary<string, string>> themes)
    {
        Validate(themes);
        object Token(string hex) => new Dictionary<string, object>
        {
            ["$type"] = "color", ["$value"] = new { colorSpace = "srgb", components = Enumerable.Range(0, 3).Select(i => Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255d).ToArray(), alpha = 1, hex = hex.ToLowerInvariant() }
        };
        return JsonSerializer.Serialize(themes.ToDictionary(t => t.Key, t => t.Value.ToDictionary(r => r.Key, r => Token(r.Value))), Workspace.Json);
    }
    static void Validate(Dictionary<string, Dictionary<string, string>> themes)
    {
        if (themes.Count == 0 || themes.Any(t => t.Key is not ("light" or "dark") || !Valid(t.Value) || t.Value.Count == 0)) throw new InvalidDataException("Assign valid semantic roles before export.");
    }
    internal sealed record Pair(string Foreground, string Background, double Ratio, bool AA, bool Large, bool AAA);
    public static Pair[] Checks(Dictionary<string, string> colors)
    {
        var rows = new List<Pair>();
        foreach (string foreground in new[] { "text", "muted", "primary", "accent" })
            foreach (string background in new[] { "background", "surface" })
                if (colors.TryGetValue(foreground, out var fg) && colors.TryGetValue(background, out var bg))
                {
                    var result = Core.Contrast(fg, bg);
                    rows.Add(new(foreground, background, result.GetProperty("ratio").GetDouble(), result.GetProperty("aa").GetBoolean(), result.GetProperty("large").GetBoolean(), result.GetProperty("aaa").GetBoolean()));
                }
        return rows.ToArray();
    }
    public static string Report(Dictionary<string, Dictionary<string, string>> themes) => JsonSerializer.Serialize(new
    {
        standard = "WCAG 2 contrast (opaque sRGB)",
        themes = themes.ToDictionary(p => p.Key, p => Checks(p.Value))
    }, Workspace.Json);
    public static Discovery Archive(JsonElement palette) => new()
    {
        Id = palette.Text("id") + ":" + palette.GetProperty("serial").GetInt64(), Name = palette.Text("name"), CreatedAt = palette.Text("createdAt"), Light = Read(palette, "light"), Dark = Read(palette, "dark")
    };
}
