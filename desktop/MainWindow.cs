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
    readonly StackPanel content = new() { Spacing = 15 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button eyedropper;
    readonly WrapPanel nav = new();
    readonly string[] pages = ["ملتقط الألوان", "لوحات الألوان", "فحص التباين", "تحليل الصور", "محرّر التدرجات", "السجل"];
    readonly string[] modes = ["متجاورة", "متكاملة", "ثلاثية", "متكاملة منقسمة", "أحادية اللون", "عالية التباين"];
    int page, harmony;
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
    IBrush Ink => Paint(dark ? "#F2F6FC" : "#172C40");
    IBrush Paper => Paint(dark ? "#1D2938" : "#FFFFFF");
    IBrush Line => Paint(dark ? "#3A4B5C" : "#DCE4ED");
    static IBrush Paint(string hex) => new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    static string Serialize<T>(T v) => JsonSerializer.Serialize(v, Workspace.Json);
    Palette Current => state.Palettes.FirstOrDefault(p => p.Id == paletteId) ?? state.Palettes[0];

    public MainWindow()
    {
        Title = "PickerHub"; Width = 970; Height = 760; MinWidth = 450; MinHeight = 480;
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
        var frame = new DockPanel { Margin = new Thickness(22, 14) };
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 12) };
        var brand = Text("PickerHub", 27, true); brand.FlowDirection = FlowDirection.LeftToRight; DockPanel.SetDock(brand, Dock.Right); header.Children.Add(brand);
        eyedropper = Btn("القطّارة", PickDesktop, true); DockPanel.SetDock(eyedropper, Dock.Left); header.Children.Add(eyedropper);
        DockPanel.SetDock(header, Dock.Top); frame.Children.Add(header);
        DockPanel.SetDock(nav, Dock.Top); frame.Children.Add(nav);
        var foot = Row(Btn("المظهر", () => Edit(() => state.Theme = dark ? "light" : "dark")), Btn("نسخة احتياطية", () => WriteFile("PickerHub-backup.json", Serialize(state))), Btn("استعادة", Restore));
        var footer = Col(foot, status); DockPanel.SetDock(footer, Dock.Bottom); frame.Children.Add(footer);
        frame.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = frame; Closed += (_, _) => { preview?.Dispose(); instanceLock?.Dispose(); };
        KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.F8 && !picking) { ev.Handled = true; _ = Guard(PickDesktop); } };
        Render();
    }
    static StackPanel Col(params Control[] controls) { var p = new StackPanel { Spacing = 8 }; foreach (var c in controls) p.Children.Add(c); return p; }
    static WrapPanel Row(params Control[] controls) { var p = new WrapPanel(); foreach (var c in controls) { c.Margin = new Thickness(3); p.Children.Add(c); } return p; }
    TextBlock Text(string text, double size = 15, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Foreground = Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    Button Btn(string label, Func<Task> act, bool primary = false)
    {
        var b = new Button { Content = label, CornerRadius = new CornerRadius(8), Padding = new Thickness(13, 6), Margin = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.Background = Paint("#214D8D"); b.Foreground = Brushes.White; }
        b.Click += async (_, _) => await Guard(act); return b;
    }
    Button Btn(string label, Action act, bool primary = false) => Btn(label, () => { act(); return Task.CompletedTask; }, primary);
    async Task Guard(Func<Task> action) { try { await action(); } catch (Exception ex) { status.Text = ex.Message; } }
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
        try { var h = await Picker.Start(this); if (h != null) { page = 0; Select(h); status.Text = "تم التقاط " + h; } else status.Text = "أُلغي الالتقاط"; }
        finally { picking = false; eyedropper.IsEnabled = true; }
    }
    void Render()
    {
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Paint(dark ? "#101923" : "#F3F6FA"); Foreground = Ink; status.Foreground = Ink;
        nav.Children.Clear(); for (int i = 0; i < pages.Length; i++) { int idx = i; var b = Btn(pages[i], () => { page = idx; Render(); }, page == i); nav.Children.Add(b); }
        content.Children.Clear(); content.Children.Add(Text(pages[page], 27, true));
        switch (page) { case 0: ColorPage(); break; case 1: Palettes(); break; case 2: ContrastPage(); break; case 3: Images(); break; case 4: Gradients(); break; case 5: History(); break; }
    }
    Border Card(Control c) => new() { Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(15), CornerRadius = new CornerRadius(12), Child = c };
    Button Chip(string hex, Action act, string? label = null)
    {
        var b = Btn(label ?? hex, act); b.Background = Paint(hex); b.Foreground = Paint(Core.Describe(hex).Text("ink")); b.MinHeight = 41;
        b.FlowDirection = FlowDirection.LeftToRight; return b;
    }
    ComboBox Choose(IEnumerable<string> options, int index, Action<int> changed)
    {
        var box = new ComboBox { ItemsSource = options.ToArray(), SelectedIndex = index, MinWidth = 125, Margin = new Thickness(3) };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) changed(box.SelectedIndex); }; return box;
    }
    async Task Copy(string value) { if (Clipboard == null) throw new InvalidOperationException("الحافظة غير متاحة."); await Clipboard.SetTextAsync(value); status.Text = "نُسخت القيمة إلى الحافظة"; }
    async Task<string?> Prompt(string title, string initial, bool hex = false)
    {
        var input = new TextBox { Text = initial, MaxLength = hex ? 7 : 100, FlowDirection = hex ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        var d = Dialog(title); void OK() { var val = (input.Text ?? "").Trim(); if (hex) val = "#" + val.TrimStart('#').ToUpperInvariant(); if (val.Length > 0 && (!hex || Workspace.Hex(val))) d.Close(val); }
        d.Content = Col(Text(title, 18, true), input, Row(Btn("حفظ", OK, true), Btn("إلغاء", () => d.Close())));
        input.KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.Enter) { ev.Handled = true; OK(); } };
        d.Opened += (_, _) => { input.Focus(); input.SelectAll(); }; return await d.ShowDialog<string?>(this);
    }
    Window Dialog(string title)
    {
        var d = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(18),
            FontFamily = FontFamily,
            FlowDirection = FlowDirection.RightToLeft,
            RequestedThemeVariant = RequestedThemeVariant
        };
        d.KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.Escape) d.Close(); }; return d;
    }
    async Task<bool> Confirm(string title) { var d = Dialog(title); d.Content = Col(Text(title, 17), Row(Btn("تأكيد", () => d.Close(true), true), Btn("إلغاء", () => d.Close(false)))); return await d.ShowDialog<bool>(this); }
    async Task<int?> SelectOption(string title, string[] choices, int start = 0)
    {
        var d = Dialog(title); var choice = new ComboBox { ItemsSource = choices, SelectedIndex = start, HorizontalAlignment = HorizontalAlignment.Stretch };
        d.Content = Col(Text(title, 17), choice, Row(Btn("اختيار", () => d.Close((int?)choice.SelectedIndex), true), Btn("إلغاء", () => d.Close()))); return await d.ShowDialog<int?>(this);
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
    void ColorPage()
    {
        var c = Core.Describe(state.Color); var fg = Paint(c.Text("ink")); var hex = Text(state.Color, 47, true); hex.FlowDirection = FlowDirection.LeftToRight; hex.Foreground = fg;
        var name = Text(c.Text("name") + "   ·   " + c.Text("english"), 20, true); name.Foreground = fg;
        var details = Text(c.Text("base") + "  ·  " + c.Text("shade") + "  ·  " + "\u2066" + c.Text("reference") + "\u2069", 13); details.Foreground = fg;
        content.Children.Add(new Border { Background = Paint(state.Color), CornerRadius = new CornerRadius(14), Padding = new Thickness(25, 16), Child = Col(name, hex, details) });
        var formats = c.GetProperty("formats");
        content.Children.Add(Row(Btn("إضافة إلى لوحة", () => AddToPalette([state.Color]), true), Btn("تعديل HEX", async () => { var h = await Prompt("قيمة HEX", state.Color, true); if (h != null) Select(h); }),
            Btn("نسخ جميع الصيغ", () => Copy(string.Join("\n", formats.EnumerateObject().Select(k => k.Name + ": " + k.Value.GetString())))), Btn("تصدير اللون", () => Export(new Palette { Name = c.Text("name"), Colors = [Swatch.From(state.Color)] }))));
        var fmt = Row(); foreach (var key in new[] { "HEX", "RGB", "RGBA", "HSL", "HSV", "HSB", "CMYK" }) { var val = formats.Text(key); var b = Btn(key + "   " + val, () => Copy(val)); b.FlowDirection = FlowDirection.LeftToRight; fmt.Children.Add(b); }
        content.Children.Add(Card(Col(Text("صيغ اللون", 18, true), fmt)));
        var colors = Core.Harmony(state.Color, harmony); var group = Row(); foreach (var h in colors) group.Children.Add(Chip(h, () => Select(h)));
        content.Children.Add(Card(Col(Row(Text("تناغم الألوان", 18, true), Choose(modes, harmony, i => { harmony = i; Render(); })), group, Btn("إضافة المجموعة إلى لوحة", () => AddToPalette(colors)))));
    }
    async Task AddToPalette(string[] colors)
    {
        int? index = state.Palettes.Count == 1 ? 0 : await SelectOption("إضافة إلى لوحة", state.Palettes.Select(p => p.Name).ToArray(), state.Palettes.IndexOf(Current)); if (index == null) return;
        Edit(() => state.Palettes[index.Value].Colors.AddRange(colors.Select(Swatch.From))); status.Text = "أُضيفت الألوان إلى اللوحة";
    }
}
