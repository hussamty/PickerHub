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
    readonly StackPanel content = new() { Spacing = 18, Margin = new Thickness(0, 0, 16, 0) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button eyedropper;
    readonly string[] pages = ["ملتقط الألوان", "لوحات الألوان", "فحص التباين", "تحليل الصور", "محرّر التدرجات", "السجل"];
    readonly string[] modes = ["متجاورة", "متكاملة", "ثلاثية", "متكاملة منقسمة", "أحادية اللون", "عالية التباين"];
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
    FileStream? instanceLock;
    bool dark => state.Theme == "dark";
    bool Motion => !state.ReduceMotion;
    static string Serialize<T>(T v) => JsonSerializer.Serialize(v, Workspace.Json);
    Palette Current => state.Palettes.FirstOrDefault(p => p.Id == paletteId) ?? state.Palettes[0];

    public MainWindow()
    {
        Title = "PickerHub"; Width = 1120; Height = 820; MinWidth = 640; MinHeight = 560;
        FlowDirection = FlowDirection.RightToLeft;
        FontFamily = new FontFamily("avares://PickerHub/Fonts#Noto Sans Arabic, Segoe UI, sans-serif");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            Directory.CreateDirectory(Workspace.Folder);
            instanceLock = new FileStream(Path.Combine(Workspace.Folder, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            state = Workspace.Load();
        }
        catch (Exception e) { damaged = true; status.Text = "تعذّر فتح المكتبة للتحرير. " + e.Message; }
        lastSaved = Serialize(state);
        eyedropper = Btn("color-picker", "القطّارة", PickDesktop, true);
        BuildShell();
        Closed += (_, _) => { if(Picker.UsesPortal)WaylandCapture.Stop();preview?.Dispose(); instanceLock?.Dispose(); };
        KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.F8 && !picking) { ev.Handled = true; _ = Guard(PickDesktop); } };
        Render();
    }
    async Task Guard(Func<Task> action) { try { await action(); } catch (Exception ex) { Console.Error.WriteLine(ex);status.Text = ex.Message; } }
    void Edit(Action mutation)
    {
        if (damaged) throw new InvalidOperationException("المكتبة للقراءة فقط. أغلق النسخة الأخرى أو استعد نسخة احتياطية صالحة.");
        try { mutation(); state.Save(); lastSaved = Serialize(state); } catch { state = Workspace.Parse(lastSaved); Render(); throw; }
        Render();
    }
    void Select(string h)
    {
        if (!Workspace.Hex(h)) throw new InvalidDataException("أدخل ست خانات HEX.");
        Edit(() => { state.Color = h.ToUpperInvariant(); state.History.RemoveAll(x => x.Equals(h, StringComparison.OrdinalIgnoreCase)); state.History.Insert(0, state.Color); state.History = state.History.Take(state.Limit).ToList(); });
    }
    async Task PickDesktop()
    {
        if (picking) return; picking = true; eyedropper.IsEnabled = false;
        status.Text = "انقر على البكسل المطلوب · Esc للإلغاء";
        var previousState = WindowState;
        var previousOpacity = Opacity;
        bool share = Picker.UsesPortal && state.UseWaylandScreenSharing;
        try
        {
            // GNOME authorizes only the focused application. Keep its parent window
            // mapped and focused, but transparent, until the portal has shown consent.
            if (share) { WaylandCapture.ClearPending(); Picker.SetPortalParentTransparent(this, true); }
            else Hide();
            await Task.Delay(180);
            var h = await Picker.Start(this, share);
            if (h != null) { page = 0; Select(h); status.Text = "تم التقاط " + h; } else status.Text = "أُلغي الالتقاط";
        }
        finally
        {
            picking = false; eyedropper.IsEnabled = true;
            try { if (share) Picker.SetPortalParentTransparent(this, false); }
            finally { Opacity = previousOpacity; Show(); WindowState = previousState; Activate(); }
        }
    }
    void Render()
    {
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; App.Accent(dark);
        Classes.Set("calm", !Motion);
        StyleShell();
        content.Children.Clear();
        if (page != shownPage) { shownPage = page; Arrive(); }
        switch (page) { case 0: ColorPage(); break; case 1: Palettes(); break; case 2: ContrastPage(); break; case 3: Images(); break; case 4: Gradients(); break; case 5: History(); break; }
    }
    async Task WriteFile(string name, string text)
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "حفظ الملف", SuggestedFileName = name, DefaultExtension = Path.GetExtension(name).TrimStart('.') });
        if (f == null) return; await using var s = await f.OpenWriteAsync(); if (s.CanSeek) s.SetLength(0); await using var w = new StreamWriter(s, new UTF8Encoding(false)); await w.WriteAsync(text); status.Text = "حُفظ الملف";
    }
    async Task<byte[]?> ReadFile(string title, string[] patterns, int limit)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }] }); if (files.Count == 0) return null;
        await using var s = await files[0].OpenReadAsync(); if (s.CanSeek && s.Length > limit) throw new InvalidDataException("الملف أكبر من الحد المسموح.");
        using var m = new MemoryStream(); var buf = new byte[65536]; int n; while ((n = await s.ReadAsync(buf)) > 0) { if (m.Length + n > limit) throw new InvalidDataException("الملف أكبر من الحد المسموح."); m.Write(buf, 0, n); }
        return m.ToArray();
    }
    async Task Restore()
    {
        if (instanceLock == null) throw new IOException("أغلق النسخة الأخرى قبل الاستعادة.");
        var bytes = await ReadFile("نسخة PickerHub", ["*.json"], 16 << 20); if (bytes == null) return; var restored = Workspace.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        if (!await Confirm("استبدال المكتبة الحالية بالنسخة المختارة؟")) return;
        restored.Save(); state = restored; lastSaved = Serialize(state); damaged = false; paletteId = null; Render(); status.Text = "استُعيدت المكتبة";
    }
    async Task AddToPalette(string[] colors)
    {
        int? index = state.Palettes.Count == 1 ? 0 : await SelectOption("إضافة إلى لوحة", state.Palettes.Select(p => p.Name).ToArray(), state.Palettes.IndexOf(Current)); if (index == null) return;
        Edit(() => state.Palettes[index.Value].Colors.AddRange(colors.Select(Swatch.From))); status.Text = "أُضيفت الألوان إلى اللوحة";
    }
}
