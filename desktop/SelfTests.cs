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
    // The image guide's leaders: across many random pictures, no two leaders cross, no leader grazes another
    // color's mark when there is room, no labels overlap, and each column's labels are evenly spaced.
    static void Callouts()
    {
        var random = new Random(41);
        var frame = new Avalonia.Rect(240, 40, 420, 300);
        foreach (bool both in new[] { true, false })
            for (int round = 0; round < 400; round++)
            {
                int n = 1 + random.Next(16);
                var marks = Enumerable.Range(0, n).Select(_ => new Avalonia.Point(frame.X + 12 + random.NextDouble() * (frame.Width - 24), frame.Y + 12 + random.NextDouble() * (frame.Height - 24))).ToList();
                var placed = PickerHub.Callouts.Layout(marks, frame, new Avalonia.Size(196, 48), both, true, 8, 16);
                Check(placed.Length == n && placed.All(p => p.Label.Width > 0), "Every color gets a label");
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        Check(!PickerHub.Callouts.Collide(placed[i], placed[j]), $"Leaders never cross ({n} colors, round {round})");
                        Check(!placed[i].Label.Intersects(placed[j].Label), "Labels never overlap");
                    }
                Check(placed.All(p => p.Left ? p.Label.Right <= frame.Left : p.Label.Left >= frame.Right), "Labels sit outside the frame");
                foreach (var side in placed.GroupBy(p => p.Left))
                {
                    var ys = side.Select(p => p.Label.Center.Y).OrderBy(y => y).ToArray();
                    var gaps = ys.Zip(ys.Skip(1), (a, b) => b - a).ToArray();
                    Check(gaps.Length == 0 || gaps.Max() - gaps.Min() < .01, "Each column's labels are evenly spaced");
                }
            }
        // Marks in a row on one side: the leaders still fan out without crossing or passing through a mark.
        var row = Enumerable.Range(0, 6).Select(i => new Avalonia.Point(frame.X + 40 + i * 60, frame.Center.Y)).ToList();
        var fan = PickerHub.Callouts.Layout(row, frame, new Avalonia.Size(196, 48), false, true, 8, 16);
        for (int i = 0; i < fan.Length; i++)
            for (int j = 0; j < row.Count; j++)
                if (j != i) Check(PickerHub.Callouts.Clearance(fan[i], row[j]) >= 15.5, $"A leader keeps clear of other marks: leader {i} passes {PickerHub.Callouts.Clearance(fan[i], row[j]):0.0} px from mark {j}");
        for (int i = 0; i < fan.Length; i++)
            for (int j = i + 1; j < fan.Length; j++) Check(!PickerHub.Callouts.Collide(fan[i], fan[j]), "Bent leaders never cross");
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
            foreach (var size in new[] { new Avalonia.Size(1000, 190), new Avalonia.Size(190, 1000), new Avalonia.Size(300, 300) })
            {
                var (start, end) = MainWindow.CssGradientLine(45, size);
                var axis = end.Point - start.Point;
                Check(Math.Abs(axis.X + axis.Y) < .001 && Math.Abs(axis.X - (size.Width + size.Height) / 2) < .001,
                    "The CSS gradient keeps a 45° angle and projects opposite corners to its endpoints");
            }
            var imported = PaletteExchange.Parse(System.Text.Encoding.UTF8.GetBytes(
                """{"name":"New palette","colors":[{"name":"Accent","hex":"#87ceeb"},{"name":"Duplicate","hex":"#87CEEB"},{"name":"Text","hex":"#111111"}]}"""));
            Check(imported.Colors.Count == 3 && imported.Colors[0].Hex == "#87CEEB", "Palette JSON preserves names and normalizes hex");
            var unique = PaletteExchange.UniqueAgainst(imported, [new Swatch { Hex = "#87ceeb" }]);
            Check(unique.Length == 1 && unique[0].Name == "Text", "Palette merge skips destination and incoming duplicates case-insensitively");
            Reject(() => PaletteExchange.Parse(System.Text.Encoding.UTF8.GetBytes("""{"name":"Invalid","colors":[{"name":"Nope","hex":"red"}]}""")), "Palette import rejects malformed swatches");
            imported.Collection = "Studio Website";
            imported.Colors[0].Favorite = true;
            imported.Roles["primary"] = "#111111";
            Check(ProjectSearch.Find([imported], " website ", false).Single().Palette == imported &&
                ProjectSearch.Find([imported], "87ce", true).Single().Color == imported.Colors[0] &&
                ProjectSearch.Find([imported], "primary", false).Single().Role == "primary",
                "Project search finds project names, favorite hex values and semantic roles");
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
        Naming();
        Console.WriteLine("Phase 1: persistence, recovery, migration and undo passed");
    }
    // Every name in the app comes from the engine's perceptual classifier; these are the cases that used to go wrong.
    static void Naming()
    {
        string English(string hex) => Core.Describe(hex).GetProperty("english").GetString()!;
        string Family(string hex) => Core.Describe(hex).GetProperty("family").GetString()!;
        Check(English("#7B5EA7") == "Medium purple" && Family("#7B5EA7") == "Purple", "A muted purple is named purple, not gray");
        foreach (var hex in new[] { "#800080", "#663399", "#8E7CC3", "#866F91", "#301934", "#4B0082", "#6A5ACD", "#8A2BE2" })
            Check(Family(hex) is "Purple" or "Violet" && !English(hex).Contains("gray", StringComparison.OrdinalIgnoreCase), $"{hex} keeps its purple family");
        Check(English("#8B8197") == "Purple gray" && Core.Describe("#8B8197").Text("tone") == "tinted", "A gray purple keeps its tint");
        foreach (var hex in new[] { "#808080", "#7E8081", "#D3D3D3", "#404040" })
            Check(Family(hex) == "Neutral", $"{hex} is a true gray");
        Check(Family("#FF00FF") == "Magenta" && Family("#C0007A") == "Magenta", "Magentas keep their family");
        Check(Family("#8B4513") == "Brown" && Family("#5C4033") == "Brown", "Browns are not charcoal");
        Check(Family("#4682B4") == "Blue" && Family("#1E3A8A") == "Blue", "Blues keep their family");
        L.Language = "en"; bool english = Core.Describe("#7B5EA7").Text("base") == "Purple" && Core.Describe("#8B4513").Text("base") == "Brown";
        L.Language = "ar"; Check(english && Core.Describe("#7B5EA7").Text("base") == "أرجواني", "Families read in both languages");

        // A library saved by 1.0 keeps the names people typed, but drops the old automatic ones.
        var library = new Workspace();
        library.Palettes[0].Colors.AddRange([
            new Swatch { Hex = "#7B5EA7", Name = "رمادي" }, new Swatch { Hex = "#7B5EA7", Name = "Gray" },
            new Swatch { Hex = "#5C4033", Name = "Charcoal" }, new Swatch { Hex = "#7B5EA7", Name = "Charcoal" },
            new Swatch { Hex = "#7B5EA7", Name = "Studio wall" }, new Swatch { Hex = "#808080", Name = "Gray" }]);
        var saved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(library))!;
        saved.Remove("nameScheme");
        var names = Workspace.Parse(JsonSerializer.Serialize(saved)).Palettes[0].Colors.Select(c => c.Name).ToArray();
        Check(names.SequenceEqual(["أرجواني متوسط", "Medium purple", "Dark brown", "Charcoal", "Studio wall", "Gray"]),
            "Old automatic swatch names are renamed once and typed names are kept: " + string.Join(", ", names));
        var current = Workspace.Parse(Json(library)).Palettes[0].Colors.Select(c => c.Name);
        Check(current.SequenceEqual(library.Palettes[0].Colors.Select(c => c.Name)), "Current libraries are never renamed");
        var imported = PaletteExchange.Parse(System.Text.Encoding.UTF8.GetBytes("""{"name":"Wall","colors":[{"name":"Gray","hex":"#7B5EA7"},{"name":"Lilac wall","hex":"#7B5EA7"}]}"""));
        Check(imported.Colors[0].Name == "Medium purple" && imported.Colors[1].Name == "Lilac wall", "Imported palettes from 1.0 lose old automatic names only");
    }
    // Compact view keeps the five most dominant colors, largest share first; equal shares keep their order.
    static void CompactView()
    {
        static ColorGuide.Entry E(string hex, double share) => new(hex, hex, share, new Avalonia.Point(.5, .5));
        var all = new[] { E("#000001", 10), E("#000002", 40), E("#000003", 5), E("#000004", 20), E("#000005", 15), E("#000006", 5), E("#000007", 3) };
        Check(ColorGuide.Visible(all, false).Select(e => e.Hex).SequenceEqual(["#000002", "#000004", "#000005", "#000001", "#000003"]),
            "Compact view keeps the five most dominant colors, largest first");
        Check(ColorGuide.Visible(all, true).Select(e => e.Hex).SequenceEqual(["#000002", "#000004", "#000005", "#000001", "#000003", "#000006", "#000007"]),
            "Show all lists every color by dominance");
        Check(ColorGuide.Visible(all[..4], false).Length == 4 && ColorGuide.Visible([], false).Length == 0, "Five colors or fewer are all shown");
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
        var originalPixel = new Avalonia.PixelPoint(1536, 700);
        var transparentPixel = new Avalonia.PixelPoint(1537, 700);
        bitmap.SetPixel(originalPixel.X, originalPixel.Y, new SkiaSharp.SKColor(0x92, 0x17, 0x54));
        bitmap.SetPixel(transparentPixel.X, transparentPixel.Y, SkiaSharp.SKColors.Transparent);
        using var original = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var encoded = original.ToArray();
        Check(ImageAnalysis.Sample(encoded, originalPixel, CancellationToken.None) == "#921754",
            "Image sampling reads the requested original pixel at full resolution");
        Check(ImageAnalysis.Sample(encoded, transparentPixel, CancellationToken.None) == null,
            "Fully transparent pixels do not produce a swatch");
        var fitted = ImageSampler.ImageBounds(new Avalonia.Size(420, 220), new Avalonia.Size(200, 200));
        Check(ImageSampler.PixelAt(new Avalonia.Point(210, 110), fitted, new Avalonia.PixelSize(1920, 1920)) == new Avalonia.PixelPoint(960, 960) &&
            ImageSampler.PixelAt(new Avalonia.Point(40, 110), fitted, new Avalonia.PixelSize(1920, 1920)) == null,
            "Image pointer coordinates map to the source and exclude letterboxing");
        Callouts();
        CompactView();
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
