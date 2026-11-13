using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Automation;
using System.Xml.Linq;

namespace PickerHub;

internal sealed partial class MainWindow
{
    IBrush Muted => Paint(dark ? "#AFBAC9" : "#606D80");
    IBrush Tint => Paint(dark ? "#253143" : "#F2F5F9");
    IBrush Accent => Paint(dark ? "#AAC9FF" : "#2856B4");
    readonly DockPanel shell = new();
    readonly StackPanel sidebar = new() { Spacing = 5 };
    readonly Border sidebarSurface = new();
    readonly TextBlock pageTitle = new();
    readonly TextBlock pageDescription = new();
    readonly Border notice = new();
    TextBlock? brandLabel;
    Button? settingsButton;
    readonly string[] symbols = ["color-picker", "palette", "contrast-2", "photo", "color-swatch", "history"];
    readonly string[] descriptions = ["اختر لوناً واضبطه وانسخ قيمه.", "ألوان مشاريعك في مكان واحد.", "تأكد من وضوح النص على الخلفية.", "استخرج لوحة ألوان من صورتك.", "امزج الألوان واصنع تدرجك.", "كل الألوان التي التقطتها مؤخراً."];

    new Control Icon(string name, IBrush? ink = null, double size = 19)
    {
        using var stream = AssetLoader.Open(new Uri("avares://PickerHub/Icons/" + name + ".svg"));
        var xml = XDocument.Load(stream);
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var p in xml.Descendants().Where(n => n.Name.LocalName == "path"))
        {
            var d = p.Attribute("d")?.Value;
            if (d == null || p.Attribute("stroke")?.Value == "none") continue;
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(d), Stroke = ink ?? Muted, StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }
    Button IconButton(string symbol, string label, Func<Task> action)
    {
        var b = Btn("", action); b.Content = Icon(symbol); b.Padding = new Thickness(8); b.MinWidth = 34;
        ToolTip.SetTip(b, label); AutomationProperties.SetName(b, label); return b;
    }
    Button IconButton(string symbol, string label, Action action) => IconButton(symbol, label, () => { action(); return Task.CompletedTask; });
    Control Label(string value) { var t = Text(value, 12); t.Foreground = Muted; if (Workspace.Hex(value)) { t.FlowDirection = FlowDirection.LeftToRight; t.HorizontalAlignment = HorizontalAlignment.Right; } return t; }
    Control Field(string label, Control input) => Col(Label(label), input);
    Control Pair(Control first, Control second, double firstWidth = 1, double secondWidth = 1)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{firstWidth}*,18,{secondWidth}*") };
        grid.Children.Add(first); Grid.SetColumn(second, 2); grid.Children.Add(second);
        // Narrow native windows stack editors instead of compressing inputs.
        void Layout() { bool narrow = grid.Bounds.Width < 570; grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : $"{firstWidth}*,18,{secondWidth}*"); grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,18,Auto" : "Auto"); Grid.SetColumn(second, narrow ? 0 : 2); Grid.SetRow(second, narrow ? 2 : 0); }
        grid.AttachedToVisualTree += (_, _) => Layout(); grid.SizeChanged += (_, _) => Layout(); return grid;
    }
    Control Section(string title, Control body, Control? actions = null)
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 13) };
        if (actions != null) { DockPanel.SetDock(actions, Dock.Left); top.Children.Add(actions); }
        var titleText = Text(title, 16, true); titleText.Margin = new Thickness(8, 0); top.Children.Add(titleText);
        return Card(Col(top, body));
    }
    Control Empty(string title, string help, string symbol, Control? action = null)
    {
        var stack = Col(Icon(symbol, Muted, 32), Text(title, 17, true), Label(help));
        stack.Spacing = 12; stack.HorizontalAlignment = HorizontalAlignment.Center; stack.Margin = new Thickness(20, 30);
        if (action != null) stack.Children.Add(action);
        return Card(stack);
    }
    void BuildShell()
    {
        shell.LastChildFill = true; shell.FlowDirection = FlowDirection.LeftToRight;
        var top = new Grid { FlowDirection = FlowDirection.LeftToRight, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 64, Margin = new Thickness(25, 0) };
        var brand = Text("PickerHub", 23, true); brandLabel = brand; brand.FlowDirection = FlowDirection.LeftToRight; brand.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(brand, 1); top.Children.Add(brand);
        eyedropper.HorizontalAlignment = HorizontalAlignment.Left; top.Children.Add(eyedropper);
        var header = new Border { Child = top, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(header, Dock.Top); shell.Children.Add(header);
        sidebarSurface.FlowDirection = FlowDirection.RightToLeft; sidebarSurface.Width = 204; sidebarSurface.Padding = new Thickness(13, 22); sidebarSurface.BorderThickness = new Thickness(0, 0, 0, 0);
        var side = new DockPanel();
        var settings = Btn("الإعدادات", Settings); settingsButton = settings; settings.Content = Row(Icon("settings"), Text("الإعدادات", 13));
        DockPanel.SetDock(settings, Dock.Bottom); side.Children.Add(settings); side.Children.Add(sidebar);
        sidebarSurface.Child = side; DockPanel.SetDock(sidebarSurface, Dock.Right); shell.Children.Add(sidebarSurface);
        var work = new DockPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(24, 16) };
        status.Text = "جاهز لالتقاط الألوان"; notice.Child = status; notice.Padding = new Thickness(12, 6); notice.CornerRadius = new CornerRadius(8); notice.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(notice, Dock.Bottom); work.Children.Add(notice);
        var heading = Col(pageTitle, pageDescription); heading.Spacing = 3; heading.Margin = new Thickness(0, 0, 0, 14);
        DockPanel.SetDock(heading, Dock.Top); work.Children.Add(heading);
        work.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        shell.Children.Add(work); Content = shell;
        void Layout() { sidebarSurface.Width = Bounds.Width < 850 ? 184 : 204; }
        SizeChanged += (_, _) => Layout();

    }
    void StyleShell()
    {
        Background = Paint(dark ? "#121923" : "#F6F7FA"); Foreground = Ink; status.Foreground = Muted;
        sidebarSurface.Background = Paper; notice.Background = Tint;
        if (brandLabel != null) brandLabel.Foreground = Ink;
        if (settingsButton != null) { settingsButton.Background = Paper; settingsButton.Foreground = Ink; settingsButton.BorderBrush = Line; settingsButton.Content = Row(Icon("settings"), Text("الإعدادات", 13)); }
        pageTitle.Text = pages[page]; pageTitle.FontFamily = FontFamily; pageTitle.FontSize = 25; pageTitle.LineHeight = 38; pageTitle.FontWeight = FontWeight.Bold; pageTitle.Foreground = Ink;
        pageDescription.Text = descriptions[page]; pageDescription.FontFamily = FontFamily; pageDescription.FontSize = 12; pageDescription.LineHeight = 22; pageDescription.Foreground = Muted;
        sidebar.Children.Clear();
        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            var b = Btn(pages[i], () => { page = index; Render(); });
            b.Content = Row(Icon(symbols[i], page == i ? Accent : Muted), Text(pages[i], 13, page == i));
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Right;
            b.Padding = new Thickness(10, 9); b.Background = page == i ? Tint : Brushes.Transparent; b.BorderThickness = new Thickness(0);
            AutomationProperties.SetAutomationId(b, "nav-" + i); sidebar.Children.Add(b);
        }
        eyedropper.Content = Row(Icon("color-picker", Brushes.White), new TextBlock { Text = "القطّارة", Foreground = Brushes.White }, new TextBlock { Text = "F8", Foreground = Paint("#CBDCFF"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
    }
    async Task Settings()
    {
        var d = Dialog("الإعدادات"); d.Width = 420;
        d.Content = Col(Text("الإعدادات", 21, true), Label("المظهر"), Btn(dark ? "الوضع الفاتح" : "الوضع الداكن", () => { Edit(() => state.Theme = dark ? "light" : "dark"); d.Close(); }),
            Label("المكتبة المحلية"), Btn("تنزيل نسخة احتياطية", async () => { d.Close(); await WriteFile("PickerHub-backup.json", Serialize(state)); }),
            Btn("استعادة نسخة احتياطية", async () => { d.Close(); await Restore(); }), Btn("إغلاق", () => d.Close()));
        await d.ShowDialog(this);
    }
}
