using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace PickerHub;

// Building blocks shared by every page. Colors and states come from Theme.axaml.
internal sealed partial class MainWindow
{
    static readonly FontFamily Code = new("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Menlo, Consolas, monospace");
    static IBrush Paint(string hex) => new SolidColorBrush(Color.Parse(hex));
    static IBrush Paint(string hex, double alpha) { var c = Color.Parse(hex); return new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B)); }
    static T Tone<T>(T control, string token) where T : Control { control[!TextElement.ForegroundProperty] = new DynamicResourceExtension(token); return control; }
    static T Of<T>(T control, params string[] classes) where T : Control { foreach (var name in classes) control.Classes.Add(name); return control; }

    static StackPanel Col(params Control[] controls) { var p = new StackPanel { Spacing = 10 }; p.Children.AddRange(controls); return p; }
    // Wrapping toolbar row.
    static WrapPanel Row(params Control[] controls) { var p = new WrapPanel(); foreach (var c in controls) { c.Margin = new Thickness(4); p.Children.Add(c); } return p; }
    // Non-wrapping run, for button contents and liquid clusters.
    static StackPanel Line(double spacing, params Control[] controls) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing }; p.Children.AddRange(controls); return p; }
    // Liquid buttons that share one fluid body: a swelling button flows into its neighbours.
    static Goo Cluster(params Control[] controls) { var goo = new Goo(); foreach (var c in controls) { c.Classes.Add("goo"); goo.Add(c); } return goo; }

    static TextBlock Text(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, LineHeight = size * 1.7, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        // Arabic final letters carry dots below the line box at display sizes.
        Padding = new Thickness(0, 0, 0, size >= 18 ? size * .2 : 0)
    };
    static TextBlock Mono(string text, double size = 13, bool bold = false)
    {
        var t = Text(text, size, bold); t.FontFamily = Code; t.FlowDirection = FlowDirection.LeftToRight; return t;
    }
    static TextBlock Label(string value)
    {
        var t = Of(Text(value, 12), "muted");
        if (Workspace.Hex(value)) { t.FontFamily = Code; t.FlowDirection = FlowDirection.LeftToRight; t.HorizontalAlignment = HorizontalAlignment.Right; }
        return t;
    }
    static Control Field(string label, Control input) => Col(Label(label), input);

    Button Btn(string label, Func<Task> act, bool primary = false)
    {
        var b = Of(new Button { Content = label }, "ph"); if (primary) b.Classes.Add("primary");
        b.Click += async (_, _) => await Guard(act); return b;
    }
    Button Btn(string label, Action act, bool primary = false) => Btn(label, () => { act(); return Task.CompletedTask; }, primary);
    Button Btn(string symbol, string label, Func<Task> act, bool primary = false)
    {
        var b = Btn(label, act, primary); b.Content = Line(8, new Icon(symbol, 18), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(b, label); return b;
    }
    Button Btn(string symbol, string label, Action act, bool primary = false) => Btn(symbol, label, () => { act(); return Task.CompletedTask; }, primary);
    // Circular icon action with a pill tooltip.
    Button Liquid(string symbol, string label, Func<Task> act)
    {
        var b = Of(Btn("", act), "liquid"); b.Content = new Icon(symbol, 18) { HorizontalAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(b, label); ToolTip.SetPlacement(b, PlacementMode.Top); ToolTip.SetVerticalOffset(b, -6); ToolTip.SetShowDelay(b, 180);
        AutomationProperties.SetName(b, label); return b;
    }
    Button Liquid(string symbol, string label, Action act) => Liquid(symbol, label, () => { act(); return Task.CompletedTask; });
    // The same control placed on a sampled color: tinted with that color's readable ink.
    Button LiquidOn(string ink, string symbol, string label, Func<Task> act)
    {
        var b = Liquid(symbol, label, act); b.Background = Paint(ink, .16); b.BorderBrush = Paint(ink, .28); b.Foreground = Paint(ink); return b;
    }
    Button Chip(string hex, Action act, string? label = null)
    {
        var b = Of(Btn(label ?? hex, act), "swatch"); b.Background = Paint(hex); b.Foreground = Paint(Core.Describe(hex).Text("ink"));
        b.FontFamily = Code; b.FlowDirection = FlowDirection.LeftToRight; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(b, hex); return b;
    }
    Button Dot(string hex, Action act, double size = 34)
    {
        var b = Chip(hex, act, ""); b.Width = b.Height = b.MinHeight = size; b.Padding = new Thickness(0); b.CornerRadius = new CornerRadius(size / 2);
        b[!BorderBrushProperty] = new DynamicResourceExtension("Line"); b.BorderThickness = new Thickness(1);
        ToolTip.SetTip(b, hex); ToolTip.SetPlacement(b, PlacementMode.Top); return b;
    }
    static Glass Surface(Control child) => new() { Child = child, Padding = new Thickness(20) };
    static Control Section(string title, Control body, Control? actions = null)
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14), MinHeight = 40 };
        if (actions != null) { DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions); }
        top.Children.Add(Text(title, 16, true));
        return Surface(Col(top, body));
    }
    static Control Empty(string title, string help, string symbol, Control? action = null)
    {
        var stack = Col(Tone(new Icon(symbol, 34), "Muted"), Text(title, 18, true), Label(help));
        stack.Spacing = 12; stack.HorizontalAlignment = HorizontalAlignment.Center; stack.Margin = new Thickness(20, 44);
        foreach (var child in stack.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        if (action != null) { action.HorizontalAlignment = HorizontalAlignment.Center; action.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(action); }
        return Surface(stack);
    }
    // Two columns that stack when the window is too narrow for paired editors.
    static Control Pair(Control first, Control second, double firstWidth = 1, double secondWidth = 1)
    {
        var grid = new Grid();
        bool? wasNarrow = null;
        grid.Children.Add(first); grid.Children.Add(second);
        void Layout()
        {
            bool narrow = grid.Bounds.Width < 720;
            if (wasNarrow == narrow) return;
            wasNarrow = narrow;
            grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : FormattableString.Invariant($"{firstWidth}*,18,{secondWidth}*"));
            grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,18,Auto" : "Auto");
            Grid.SetColumn(second, narrow ? 0 : 2); Grid.SetRow(second, narrow ? 2 : 0);
        }
        Layout(); grid.SizeChanged += (_, _) => Layout(); return grid;
    }
    // A new page settles in from slightly below, so the change of tool is felt.
    void Arrive()
    {
        if (!Motion) { content.Transitions = null; content.Opacity = 1; content.RenderTransform = null; return; }
        content.Transitions = null; content.Opacity = 0; content.RenderTransform = TransformOperations.Parse("translateY(8px)");
        Dispatcher.UIThread.Post(() =>
        {
            content.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() }];
            content.Opacity = 1; content.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Background);
    }

    async Task Copy(string value) { if (Clipboard == null) throw new InvalidOperationException(L.T("الحافظة غير متاحة.")); await Clipboard.SetValueAsync(DataFormat.Text, value); status.Text = L.T("نُسخت القيمة إلى الحافظة"); }
    static Control Choices(params Control[] buttons) { var row = Line(8, buttons); row.Margin = new Thickness(0, 8, 0, 0); return row; }
    async Task<string?> Prompt(string title, string initial, bool hex = false)
    {
        var input = new TextBox { Text = initial, MaxLength = hex ? 7 : 100, MinHeight = 42, VerticalContentAlignment = VerticalAlignment.Center, FlowDirection = hex ? FlowDirection.LeftToRight : L.Flow };
        if (hex) input.FontFamily = Code;
        AutomationProperties.SetName(input, title);
        var feedback = Of(Text("", 12), "danger"); feedback.IsVisible = false;
        var d = Dialog(title);
        void OK()
        {
            var val = (input.Text ?? "").Trim(); if (hex) val = "#" + val.TrimStart('#').ToUpperInvariant();
            if (val.Length > 0 && (!hex || Workspace.Hex(val))) d.Close(val);
            else { feedback.IsVisible = true; feedback.Text = hex ? L.T("أدخل ست خانات HEX.") : L.T("أدخل اسماً."); }
        }
        d.Content = Col(Text(title, 18, true), input, feedback, Choices(Btn(L.T("حفظ"), OK, true), Btn(L.T("إلغاء"), () => d.Close())));
        input.KeyDown += (_, ev) => { if (ev.Key == Avalonia.Input.Key.Enter) { ev.Handled = true; OK(); } };
        d.Opened += () => { input.Focus(); input.SelectAll(); }; return await d.Show<string?>();
    }
    async Task<bool> Confirm(string title)
    {
        var d = Dialog(title); d.Content = Col(Text(title, 16), Choices(Btn(L.T("تأكيد"), () => d.Close(true), true), Btn(L.T("إلغاء"), () => d.Close(false))));
        return await d.Show<bool>();
    }
    async Task<int?> SelectOption(string title, string[] choices, int start = 0)
    {
        var d = Dialog(title); int picked = start; var choice = Choose(choices, start, i => picked = i); choice.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(choice, title);
        d.Content = Col(Text(title, 16), choice, Choices(Btn(L.T("اختيار"), () => d.Close((int?)picked), true), Btn(L.T("إلغاء"), () => d.Close())));
        return await d.Show<int?>();
    }
}
