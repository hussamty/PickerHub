using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace PickerHub;
internal sealed partial class MainWindow
{
    internal async Task Smoke()
    {
        try
        {
            await Copy("PickerHub clipboard probe");
            SelfTests.Check(await Clipboard!.TryGetTextAsync() == "PickerHub clipboard probe", "Baseline clipboard round trip");
            ShowError("First error"); ShowError("Second error");
            SelfTests.Check(errorBanner.Child!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Second error"), "Repeated error banners replace safely");
            errorBanner.IsVisible = false;
            state.ReduceMotion = true; state.Palettes[0].Name = "QA palette";
            for (int i = 0; i < 2048; i++) state.Palettes[0].Colors.Add(new Swatch { Name = "Sample " + i, Hex = "#" + ((i * 7919) & 0xffffff).ToString("X6"), Favorite = i % 5 == 0 });
            state.History = ["#87CEEB", "#112233", "#E97522"]; state.Save(); lastSaved = Serialize(state);
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
                Console.WriteLine("FRAME " + L.Language + "-page-" + page); await Task.Run(Console.ReadLine);
            }
            page = 1; state.Theme = "light"; state.ReduceTransparency = true; Width = 680; Height = 600; Render(); await Task.Delay(200);
            Console.WriteLine("FRAME " + L.Language + "-compact"); await Task.Run(Console.ReadLine);
            foreach (var (name, open) in new (string, Func<Task>)[] { ("choose", async () => { await SelectOption(L.T("إضافة إلى لوحة"), ["QA palette", "Another palette"]); }), ("roles", () => Roles(Current)), ("contrast", () => PaletteContrast(Current)), ("archive", DailyArchive) })
            {
                var shown = open(); await Task.Delay(200);
                Console.WriteLine("FRAME " + L.Language + "-" + name); await Task.Run(Console.ReadLine);
                overlay.Children.Last().RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
                await shown;
            }
            // Change settings through the actual controls, in this process.
            var settings = Settings(); await Task.Delay(100);
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
            await Task.Delay(250);
            SelfTests.Check(spine.Width == Spine.Closed, "Picker return leaves navigation collapsed");
            var copied = await Clipboard!.TryGetTextAsync();
            SelfTests.Check(copied == PickValue(state, target.Colors[^1].Hex), $"Multi pick uses the current copy format: {copied ?? "null"} expected {PickValue(state, target.Colors[^1].Hex)}");
            await writer.FlushAsync();
            SelfTests.Check(Workspace.Load().Palettes.Last().Colors.Count == 8, "All multi picks persisted");
            Travel(false); SelfTests.Check(Current.Colors.Count == 7, "One undo removes exactly one pick");
            Travel(true); SelfTests.Check(Current.Colors.Count == 8, "One redo restores exactly one pick");
            _ = Settings(); await Task.Delay(200);
            Console.WriteLine("FRAME " + L.Language + "-settings"); await Task.Run(Console.ReadLine);
            Console.WriteLine("PASS: UI pages, editor focus, library bounds, diagnostics and render lifecycle");
        }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
