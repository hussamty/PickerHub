using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace PickerHub;
internal sealed partial class MainWindow
{
    async Task Capture(string name)
    {
        var origin = this.PointToScreen(default); int width = (int)Math.Round(Bounds.Width * RenderScaling), height = (int)Math.Round(Bounds.Height * RenderScaling);
        Console.WriteLine($"FRAME {name} {origin.X} {origin.Y} {width} {height}"); await Task.Run(Console.ReadLine);
    }
    // Controls laid out past either side of the page viewport, where the scroll area would cut them off.
    // Stages that clip or pose their own children (the envelope, the card deck) are judged by their own bounds.
    string[] Overflowing(ScrollViewer scroller)
    {
        double width = scroller.Viewport.Width;
        bool Exempt(Visual v) => v.GetVisualAncestors().TakeWhile(a => a != content).Any(a => a is PackStage or Deck || (a is Control c && c.ClipToBounds));
        var found = new List<string>();
        foreach (var c in content.GetVisualDescendants().OfType<Control>())
        {
            if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0 || Exempt(c)) continue;
            // Walk up by hand, as Locate does: a subtree whose direction flips is mirrored about its own centre.
            double x0 = 0, x1 = c.Bounds.Width;
            for (Visual? v = c; v != null && v != scroller; v = v.GetVisualParent())
            {
                if (v.GetVisualParent() is { } parent && v.FlowDirection != parent.FlowDirection) (x0, x1) = (v.Bounds.Width - x1, v.Bounds.Width - x0);
                x0 += v.Bounds.X; x1 += v.Bounds.X;
            }
            if (x0 < -1 || x1 > width + 1) found.Add($"{c.GetType().Name} {x0:0}..{x1:0}/{width:0}");
        }
        return found.Distinct().Take(6).ToArray();
    }
    // Top bar actions that overlap one another or leave the window.
    string[] TopBarCollisions()
    {
        var parts = new Control?[] { eyedropper, captureMode, undoButton, redoButton, themeSwitch, minimizeWindow, maximizeWindow, closeWindow }
            .OfType<Control>().Where(c => c.IsEffectivelyVisible).Select(c => (c, r: Locate(c))).ToArray();
        var found = parts.Where(p => p.r.X < 0 || p.r.Right > ClientSize.Width).Select(p => (AutomationProperties.GetName(p.c) ?? p.c.GetType().Name) + " leaves the window").ToList();
        for (int i = 0; i < parts.Length; i++)
            for (int j = i + 1; j < parts.Length; j++)
                if (parts[i].r.Intersects(parts[j].r)) found.Add((AutomationProperties.GetName(parts[i].c) ?? "?") + " overlaps " + (AutomationProperties.GetName(parts[j].c) ?? "?"));
        return found.ToArray();
    }
    // A picture with distinct areas: the guide marks each color inside its own area, its leaders draw out
    // over time and never cross, its labels never overlap, and it reads in both themes.
    async Task GuideScenario()
    {
        byte[] png;
        using (var picture = new SkiaSharp.SKBitmap(480, 320))
        using (var canvas = new SkiaSharp.SKCanvas(picture))
        using (var paint = new SkiaSharp.SKPaint { IsAntialias = false })
        {
            void Fill(uint color, Action<SkiaSharp.SKPaint> draw) { paint.Color = new SkiaSharp.SKColor(color); draw(paint); }
            Fill(0xFF8EC9F0, p => canvas.DrawRect(0, 0, 480, 200, p));                       // sky
            Fill(0xFFF4C430, p => canvas.DrawCircle(390, 70, 38, p));                         // sun
            Fill(0xFFFFFFFF, p => canvas.DrawOval(120, 60, 60, 22, p));                       // cloud
            Fill(0xFF4C9A3F, p => canvas.DrawRect(0, 200, 480, 120, p));                      // meadow
            Fill(0xFF2E5E2A, p => canvas.DrawRect(40, 120, 36, 90, p));                       // tree
            Fill(0xFFC8553D, p => canvas.DrawRect(250, 140, 110, 80, p));                     // house
            Fill(0xFF3B2A20, p => canvas.DrawRect(290, 180, 26, 40, p));                      // door
            Fill(0xFF7B5EA7, p => canvas.DrawRect(0, 290, 480, 30, p));                       // lavender row
            using var image = SkiaSharp.SKImage.FromBitmap(picture); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
        }
        state.ReduceMotion = false; page = 3;
        await ImportImage(() => Task.FromResult<Stream>(new MemoryStream(png)), "meadow-guide.png");
        await Task.Delay(120);
        var guide = content.GetVisualDescendants().OfType<ColorGuide>().Single();
        SelfTests.Check(guide.Labels.Count == recentImage.Length && recentImage.Length >= 6, $"The guide labels every color: {recentImage.Length}");
        SelfTests.Check(!guide.Settled && guide.Labels.Any(l => l.Opacity < 1), "Leaders draw out over time rather than appearing at once");
        await Task.Delay(2600);
        SelfTests.Check(guide.Settled && guide.Labels.All(l => l.Opacity == 1) && Enumerable.Range(0, guide.Placements.Length).All(i => guide.LeaderProgress(i) == 1), "The guide settles with every leader drawn");
        var placed = guide.Placements;
        for (int i = 0; i < placed.Length; i++)
            for (int j = i + 1; j < placed.Length; j++)
            {
                SelfTests.Check(!Callouts.Collide(placed[i], placed[j]), "Guide leaders never cross");
                SelfTests.Check(!placed[i].Label.Intersects(placed[j].Label), "Guide labels never overlap");
            }
        SelfTests.Check(placed.All(p => guide.FrameRect.Deflate(12).Contains(p.Mark) && !guide.FrameRect.Contains(p.Tip)), "Every leader runs from inside the picture to outside its frame");
        // Each mark sits on a pixel nearer its own color than any other listed color.
        using (var decoded = SkiaSharp.SKBitmap.Decode(png))
        {
            var inner = guide.FrameRect.Deflate(12);
            double Apart(SkiaSharp.SKColor a, string hex) { var b = Avalonia.Media.Color.Parse(hex); return Math.Pow(a.Red - b.R, 2) + Math.Pow(a.Green - b.G, 2) + Math.Pow(a.Blue - b.B, 2); }
            for (int i = 0; i < placed.Length; i++)
            {
                var px = decoded.GetPixel(Math.Clamp((int)((placed[i].Mark.X - inner.X) / inner.Width * decoded.Width), 0, decoded.Width - 1), Math.Clamp((int)((placed[i].Mark.Y - inner.Y) / inner.Height * decoded.Height), 0, decoded.Height - 1));
                int nearest = Enumerable.Range(0, recentImage.Length).OrderBy(k => Apart(px, recentImage[k])).First();
                SelfTests.Check(nearest == i, $"The mark for {recentImage[i]} sits on its own color (found {px})");
            }
        }
        // The lavender row is named for its hue, as every label is: by the engine's perceptual classifier.
        {
            var lavender = guide.Entries.FirstOrDefault(e => Core.Describe(e.Hex).GetProperty("family").GetString() is "Purple" or "Violet");
            SelfTests.Check(lavender != null && lavender.Name == Core.Describe(lavender.Hex).Text("name") && lavender.Name != Core.Describe("#808080").Text("name"),
                $"The lavender row is labelled as a purple, not gray: {string.Join(", ", guide.Entries.Select(e => e.Hex + " " + e.Name))}");
        }
        foreach (var theme in new[] { "dark", "light" })
        {
            state.Theme = theme; Render(); await Task.Delay(250);
            await Capture(L.Language + "-guide-" + theme);
        }
        state.Theme = "dark"; Render();
    }
    internal async Task Smoke()
    {
        try
        {
            await Copy("PickerHub clipboard probe");
            SelfTests.Check(await Clipboard!.TryGetTextAsync() == "PickerHub clipboard probe", "Baseline clipboard round trip");
            page = 3; Render();
            using (var pixels = new SkiaSharp.SKBitmap(16, 16))
            {
                pixels.Erase(new SkiaSharp.SKColor(18, 52, 86));
                using var png = pixels.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(png.AsStream());
                await Clipboard!.SetBitmapAsync(bitmap);
                Activate(); Focus();
                Console.WriteLine("PASTE"); await Task.Run(Console.ReadLine);
                for (int i = 0; (analyzing || preview == null) && i < 100; i++) await Task.Delay(20);
                SelfTests.Check(recentImage.Contains("#123456") && preview != null, "Clipboard bitmap imports through the shared analyzer");
                var sampler = content.GetVisualDescendants().OfType<ImageSampler>().Single();
                sampler.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
                for (int i = 0; imageSampleRequest != null && i < 100; i++) await Task.Delay(20);
                SelfTests.Check(sampledColor == "#123456" && sampledPixel == new PixelPoint(8, 8),
                    "Enter samples the original image at its center");
                SelfTests.Check(content.GetVisualDescendants().OfType<WrapPanel>().Any(row =>
                        row.Children.OfType<TextBlock>().Any(t => t.Text == "#123456") && row.Children.OfType<TextBlock>().Any(t => t.IsVisible && t.Text == Core.Describe("#123456").Text("name"))),
                    "The sampled pixel is shown with its name");
                await PasteImage();
                SelfTests.Check(sampledPixel == null && sampledColor == null, "New images clear the previous pixel selection");
                SelfTests.Check(recentImage.Contains("#123456"), "Repeated image paste remains readable");
                await Copy("ordinary text"); var before = preview;
                await PasteImage();
                SelfTests.Check(preview == before, "Unsupported clipboard content preserves the current image");
                using var transfer = new DataTransfer(); transfer.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
                content.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, content, new Point(10, 10), KeyModifiers.None));
                for (int i = 0; analyzing && i < 100; i++) await Task.Delay(20);
                SelfTests.Check(!analyzing && preview != before && recentImage.Contains("#123456"), "Dropped pixels import without a file dialog");
            }
            using (var pixels = new SkiaSharp.SKBitmap(20, 20))
            {
                pixels.Erase(new SkiaSharp.SKColor(171, 205, 239));
                using var png = pixels.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                var filePath = Path.Combine(Workspace.Folder, "drop-test.png"); File.WriteAllBytes(filePath, png.ToArray());
                var file = (await StorageProvider.TryGetFileFromPathAsync(new Uri(filePath)))!;
                await Clipboard!.SetFileAsync(file); await PasteImage();
                SelfTests.Check(recentImage.Contains("#ABCDEF"), "Copied image files import from the clipboard");
                var before = preview;
                using var transfer = new DataTransfer(); transfer.Add(DataTransferItem.Create(DataFormat.File, file));
                content.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, content, new Point(10, 10), KeyModifiers.None));
                for (int i = 0; analyzing && i < 100; i++) await Task.Delay(20);
                SelfTests.Check(preview != before && recentImage.Contains("#ABCDEF"), "Dropped image files import through the same pipeline");
                before = preview;
                var gate = new TaskCompletionSource<Stream>(); var canceled = ImportImage(() => gate.Task, "canceled.png");
                analysisCancellation!.Cancel(); gate.SetResult(new MemoryStream(png.ToArray())); await canceled;
                SelfTests.Check(preview == before && !analyzing, "Canceled analysis preserves the previous result and releases the input gate");
                File.Delete(filePath);
            }
            await GuideScenario();
            if (CustomChrome)
            {
                SelfTests.Check(SystemDecorations == SystemDecorations.None && ResizeEdge(new Point(2, 2)) == WindowEdge.NorthWest && ResizeEdge(new Point(100, 100)) == null, "Custom chrome preserves corner resize targets");
                if (Environment.GetEnvironmentVariable("PICKERHUB_TEST_WM") == "1")
                {
                    maximizeWindow!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    SelfTests.Check(WindowState == WindowState.Maximized && maximizeWindow.IsVisible && maximizeWindow.Tag as string == "restore" && minimizeWindow!.IsVisible && ResizeEdge(new Point(2, 2)) == null, "Maximized chrome offers restore beside minimize and disables resize grips");
                    await Capture(L.Language + "-maximized");
                    // A real click on the restore action returns to the normal size; it must never minimize.
                    var restorePoint = maximizeWindow.PointToScreen(new Point(maximizeWindow.Bounds.Width / 2, maximizeWindow.Bounds.Height / 2));
                    Console.WriteLine($"CLICK {restorePoint.X} {restorePoint.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(300);
                    SelfTests.Check(WindowState == WindowState.Normal && maximizeWindow.Tag as string == "square", $"Restore returns the window to its normal size: {WindowState}");
                    maximizeWindow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    var titlePoint = this.PointToScreen(new Point(Bounds.Width / 2, 28));
                    Console.WriteLine($"DOUBLECLICK {titlePoint.X} {titlePoint.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(300);
                    SelfTests.Check(WindowState == WindowState.Normal && maximizeWindow.IsVisible, "Titlebar double-click restores the window");
                    minimizeWindow!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
                    SelfTests.Check(WindowState == WindowState.Minimized, "Minimize works with a window manager");
                    BringForward(); await Task.Delay(200);
                    var oldPosition = Position;
                    var grab = this.PointToScreen(new Point(Width / 2, 25));
                    Console.WriteLine($"DRAG {grab.X} {grab.Y} {grab.X + 45} {grab.Y + 35}"); await Task.Run(Console.ReadLine); await Task.Delay(150);
                    SelfTests.Check(Position != oldPosition, "Custom titlebar drags the actual desktop window");
                    double oldWidth = Bounds.Width, oldHeight = Bounds.Height;
                    var edge = this.PointToScreen(new Point(oldWidth - 2, oldHeight - 2));
                    Console.WriteLine($"DRAG {edge.X} {edge.Y} {edge.X + 40} {edge.Y + 30}"); await Task.Run(Console.ReadLine); await Task.Delay(150);
                    SelfTests.Check(Bounds.Width > oldWidth + 20 && Bounds.Height > oldHeight + 15, "Custom resize corner resizes the actual desktop window");
                    Width = 1120; Height = 820; Position = new PixelPoint(100, 40); await Task.Delay(150);
                }
            }
            SelfTests.Check(Icon != null && tray?.Icon != null, "Window and tray have application icons");
            state.ReduceMotion = false; Render(); spine.Collapse(); await Task.Delay(550);
            var rail = Locate(spine); var enter = overlay.PointToScreen(new Point(rail.Right - 25, rail.Top + 40));
            Console.WriteLine($"HOVER {enter.X} {enter.Y}"); await Task.Run(Console.ReadLine);
            SelfTests.Check(spine.IsExpanded && spine.Bounds.Width > Spine.Closed && spine.Bounds.Width < Spine.Open, "Sidebar expands progressively rather than snapping");
            await Task.Delay(500);
            SelfTests.Check(Math.Abs(spine.Bounds.Width - Spine.Open) < 1, "Sidebar settles at its open width");
            await Capture(L.Language + "-sidebar");
            var leave = this.PointToScreen(new Point(Width / 2, 28));
            Console.WriteLine($"HOVER {leave.X} {leave.Y}"); await Task.Run(Console.ReadLine);
            SelfTests.Check(spine.Bounds.Width > Spine.Closed + 40, "Sidebar does not snap closed on pointer exit");
            // Re-enter during the close delay, then leave again.
            Console.WriteLine($"HOVER {enter.X} {enter.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(250);
            SelfTests.Check(spine.IsExpanded, "Re-entry cancels a pending close");
            Console.WriteLine($"HOVER {leave.X} {leave.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(700);
            SelfTests.Check(Math.Abs(spine.Bounds.Width - Spine.Closed) < 1, "Sidebar finishes closing");
            spine.Motion = false; spine.Collapse(); await Task.Delay(30);
            SelfTests.Check(spine.Width == Spine.Closed, "Reduced motion settles immediately");
            ShowError("First error"); ShowError("Second error");
            SelfTests.Check(errorBanner.Child!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Second error"), "Repeated error banners replace safely");
            errorBanner.IsVisible = false;
            state.ReduceMotion = true; state.Palettes[0].Name = "QA palette";
            for (int i = 0; i < 2048; i++) state.Palettes[0].Colors.Add(new Swatch { Name = "Sample " + i, Hex = "#" + ((i * 7919) & 0xffffff).ToString("X6"), Favorite = i % 5 == 0 });
            state.History = ["#87CEEB", "#112233", "#E97522"]; state.Save(); lastSaved = Serialize(state);
            // Today's pack is still sealed: record the envelope in both themes before it is opened.
            foreach (var theme in new[] { "dark", "light" })
            {
                state.Theme = theme; page = DailyTool; Render(); await Task.Delay(250);
                SelfTests.Check(content.GetVisualDescendants().OfType<PackStage>().Any(s => !s.HasCards), "The daily page opens on a sealed envelope");
                await Capture(L.Language + "-envelope-" + theme);
            }
            state.Theme = "dark";
            Core.Daily("reveal");
            page = 0; Render(); await Task.Delay(200);
            var spectrum = this.GetVisualDescendants().OfType<Spectrum>().Single(); spectrum.Focus();
            Select("#123456", false);
            SelfTests.Check(this.GetVisualDescendants().OfType<Spectrum>().Single() == spectrum && spectrum.IsFocused, "Editing must preserve spectrum and focus");
            SelfTests.Check(!Diagnostics().Contains("#123456") && !Diagnostics().Contains(Workspace.Folder), "Support report privacy");
            foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                page = index; Render(); await Task.Delay(200);
                if (page == 1) SelfTests.Check(content.GetVisualDescendants().OfType<CheckBox>().Count() <= LibraryOps.PageSize + 2, "Library UI bounded at 2048 colors");
                await Capture(L.Language + "-page-" + page);
            }
            // Each language reads from its own side: the specimen's name and the first daily card sit where reading starts.
            {
                bool english = L.Language == "en";
                page = 0; Render(); await Task.Delay(150);
                var hex = content.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == state.Color);
                var named = content.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Core.Describe(state.Color).Text("name"));
                SelfTests.Check(Locate(named).X < Locate(hex).X == english, "The specimen's name sits where reading starts");
                page = DailyTool; Render(); await Task.Delay(300);
                var cards = content.GetVisualDescendants().OfType<SwatchCard>().ToList();
                SelfTests.Check(cards.Count == 7 && Locate(cards[0]).X < Locate(cards[^1]).X == english, "The first daily role sits where reading starts");
                var role = cards[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == cards[0].Spec.Label);
                double lead = english ? Locate(role).X - Locate(cards[0]).X : Locate(cards[0]).Right - Locate(role).Right;
                SelfTests.Check(lead < 24, $"Daily card labels align to the reading start: {lead:0}");
                if (english) SelfTests.Check(!content.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && (t.Text ?? "").Any(c => c is >= '؀' and <= 'ۿ')), "English daily page shows no Arabic text");
            }
            // Switching language while the app runs turns every reading surface with it: the heading starts at
            // the right in Arabic and at the left in English, and the toast rises from the matching corner.
            string startLanguage = L.Language;
            foreach (var language in new[] { startLanguage == "ar" ? "en" : "ar", startLanguage })
            {
                state.Language = language; ApplyLanguage(); page = 0; Render(); await Task.Delay(220);
                bool arabic = language == "ar";
                var area = Locate((Control)content.Parent!); var title = Locate(pageTitle);
                double gap = arabic ? area.Right - title.Right : title.X - area.X;
                SelfTests.Check(gap >= 0 && gap < 40, $"After switching to {language}, the heading starts at its reading edge: {gap:0}");
                status.Text = L.T("نُسخت القيمة إلى الحافظة"); await Task.Delay(120);
                var note = Locate(toast!);
                SelfTests.Check(toast!.IsVisible && (arabic ? note.Right > ClientSize.Width * .6 : note.X < ClientSize.Width * .4) && note.Bottom > ClientSize.Height * .8, $"The {language} toast rises from its reading corner");
                await Capture(language + "-toast-after-switch-" + startLanguage);
                HideToast(); await Task.Delay(60);
            }
            // A toast leaves on its own after a few seconds, and the same news can be told again.
            status.Text = L.T("نُسخت القيمة إلى الحافظة"); await Task.Delay(120);
            SelfTests.Check(toast!.IsVisible, "A status message rises as a toast");
            await Task.Delay(3900);
            SelfTests.Check(!toast.IsVisible && status.Text == Idle, "The toast dismisses itself and rests the status line");
            status.Text = L.T("نُسخت القيمة إلى الحافظة"); await Task.Delay(120);
            SelfTests.Check(toast.IsVisible, "The same message can be told again");
            HideToast();
            page = 5; state.ReduceMotion = false; Render(); await Task.Delay(100);
            var deck = content.GetVisualDescendants().OfType<Deck>().Single();
            var oldCard = historyCard; deck.Flip(); deck.Next(); await Task.Delay(280);
            SelfTests.Check(historyCard == (oldCard + 1) % state.History.Count, "Navigation interrupts a flip without dropping the action");
            for (int i = 0; i < 20; i++) deck.Next();
            await Task.Delay(260);
            SelfTests.Check(deck.Children.Count <= 5, "Rapid card navigation leaves a bounded visual tree");
            state.ReduceMotion = true;
            foreach (var size in new[] { new Size(1120, 700), new Size(1360, 860), new Size(1800, 1040), new Size(680, 600) })
            {
                Width = size.Width; Height = size.Height; Position = new PixelPoint(20, 20); page = 1; Render(); await Task.Delay(200);
                var scroller = (ScrollViewer)content.Parent!; scroller.Offset = default; await Task.Delay(80);
                var point = scroller.PointToScreen(new Point(scroller.Bounds.Width * .6, scroller.Bounds.Height * .7));
                Console.WriteLine($"SCROLL {point.X} {point.Y} 140"); await Task.Run(Console.ReadLine); await Task.Delay(100);
                Console.WriteLine($"TIMING viewport-{size.Width} extent={scroller.Extent.Height:0} viewport={scroller.Viewport.Height:0} offset={scroller.Offset.Y:0}");
                SelfTests.Check(scroller.Extent.Height > scroller.Viewport.Height && scroller.Offset.Y > 100, "Wheel input scrolls the actual library viewport");
                SelfTests.Check(Math.Abs(scroller.Offset.Y + scroller.Viewport.Height - scroller.Extent.Height) < 3, "Library bottom is reachable by scrolling");
                var lastColor = content.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "Sample " + (LibraryOps.PageSize - 1)));
                var bottom = lastColor.TranslatePoint(new Point(0, lastColor.Bounds.Height), this)!.Value.Y;
                SelfTests.Check(bottom <= ClientSize.Height - 12, $"The last color is visible: {bottom} / {ClientSize.Height}");
                await Capture(L.Language + "-scroll-" + size.Width);
                var thumb = scroller.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single(t => t.Name == "scrollThumb" && t.IsEffectivelyVisible);
                var thumbRect = Locate(thumb);
                var from = overlay.PointToScreen(thumbRect.Center);
                var to = overlay.PointToScreen(new Point(thumbRect.Center.X, Locate(scroller).Y + thumb.Bounds.Height / 2 - 12));
                Console.WriteLine($"DRAG {from.X} {from.Y} {from.X} {to.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(100);
                SelfTests.Check(scroller.Offset.Y < 12, $"Dragging the custom scrollbar returns to the top: {scroller.Offset.Y:0}");
            }
            // From the smallest window up, every page fits its width and the top bar keeps every action whole.
            foreach (var size in new[] { new Size(560, 480), new Size(820, 620), new Size(1120, 820), new Size(1680, 1000) })
            {
                Width = size.Width; Height = size.Height; Position = new PixelPoint(20, 20); await Task.Delay(150);
                var bar = TopBarCollisions();
                SelfTests.Check(bar.Length == 0, $"Top bar fits a {size.Width} px window: {string.Join(", ", bar)}");
                var rows = spine.GetVisualDescendants().OfType<Button>().Select(Locate).ToArray();
                SelfTests.Check(rows.Length >= 9 && rows.All(r => r.Top >= 0 && r.Bottom <= ClientSize.Height - 4 && r.Height >= 30), $"Navigation keeps every tool on screen at {size.Height} px");
                foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    page = index; Render(); await Task.Delay(160);
                    var clipped = Overflowing((ScrollViewer)content.Parent!);
                    SelfTests.Check(clipped.Length == 0, $"Page {index} fits a {size.Width} px window: {string.Join(", ", clipped)}");
                    if (size.Width == 560 && index is 0 or 1)
                    {
                        string[] visibleIds = index == 0 ? ["hex-input", "add-editor-color"] : ["palette-search", "palette-next-page"];
                        foreach (var id in visibleIds)
                        {
                            var control = content.GetVisualDescendants().OfType<Control>().Single(c => AutomationProperties.GetAutomationId(c) == id);
                            double end = Locate(control).Bottom;
                            SelfTests.Check(end <= overlay.Bounds.Height, $"{id} is visible without scrolling at the minimum size ({end:0}/{overlay.Bounds.Height:0})");
                        }
                        if (index == 1)
                        {
                            var swatch = content.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "first-palette-swatch");
                            SelfTests.Check(Locate(swatch).Y + 20 <= overlay.Bounds.Height, "The first palette swatch is visible at the minimum size");
                        }
                    }
                    if (size.Width is 560 or 1680) await Capture(L.Language + "-fit-" + size.Width + "-" + index);
                }
            }
            Width = 1120; Height = 820; Position = new PixelPoint(100, 40);
            foreach (var theme in new[] { "dark", "light" })
            {
                page = 1; state.Theme = theme; state.ReduceTransparency = false; Render(); ((ScrollViewer)content.Parent!).Offset = default; await Task.Delay(150);
                content.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content is string name && name == "Sample 0").IsChecked = true;
                var action = content.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("goo") && b.IsEffectivelyEnabled);
                var position = overlay.PointToScreen(Locate(action).Center);
                Console.WriteLine($"HOVER {position.X} {position.Y}"); await Task.Run(Console.ReadLine); await Task.Delay(150);
                SelfTests.Check(action.IsPointerOver, "Real pointer reaches the liquid action");
                await Capture(L.Language + "-liquid-" + theme);
                var select = content.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("select"));
                select.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                var menu = overlay.GetVisualDescendants().OfType<Glass>().Last();
                SelfTests.Check(menu.Bounds.Right <= overlay.Bounds.Width && menu.Bounds.Bottom <= overlay.Bounds.Height, "Glass menu stays within the window");
                await Capture(L.Language + "-menu-" + theme);
                overlay.Children.Last().RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Escape });
            }
            page = 1; state.Theme = "light"; state.ReduceTransparency = true; Width = 680; Height = 600; Render(); await Task.Delay(200);
            await Capture(L.Language + "-compact");
            var search = FindInProjects();
            await Task.Delay(150);
            var searchInput = overlay.GetVisualDescendants().OfType<TextBox>().Last();
            searchInput.Text = "Sample 1001";
            searchInput.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
            await search;
            SelfTests.Check(libraryQuery == Current.Colors[1001].Hex && !OverlayOpen, "Enter opens the selected project search result in its palette");
            libraryQuery = ""; Render();
            SupportLog.Enabled = true;
            try
            {
                var paletteSearch = content.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "palette-search");
                paletteSearch.Text = "Sample 1";
                await Task.Delay(260);
                var duration = SupportLog.Snapshot().LastOrDefault(e => e.Operation == "palette-search-to-frame")?.Milliseconds;
                SelfTests.Check(duration.HasValue, "Opt-in diagnostics record a palette search frame marker");
                Console.WriteLine($"TIMING palette-search-to-frame {duration:0.0}ms");
            }
            finally { SupportLog.Enabled = state.DiagnosticsEnabled; }
            libraryQuery = ""; Render();
            var firstPalette = Current;
            var exchange = new Palette { Name = "Import preview", Colors = [new() { Name = "Imported color", Hex = "#123456" }] };
            var importPreview = PreviewPaletteImport(exchange, firstPalette);
            await Task.Delay(160);
            var createImport = overlay.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, L.T("إنشاء لوحة جديدة")));
            createImport.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await importPreview;
            SelfTests.Check(Current == exchange && exchange.Colors.Count == 1, "Import preview creates a new palette");
            var mergeSource = new Palette { Name = "Merge preview", Colors = [new() { Name = "Duplicate", Hex = "#123456" }, new() { Name = "New color", Hex = "#112233" }] };
            importPreview = PreviewPaletteImport(mergeSource, exchange);
            await Task.Delay(160);
            var mergeImport = overlay.GetVisualDescendants().OfType<Button>().Last(b => Equals(b.Content, L.T("دمج في اللوحة الحالية")));
            mergeImport.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await importPreview;
            SelfTests.Check(exchange.Colors.Select(c => c.Hex).SequenceEqual(new[] { "#123456", "#112233" }), "Import preview merges only new hex values");
            await writer.FlushAsync();
            SelfTests.Check(Workspace.Load().Palettes.Last().Colors.Count == 2, "Merged palette persists");
            var colorActions = ColorActions(exchange.Colors[0].Hex);
            await Task.Delay(120);
            overlay.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "use-as-text")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await colorActions;
            SelfTests.Check(page == 2 && foreground == "#123456", "Color actions send a swatch to the contrast text input");
            SetContrastColor(exchange.Colors[1].Hex, asText: false);
            Edit(() => exchange.Roles["primary"] = "#87CEEB", false);
            var saveRoles = SaveContrastRoles();
            await Task.Delay(150);
            overlay.GetVisualDescendants().OfType<Button>().Last(b => Equals(b.Content, L.T("اختيار")))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await saveRoles;
            await writer.FlushAsync();
            var persistedRoles = Workspace.Load().Palettes.Last().Roles;
            SelfTests.Check(persistedRoles["text"] == "#123456" && persistedRoles["background"] == "#112233" &&
                persistedRoles["primary"] == "#87CEEB", "Saving a contrast pair persists both roles and preserves other roles");
            Edit(() => { state.Palettes.Remove(exchange); paletteId = firstPalette.Id; });
            page = 1; Render();
            foreach (var (name, open) in new (string, Func<Task>)[] { ("choose", async () => { await SelectOption(L.T("إضافة إلى لوحة"), ["QA palette", "Another palette"]); }), ("roles", () => Roles(Current)), ("contrast", () => PaletteContrast(Current)), ("archive", DailyArchive) })
            {
                var shown = open(); await Task.Delay(200);
                await Capture(L.Language + "-" + name);
                overlay.Children.Last().RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
                await shown;
            }
            // Change settings through the actual controls, in this process.
            var settings = Settings(); await Task.Delay(100);
            overlay.Children.Last().RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.F8 });
            SelfTests.Check(!picking && OverlayOpen, "F8 cannot start a desktop pick from a modal");
            Width = 760; Height = 480; await Task.Delay(150);
            var settingsPane = overlay.GetVisualDescendants().OfType<Glass>().First();
            SelfTests.Check(settingsPane.Bounds.Bottom <= overlay.Bounds.Height && settingsPane.Bounds.Right <= overlay.Bounds.Width, "Open settings reflow inside the resized viewport");
            var auto = overlay.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, L.T("نسخ اللون تلقائياً بعد الالتقاط")));
            auto.IsChecked = false;
            SelfTests.Check(!state.AutoCopy, "Quick Copy applies in the open session");
            var upper = overlay.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, L.T("أحرف HEX كبيرة")));
            var prefix = overlay.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, L.T("تضمين #")));
            upper.IsChecked = false; prefix.IsChecked = false;
            SelfTests.Check(PickValue(state, "#ABCDEF") == "abcdef", "HEX settings apply without restart");
            var format = overlay.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("select") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "HEX"));
            format.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            var rgb = overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("option") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "RGB"));
            rgb.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            SelfTests.Check(state.CopyFormat == "RGB" && PickValue(state, "#ABCDEF").StartsWith("rgb("), "Copy format applies from Settings immediately");
            overlay.Children.Last().RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            await settings;
            SelfTests.Check(!Picker.UsesPortal, $"X11 UI test keeps its capture backend: session={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")}, display={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")}");
            await Copy("keep this clipboard");
            Console.WriteLine("SINGLEPICK"); await PickDesktop();
            SelfTests.Check(await Clipboard!.TryGetTextAsync() == "keep this clipboard", "Disabled Quick Copy preserves the clipboard immediately");
            Edit(() => state.AutoCopy = true, false);
            Console.WriteLine("SINGLEPICK"); await PickDesktop();
            SelfTests.Check(await Clipboard!.TryGetTextAsync() == PickValue(state, state.Color), "Enabled Quick Copy uses RGB on the next pick without restart");
            await pickCopies;
            SelfTests.Check(status.Text == string.Format(L.T("نُسخ {0} بصيغة {1}"), Isolate(state.Color), Isolate("RGB")),
                "Copy confirmation names the saved color and requested clipboard format");
            page = 2; Render();
            Edit(() => state.AutoCopy = false, false);
            await Copy("replace this clipboard");
            Console.WriteLine("SINGLEPICK"); await PickDesktop(PickAction.Copy);
            await pickCopies;
            SelfTests.Check(IsVisible && page == 2 && await Clipboard!.TryGetTextAsync() == PickValue(state, state.Color),
                "Pick and copy copies even with automatic copying disabled and keeps the current tool");
            Edit(() => state.AutoCopy = true, false);
            await Copy("preserve this clipboard");
            Console.WriteLine("SINGLEPICK"); await PickDesktop(PickAction.Edit);
            SelfTests.Check(IsVisible && page == 0 && FocusManager?.GetFocusedElement() is TextBox editInput &&
                AutomationProperties.GetAutomationId(editInput) == "hex-input" && Math.Abs(editInput.SelectionEnd - editInput.SelectionStart) == state.Color.Length &&
                await Clipboard!.TryGetTextAsync() == "preserve this clipboard", "Pick and edit focuses and selects HEX without copying");
            var clipboardRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pickClipboard = async value => { await clipboardRelease.Task; await Copy(value); };
            var returnTime = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("SINGLEPICK"); await PickDesktop();
            SelfTests.Check(copyingPick && !clipboardRelease.Task.IsCompleted && IsVisible && !picking,
                "Slow clipboard ownership never delays restoring the workbench");
            SelfTests.Check(returnTime.ElapsedMilliseconds < 1500, "Picker returns before clipboard acknowledgement timeout");
            clipboardRelease.SetResult(); await pickCopies; pickClipboard = null;
            page = 2; Render();
            Console.WriteLine("FOCUS_REFERENCE"); await Task.Run(Console.ReadLine); await Task.Delay(100);
            Console.WriteLine("SINGLEPICK"); await PickDesktop(PickAction.Quick);
            await pickCopies;
            SelfTests.Check(!IsVisible && page == 2 && status.Text == Idle && await Clipboard!.TryGetTextAsync() == PickValue(state, state.Color),
                "Quick Pick copies silently, preserves the current tool, and leaves PickerHub in the tray");
            Console.WriteLine("CHECK_REFERENCE_FOCUS"); await Task.Run(Console.ReadLine);
            BringForward();
            var decision = Confirm("Capture decision");
            await Task.Delay(130);
            Hide();
            await PickDesktop(PickAction.Quick, revealDecision: true);
            await Task.Delay(100);
            var decisionLayer = overlay.Children.Last();
            SelfTests.Check(!picking && IsVisible && FocusManager?.GetFocusedElement() is Visual focusedDecision &&
                decisionLayer.IsVisualAncestorOf(focusedDecision), "A tray pick brings forward an existing decision and keeps focus inside it");
            decisionLayer.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Escape });
            await decision;
            await writer.FlushAsync();
            var time = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 12; i++) { Travel(false); Travel(true); }
            Console.WriteLine($"TIMING undo-redo-24 {time.ElapsedMilliseconds}ms");
            await writer.FlushAsync();
            SelfTests.Check(Workspace.Load().CopyFormat == state.CopyFormat, "Last undo/redo persisted in order");
            state.ReduceMotion = false; state.ReduceTransparency = false; Render();
            // Repeated mounting/unmounting exercises render-thread surface disposal.
            for (int i = 0; i < 35; i++) { page = i % 3; Render(); await Task.Delay(20); }
            var target = new Palette { Name = "Multi pick" }; state.Palettes.Add(target); paletteId = target.Id;
            Console.WriteLine("MULTIPICK"); await PickDesktop(PickAction.Collect);
            SelfTests.Check(target.Colors.Count == 8 && !picking && IsVisible, "Eight stationary multi picks and Escape return the window");
            SelfTests.Check(target.Colors.Select(c => c.Hex).SequenceEqual(new[] { "#112233", "#223344", "#334455", "#445566", "#556677", "#667788", "#778899", "#8899AA" }), "Each stationary click samples the latest source frame");
            await Task.Delay(700);
            SelfTests.Check(spine.Width == Spine.Closed, "Picker return leaves navigation collapsed");
            var copied = await Clipboard!.TryGetTextAsync();
            SelfTests.Check(copied == PickValue(state, target.Colors[^1].Hex), $"Multi pick uses the current copy format: {copied ?? "null"} expected {PickValue(state, target.Colors[^1].Hex)}");
            SelfTests.Check(status.Text.Contains(L.T("الألوان الملتقطة: ") + 8) && status.Text.Contains(Isolate(target.Colors[^1].Hex)),
                "Multi-pick confirmation preserves the count and identifies the last copied color");
            await writer.FlushAsync();
            SelfTests.Check(Workspace.Load().Palettes.Last().Colors.Count == 8, "All multi picks persisted");
            Travel(false); SelfTests.Check(Current.Colors.Count == 7, "One undo removes exactly one pick");
            Travel(true); SelfTests.Check(Current.Colors.Count == 8, "One redo restores exactly one pick");
            _ = Settings(); await Task.Delay(200);
            await Capture(L.Language + "-settings");
            Console.WriteLine("PASS: UI pages, editor focus, library bounds, diagnostics and render lifecycle");
        }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
        finally { if (closeWindow != null) closeWindow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else Close(); }
    }
}
