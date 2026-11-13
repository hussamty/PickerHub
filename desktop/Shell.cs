using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace PickerHub;

// Window frame: header with the global eyedropper, the page area, and the spine navigation.
internal sealed partial class MainWindow
{
    readonly Spine spine = new();
    readonly TextBlock pageTitle = Text("", 28, true);
    readonly TextBlock pageDescription = Label("");
    readonly Border currentDot = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center };
    Button? themeButton;
    readonly string[] symbols = ["color-picker", "palette", "contrast-2", "photo", "color-swatch", "history"];
    readonly string[] descriptions = ["اختر لوناً واضبطه وانسخ قيمه.", "ألوان مشاريعك في مكان واحد.", "تأكد من وضوح النص على الخلفية.", "استخرج لوحة ألوان من صورتك.", "امزج الألوان واصنع تدرجك.", "كل الألوان التي التقطتها مؤخراً."];

    void BuildShell()
    {
        this[!BackgroundProperty] = new DynamicResourceExtension("Canvas");
        this[!ForegroundProperty] = new DynamicResourceExtension("Ink");

        // The eyedropper keeps its top-left position: it is the one global action.
        eyedropper.Height = 44; eyedropper.HorizontalAlignment = HorizontalAlignment.Left; eyedropper.VerticalAlignment = VerticalAlignment.Center;
        eyedropper.Content = Line(9, new Icon("color-picker", 19), new TextBlock { Text = "القطّارة", VerticalAlignment = VerticalAlignment.Center },
            new Border { Background = Paint("#FFFFFF", .2), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "F8", FontSize = 11, FontFamily = Code } });
        eyedropper.FlowDirection = FlowDirection.RightToLeft;
        AutomationProperties.SetName(eyedropper, "القطّارة");

        status.Text = "جاهز لالتقاط الألوان"; status.FontSize = 12; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        Tone(status, "Muted"); status.FlowDirection = FlowDirection.RightToLeft;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var notice = Of(new Border { Child = status, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0) }, "status");

        var brand = Line(9, currentDot, new TextBlock { Text = "PickerHub", FontSize = 19, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        brand.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 72, Margin = new Thickness(24, 0), FlowDirection = FlowDirection.LeftToRight };
        header.Children.Add(eyedropper); Grid.SetColumn(notice, 1); header.Children.Add(notice); Grid.SetColumn(brand, 2); header.Children.Add(brand);

        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            AutomationProperties.SetAutomationId(spine.Add(symbols[i], pages[i], () => { page = index; Render(); }), "nav-" + i);
        }
        spine.Divide();
        themeButton = spine.Add("moon", "", () => _ = Guard(() => { Edit(() => state.Theme = dark ? "light" : "dark"); return Task.CompletedTask; }), false);
        spine.Add("settings", "الإعدادات", () => _ = Guard(Settings), false);
        spine.Margin = new Thickness(0, 0, 16, 16);

        var heading = Col(pageTitle, pageDescription); heading.Spacing = 2; heading.Margin = new Thickness(0, 4, 0, 18);
        var work = new DockPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(28, 0, Spine.Closed + 36, 0) };
        DockPanel.SetDock(heading, Dock.Top); work.Children.Add(heading);
        work.Children.Add(new ScrollViewer { Content = content, Padding = new Thickness(0, 0, 0, 24), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var body = new Panel(); body.Children.Add(work); body.Children.Add(spine);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        shell.Children.Add(header); Grid.SetRow(body, 1); shell.Children.Add(body);
        Content = shell;
    }
    void StyleShell()
    {
        pageTitle.Text = pages[page]; pageDescription.Text = descriptions[page];
        currentDot.Background = Paint(state.Color); ToolTip.SetTip(currentDot, state.Color);
        spine.Motion = Motion; spine.Activate(page);
        if (themeButton != null) spine.Label(themeButton, dark ? "sun" : "moon", dark ? "الوضع الفاتح" : "الوضع الداكن");
    }
    async Task Settings()
    {
        var d = Dialog("الإعدادات"); d.Width = 440;
        var panel = Col(Text("الإعدادات", 20, true)); panel.Spacing = 12;
        var calm = new CheckBox { Content = "تقليل الحركة", IsChecked = state.ReduceMotion, IsEnabled = !damaged };
        calm.IsCheckedChanged += async (_, _) => await Guard(() => { Edit(() => state.ReduceMotion = calm.IsChecked == true); d.Classes.Set("calm", !Motion); return Task.CompletedTask; });
        panel.Children.Add(Col(Label("المظهر"), calm, Btn(dark ? "sun" : "moon", dark ? "الوضع الفاتح" : "الوضع الداكن", () => { Edit(() => state.Theme = dark ? "light" : "dark"); d.Close(); })));
        if (Picker.UsesPortal)
        {
            var mode = new CheckBox { Content = "استخدام عدسة PickerHub عبر مشاركة الشاشة", IsChecked = state.UseWaylandScreenSharing, IsEnabled = !damaged };
            Button? stop = null;
            mode.IsCheckedChanged += async (_, _) => await Guard(() =>
            {
                Edit(() => state.UseWaylandScreenSharing = mode.IsChecked == true);
                if (!state.UseWaylandScreenSharing)
                {
                    WaylandCapture.Stop();
                    if (stop != null) stop.IsVisible = false;
                }
                return Task.CompletedTask;
            });
            var section = Col(Label("القطّارة"), mode, Label("تستخدم القطّارة العادية منتقي ألوان النظام. فعّل المشاركة للعدسة الحيّة والتبديل بين النوافذ أثناء الالتقاط."));
            if (WaylandCapture.IsSharing) section.Children.Add(stop = Btn("إنهاء مشاركة الشاشة", async () =>
            {
                await Picker.RequestPermissionAgain();
                d.Close();
                status.Text = "توقفت مشاركة الشاشة.";
            }));
            panel.Children.Add(section);
        }
        panel.Children.Add(Col(Label("المكتبة المحلية"),
            Btn("download", "تنزيل نسخة احتياطية", async () => { d.Close(); await WriteFile("PickerHub-backup.json", Serialize(state)); }),
            Btn("upload", "استعادة نسخة احتياطية", async () => { d.Close(); await Restore(); })));
        panel.Children.Add(Choices(Btn("إغلاق", () => d.Close())));
        foreach (var group in panel.Children.OfType<StackPanel>()) foreach (var b in group.Children.OfType<Button>()) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        d.Content = panel;
        await d.ShowDialog(this);
    }
}
