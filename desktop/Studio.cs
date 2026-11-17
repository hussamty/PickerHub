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
    void Images()
    {
        var inputActions = Row(Btn("upload", L.T("اختيار صورة"), Analyze, true), Btn("copy", L.T("لصق صورة"), PasteImage));
        var hint = Label(L.T("اسحب صورة هنا أو الصقها باستخدام ") + (OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V") + " · PNG / JPG / WEBP · 32 MB");
        content.Children.Add(Surface(Col(hint, inputActions)));
        if (preview == null) { content.Children.Add(Empty(L.T("لوحة جديدة من صورة"), L.T("الصورة تبقى على جهازك. تُستخرج الألوان محلياً."), "photo")); return; }
        Palette Make() => new() { Name = imageName, Colors = recentImage.Select(Swatch.From).ToList() };
        var photo = Section(imageName, ImageSamplingControls(), Of(Liquid("upload", L.T("تغيير الصورة"), Analyze), "still"));
        // Each bar is as long as that color's share of the image.
        var list = new StackPanel { Spacing = 6 };
        double widest = percentages.Length == 0 ? 1 : Math.Max(percentages.Max(), .1);
        for (int i = 0; i < recentImage.Length; i++)
        {
            var h = recentImage[i]; double share = Math.Clamp(percentages[i] / widest, .06, 1);
            var bar = Of(Chip(h, () => { page = 0; Select(h); }, ""), "flat"); bar.Height = 26; bar.MinHeight = 26; bar.CornerRadius = new CornerRadius(9); ToolTip.SetTip(bar, h);
            var track = new Grid { ColumnDefinitions = new ColumnDefinitions(FormattableString.Invariant($"{share}*,{1 - share}*")) }; track.Children.Add(bar);
            var percent = Mono(percentages[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%", Typography.Label); percent.HorizontalAlignment = HorizontalAlignment.Left;
            var name = Text(Core.Describe(h).Text("name"), 12); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("80,8,*,8,48,Auto") };
            line.Children.Add(name); Grid.SetColumn(track, 2); line.Children.Add(track); Grid.SetColumn(percent, 4); line.Children.Add(percent);
            var use = Liquid("dots", L.T("إجراءات اللون"), () => ColorActions(h));
            Grid.SetColumn(use, 5); line.Children.Add(use);
            list.Children.Add(line);
        }
        var save = Btn("device-floppy", L.T("حفظ كلوحة"), async () => { var p = Make(); var name = await Prompt(L.T("اسم اللوحة"), p.Name); if (name != null) { p.Name = name; Edit(() => { state.Palettes.Add(p); paletteId = p.Id; paletteCard = 0; }); } }, true);
        var actions = Line(8, save, Of(Liquid("download", L.T("تصدير"), () => Export(Make())), "still")); actions.Margin = new Thickness(0, 10, 0, 0); list.Children.Add(actions);
        content.Children.Add(Pair(photo, Section(L.T("ألوان الصورة"), list)));
    }
    static string Css(Gradient g) => (g.Type == "linear" ? $"linear-gradient({g.Angle}deg, " : "radial-gradient(circle, ") + string.Join(", ", g.Stops.OrderBy(s => s.Pos).Select(s => $"{s.Hex} {s.Pos}%")) + ")";
    internal static (RelativePoint Start, RelativePoint End) CssGradientLine(double degrees, Size size)
    {
        double radians = degrees * Math.PI / 180;
        var direction = new Vector(Math.Sin(radians), -Math.Cos(radians));
        var center = new Point(size.Width / 2, size.Height / 2);
        double halfLength = (Math.Abs(direction.X) * size.Width + Math.Abs(direction.Y) * size.Height) / 2;
        var offset = direction * halfLength;
        return (new RelativePoint(center - offset, RelativeUnit.Absolute), new RelativePoint(center + offset, RelativeUnit.Absolute));
    }
    static IBrush GradientBrush(Gradient g, Size size)
    {
        var stops = new GradientStops();
        foreach (var stop in g.Stops.OrderBy(s => s.Pos))
            stops.Add(new GradientStop(Avalonia.Media.Color.Parse(stop.Hex), stop.Pos / 100d));
        if (g.Type == "linear")
        {
            var (start, end) = CssGradientLine(g.Angle, size);
            return new LinearGradientBrush { StartPoint = start, EndPoint = end, GradientStops = stops };
        }

        // CSS's default circle reaches the farthest corner of the preview.
        double radius = Math.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2;
        var length = new RelativeScalar(radius, RelativeUnit.Absolute);
        return new RadialGradientBrush { RadiusX = length, RadiusY = length, GradientStops = stops };
    }
    void Gradients()
    {
        var preview = new Border { Height = 190, CornerRadius = new CornerRadius(24), FlowDirection = FlowDirection.LeftToRight };
        var code = Mono(Css(gradient), Typography.Label);
        void Refresh() { preview.Background = GradientBrush(gradient, preview.Bounds.Size); code.Text = Css(gradient); }
        preview.SizeChanged += (_, _) => Refresh();
        var copyCss = Of(Btn("", () => Copy("background: " + Css(gradient) + ";")), "row"); copyCss.Padding = new Thickness(12, 10); copyCss.CornerRadius = new CornerRadius(16);
        var css = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), FlowDirection = FlowDirection.LeftToRight };
        css.Children.Add(code); var copyIcon = Tone(new Icon("copy", 15), "Muted"); Grid.SetColumn(copyIcon, 2); css.Children.Add(copyIcon);
        copyCss.Content = css; AutomationProperties.SetName(copyCss, L.T("نسخ CSS")); ToolTip.SetTip(copyCss, L.T("نسخ CSS")); ToolTip.SetPlacement(copyCss, PlacementMode.Top);

        var angle = new NumericUpDown { MinWidth = 130, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 360, Value = gradient.Angle, Increment = 5, IsEnabled = gradient.Type == "linear" };
        AutomationProperties.SetName(angle, L.T("الزاوية"));
        angle.ValueChanged += (_, _) => { gradient.Angle = (int)(angle.Value ?? 0); Refresh(); };
        var save = Btn("device-floppy", L.T("حفظ التدرج"), async () =>
        {
            var name = await Prompt(L.T("اسم التدرج"), gradient.Name); if (name == null) return; gradient.Name = name; var saved = JsonSerializer.Deserialize<Gradient>(Serialize(gradient), Workspace.Json)!;
            Edit(() => { var i = state.Gradients.FindIndex(g => g.Id == saved.Id); if (i < 0) state.Gradients.Add(saved); else state.Gradients[i] = saved; });
        }, true);
        save.VerticalAlignment = VerticalAlignment.Bottom;
        var tools = Cluster(Liquid("refresh", L.T("تدرج جديد"), () => { gradient = new(); Render(); }), Liquid("download", L.T("تصدير CSS"), () => WriteFile("gradient.css", ".gradient { background: " + Css(gradient) + "; }")));
        tools.VerticalAlignment = VerticalAlignment.Bottom;
        content.Children.Add(Col(preview, copyCss));
        content.Children.Add(Row(Field(L.T("النوع"), Choose([L.T("خطي"), L.T("دائري")], gradient.Type == "linear" ? 0 : 1, i => { gradient.Type = i == 0 ? "linear" : "radial"; Render(); })), Field(L.T("الزاوية"), angle), tools, save));

        var stops = new StackPanel { Spacing = 12 };
        foreach (var stop in gradient.Stops)
        {
            var pos = new NumericUpDown { Width = 120, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 100, Value = stop.Pos, Increment = 1, FormatString = "0" };
            var slider = new Slider { FlowDirection = FlowDirection.LeftToRight, Minimum = 0, Maximum = 100, Value = stop.Pos, TickFrequency = 1, IsSnapToTickEnabled = true };
            AutomationProperties.SetName(pos, L.T("موضع النقطة")); AutomationProperties.SetName(slider, L.T("موضع النقطة"));
            slider.ValueChanged += (_, _) => { stop.Pos = (int)Math.Round(slider.Value); pos.Value = stop.Pos; Refresh(); };
            pos.ValueChanged += (_, _) => { stop.Pos = (int)(pos.Value ?? 0); slider.Value = stop.Pos; Refresh(); };
            var remove = Of(Liquid("trash", L.T("إزالة النقطة"), () => { if (gradient.Stops.Count > 2) { gradient.Stops.Remove(stop); Render(); } }), "still"); remove.IsEnabled = gradient.Stops.Count > 2;
            var color = EditHex(L.T("لون النقطة"), stop.Hex, h => stop.Hex = h);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*,14,Auto,10,Auto"), FlowDirection = FlowDirection.LeftToRight };
            line.Children.Add(color); Grid.SetColumn(slider, 2); line.Children.Add(slider); Grid.SetColumn(pos, 4); line.Children.Add(pos); Grid.SetColumn(remove, 6); line.Children.Add(remove);
            stops.Children.Add(line);
        }
        var addStop = Of(Liquid("plus", L.T("إضافة نقطة"), () => { if (gradient.Stops.Count < 32) { gradient.Stops.Add(new Stop { Hex = state.Color, Pos = 50 }); Render(); } }), "still");
        content.Children.Add(Section(L.T("نقاط التدرج"), stops, addStop));

        if (state.Gradients.Count == 0) { content.Children.Add(Label(L.T("احفظ تدرجك للعودة إليه في أي وقت."))); return; }
        var shelf = new WrapPanel();
        foreach (var g in state.Gradients)
        {
            var open = Of(Btn("", () => { gradient = JsonSerializer.Deserialize<Gradient>(Serialize(g), Workspace.Json)!; Render(); }), "swatch");
            open.Height = 92; open.CornerRadius = new CornerRadius(16);
            open.HorizontalAlignment = HorizontalAlignment.Stretch; open.FlowDirection = FlowDirection.LeftToRight;
            open.SizeChanged += (_, _) => open.Background = GradientBrush(g, open.Bounds.Size);
            AutomationProperties.SetName(open, L.T("فتح ") + g.Name); ToolTip.SetTip(open, L.T("فتح")); ToolTip.SetPlacement(open, PlacementMode.Top);
            var title = Text(g.Name, Typography.Control, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            var foot = new DockPanel(); var delete = Of(Liquid("trash", L.T("حذف التدرج"), async () => { if (await Confirm(L.T("حذف التدرج؟"))) Edit(() => state.Gradients.Remove(g)); }), "still");
            DockPanel.SetDock(delete, Dock.Right); foot.Children.Add(delete); foot.Children.Add(title);
            var tile = Col(open, foot); tile.Width = 190; tile.Margin = new Thickness(0, 0, 14, 14); shelf.Children.Add(tile);
        }
        content.Children.Add(Section(L.T("مكتبة التدرجات"), shelf));
    }
}
