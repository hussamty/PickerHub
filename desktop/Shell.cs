using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace PickerHub;

// Window frame: header with the global eyedropper, the page area, and the spine navigation.
internal sealed partial class MainWindow
{
    readonly Spine spine = new();
    readonly TextBlock pageTitle = Text("", 26, true);
    readonly TextBlock pageDescription = Label("");
    readonly Border currentDot = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center };
    readonly ThemeSwitch themeSwitch = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Backdrop backdrop = new();
    readonly string[] symbols = ["color-picker", "palette", "contrast-2", "photo", "color-swatch", "history"];
    readonly string[] descriptions = ["اختر لوناً واضبطه وانسخ قيمه.", "ألوان مشاريعك في مكان واحد.", "تأكد من وضوح النص على الخلفية.", "استخرج لوحة ألوان من صورتك.", "امزج الألوان واصنع تدرجك.", "كل الألوان التي التقطتها مؤخراً."];

    void BuildShell()
    {
        this[!BackgroundProperty] = new DynamicResourceExtension("Canvas");
        // The switch plays the change: the room behind the glass follows the knob, and the theme itself turns at the midpoint.
        themeSwitch.Scrubbed += mix => backdrop.Mix = mix;
        // Turning the theme restyles what is already on screen; nothing is rebuilt, and the library is written once the knob has come to rest.
        themeSwitch.Toggled += light =>
        {
            if (dark != light) return;
            if (damaged) { status.Text = "المكتبة للقراءة فقط، لذلك لا يمكن حفظ المظهر."; return; }
            state.Theme = light ? "light" : "dark"; RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light; App.Accent(dark); themeSwitch.Sync(!dark);
        };
        themeSwitch.Settled += () => _ = Guard(() => { if (!damaged && Serialize(state) != lastSaved) { state.Save(); lastSaved = Serialize(state); } return Task.CompletedTask; });
        ToolTip.SetTip(themeSwitch, "المظهر"); ToolTip.SetPlacement(themeSwitch, PlacementMode.Bottom);
        // A click anywhere else lets a pinned spine close.
        AddHandler(PointerPressedEvent, (_, e) => { if (e.Source is Visual hit && hit != spine && !spine.IsVisualAncestorOf(hit)) spine.Release(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this[!ForegroundProperty] = new DynamicResourceExtension("Ink");

        // The eyedropper keeps its top-left position: it is the one global action.
        eyedropper.Height = 44; eyedropper.CornerRadius = new CornerRadius(22); eyedropper.HorizontalAlignment = HorizontalAlignment.Left; eyedropper.VerticalAlignment = VerticalAlignment.Center;
        eyedropper.Content = Line(9, new Icon("color-picker", 19), new TextBlock { Text = "القطّارة", VerticalAlignment = VerticalAlignment.Center },
            Of(new Border { VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "F8", FontSize = 11, FontFamily = Code } }, "keycap"));
        eyedropper.FlowDirection = FlowDirection.RightToLeft;
        AutomationProperties.SetName(eyedropper, "القطّارة");

        status.Text = "جاهز لالتقاط الألوان"; status.FontSize = 12; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        Tone(status, "Muted"); status.FlowDirection = FlowDirection.RightToLeft;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var notice = Of(new Border { Child = status, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0) }, "status");

        // The wordmark follows the TrineHub rule: only the capital H takes the signal color.
        var wordmark = new TextBlock { FontSize = 19, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var signal = new Avalonia.Controls.Documents.Run("H"); signal[!Avalonia.Controls.Documents.TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentText");
        wordmark.Inlines = [new Avalonia.Controls.Documents.Run("Picker"), signal, new Avalonia.Controls.Documents.Run("ub")];
        var brand = Line(14, themeSwitch, currentDot, wordmark);
        brand.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 68, Margin = new Thickness(24, 0), FlowDirection = FlowDirection.LeftToRight };
        header.Children.Add(eyedropper); Grid.SetColumn(notice, 1); header.Children.Add(notice); Grid.SetColumn(brand, 2); header.Children.Add(brand);

        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            AutomationProperties.SetAutomationId(spine.Add(symbols[i], pages[i], () => Go(index)), "nav-" + i);
        }
        spine.Divide();
        spine.Add("settings", "الإعدادات", () => _ = Guard(Settings), false);
        spine.Margin = new Thickness(0, 0, 0, 16);

        // Title and its one-line purpose share a row, so the page keeps the height for its tools.
        pageDescription.VerticalAlignment = VerticalAlignment.Bottom; pageDescription.Margin = new Thickness(0, 0, 0, 9); pageDescription.TextWrapping = TextWrapping.NoWrap;
        var heading = Line(14, pageTitle, pageDescription); heading.Margin = new Thickness(0, 0, 0, 12);
        var work = new DockPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(28, 0, Spine.Closed + 22, 0), MaxWidth = 1300 };
        DockPanel.SetDock(heading, Dock.Top); work.Children.Add(heading);
        work.Children.Add(new ScrollViewer { Content = content, Padding = new Thickness(0, 0, 0, 24), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var body = new Panel(); body.Children.Add(work); body.Children.Add(spine);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        shell.Children.Add(header); Grid.SetRow(body, 1); shell.Children.Add(body);
        var room = new Panel(); room.Children.Add(backdrop); room.Children.Add(shell); room.Children.Add(overlay);
        Content = room;
    }
    // Changing tool answers at once: the marker moves on this frame and the page is built on the next.
    bool pageQueued;
    void Go(int index)
    {
        page = index; StyleShell();
        if (pageQueued) return;
        pageQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { pageQueued = false; Render(); }, Avalonia.Threading.DispatcherPriority.Background);
    }
    void StyleShell()
    {
        pageTitle.Text = pages[page]; pageDescription.Text = descriptions[page];
        currentDot.Background = Paint(state.Color); ToolTip.SetTip(currentDot, state.Color);
        spine.Motion = Motion; spine.Activate(page);
        themeSwitch.Motion = Motion; themeSwitch.Sync(!dark);
    }
    async Task Settings()
    {
        var d = Dialog("الإعدادات"); d.Width = 440;
        var panel = Col(Text("الإعدادات", 20, true)); panel.Spacing = 12;
        var calm = new CheckBox { Content = "تقليل الحركة", IsChecked = state.ReduceMotion, IsEnabled = !damaged };
        calm.IsCheckedChanged += async (_, _) => await Guard(() => { Edit(() => state.ReduceMotion = calm.IsChecked == true); return Task.CompletedTask; });
        panel.Children.Add(Col(Label("الحركة"), calm));
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
        await d.Show();
    }
}
