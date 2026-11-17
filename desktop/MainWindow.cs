using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using System.Text;
using System.Text.Json;

namespace PickerHub;

internal sealed partial class MainWindow : Window
{
    Workspace state = new();
    // The margin keeps pane rims and shadows clear of the scroll area's edges.
    readonly StackPanel content = new() { Spacing = 20, Margin = new Thickness(20, 10, 20, 40) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button eyedropper;
    readonly WorkspaceWriter writer = new();
    bool closeReady, flushing;
    string[] modes = [L.T("متجاورة"), L.T("متكاملة"), L.T("ثلاثية"), L.T("متكاملة منقسمة"), L.T("أحادية اللون"), L.T("عالية التباين")];
    int page, harmony, shownPage = -1, paletteCard, historyCard;
    string? paletteId;
    bool picking, damaged;
    string foreground = "#172D46", background = "#FFFFFF";
    Gradient gradient = new();
    string[] recentImage = [];
    double[] percentages = [];
    string imageName = "";
    Avalonia.Media.Imaging.Bitmap? preview;
    string lastSaved = "";
    bool dark => state.FollowSystemTheme ? Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant != Avalonia.Platform.PlatformThemeVariant.Light : state.Theme == "dark";
    bool Motion => !state.ReduceMotion;
    static string Serialize<T>(T v) => JsonSerializer.Serialize(v, Workspace.Json);
    Palette Current => state.Palettes.FirstOrDefault(p => p.Id == paletteId) ?? state.Palettes[0];

    public MainWindow()
    {
        Title = "PickerHub";
        using (var mascot = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PickerHub/Assets/icon-256.png"))) Icon = new WindowIcon(mascot);
        // The smallest window still shows every tool in the rail at a comfortable row height.
        Width = 1120; Height = 820; MinWidth = 560; MinHeight = 480;
        FlowDirection = L.Flow;
        FontFamily = Typography.Face;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            Directory.CreateDirectory(Workspace.Folder);
            BackupBundle.RecoverInterrupted();
            state = Workspace.Load();
        }
        catch (Exception e) { damaged = true; status.Text = L.T("تعذّر فتح المكتبة للتحرير. ") + e.Message; }
        L.Language = state.Language; FlowDirection = L.Flow; SupportLog.Enabled = state.DiagnosticsEnabled;
        lastSaved = Serialize(state);
        eyedropper = Btn("color-picker", L.T("القطّارة"), () => PickDesktop(), true);
        var startupError = damaged ? status.Text : Workspace.RecoveryNotice;
        BuildShell(); SetupQuickActions(); SetupImageInput(); ApplyLanguage();
        if (App.Preview) { page = DailyTool; Title = "PickerHub · " + L.T("معاينة ظرف باقة اليوم"); }
        bool fitted = false;
        Opened += (_, _) =>
        {
            if (fitted) return; fitted = true;
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            double availableWidth = Math.Max(320, screen.WorkingArea.Width / screen.Scaling - 32);
            double availableHeight = Math.Max(280, screen.WorkingArea.Height / screen.Scaling - 32);
            MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Clamp(Width, MinWidth, availableWidth);
            Height = Math.Clamp(Height, MinHeight, availableHeight);
        };
        writer.Failed += ex => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!closed) ShowError(L.T("تعذّر حفظ التغييرات. ما زالت مفتوحة؛ أعد محاولة الحفظ."), async () => { writer.Retry(); await writer.FlushAsync(); });
            Console.Error.WriteLine(ex);
        });
        Closing += async (_, e) =>
        {
            if (closeReady || damaged) return;
            e.Cancel = true;
            if (flushing) return;
            flushing = true;
            try { await writer.FlushAsync(); closeReady = true; Avalonia.Threading.Dispatcher.UIThread.Post(Close); }
            catch (Exception ex) { ShowError(ex.Message, async () => { writer.Retry(); await writer.FlushAsync(); Close(); }); }
            finally { flushing = false; }
        };
        if (startupError != null) ShowError(startupError);
        Closed += (_, _) => { analysisCancellation?.Cancel(); if(Picker.UsesPortal)WaylandCapture.Stop();preview?.Dispose();  };
        KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.F8 && !picking) { ev.Handled = true; _ = Guard(() => PickDesktop()); } };
        Render();
    }
    async Task Guard(Func<Task> action) { try { await action(); } catch (Exception ex) { SupportLog.Record("action", ex); Console.Error.WriteLine(ex); if (!closed) ShowError(ex.Message); } }
    internal void BringForward()
    {
        if (closed || picking) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); eyedropper.Focus(); spine.Collapse();
    }
    void Edit(Action mutation, bool render = true)
    {
        if (damaged) throw new InvalidOperationException(L.T("المكتبة للقراءة فقط. أغلق النسخة الأخرى أو استعد نسخة احتياطية صالحة."));
        try { mutation(); var next = Serialize(state); if (next != lastSaved) { Workspace.Parse(next); writer.Enqueue(next); edits.Commit(lastSaved, next); lastSaved = next; } } catch { state = Workspace.Parse(lastSaved); Render(); throw; }
        if (render) Render();
    }
    void Select(string h, bool render = true)
    {
        if (!Workspace.Hex(h)) throw new InvalidDataException(L.T("أدخل ست خانات HEX."));
        Edit(() => { state.Color = h.ToUpperInvariant(); state.History.RemoveAll(x => x.Equals(h, StringComparison.OrdinalIgnoreCase)); state.History.Insert(0, state.Color); state.History = state.History.Take(state.Limit).ToList(); }, render);
        if (!render) refreshColor?.Invoke();
    }
    async Task PickDesktop(bool? copy = null, bool collect = false)
    {
        if (picking) return;
        if (collect && Picker.UsesPortal && !state.UseWaylandScreenSharing) throw new InvalidOperationException(L.T("الالتقاط المتعدد يحتاج العدسة الحية. فعّلها من الإعدادات."));
        picking = true; eyedropper.IsEnabled = false; spine.Collapse();
        status.Text = L.T("انقر على البكسل المطلوب · Esc للإلغاء");
        var previousState = WindowState;
        var previousOpacity = Opacity;
        bool share = Picker.UsesPortal && state.UseWaylandScreenSharing;
        int pickedCount = 0;
        try
        {
            // GNOME authorizes only the focused application. Keep its parent window
            // mapped and focused, but transparent, until the portal has shown consent.
            if (share) { Picker.SetPortalParentTransparent(this, true); }
            else Hide();
            await Task.Delay(60);
            void Accept(string h)
            {
                // One transaction per selection; no hidden page rebuild or lens restart.
                page = collect ? 1 : 0;
                Edit(() =>
                {
                    state.Color = h.ToUpperInvariant();
                    state.History.RemoveAll(x => x.Equals(h, StringComparison.OrdinalIgnoreCase));
                    state.History.Insert(0, state.Color);
                    state.History = state.History.Take(state.Limit).ToList();
                    if (collect) Current.Colors.Add(Swatch.From(h));
                }, false);
                pickedCount++;
                if (copy ?? state.AutoCopy) QueuePickCopy(PickValue(state, h));
                status.Text = L.T("تم التقاط ") + Isolate(h);
            }
            var selected = await Picker.Start(this, share, collect ? Accept : null);
            if (!collect && selected != null) Accept(selected);
            if (selected == null) status.Text = pickedCount == 0 ? L.T("أُلغي الالتقاط") : L.T("الألوان الملتقطة: ") + pickedCount;
            // Clipboard ownership is independent of capture. Never hold the hidden
            // main window or the next shortcut hostage to a clipboard manager.

        }
        catch (Exception ex) { Console.Error.WriteLine(ex); ShowError(ex.Message, () => PickDesktop(copy, collect)); }
        finally
        {
            picking = false; eyedropper.IsEnabled = true;
            try { if (share) Picker.SetPortalParentTransparent(this, false); }
            finally
            {
                if (!closed)
                {
                    Opacity = previousOpacity; if (content.Parent is ScrollViewer scroll) scroll.Offset = default; Render(); Show(); WindowState = previousState; Activate();
                    eyedropper.Focus(); spine.Collapse();
                }
            }
        }
    }
    void Render()
    {
        Glass.Dark = dark; Glass.ReduceTransparency = state.ReduceTransparency;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; App.Accent(dark);
        Classes.Set("calm", !Motion);
        StyleShell();
        if (undoButton != null) undoButton.IsEnabled = edits.CanUndo && !damaged;
        if (redoButton != null) redoButton.IsEnabled = edits.CanRedo && !damaged;
        refreshColor = null;
        content.Children.Clear();
        if (page != shownPage) { shownPage = page; if (content.Parent is ScrollViewer scroll) scroll.Offset = default; Arrive(); }
        tools[page].Build();
    }
    async Task WriteFile(string name, string text)
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = L.T("حفظ الملف"), SuggestedFileName = name, DefaultExtension = Path.GetExtension(name).TrimStart('.') });
        if (f == null) return; await using var s = await f.OpenWriteAsync(); if (s.CanSeek) s.SetLength(0); await using var w = new StreamWriter(s, new UTF8Encoding(false)); await w.WriteAsync(text); status.Text = L.T("حُفظ الملف");
    }
    async Task<byte[]?> ReadFile(string title, string[] patterns, int limit)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }] }); if (files.Count == 0) return null;
        await using var s = await files[0].OpenReadAsync(); if (s.CanSeek && s.Length > limit) throw new InvalidDataException(L.T("الملف أكبر من الحد المسموح."));
        using var m = new MemoryStream(); var buf = new byte[65536]; int n; while ((n = await s.ReadAsync(buf)) > 0) { if (m.Length + n > limit) throw new InvalidDataException(L.T("الملف أكبر من الحد المسموح.")); m.Write(buf, 0, n); }
        return m.ToArray();
    }
    async Task Restore()
    {
        if (App.Instance == null) throw new IOException(L.T("أغلق النسخة الأخرى قبل الاستعادة."));
        var bytes = await ReadFile(L.T("نسخة PickerHub"), ["*.json"], 16 << 20); if (bytes == null) return; var restored = BackupBundle.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        if (!await Confirm(L.T("استبدال المكتبة الحالية بالنسخة المختارة؟"))) return;
        await writer.FlushAsync();
        BackupBundle.Restore(restored); state = restored.Workspace; lastSaved = Serialize(state); damaged = false; edits.Clear(); errorBanner.IsVisible = false; paletteId = null; ApplyLanguage(); await ConfigureShortcut(); if (!state.UseWaylandScreenSharing && Picker.UsesPortal) WaylandCapture.Stop(); Render(); status.Text = L.T("استُعيدت المكتبة");
    }
    async Task AddToPalette(string[] colors)
    {
        int? index = state.Palettes.Count == 1 ? 0 : await SelectOption(L.T("إضافة إلى لوحة"), state.Palettes.Select(p => p.Name).ToArray(), state.Palettes.IndexOf(Current)); if (index == null) return;
        Edit(() => state.Palettes[index.Value].Colors.AddRange(colors.Select(Swatch.From))); status.Text = L.T("أُضيفت الألوان إلى اللوحة");
    }
}
