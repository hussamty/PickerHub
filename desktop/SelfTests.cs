using System.Text.Json;

namespace PickerHub;

// Runs against an isolated data directory on every release host, without a display server.
internal static class SelfTests
{
    static int checks;
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    internal static void Reject(Action action, string message)
    {
        bool failed = false; try { action(); } catch { failed = true; } Check(failed, message);
    }
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pickerhub-test-" + Guid.NewGuid().ToString("N"));
        Workspace.RootOverride = root;
        try
        {
            Check(!Picker.WaylandSession("x11", "wayland-0"), "Explicit X11 wins over a stale or probed Wayland display");
            Check(Picker.WaylandSession("wayland", null), "Wayland session requires the portal even without a display hint");
            Check(Picker.WaylandSession(null, "wayland-0") && !Picker.WaylandSession(null, ""), "Unknown sessions use the display hint");
            InstanceChecks().GetAwaiter().GetResult(); Phase1(); Phase2(); Phase3(); Phase4(); WriterChecks().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} desktop checks"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); Workspace.RootOverride = null; }
    }
    static async Task InstanceChecks()
    {
        using var owner = SingleInstance.Open();
        Check(owner != null, "First process owns the workspace");
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner!.OnActivate(() => activated.TrySetResult());
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(Workspace.RootOverride!);
        using (var child = System.Diagnostics.Process.Start(start)!)
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Check(child.ExitCode == 0, "Second process activates the owner without opening the library");
        }
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(activated.Task.IsCompletedSuccessfully, "Owner receives activation");
    }
    static async Task WriterChecks()
    {
        var values = new List<string>();
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var writer = new WorkspaceWriter(s => { started.Set(); release.Wait(); lock (values) values.Add(s); });
        writer.Enqueue("first");
        Check(started.Wait(TimeSpan.FromSeconds(5)), "Writer starts off the caller thread");
        for (int i = 0; i < 100; i++) writer.Enqueue(i.ToString());
        Check(values.Count == 0, "Slow disk never blocks editing");
        release.Set(); await writer.FlushAsync();
        Check(values.SequenceEqual(new[] { "first", "99" }), "Writer coalesces bursts and persists latest in order");
        bool fail = true; string? saved = null; int failures = 0;
        var recovery = new WorkspaceWriter(s => { if (fail) throw new IOException("disk full"); saved = s; });
        recovery.Failed += _ => failures++;
        recovery.Enqueue("keep me");
        try { await recovery.FlushAsync(); Check(false, "Flush must expose a failed save"); } catch (IOException) { }
        Check(failures == 1 && saved == null, "Failed save is reported without losing the open snapshot");
        fail = false; recovery.Retry(); await recovery.FlushAsync();
        Check(saved == "keep me", "Retry saves the retained snapshot");
        recovery.Enqueue("newest"); await recovery.FlushAsync();
        Check(saved == "newest", "Writer remains usable after recovery");
    }
    static string Json(Workspace w) => JsonSerializer.Serialize(w, Workspace.Json);
    static void Phase1()
    {
        var original = new Workspace(); original.Save();
        var saved = Workspace.Load(); saved.Color = "#112233"; saved.Save();
        Check(Workspace.Load().Color == "#112233", "Saved workspace round trip");
        File.WriteAllText(Workspace.FileName, "{broken");
        Check(Workspace.Load().Color == original.Color, "Recover last known good workspace");
        Check(Workspace.RecoveryNotice != null && Directory.GetFiles(Workspace.Folder, "*.damaged-*").Length == 1, "Preserve damaged data and explain recovery");
        File.WriteAllText(Workspace.FileName, Json(original).Replace("\"version\": 2", "\"version\": 99"));
        Reject(() => Workspace.Load(), "Never downgrade future workspace");
        Check(File.ReadAllText(Workspace.FileName).Contains("99"), "Future workspace stays untouched");
        original.Save();
        var edit = new EditHistory(); string a = Json(original); original.Color = "#112233"; string b = Json(original);
        edit.Commit(a, b);
        Reject(() => edit.Travel(b, false, _ => throw new IOException()), "Rejected transaction must reject undo");
        Check(edit.CanUndo && !edit.CanRedo, "Rejected undo retains stacks");
        Check(edit.Travel(b, false, _ => { }) == a && edit.CanRedo, "Undo returns previous workspace");
        Check(edit.Travel(a, true, _ => { }) == b, "Redo returns newer workspace");
        edit.Travel(b, false, _ => { }); edit.Commit(a, b); Check(!edit.CanRedo, "New edit invalidates redo");
        for (int i = 0; i < 100; i++) edit.Commit(i.ToString(), (i+1).ToString());
        int n = 0; while (edit.CanUndo) { edit.Travel("now", false, _ => { }); n++; }
        Check(n == 40, "Undo history bounded");
        using var legacy = JsonDocument.Parse(a);
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(a)!;
        foreach (string key in new[] { "autoCopy", "copyFormat", "hexUppercase", "hexPrefix", "globalShortcut", "shortcut" }) map.Remove(key);
        map["version"] = JsonSerializer.SerializeToElement(1);
        var upgraded = Workspace.Parse(JsonSerializer.Serialize(map));
        Check(upgraded.Version == 2 && upgraded.AutoCopy && upgraded.CopyFormat == "HEX" && !upgraded.GlobalShortcut, "Old settings migrate safely without requesting global permissions");
        Check(Core.Describe("#87CEEB").Text("hex") == "#87CEEB", "Native core ABI works");
        Console.WriteLine("Phase 1: persistence, recovery, migration and undo passed");
    }
    static void Phase2()
    {
        Core.Daily("reveal");
        string daily = File.ReadAllText(BackupBundle.DailyPath);
        var bundle = BackupBundle.Parse(BackupBundle.Export(Workspace.Load()));
        Check(bundle.IncludesDaily && bundle.Daily == daily, "Backup includes exact daily state");
        string before = File.ReadAllText(Workspace.FileName); bundle.Workspace.Color = "#ABCDEF";
        Reject(() => BackupBundle.Restore(bundle, () => throw new IOException("disk failure")), "Failed restore must roll back");
        Check(File.ReadAllText(Workspace.FileName) == before && File.ReadAllText(BackupBundle.DailyPath) == daily, "Both files rolled back");
        BackupBundle.Restore(bundle); Check(Workspace.Load().Color == "#ABCDEF", "Bundle restore succeeds");
        Check(!BackupBundle.Parse(before).IncludesDaily, "Legacy backups preserve daily state");
        Reject(() => BackupBundle.Parse("{\"backupVersion\":99}"), "Reject future backup version");
        Check(!BackupBundle.RecoverInterrupted(), "Successful restore leaves no pending journal");
        Console.WriteLine("Phase 2: complete backups, atomic restore, legacy upgrade passed");
    }
    static void Phase3()
    {
        var p = new Palette();
        for (int i = 0; i < 2048; i++) p.Colors.Add(new Swatch { Name = "Color " + i, Hex = "#" + i.ToString("X6"), Favorite = i % 100 == 0 });
        Check(LibraryOps.Filter(p, "#0007ff", false).Length == 1, "Search is case insensitive");
        Check(LibraryOps.Filter(p, "", true).Length == 21, "Favorites filter");
        Check(LibraryOps.Filter(p, "missing", false).Length == 0, "Empty search result");
        var duplicate = new Palette { Colors = [new() { Hex = "#abcdef", Name = "First" }, new() { Hex = "#ABCDEF", Name = "Second", Favorite = true }] };
        Check(LibraryOps.Deduplicate(duplicate) == 1 && duplicate.Colors[0].Favorite, "Duplicates merge favorite state");
        var target = new Palette(); var ids = p.Colors.Take(3).Select(c => c.Id).ToHashSet();
        LibraryOps.Move(p, target, ids);
        Check(target.Colors.Count == 3 && p.Colors.Count == 2045, "Bulk move retains all colors");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, 1500, i => CheckColor(i));
        Check(Core.CachedDescriptions <= 512, "Color metadata cache is bounded");
        Console.WriteLine($"1500 color descriptions: {watch.ElapsedMilliseconds} ms (local observation)");
        using var bitmap = new SkiaSharp.SKBitmap(1920, 1080);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap)) canvas.Clear(new SkiaSharp.SKColor(49, 117, 179));
        using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); using var stream = png.AsStream();
        watch.Restart(); var image = ImageAnalysis.Analyze(stream, CancellationToken.None);
        Check(image.Colors[0] == "#3175B3", "Background image analysis preserves sRGB color");
        using var preview = SkiaSharp.SKBitmap.Decode(image.Preview);
        Check(preview.Width == 768 && preview.Height == 432, "Preview dimensions bounded");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject(() => ImageAnalysis.Analyze(stream, cancelled.Token), "Cancelled analysis stops before decoding");
        Console.WriteLine($"1920x1080 image analysis: {watch.ElapsedMilliseconds} ms (local observation)");
        L.Language = "en"; Check(L.T("القطّارة") == "Eyedropper" && Core.Describe("#87CEEB").Text("name") == Core.Describe("#87CEEB").Text("english"), "English UI and color names");
        L.Language = "ar"; Check(L.T("القطّارة") == "القطّارة", "Arabic UI remains the default");
        Console.WriteLine("Phase 3: large libraries, bulk actions, cache bounds, image analysis and languages passed");
    }
    static void CheckColor(int i)
    {
        string hex = "#" + i.ToString("X6");
        if (Core.Describe(hex).Text("hex") != hex) throw new Exception("Concurrent core buffers corrupted");
    }
    static void Phase4()
    {
        var pair = new Dictionary<string, Dictionary<string, string>>
        {
            ["light"] = new() { ["background"] = "#FFFFFF", ["text"] = "#000000", ["primary"] = "#3175B3" },
            ["dark"] = new() { ["background"] = "#000000", ["text"] = "#FFFFFF" }
        };
        var css = SemanticPalette.Css(pair);
        Check(css.Contains("--color-background: #FFFFFF") && css.Contains("prefers-color-scheme: dark"), "CSS exports both modes with system preference");
        using var tokens = JsonDocument.Parse(SemanticPalette.Tokens(pair));
        var primary = tokens.RootElement.GetProperty("light").GetProperty("primary");
        Check(primary.GetProperty("$type").GetString() == "color" && primary.GetProperty("$value").GetProperty("colorSpace").GetString() == "srgb", "DTCG typed sRGB tokens");
        Check(Math.Abs(primary.GetProperty("$value").GetProperty("components")[0].GetDouble() - 49 / 255d) < 1e-9, "Token components retain precision");
        var check = SemanticPalette.Checks(pair["light"]).Single(c => c.Foreground == "text");
        Check(check.Ratio == 21 && check.AA && check.AAA, "Semantic contrast reports 21:1 black on white");
        var locked = SemanticPalette.WithLocks(pair["light"], new() { ["text"] = "#FFFFFF" });
        Check(!SemanticPalette.Checks(locked).Single(c => c.Foreground == "text").AA && pair["light"]["text"] == "#000000", "Locks preserve original and recheck contrast");
        Reject(() => SemanticPalette.Css(new() { ["light"] = new() { ["bad;}"] = "#FFFFFF" } }), "Unsafe CSS role rejected");
        var palette = Core.Daily("status").GetProperty("palette");
        var discovery = SemanticPalette.Archive(palette); var w = Workspace.Load(); w.Discoveries.Add(discovery); w.BrandLocks["primary"] = "#3175B3"; w.Palettes[0].Roles = pair["light"]; w.Save();
        var again = Workspace.Load();
        Check(again.Discoveries[0].Light.Count == 7 && again.BrandLocks["primary"] == "#3175B3" && again.Palettes[0].Roles["text"] == "#000000", "Roles, archive and locks survive save/reload");
        Check(Core.Describe("#FF0000").GetProperty("formats").Text("OKLCH") == "oklch(62.80% 0.25768 29.23)", "Known sRGB red OKLCH vector");
        Check(Core.Describe("#000000").GetProperty("formats").Text("OKLCH") == "oklch(0.00% 0.00000 0.00)", "Achromatic OKLCH has defined hue");
        Check(ColorVision.Simulate("#FF0000", 1) == "#A39000", "Linear sRGB deuteranopia reference red");
        for (int mode = 0; mode < 3; mode++) Check(ColorVision.Simulate("#FFFFFF", mode) == "#FFFFFF" && ColorVision.Simulate("#000000", mode) == "#000000", "Vision previews preserve neutral extremes");
        Console.WriteLine("Phase 4: semantic CSS, DTCG tokens, contrast, archive, brand locks and OKLCH passed");
    }
}
