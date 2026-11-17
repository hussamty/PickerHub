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
    static Spectrum() => AffectsRender<Spectrum>(IsFocusedProperty);
    public HsvColor Value { get; set; }
    public event Action<string>? Selected;
    public event Action<string>? PreviewChanged;
    public Spectrum(string hex)
    {
        Value = Color.Parse(hex).ToHsv(); Height = 120; MinWidth = 150; Cursor = new Cursor(StandardCursorType.Cross);
        FlowDirection = FlowDirection.LeftToRight; Focusable = true; AutomationProperties.SetName(this, L.T("تشبع اللون وسطوعه"));
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus(); e.Pointer.Capture(this); Update(e.GetPosition(this));
        };
        PointerMoved += (_, e) => { if (e.Pointer.Captured == this) Update(e.GetPosition(this)); };
        PointerReleased += (_, e) => { if (e.Pointer.Captured == this) e.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => Selected?.Invoke(CurrentHex());
        KeyDown += (_, e) =>
        {
            double s = Value.S, v = Value.V;
            switch (e.Key)
            {
                case Key.Left: s -= .01; break;
                case Key.Right: s += .01; break;
                case Key.Up: v += .01; break;
                case Key.Down: v -= .01; break;
                default: return;
            }
            e.Handled = true;
            Value = HsvColor.FromHsv(Value.H, Math.Clamp(s, 0, 1), Math.Clamp(v, 0, 1));
            InvalidateVisual(); PreviewChanged?.Invoke(CurrentHex());
        };
        KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { e.Handled = true; Selected?.Invoke(CurrentHex()); } };
    }
    public string CurrentHex() { var c = Value.ToRgb(); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
    void Update(Point p)
    {
        Value = HsvColor.FromHsv(Value.H, Math.Clamp(p.X / Bounds.Width, 0, 1), 1 - Math.Clamp(p.Y / Bounds.Height, 0, 1));
        InvalidateVisual(); PreviewChanged?.Invoke(CurrentHex());
    }
    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(Bounds.Size);
        ctx.DrawRectangle(new SolidColorBrush(HsvColor.FromHsv(Value.H, 1, 1).ToRgb()), null, rect);
        ctx.DrawRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = [new(Colors.White, 0), new(Colors.Transparent, 1)] }, null, rect);
        ctx.DrawRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = [new(Colors.Transparent, 0), new(Colors.Black, 1)] }, null, rect);
        var pt = new Point(Value.S * Bounds.Width, (1 - Value.V) * Bounds.Height);
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 4), pt, 6, 6); ctx.DrawEllipse(null, new Pen(Brushes.White, 2), pt, 6, 6);
        if (IsFocused)
        {
            ctx.DrawRectangle(null, new Pen(Brushes.Black, 3), rect.Deflate(3), 11, 11);
            ctx.DrawRectangle(null, new Pen(Brushes.White, 1), rect.Deflate(3), 11, 11);
        }
    }
}

