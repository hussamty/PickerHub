using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Automation;

namespace PickerHub;

// The spectrum is an interactive editor, rendered with the toolkit's sRGB brushes.
internal sealed class Spectrum : Control
{
    public HsvColor Value { get; set; }
    public event Action<string>? Selected;
    public Spectrum(string hex)
    {
        Value = Color.Parse(hex).ToHsv(); Height = 120; MinWidth = 150; Cursor = new Cursor(StandardCursorType.Cross);
        FlowDirection = FlowDirection.LeftToRight; Focusable = true; AutomationProperties.SetName(this, "تشبع اللون وسطوعه");
        PointerPressed += (_, e) => { e.Pointer.Capture(this); Update(e.GetPosition(this)); };
        PointerMoved += (_, e) => { if (e.Pointer.Captured == this) Update(e.GetPosition(this)); };
        PointerReleased += (_, e) => { if (e.Pointer.Captured == this) { e.Pointer.Capture(null); Selected?.Invoke(Hex()); } };
        KeyDown += (_, e) => { double s = Value.S, v = Value.V; switch (e.Key) { case Key.Left: s -= .01; break; case Key.Right: s += .01; break; case Key.Up: v += .01; break; case Key.Down: v -= .01; break; default: return; } e.Handled = true; Value = HsvColor.FromHsv(Value.H, Math.Clamp(s, 0, 1), Math.Clamp(v, 0, 1)); Selected?.Invoke(Hex()); InvalidateVisual(); };
    }
    string Hex() { var c = Value.ToRgb(); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
    void Update(Point p) { Value = HsvColor.FromHsv(Value.H, Math.Clamp(p.X / Bounds.Width, 0, 1), 1 - Math.Clamp(p.Y / Bounds.Height, 0, 1)); InvalidateVisual(); }
    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(Bounds.Size);
        ctx.DrawRectangle(new SolidColorBrush(HsvColor.FromHsv(Value.H, 1, 1).ToRgb()), null, rect);
        ctx.DrawRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = [new(Colors.White, 0), new(Colors.Transparent, 1)] }, null, rect);
        ctx.DrawRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = [new(Colors.Transparent, 0), new(Colors.Black, 1)] }, null, rect);
        var pt = new Point(Value.S * Bounds.Width, (1 - Value.V) * Bounds.Height);
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 4), pt, 6, 6); ctx.DrawEllipse(null, new Pen(Brushes.White, 2), pt, 6, 6);
    }
}

