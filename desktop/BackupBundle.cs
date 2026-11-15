using System.Text.Json;

namespace PickerHub;

internal static class BackupBundle
{
    internal static string DailyPath => Path.Combine(Workspace.Folder, "daily_palette_state.json");
    static string Journal => Path.Combine(Workspace.Folder, "restore-journal.json");
    internal sealed record Contents(Workspace Workspace, string? Daily, bool IncludesDaily);
    internal static string Export(Workspace workspace)
    {
        string? daily = File.Exists(DailyPath) ? File.ReadAllText(DailyPath) : null;
        ValidateDaily(daily);
        return JsonSerializer.Serialize(new { backupVersion = 1, workspace, dailyState = daily }, Workspace.Json);
    }
    static void ValidateDaily(string? daily)
    {
        if (daily == null) return;
        if (daily.Length > 1024 * 1024) throw new InvalidDataException(L.T("حالة الباقة اليومية أكبر من الحد المسموح."));
        using var doc = JsonDocument.Parse(daily);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("schema_version", out var schema) || !schema.TryGetInt32(out int version) || version < 1 || version > 1)
            throw new InvalidDataException(L.T("نسخة حالة الباقة اليومية غير مدعومة. حدّث PickerHub قبل الاستعادة."));
    }
    internal static Contents Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("backupVersion", out var version)) return new(Workspace.Parse(text), null, false);
        if (version.GetInt32() != 1) throw new InvalidDataException(L.T("صيغة النسخة الاحتياطية أحدث من التطبيق."));
        var workspace = Workspace.Parse(doc.RootElement.GetProperty("workspace").GetRawText());
        var daily = doc.RootElement.GetProperty("dailyState").GetString(); ValidateDaily(daily);
        return new(workspace, daily, true);
    }
    internal static void Restore(Contents contents, Action? afterWorkspace = null)
    {
        Directory.CreateDirectory(Workspace.Folder);
        RecoverInterrupted();
        var before = new Dictionary<string, string?>
        {
            ["workspace"] = File.Exists(Workspace.FileName) ? File.ReadAllText(Workspace.FileName) : null,
            ["daily"] = File.Exists(DailyPath) ? File.ReadAllText(DailyPath) : null
        };
        // A durable journal makes the two-file restore recoverable after a crash or disk error.
        Workspace.AtomicWrite(Journal, JsonSerializer.Serialize(before));
        try
        {
            contents.Workspace.Save(); afterWorkspace?.Invoke();
            if (contents.IncludesDaily) Set(DailyPath, contents.Daily);
            File.Delete(Journal);
        }
        catch { RecoverInterrupted(); throw; }
    }
    static void Set(string path, string? data) { if (data == null) File.Delete(path); else Workspace.AtomicWrite(path, data); }
    internal static bool RecoverInterrupted()
    {
        if (!File.Exists(Journal)) return false;
        var before = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(Journal))!;
        Set(Workspace.FileName, before["workspace"]); Set(DailyPath, before["daily"]); File.Delete(Journal); return true;
    }
}
