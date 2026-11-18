using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickerHub;

internal class Swatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#87CEEB";
    public bool Favorite { get; set; }
    public static Swatch From(string h) => new() { Name = Core.Describe(h).Text("name"), Hex = h };
}
internal class Palette
{
    public const int MaxColors = 2048;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "لوحتي الأولى";
    public List<Swatch> Colors { get; set; } = [];
    public string Collection { get; set; } = "";
    public Dictionary<string, string> Roles { get; set; } = [];
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
    public const int MaxPalettes = 256;
    public int Version { get; set; } = 2;
    // Which automatic color names the swatches carry: 1 is the perceptual naming of PickerHub 1.1 and later.
    // Files written before it have no such field; their swatches are renamed once (see RefreshAutomaticNames).
    // Older versions ignore the field, so this needs no new workspace version.
    public int NameScheme { get; set; } = 1;
    public string Color { get; set; } = "#87CEEB";
    public int Limit { get; set; } = 32;
    public bool UseWaylandScreenSharing { get; set; }
    public bool ReduceMotion { get; set; }
    public bool DiagnosticsEnabled { get; set; }
    public string Language { get; set; } = "ar";
    public bool ReduceTransparency { get; set; }
    public string LibraryView { get; set; } = "grid";
    public bool FollowSystemTheme { get; set; }
    public bool AutoCopy { get; set; } = true;
    public string CopyFormat { get; set; } = "HEX";
    public bool HexUppercase { get; set; } = true;
    public bool HexPrefix { get; set; } = true;
    public bool GlobalShortcut { get; set; }
    public bool QuickPick { get; set; }
    public string Shortcut { get; set; } = "Ctrl+Shift+P";
    public string Theme { get; set; } = "dark";
    public List<Discovery> Discoveries { get; set; } = [];
    public Dictionary<string, string> BrandLocks { get; set; } = [];
    public List<string> History { get; set; } = [];
    public List<Palette> Palettes { get; set; } = [new()];
    public List<Gradient> Gradients { get; set; } = [];
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static string DataRoot()
    {
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_DATA_HOME") is string dir && Path.IsPathFullyQualified(dir)) return dir;
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    }
    internal static string? RootOverride;
    internal static string Folder => Path.Combine(RootOverride ?? DataRoot(), "PickerHub");
    internal static string FileName => Path.Combine(Folder, "workspace.json");
    internal static string BackupName => FileName + ".bak";
    internal static string? RecoveryNotice { get; private set; }
    public static bool Hex(string? s) => s != null && Regex.IsMatch(s, @"\A#[0-9a-fA-F]{6}\z");
    public static Workspace Parse(string text)
    {
        using var obj = JsonDocument.Parse(text);
        foreach (var k in new[] { "version", "color", "limit", "theme", "palettes", "gradients", "history" })
            if (!obj.RootElement.TryGetProperty(k, out _)) throw new InvalidDataException("اختر نسخة PickerHub احتياطية صالحة.");
        var w = JsonSerializer.Deserialize<Workspace>(text, Json)!;
        if (w.Version == 1) w.Version = 2;
        bool oldNames = !obj.RootElement.TryGetProperty("nameScheme", out _) || w.NameScheme < 1;
        static bool Name(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 100;
        static bool Ids(IEnumerable<string> ids) { var seen = new HashSet<string>(); return ids.All(id => id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9-]{1,64}\z") && seen.Add(id)); }
        if (w.Version != 2 || !Hex(w.Color) || w.Theme is not ("light" or "dark") || w.CopyFormat is not ("HEX" or "RGB" or "HSL") ||
           w.Language is not ("ar" or "en") || w.LibraryView is not ("grid" or "list" or "deck") || w.Shortcut is not ("Ctrl+Shift+P" or "Ctrl+Alt+P" or "Ctrl+Shift+C") || w.Limit is < 1 or > 256 ||
           !SemanticPalette.Valid(w.BrandLocks) || w.Discoveries == null || w.Discoveries.Count > 90 ||
           !w.Discoveries.All(d => d != null && d.Id != null && d.Id.Length is > 0 and <= 160 && Name(d.Name) && DateTimeOffset.TryParse(d.CreatedAt, out _) && SemanticPalette.Valid(d.Light) && SemanticPalette.Valid(d.Dark)) ||
           w.Discoveries.Select(d => d.Id).Distinct().Count() != w.Discoveries.Count || w.History == null || w.History.Count > w.Limit || !w.History.All(Hex) || w.Palettes == null || w.Palettes.Count is < 1 or > MaxPalettes ||
           !w.Palettes.All(p => p != null && SemanticPalette.Valid(p.Roles) && p.Collection != null && p.Collection.Length <= 100 && Name(p.Name) && p.Colors != null && p.Colors.Count <= Palette.MaxColors && p.Colors.All(c => c != null && Name(c.Name) && Hex(c.Hex)) && Ids(p.Colors.Select(c => c.Id))) ||
           !Ids(w.Palettes.Select(p => p.Id)) || w.Gradients == null || w.Gradients.Count > 512 ||
           !w.Gradients.All(g => g != null && Name(g.Name) && g.Type is "linear" or "radial" && g.Angle is >= 0 and <= 360 && g.Stops != null && g.Stops.Count is >= 2 and <= 32 && g.Stops.All(s => s != null && Hex(s.Hex) && s.Pos is >= 0 and <= 100)) ||
           !Ids(w.Gradients.Select(g => g.Id))) throw new InvalidDataException("بيانات المكتبة غير صالحة.");
        if (oldNames) { RefreshAutomaticNames(w.Palettes.SelectMany(p => p.Colors)); w.NameScheme = 1; }
        return w;
    }
    // Before 1.1 a new swatch was named after the nearest of 51 references by RGB distance, which called muted
    // purples "Gray" and dark browns "Charcoal". A swatch whose name is still exactly what that gave its color was
    // never renamed by its owner, so it takes its perceptual name, in the same language. Names people typed stay.
    // Used for libraries saved before 1.1 and for imported palette files, which may have been exported by 1.0.
    internal static void RefreshAutomaticNames(IEnumerable<Swatch> swatches)
    {
        foreach (var swatch in swatches)
        {
            var info = Core.Describe(swatch.Hex); var previous = info.GetProperty("previous");
            if (swatch.Name == previous.GetProperty("name").GetString()) swatch.Name = info.GetProperty("name").GetString()!;
            else if (swatch.Name == previous.GetProperty("english").GetString()) swatch.Name = info.GetProperty("english").GetString()!;
        }
    }
    public static Workspace Load()
    {
        RecoveryNotice = null;
        if (!File.Exists(FileName)) return new();
        try { return Parse(File.ReadAllText(FileName)); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            // Never downgrade a newer application's workspace, even if an older backup exists.
            try { using var doc = JsonDocument.Parse(File.ReadAllText(FileName)); if (doc.RootElement.GetProperty("version").GetInt32() > 2) throw new IOException("هذه المكتبة تحتاج إلى إصدار أحدث من PickerHub."); }
            catch (JsonException) { } catch (KeyNotFoundException) { }
            var recovered = Parse(File.ReadAllText(BackupName));
            string quarantine = FileName + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(FileName, quarantine, false);
            AtomicWrite(FileName, JsonSerializer.Serialize(recovered, Json));
            RecoveryNotice = "استُعيدت آخر نسخة سليمة. حُفظ الملف التالف للمراجعة ولم تُحذف بياناته.";
            return recovered;
        }
    }
    public void Save()
    {
        SaveSnapshot(JsonSerializer.Serialize(this, Json));
    }
    internal static void SaveSnapshot(string json)
    {
        Parse(json);
        Directory.CreateDirectory(Folder);
        if (File.Exists(FileName))
        {
            var old = File.ReadAllText(FileName);
            try { Parse(old); AtomicWrite(BackupName, old); }
            catch (Exception e) when (e is JsonException or InvalidDataException) { /* Keep the last valid backup. */ }
        }
        AtomicWrite(FileName, json);
    }
    internal static void AtomicWrite(string path, string json)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var data = System.Text.Encoding.UTF8.GetBytes(json); f.Write(data); f.Flush(true); }
            File.Move(tmp, path, true);
        }
        finally { File.Delete(tmp); }
    }
}
