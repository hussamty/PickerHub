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
        var parts = new Control?[] { eyedropper, undoButton, redoButton, themeSwitch, minimizeWindow, maximizeWindow, closeWindow }
            .OfType<Control>().Where(c => c.IsEffectivelyVisible).Select(c => (c, r: Locate(c))).ToArray();
        var found = parts.Where(p => p.r.X < 0 || p.r.Right > ClientSize.Width).Select(p => (AutomationProperties.GetName(p.c) ?? p.c.GetType().Name) + " leaves the window").ToList();
        for (int i = 0; i < parts.Length; i++)
            for (int j = i + 1; j < parts.Length; j++)
                if (parts[i].r.Intersects(parts[j].r)) found.Add((AutomationProperties.GetName(parts[i].c) ?? "?") + " overlaps " + (AutomationProperties.GetName(parts[j].c) ?? "?"));
        return found.ToArray();
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
                await PasteImage();
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
                var bottomAction = content.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, L.T("إلغاء التحديد")));
                var bottom = bottomAction.TranslatePoint(new Point(0, bottomAction.Bounds.Height), this)!.Value.Y;
                SelfTests.Check(bottom <= ClientSize.Height - 12, $"Bottom actions are actually visible: {bottom} / {ClientSize.Height}");
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
                    if (size.Width is 560 or 1680) await Capture(L.Language + "-fit-" + size.Width + "-" + index);
                }
            }
            Width = 1120; Height = 820; Position = new PixelPoint(100, 40);
            foreach (var theme in new[] { "dark", "light" })
            {
                page = 1; state.Theme = theme; state.ReduceTransparency = false; Render(); ((ScrollViewer)content.Parent!).Offset = default; await Task.Delay(150);
                content.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content is string name && name == "Sample 0").IsChecked = true;
                var action = content.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("goo") && b.IsEffectivelyEnabled);
                var position = action.PointToScreen(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2));
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
            foreach (var (name, open) in new (string, Func<Task>)[] { ("choose", async () => { await SelectOption(L.T("إضافة إلى لوحة"), ["QA palette", "Another palette"]); }), ("roles", () => Roles(Current)), ("contrast", () => PaletteContrast(Current)), ("archive", DailyArchive) })
            {
                var shown = open(); await Task.Delay(200);
                await Capture(L.Language + "-" + name);
                overlay.Children.Last().RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
                await shown;
            }
            // Change settings through the actual controls, in this process.
            var settings = Settings(); await Task.Delay(100);
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
            var clipboardRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pickClipboard = async value => { await clipboardRelease.Task; await Copy(value); };
            var returnTime = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("SINGLEPICK"); await PickDesktop();
            SelfTests.Check(copyingPick && !clipboardRelease.Task.IsCompleted && IsVisible && !picking,
                "Slow clipboard ownership never delays restoring the workbench");
            SelfTests.Check(returnTime.ElapsedMilliseconds < 1500, "Picker returns before clipboard acknowledgement timeout");
            clipboardRelease.SetResult(); await pickCopies; pickClipboard = null;
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
            Console.WriteLine("MULTIPICK"); await PickDesktop(true, true);
            SelfTests.Check(target.Colors.Count == 8 && !picking && IsVisible, "Eight stationary multi picks and Escape return the window");
            SelfTests.Check(target.Colors.Select(c => c.Hex).SequenceEqual(new[] { "#112233", "#223344", "#334455", "#445566", "#556677", "#667788", "#778899", "#8899AA" }), "Each stationary click samples the latest source frame");
            await Task.Delay(700);
            SelfTests.Check(spine.Width == Spine.Closed, "Picker return leaves navigation collapsed");
            var copied = await Clipboard!.TryGetTextAsync();
            SelfTests.Check(copied == PickValue(state, target.Colors[^1].Hex), $"Multi pick uses the current copy format: {copied ?? "null"} expected {PickValue(state, target.Colors[^1].Hex)}");
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
