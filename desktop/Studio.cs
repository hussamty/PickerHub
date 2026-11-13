using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PickerHub;

internal sealed partial class MainWindow
{
    bool analyzing;
    async Task Analyze()
    {
        if (analyzing) return;
        analyzing = true;
        status.Text = "جارٍ تحليل الصورة…";
        try { await AnalyzeImage(); }
        finally { analyzing = false; if (status.Text == "جارٍ تحليل الصورة…") status.Text = "جاهز لالتقاط الألوان"; }
    }
    async Task AnalyzeImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "اختيار صورة", FileTypeFilter = [new FilePickerFileType("PNG · JPG · WEBP") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }] }); if (files.Count == 0) return;
        using var stream = await files[0].OpenReadAsync();
        if (stream.CanSeek && stream.Length > 32L << 20) throw new InvalidDataException("اختر صورة أصغر من 32 ميغابايت.");
        var (data, rgba) = await Task.Run(() =>
        {
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("تعذّر قراءة الصورة.");
            var i = codec.Info; if (i.Width < 1 || i.Height < 1 || (long)i.Width * i.Height > 40_000_000) throw new InvalidDataException("الحد الأقصى 40 مليون بكسل.");
            using var src = SKBitmap.Decode(codec) ?? throw new InvalidDataException("الصورة تالفة.");
            double scale = Math.Min(1, 768d / Math.Max(i.Width, i.Height));
            using var small = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(i.Width * scale)), Math.Max(1, (int)Math.Round(i.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (!src.ScalePixels(small, SKFilterQuality.None)) throw new InvalidDataException("تعذّر تصغير الصورة.");
            using var image = SKImage.FromBitmap(small); using var png = image.Encode(SKEncodedImageFormat.Png, 100); return (png.ToArray(), small.Bytes);
        });
        var result = Core.Extract(rgba); if (result.GetArrayLength() == 0) throw new InvalidDataException("الصورة شفافة بالكامل.");
        var colors = result.EnumerateArray().ToArray(); recentImage = colors.Select(c => c.Text("hex")).ToArray(); percentages = colors.Select(c => c.GetProperty("percent").GetDouble()).ToArray();
        imageName = Path.GetFileNameWithoutExtension(files[0].Name); if (imageName.Length == 0) imageName = "لوحة الصورة"; if (imageName.Length > 100) imageName = imageName[..100];
        preview?.Dispose(); using var ms = new MemoryStream(data); preview = new Bitmap(ms); Render();
    }
    void Images()
    {
        if (preview == null) { content.Children.Add(Empty("لوحة جديدة من صورة", "PNG أو JPG أو WEBP بحجم يصل إلى 32 ميغابايت.", "photo", Btn("upload", "اختيار صورة", Analyze, true))); return; }
        Palette Make() => new() { Name = imageName, Colors = recentImage.Select(Swatch.From).ToList() };
        var picture = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = new Image { Source = preview, MaxHeight = 360, Stretch = Stretch.Uniform } };
        var photo = Section(imageName, picture, Of(Liquid("upload", "تغيير الصورة", Analyze), "still"));
        // Each bar is as long as that color's share of the image.
        var list = new StackPanel { Spacing = 8 };
        double widest = percentages.Length == 0 ? 1 : Math.Max(percentages.Max(), .1);
        for (int i = 0; i < recentImage.Length; i++)
        {
            var h = recentImage[i]; double share = Math.Clamp(percentages[i] / widest, .06, 1);
            var bar = Of(Chip(h, () => { page = 0; Select(h); }, ""), "flat"); bar.Height = 34; bar.MinHeight = 34; ToolTip.SetTip(bar, h);
            var track = new Grid { ColumnDefinitions = new ColumnDefinitions(FormattableString.Invariant($"{share}*,{1 - share}*")) }; track.Children.Add(bar);
            var percent = Mono(percentages[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%", 12); percent.HorizontalAlignment = HorizontalAlignment.Left;
            var name = Text(Core.Describe(h).Text("name"), 12); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("96,10,*,10,52") };
            line.Children.Add(name); Grid.SetColumn(track, 2); line.Children.Add(track); Grid.SetColumn(percent, 4); line.Children.Add(percent);
            list.Children.Add(line);
        }
        var save = Btn("device-floppy", "حفظ كلوحة", async () => { var p = Make(); var name = await Prompt("اسم اللوحة", p.Name); if (name != null) { p.Name = name; Edit(() => { state.Palettes.Add(p); paletteId = p.Id; paletteCard = 0; }); } }, true);
        var actions = Line(8, save, Of(Liquid("download", "تصدير", () => Export(Make())), "still")); actions.Margin = new Thickness(0, 10, 0, 0); list.Children.Add(actions);
        content.Children.Add(Pair(photo, Section("ألوان الصورة", list)));
    }
    static string Css(Gradient g) => (g.Type == "linear" ? $"linear-gradient({g.Angle}deg, " : "radial-gradient(circle, ") + string.Join(", ", g.Stops.OrderBy(s => s.Pos).Select(s => $"{s.Hex} {s.Pos}%")) + ")";
    static IBrush GradientBrush(Gradient g)
    {
        var stops = new GradientStops(); foreach (var s in g.Stops.OrderBy(s => s.Pos)) stops.Add(new GradientStop(Avalonia.Media.Color.Parse(s.Hex), s.Pos / 100d));
        double angle = g.Angle * Math.PI / 180, dx = Math.Sin(angle) / 2, dy = -Math.Cos(angle) / 2;
        return g.Type == "linear" ? new LinearGradientBrush { StartPoint = new RelativePoint(.5 - dx, .5 - dy, RelativeUnit.Relative), EndPoint = new RelativePoint(.5 + dx, .5 + dy, RelativeUnit.Relative), GradientStops = stops } : new RadialGradientBrush { GradientStops = stops };
    }
    void Gradients()
    {
        var preview = new Border { Height = 190, CornerRadius = new CornerRadius(24), Background = GradientBrush(gradient) };
        var code = Mono(Css(gradient), 12);
        void Refresh() { preview.Background = GradientBrush(gradient); code.Text = Css(gradient); }
        var copyCss = Of(Btn("", () => Copy("background: " + Css(gradient) + ";")), "row"); copyCss.Padding = new Thickness(12, 10); copyCss.CornerRadius = new CornerRadius(16);
        var css = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), FlowDirection = FlowDirection.LeftToRight };
        css.Children.Add(code); var copyIcon = Tone(new Icon("copy", 15), "Muted"); Grid.SetColumn(copyIcon, 2); css.Children.Add(copyIcon);
        copyCss.Content = css; AutomationProperties.SetName(copyCss, "نسخ CSS"); ToolTip.SetTip(copyCss, "نسخ CSS"); ToolTip.SetPlacement(copyCss, PlacementMode.Top);

        var angle = new NumericUpDown { MinWidth = 130, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 360, Value = gradient.Angle, Increment = 5, IsEnabled = gradient.Type == "linear" };
        AutomationProperties.SetName(angle, "الزاوية");
        angle.ValueChanged += (_, _) => { gradient.Angle = (int)(angle.Value ?? 0); Refresh(); };
        var save = Btn("device-floppy", "حفظ التدرج", async () =>
        {
            var name = await Prompt("اسم التدرج", gradient.Name); if (name == null) return; gradient.Name = name; var saved = JsonSerializer.Deserialize<Gradient>(Serialize(gradient), Workspace.Json)!;
            Edit(() => { var i = state.Gradients.FindIndex(g => g.Id == saved.Id); if (i < 0) state.Gradients.Add(saved); else state.Gradients[i] = saved; });
        }, true);
        save.VerticalAlignment = VerticalAlignment.Bottom;
        var tools = Cluster(Liquid("refresh", "تدرج جديد", () => { gradient = new(); Render(); }), Liquid("download", "تصدير CSS", () => WriteFile("gradient.css", ".gradient { background: " + Css(gradient) + "; }")));
        tools.VerticalAlignment = VerticalAlignment.Bottom;
        content.Children.Add(Col(preview, copyCss));
        content.Children.Add(Row(Field("النوع", Choose(["خطي", "دائري"], gradient.Type == "linear" ? 0 : 1, i => { gradient.Type = i == 0 ? "linear" : "radial"; Render(); })), Field("الزاوية", angle), tools, save));

        var stops = new StackPanel { Spacing = 12 };
        foreach (var stop in gradient.Stops)
        {
            var pos = new NumericUpDown { Width = 120, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 100, Value = stop.Pos, Increment = 1, FormatString = "0" };
            var slider = new Slider { FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 100, Value = stop.Pos, TickFrequency = 1, IsSnapToTickEnabled = true };
            AutomationProperties.SetName(pos, "موضع النقطة"); AutomationProperties.SetName(slider, "موضع النقطة");
            slider.ValueChanged += (_, _) => { stop.Pos = (int)Math.Round(slider.Value); pos.Value = stop.Pos; Refresh(); };
            pos.ValueChanged += (_, _) => { stop.Pos = (int)(pos.Value ?? 0); slider.Value = stop.Pos; Refresh(); };
            var remove = Of(Liquid("trash", "إزالة النقطة", () => { if (gradient.Stops.Count > 2) { gradient.Stops.Remove(stop); Render(); } }), "still"); remove.IsEnabled = gradient.Stops.Count > 2;
            var color = EditHex("لون النقطة", stop.Hex, h => stop.Hex = h);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*,14,Auto,10,Auto"), FlowDirection = FlowDirection.LeftToRight };
            line.Children.Add(color); Grid.SetColumn(slider, 2); line.Children.Add(slider); Grid.SetColumn(pos, 4); line.Children.Add(pos); Grid.SetColumn(remove, 6); line.Children.Add(remove);
            stops.Children.Add(line);
        }
        var addStop = Of(Liquid("plus", "إضافة نقطة", () => { if (gradient.Stops.Count < 32) { gradient.Stops.Add(new Stop { Hex = state.Color, Pos = 50 }); Render(); } }), "still");
        content.Children.Add(Section("نقاط التدرج", stops, addStop));

        if (state.Gradients.Count == 0) { content.Children.Add(Label("احفظ تدرجك للعودة إليه في أي وقت.")); return; }
        var shelf = new WrapPanel();
        foreach (var g in state.Gradients)
        {
            var open = Of(Btn("", () => { gradient = JsonSerializer.Deserialize<Gradient>(Serialize(g), Workspace.Json)!; Render(); }), "swatch"); open.Background = GradientBrush(g); open.Height = 92; open.CornerRadius = new CornerRadius(16); open.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(open, "فتح " + g.Name); ToolTip.SetTip(open, "فتح"); ToolTip.SetPlacement(open, PlacementMode.Top);
            var title = Text(g.Name, 13, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            var foot = new DockPanel(); var delete = Of(Liquid("trash", "حذف التدرج", async () => { if (await Confirm("حذف التدرج؟")) Edit(() => state.Gradients.Remove(g)); }), "still");
            DockPanel.SetDock(delete, Dock.Right); foot.Children.Add(delete); foot.Children.Add(title);
            var tile = Col(open, foot); tile.Width = 190; tile.Margin = new Thickness(0, 0, 14, 14); shelf.Children.Add(tile);
        }
        content.Children.Add(Section("مكتبة التدرجات", shelf));
    }
}