internal sealed partial class MainWindow
{
    void ColorPage()
    {
        var c = Core.Describe(state.Color);
        var formats = c.GetProperty("formats");
        string ink = c.Text("ink");

        // The specimen: the selected color at full size, labelled in its own readable ink.
        var title = Col(Text(c.Text("name"), 26, true), Text(c.Text("base") + "، " + c.Text("shade"), 12)); title.Spacing = 0; title.VerticalAlignment = VerticalAlignment.Bottom; title.HorizontalAlignment = HorizontalAlignment.Right; title.FlowDirection = FlowDirection.RightToLeft;
        var code = Mono(state.Color, 20, true); code.VerticalAlignment = VerticalAlignment.Bottom; code.HorizontalAlignment = HorizontalAlignment.Left;
        var english = Mono(c.Text("english").ToLowerInvariant(), 12); english.VerticalAlignment = VerticalAlignment.Top; english.HorizontalAlignment = HorizontalAlignment.Right; english.Opacity = .8;
        var quick = Cluster(LiquidOn(ink, "copy", "نسخ HEX", () => Copy(state.Color)), LiquidOn(ink, "download", "تصدير اللون", () => Export(new Palette { Name = c.Text("name"), Colors = [Swatch.From(state.Color)] })));
        quick.HorizontalAlignment = HorizontalAlignment.Left; quick.VerticalAlignment = VerticalAlignment.Top; quick.FlowDirection = FlowDirection.LeftToRight;
        var face = new Panel { FlowDirection = FlowDirection.LeftToRight }; face.Children.AddRange([english, quick, title, code]);
        var specimen = new Border { Background = Paint(state.Color), CornerRadius = new CornerRadius(24), Padding = new Thickness(22), Height = 172, Child = face };
        specimen[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"); specimen.BorderThickness = new Thickness(1);
        Avalonia.Controls.Documents.TextElement.SetForeground(specimen, Paint(ink));

        var hex = new TextBox { Text = state.Color, MaxLength = 7, FlowDirection = FlowDirection.LeftToRight, FontFamily = Code, FontSize = 17, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(hex, "hex-input"); AutomationProperties.SetName(hex, "قيمة HEX");
        var error = Of(Text("", 12), "danger"); error.IsVisible = false;
        void Apply() { var next = "#" + (hex.Text ?? "").Trim().TrimStart('#'); if (!Workspace.Hex(next)) { error.IsVisible = true; error.Text = "أدخل ست خانات HEX."; return; } Select(next); }
        hex.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; _ = Guard(() => { Apply(); return Task.CompletedTask; }); } };
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), FlowDirection = FlowDirection.LeftToRight };
        input.Children.Add(hex);
        var apply = Of(Liquid("check", "اعتماد HEX", Apply), "still"); Grid.SetColumn(apply, 2); input.Children.Add(apply);

        var spectrum = new Spectrum(state.Color) { Height = 150 }; spectrum.Selected += h => _ = Guard(() => { Select(h); return Task.CompletedTask; });
        var hue = new Slider { Height = 40, MinHeight = 40, Minimum = 0, Maximum = 359.99, Value = spectrum.Value.H, FlowDirection = FlowDirection.LeftToRight };
        AutomationProperties.SetName(hue, "درجة اللون");
        var hueBrush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(HsvColor.FromHsv(i * 60 % 360, 1, 1).ToRgb(), i / 6d));
        hue.Background = hueBrush;
        void Commit() { var rgb = spectrum.Value.ToRgb(); var h = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}"; if (h != state.Color) _ = Guard(() => { Select(h); return Task.CompletedTask; }); }
        hue.ValueChanged += (_, _) => { spectrum.Value = HsvColor.FromHsv(hue.Value, spectrum.Value.S, spectrum.Value.V); spectrum.InvalidateVisual(); };
        hue.PointerCaptureLost += (_, _) => Commit();
        hue.KeyUp += (_, _) => Commit();
        var save = Btn("plus", "إضافة إلى لوحة", () => AddToPalette([state.Color]), true); save.HorizontalAlignment = HorizontalAlignment.Stretch;
        var editor = Col(new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = spectrum }, hue, Field("HEX", input), error, save);

        // Every format is one click away from the clipboard.
        var rows = new StackPanel { Spacing = 2 };
        foreach (var key in new[] { "HEX", "RGB", "RGBA", "HSL", "HSV", "HSB", "CMYK" })
        {
            string value = formats.Text(key); var b = Of(Btn("", () => Copy(value)), "row");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*,20"), FlowDirection = FlowDirection.LeftToRight };
            grid.Children.Add(Tone(Mono(key, 11, true), "Muted"));
            var val = Mono(value, 13); val.TextWrapping = TextWrapping.NoWrap; val.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(val, 1); grid.Children.Add(val);
            var copy = Tone(new Icon("copy", 15), "Muted"); Grid.SetColumn(copy, 2); grid.Children.Add(copy); b.Content = grid;
            AutomationProperties.SetName(b, "نسخ " + key + " " + value); ToolTip.SetTip(b, "نسخ " + key); ToolTip.SetPlacement(b, PlacementMode.Top); rows.Children.Add(b);
        }
        var formatList = Section("صيغ اللون", rows, Of(Liquid("copy", "نسخ جميع الصيغ", () => Copy(string.Join("\n", formats.EnumerateObject().Select(k => k.Name + ": " + k.Value.GetString())))), "still"));

        var colors = Core.Harmony(state.Color, harmony);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", colors.Select(_ => "*"))), Height = 64, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < colors.Length; i++) { var h = colors[i]; var b = Of(Chip(h, () => Select(h), colors.Length > 5 ? "" : null), "flat"); b.CornerRadius = new CornerRadius(0); ToolTip.SetTip(b, h); Grid.SetColumn(b, i); strip.Children.Add(b); }
        var harmonyTools = Line(8, Choose(modes, harmony, i => { harmony = i; Render(); }), Of(Liquid("plus", "إضافة المجموعة إلى لوحة", () => AddToPalette(colors)), "still"));
        var harmonies = Section("تناغم الألوان", new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = strip }, harmonyTools);

        // Recent colors sit under the harmonies, where the shorter column has room, so the whole page shows without scrolling.
        var side = Col(formatList, harmonies); side.Spacing = 18;
        if (state.History.Count > 0)
        {
            var recent = new WrapPanel { MaxHeight = 84, ClipToBounds = true };
            foreach (var h in state.History.Take(18)) { var dot = Dot(h, () => Select(h)); dot.Margin = new Thickness(0, 0, 8, 8); recent.Children.Add(dot); }
            var block = Col(Label("الألوان الأخيرة"), recent); block.Margin = new Thickness(6, 0); side.Children.Add(block);
        }
        var main = Col(specimen, Surface(editor)); main.Spacing = 18;
        content.Children.Add(Pair(main, side, 1.1, 1));
    }
}
