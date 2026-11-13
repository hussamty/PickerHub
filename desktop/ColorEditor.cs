using Avalonia;
using Avalonia.Controls;
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
        var hex = new TextBox { Text = state.Color, MaxLength = 7, FlowDirection = FlowDirection.LeftToRight, FontSize = 22, FontWeight = FontWeight.SemiBold, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
        AutomationProperties.SetAutomationId(hex, "hex-input");
        var error = Text("", 11); error.IsVisible = false; error.Foreground = Paint(dark ? "#FFB5B5" : "#A42635");
        void Apply() { var next = "#" + (hex.Text ?? "").Trim().TrimStart('#'); if (!Workspace.Hex(next)) { error.IsVisible = true; error.Text = "أدخل ست خانات HEX."; return; } Select(next); }
        hex.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; _ = Guard(() => { Apply(); return Task.CompletedTask; }); } };
        var small = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(10), Background = Paint(state.Color), BorderBrush = Line, BorderThickness = new Thickness(1) };
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"), FlowDirection = FlowDirection.LeftToRight };
        input.Children.Add(small); Grid.SetColumn(hex, 2); input.Children.Add(hex);
        var apply = IconButton("check", "اعتماد HEX", Apply); Grid.SetColumn(apply, 3); input.Children.Add(apply);
        var spectrum = new Spectrum(state.Color); spectrum.Selected += h => _ = Guard(() => { Select(h); return Task.CompletedTask; });
        var hue = new Slider { Height = 40, MinHeight = 40, Minimum = 0, Maximum = 359.99, Value = spectrum.Value.H, FlowDirection = FlowDirection.LeftToRight };
        var hueBrush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(HsvColor.FromHsv(i * 60 % 360, 1, 1).ToRgb(), i / 6d));
        hue.Background = hueBrush;
        hue.ValueChanged += (_, _) => { spectrum.Value = HsvColor.FromHsv(hue.Value, spectrum.Value.S, spectrum.Value.V); spectrum.InvalidateVisual(); };
        hue.PointerCaptureLost += (_, _) => { var rgb = spectrum.Value.ToRgb(); var h = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}"; if (h != state.Color) _ = Guard(() => { Select(h); return Task.CompletedTask; }); };
        hue.KeyUp += (_, _) => { var rgb = spectrum.Value.ToRgb(); var h = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}"; if (h != state.Color) _ = Guard(() => { Select(h); return Task.CompletedTask; }); };
        var names = new DockPanel { LastChildFill = false }; var en = Text(c.Text("english"), 12); en.Foreground = Muted; en.FlowDirection = FlowDirection.LeftToRight; DockPanel.SetDock(en, Dock.Left); en.Margin = new Thickness(12, 0); names.Children.Add(en); names.Children.Add(Text(c.Text("name"), 18, true));
        var editor = Col(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = spectrum }, hue, names, Label(c.Text("base") + "، " + c.Text("shade")), input, error,
            Row(Btn("إضافة إلى لوحة", () => AddToPalette([state.Color]), true), IconButton("download", "تصدير اللون", () => Export(new Palette { Name = c.Text("name"), Colors = [Swatch.From(state.Color)] }))));
        editor.Spacing = 6;
        var rows = new StackPanel { Spacing = 0 };
        foreach (var key in new[] { "HEX", "RGB", "RGBA", "HSL", "HSV", "HSB", "CMYK" })
        {
            string value = formats.Text(key); var b = Btn("", () => Copy(value));
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); b.Padding = new Thickness(0, 6);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("46,*,24"), FlowDirection = FlowDirection.LeftToRight };
            var label = Text(key, 11, true); label.Foreground = Muted; grid.Children.Add(label);
            var val = Text(value, 13); val.FlowDirection = FlowDirection.LeftToRight; Grid.SetColumn(val, 1); grid.Children.Add(val);
            var copy = Icon("copy", Muted, 16); Grid.SetColumn(copy, 2); grid.Children.Add(copy); b.Content = grid;
            ToolTip.SetTip(b, "نسخ " + key); rows.Children.Add(b);
        }
        content.Children.Add(Pair(Section("محرّر اللون", editor), Section("صيغ اللون", rows, IconButton("copy", "نسخ جميع الصيغ", () => Copy(string.Join("\n", formats.EnumerateObject().Select(k => k.Name + ": " + k.Value.GetString()))))), 1.1, 1));
        var colors = Core.Harmony(state.Color, harmony);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", colors.Select(_ => "*"))), FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < colors.Length; i++) { var h = colors[i]; var b = Chip(h, () => Select(h)); b.Height = 54; b.CornerRadius = new CornerRadius(0); b.Margin = new Thickness(0); b.BorderThickness = new Thickness(0); Grid.SetColumn(b, i); strip.Children.Add(b); }
        content.Children.Add(Section("تناغم الألوان", new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = strip },
            Row(Choose(modes, harmony, i => { harmony = i; Render(); }), IconButton("plus", "إضافة المجموعة إلى لوحة", () => AddToPalette(colors)))));
        if (state.History.Count > 0) { var recent = Row(); foreach (var h in state.History.Take(12)) { var b = Chip(h, () => Select(h), " "); b.Width = 34; b.MinHeight = 30; ToolTip.SetTip(b, h); recent.Children.Add(b); } content.Children.Add(Col(Label("الألوان الأخيرة"), recent)); }
    }
}
