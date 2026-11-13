using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickerHub;

internal class Swatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#87CEEB";
    public static Swatch From(string h) => new() { Name = Core.Describe(h).Text("name"), Hex = h };
}
internal class Palette
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "لوحتي الأولى";
    public List<Swatch> Colors { get; set; } = [];
}
internal class Stop { public string Hex { get; set; } = "#FFFFFF"; public int Pos { get; set; } }
internal class Gradient
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Name { get; set; } = "تدرجي";
    public string Type { get; set; } = "linear"; public int Angle { get; set; } = 90;
    public List<Stop> Stops { get; set; } = [new() { Hex = "#2856B4", Pos = 0 }, new() { Hex = "#87CEEB", Pos = 100 }];
}
internal class Workspace
{
    public int Version { get; set; } = 1;
    public string Color { get; set; } = "#87CEEB";
    public int Limit { get; set; } = 32;
    public bool UseWaylandScreenSharing { get; set; }
    public string Theme { get; set; } = "light";
    public List<string> History { get; set; } = [];
    public List<Palette> Palettes { get; set; } = [new()];
    public List<Gradient> Gradients { get; set; } = [];
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static string DataRoot()
    {
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_DATA_HOME") is string dir && Path.IsPathFullyQualified(dir)) return dir;
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    }
    internal static readonly string Folder = Path.Combine(DataRoot(), "PickerHub");
    internal static readonly string FileName = Path.Combine(Folder, "workspace.json");
    public static bool Hex(string? s) => s != null && Regex.IsMatch(s, @"\A#[0-9a-fA-F]{6}\z");
    public static Workspace Parse(string text)
    {
        using var obj = JsonDocument.Parse(text);
        foreach (var k in new[] { "version", "color", "limit", "theme", "palettes", "gradients", "history" })
            if (!obj.RootElement.TryGetProperty(k, out _)) throw new InvalidDataException("اختر نسخة PickerHub احتياطية صالحة.");
        var w = JsonSerializer.Deserialize<Workspace>(text, Json)!;
        static bool Name(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 100;
        static bool Ids(IEnumerable<string> ids) { var seen = new HashSet<string>(); return ids.All(id => id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9-]{1,64}\z") && seen.Add(id)); }
        if (w.Version != 1 || !Hex(w.Color) || w.Theme is not ("light" or "dark") || w.Limit is < 1 or > 256 ||
           w.History == null || w.History.Count > w.Limit || !w.History.All(Hex) || w.Palettes == null || w.Palettes.Count is < 1 or > 256 ||
           !w.Palettes.All(p => p != null && Name(p.Name) && p.Colors != null && p.Colors.Count <= 2048 && p.Colors.All(c => c != null && Name(c.Name) && Hex(c.Hex)) && Ids(p.Colors.Select(c => c.Id))) ||
           !Ids(w.Palettes.Select(p => p.Id)) || w.Gradients == null || w.Gradients.Count > 512 ||
           !w.Gradients.All(g => g != null && Name(g.Name) && g.Type is "linear" or "radial" && g.Angle is >= 0 and <= 360 && g.Stops != null && g.Stops.Count is >= 2 and <= 32 && g.Stops.All(s => s != null && Hex(s.Hex) && s.Pos is >= 0 and <= 100)) ||
           !Ids(w.Gradients.Select(g => g.Id))) throw new InvalidDataException("بيانات المكتبة غير صالحة.");
        return w;
    }
    public static Workspace Load() => File.Exists(FileName) ? Parse(File.ReadAllText(FileName)) : new();
    public void Save()
    {
        var json = JsonSerializer.Serialize(this, Json); Parse(json);
        Directory.CreateDirectory(Folder); var tmp = FileName + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(tmp, json); File.Move(tmp, FileName, true); } finally { File.Delete(tmp); }
    }
}