internal sealed partial class MainWindow
{
    Action? refreshColor;
    void ColorPage()
    {
        var c = Core.Describe(state.Color);
        var formats = c.GetProperty("formats");
        string ink = c.Text("ink");

        // The specimen: the selected color at full size, labelled in its own readable ink. Its face is laid out
        // left to right, so the name is placed at the reading start (right in Arabic, left in English) and the
        // code and actions at the other end. In English the name already is the English name, so it is not repeated.
        bool arabic = L.Language == "ar";
        HorizontalAlignment start = arabic ? HorizontalAlignment.Right : HorizontalAlignment.Left, end = arabic ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var title = Col(Text(c.Text("name"), Typography.Display, true), Text(c.Text("base") + L.T("، ") + c.Text("shade"), Typography.Label)); title.Spacing = 0; title.VerticalAlignment = VerticalAlignment.Bottom; title.HorizontalAlignment = start; title.FlowDirection = L.Flow;
        var code = Mono(state.Color, Typography.Heading, true); code.VerticalAlignment = VerticalAlignment.Bottom; code.HorizontalAlignment = end;
        var english = Mono(c.Text("english").ToLowerInvariant(), Typography.Label); english.VerticalAlignment = VerticalAlignment.Top; english.HorizontalAlignment = start; english.Opacity = .8; english.IsVisible = arabic;
        var quick = Cluster(LiquidOn(ink, "copy", L.T("نسخ اللون"), () => Copy(PickValue(state, state.Color))), LiquidOn(ink, "download", L.T("تصدير اللون"), () => Export(new Palette { Name = c.Text("name"), Colors = [Swatch.From(state.Color)] })));
        quick.HorizontalAlignment = end; quick.VerticalAlignment = VerticalAlignment.Top; quick.FlowDirection = FlowDirection.LeftToRight;
        var face = new Panel { FlowDirection = FlowDirection.LeftToRight }; face.Children.AddRange([english, quick, title, code]);
        var specimen = new Border { Background = Paint(state.Color), CornerRadius = new CornerRadius(24), Padding = new Thickness(22), Height = 136, Child = face };
        specimen[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"); specimen.BorderThickness = new Thickness(1);
        Avalonia.Controls.Documents.TextElement.SetForeground(specimen, Paint(ink));
        void PreviewSpecimen(string value)
        {
            var color = Core.Describe(value);
            var textInk = color.Text("ink");
            ((TextBlock)title.Children[0]).Text = color.Text("name");
            ((TextBlock)title.Children[1]).Text = color.Text("base") + L.T("، ") + color.Text("shade");
            code.Text = value;
            english.Text = color.Text("english").ToLowerInvariant();
            specimen.Background = Paint(value);
            Avalonia.Controls.Documents.TextElement.SetForeground(specimen, Paint(textInk));
            foreach (var b in quick.Children.OfType<Button>())
            {
                b.Foreground = Paint(textInk); b.Background = Paint(textInk, .16); b.BorderBrush = Paint(textInk, .28);
            }
        }

        var hex = new TextBox { Text = state.Color, MaxLength = 7, FlowDirection = FlowDirection.LeftToRight, FontFamily = Code, FontSize = 17, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(hex, "hex-input"); AutomationProperties.SetName(hex, L.T("قيمة HEX"));
        var error = Of(Text("", Typography.Label), "danger"); error.IsVisible = false;
        void Apply() { var next = "#" + (hex.Text ?? "").Trim().TrimStart('#'); if (!Workspace.Hex(next)) { error.IsVisible = true; error.Text = L.T("أدخل ست خانات HEX."); return; } Select(next); }
        hex.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; _ = Guard(() => { Apply(); return Task.CompletedTask; }); } };
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), FlowDirection = FlowDirection.LeftToRight };
        input.Children.Add(hex);
        var apply = Of(Liquid("check", L.T("اعتماد HEX"), Apply), "still"); Grid.SetColumn(apply, 2); input.Children.Add(apply);

        var spectrum = new Spectrum(state.Color) { Height = 120 };
        spectrum.PreviewChanged += PreviewSpecimen;
        void Commit(string value)
        {
            if (value == state.Color) return;
            _ = Guard(() => { Select(value, false); return Task.CompletedTask; });
        }
        spectrum.Selected += Commit;
        var hue = new Slider { Height = 40, MinHeight = 40, Minimum = 0, Maximum = 359.99, Value = spectrum.Value.H, FlowDirection = FlowDirection.LeftToRight };
        AutomationProperties.SetName(hue, L.T("درجة اللون"));
        var hueBrush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(HsvColor.FromHsv(i * 60 % 360, 1, 1).ToRgb(), i / 6d));
        hue.Background = hueBrush;
        hue.ValueChanged += (_, _) =>
        {
            spectrum.Value = HsvColor.FromHsv(hue.Value, spectrum.Value.S, spectrum.Value.V);
            spectrum.InvalidateVisual(); PreviewSpecimen(spectrum.CurrentHex());
        };
        hue.PointerCaptureLost += (_, _) => Commit(spectrum.CurrentHex());
        hue.KeyUp += (_, _) => Commit(spectrum.CurrentHex());
        var save = Btn("plus", L.T("إضافة إلى لوحة"), () => AddToPalette([state.Color]), true); save.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(save, "add-editor-color");
        var editor = Col(Field("HEX", input), error, save, new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = spectrum }, hue);

        // Every format is one click away from the clipboard.
        var rows = new StackPanel { Spacing = 2 };
        var extraRows = new StackPanel { Spacing = 2 };
        var formatRows = new Dictionary<string, (Button Button, TextBlock Value)>();
        foreach (var key in new[] { "HEX", "RGB", "RGBA", "HSL", "HSV", "HSB", "OKLCH", "CMYK" })
        {
            string value = formats.Text(key); var b = Of(Btn("", () => Copy(Core.Describe(state.Color).GetProperty("formats").Text(key))), "row");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*,20"), FlowDirection = FlowDirection.LeftToRight };
            grid.Children.Add(Tone(Mono(key, Typography.Caption, true), "Muted"));
            var val = Mono(value, 13); val.TextWrapping = TextWrapping.NoWrap; val.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(val, 1); grid.Children.Add(val);
            var copy = Tone(new Icon("copy", 15), "Muted"); Grid.SetColumn(copy, 2); grid.Children.Add(copy); b.Content = grid;
            AutomationProperties.SetName(b, L.T("نسخ ") + key + " " + value); ToolTip.SetTip(b, L.T("نسخ ") + key); ToolTip.SetPlacement(b, PlacementMode.Top); (key is "HEX" or "RGB" or "HSL" or "OKLCH" ? rows : extraRows).Children.Add(b); formatRows[key] = (b, val);
        }
        extraRows.Children.Add(Label(L.T("sRGB · CMYK تقريبي للطباعة")));
        rows.Children.Add(new Expander { Header = L.T("المزيد من الصيغ"), Content = extraRows, HorizontalAlignment = HorizontalAlignment.Stretch });
        var formatList = Section(L.T("صيغ اللون"), rows, Of(Liquid("copy", L.T("نسخ جميع الصيغ"), () => Copy(string.Join("\n", formats.EnumerateObject().Select(k => k.Name + ": " + k.Value.GetString())))), "still"));

        var colors = Core.Harmony(state.Color, harmony);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", colors.Select(_ => "*"))), Height = 64, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < colors.Length; i++) { var h = colors[i]; var b = Of(Chip(h, () => Select(h), colors.Length > 5 ? "" : null), "flat"); b.CornerRadius = new CornerRadius(0); ToolTip.SetTip(b, h); Grid.SetColumn(b, i); strip.Children.Add(b); }
        var harmonyTools = Line(8, Choose(modes, harmony, i => { harmony = i; Render(); }), Of(Liquid("plus", L.T("إضافة المجموعة إلى لوحة"), () => AddToPalette(colors)), "still"));
        var harmonies = Section(L.T("تناغم الألوان"), new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = strip }, harmonyTools);

        // Recent colors sit under the harmonies, where the shorter column has room, so the whole page shows without scrolling.
        var side = Col(formatList, harmonies); side.Spacing = 20;
        var recent = new WrapPanel();
        var block = Col(Line(10, Label(L.T("الألوان الأخيرة")), Btn(L.T("عرض السجل"), () => Go(5))), recent); block.Margin = new Thickness(6, 0); side.Children.Add(block);
        refreshColor = () =>
        {
            c = Core.Describe(state.Color); formats = c.GetProperty("formats");
            hex.Text = state.Color;
            PreviewSpecimen(state.Color);
            foreach (var (key, row) in formatRows) { row.Value.Text = formats.Text(key); AutomationProperties.SetName(row.Button, L.T("نسخ ") + key + " " + formats.Text(key)); }
            strip.Children.Clear();
            var colorsNow = Core.Harmony(state.Color, harmony);
            strip.ColumnDefinitions = new ColumnDefinitions(string.Join(",", colorsNow.Select(_ => "*")));
            for (int i = 0; i < colorsNow.Length; i++) { var h = colorsNow[i]; var b = Chip(h, () => Select(h)); b.CornerRadius = new CornerRadius(0); Grid.SetColumn(b, i); strip.Children.Add(b); }
            recent.Children.Clear(); block.IsVisible = state.History.Count > 0;
            foreach (var h in state.History.Take(8)) { var dot = Dot(h, () => Select(h)); dot.Margin = new Thickness(0, 0, 8, 8); recent.Children.Add(dot); }
            StyleShell();
            if (undoButton != null) undoButton.IsEnabled = edits.CanUndo;
            if (redoButton != null) redoButton.IsEnabled = edits.CanRedo;
        };
        refreshColor();
        var main = Col(specimen, Surface(editor)); main.Spacing = 20;
        content.Children.Add(Pair(main, side, 1.1, 1));
    }
}
