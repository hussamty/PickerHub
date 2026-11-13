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
        eyedropper = Btn("القطّارة", PickDesktop, true);
        BuildShell();
        Closed += (_, _) => { preview?.Dispose(); instanceLock?.Dispose(); };
        KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.F8 && !picking) { ev.Handled = true; _ = Guard(PickDesktop); } };
        Render();
    }
    static StackPanel Col(params Control[] controls) { var p = new StackPanel { Spacing = 8 }; foreach (var c in controls) p.Children.Add(c); return p; }
    static WrapPanel Row(params Control[] controls) { var p = new WrapPanel(); foreach (var c in controls) { c.Margin = new Thickness(3); p.Children.Add(c); } return p; }
    TextBlock Text(string text, double size = 15, bool bold = false) => new() { Text = text, LineHeight = size * 1.55, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Foreground = Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    Button Btn(string label, Func<Task> act, bool primary = false)
    {
        var b = new Button { Background = Paper, Foreground = Ink, BorderBrush = Line, BorderThickness = new Thickness(1), MinHeight = 36, FontSize = 12, Content = label, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7), Margin = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.BorderThickness = new Thickness(0); b.Background = Paint("#214D8D"); b.Foreground = Brushes.White; }
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
        StyleShell();
        content.Children.Clear();
        switch (page) { case 0: ColorPage(); break; case 1: Palettes(); break; case 2: ContrastPage(); break; case 3: Images(); break; case 4: Gradients(); break; case 5: History(); break; }
    }
    Border Card(Control c) => new() { Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(15), CornerRadius = new CornerRadius(12), Child = c };
    Button Chip(string hex, Action act, string? label = null)
    {
        var b = Btn(label ?? hex, act); b.Background = Paint(hex); b.Foreground = Paint(Core.Describe(hex).Text("ink")); b.MinHeight = 41; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.VerticalAlignment = VerticalAlignment.Stretch;
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
        var feedback = Text("", 12); feedback.Foreground = Paint(dark ? "#FFB5B5" : "#A42635"); feedback.IsVisible = false;
        var d = Dialog(title); void OK() { var val = (input.Text ?? "").Trim(); if (hex) val = "#" + val.TrimStart('#').ToUpperInvariant(); if (val.Length > 0 && (!hex || Workspace.Hex(val))) d.Close(val); else { feedback.IsVisible = true; feedback.Text = hex ? "أدخل ست خانات HEX." : "أدخل اسماً."; } }
        d.Content = Col(Text(title, 18, true), input, feedback, Row(Btn("حفظ", OK, true), Btn("إلغاء", () => d.Close())));
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
    async Task AddToPalette(string[] colors)
    {
        int? index = state.Palettes.Count == 1 ? 0 : await SelectOption("إضافة إلى لوحة", state.Palettes.Select(p => p.Name).ToArray(), state.Palettes.IndexOf(Current)); if (index == null) return;
        Edit(() => state.Palettes[index.Value].Colors.AddRange(colors.Select(Swatch.From))); status.Text = "أُضيفت الألوان إلى اللوحة";
    }
}
