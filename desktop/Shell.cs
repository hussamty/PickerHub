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
    // Every tool in one place: its name, icon, one-line purpose and the method that builds its page.
    readonly record struct Tool(string Title, string Symbol, string About, Action Build);
    Tool[] tools = [];
    const int DailyTool = 6;

    static string Idle => L.T("جاهز لالتقاط الألوان");
    void DefineTools()
    {
        tools =
        [
            new(L.T("ملتقط الألوان"), "color-picker", L.T("اختر لوناً واضبطه وانسخ قيمه."), ColorPage),
            new(L.T("لوحات الألوان"), "palette", L.T("ألوان مشاريعك في مكان واحد."), Palettes),
            new(L.T("فحص التباين"), "contrast-2", L.T("تأكد من وضوح النص على الخلفية."), ContrastPage),
            new(L.T("تحليل الصور"), "photo", L.T("استخرج كل ألوان صورتك، الكبيرة والصغيرة."), Images),
            new(L.T("محرّر التدرجات"), "color-swatch", L.T("امزج الألوان واصنع تدرجك."), Gradients),
            new(L.T("السجل"), "history", L.T("كل الألوان التي التقطتها مؤخراً."), History),
            new(L.T("باقة اليوم"), "sparkles", L.T("باقة متناسقة من الطبيعة، واحدة كل 24 ساعة."), DailyPage),
        ];
    }
    void BuildShell()
    {
        DefineTools();
        this[!BackgroundProperty] = new DynamicResourceExtension("Canvas");
        // The switch plays the change: the room behind the glass follows the knob, and the theme itself turns at the midpoint.
        themeSwitch.Scrubbed += mix => backdrop.Mix = mix;
        // Turning the theme restyles what is already on screen; nothing is rebuilt, and the library is written once the knob has come to rest.
        themeSwitch.Toggled += light =>
        {
            if (dark != light) return;
            if (damaged) { status.Text = L.T("المكتبة للقراءة فقط، لذلك لا يمكن حفظ المظهر."); return; }
            state.FollowSystemTheme = false; state.Theme = light ? "light" : "dark"; Glass.Dark = dark; RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light; App.Accent(dark); themeSwitch.Sync(!dark);
        };
        themeSwitch.Settled += () => _ = Guard(() => { if (!damaged && Serialize(state) != lastSaved) { var next = Serialize(state); writer.Enqueue(next); edits.Commit(lastSaved, next); lastSaved = next; } return Task.CompletedTask; });
        ToolTip.SetTip(themeSwitch, L.T("المظهر")); ToolTip.SetPlacement(themeSwitch, PlacementMode.Bottom);
        // A click anywhere else lets a pinned spine close.
        AddHandler(PointerPressedEvent, (_, e) => { if (e.Source is Visual hit && hit != spine && !spine.IsVisualAncestorOf(hit)) spine.Release(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this[!ForegroundProperty] = new DynamicResourceExtension("Ink");

        // The eyedropper keeps its top-left position: it is the one global action.
        eyedropper.Height = 44; eyedropper.CornerRadius = new CornerRadius(22); eyedropper.HorizontalAlignment = HorizontalAlignment.Left; eyedropper.VerticalAlignment = VerticalAlignment.Center;
        eyedropper.Content = Line(9, new Icon("color-picker", 19), new TextBlock { Text = L.T("القطّارة"), VerticalAlignment = VerticalAlignment.Center },
            Of(new Border { VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "F8", FontSize = 11, FontFamily = Code } }, "keycap"));
        eyedropper.FlowDirection = L.Flow;
        AutomationProperties.SetName(eyedropper, L.T("القطّارة"));

        status.Text = Idle; status.FontSize = 12;
        // A message is news for a few seconds, then the line goes back to rest.
        var settle = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        settle.Tick += (_, _) => { settle.Stop(); if (!picking) status.Text = Idle; };
        status.PropertyChanged += (_, change) => { if (change.Property == TextBlock.TextProperty) { settle.Stop(); if (status.Text != Idle) settle.Start(); } }; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        Tone(status, "Muted"); status.FlowDirection = L.Flow;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var notice = Of(new Border { Child = status, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0) }, "status");

        // The wordmark follows the TrineHub rule: only the capital H takes the signal color.
        var wordmark = new TextBlock { FontSize = 19, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var signal = new Avalonia.Controls.Documents.Run("H"); signal[!Avalonia.Controls.Documents.TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentText");
        wordmark.Inlines = [new Avalonia.Controls.Documents.Run("Picker"), signal, new Avalonia.Controls.Documents.Run("ub")];
        undoButton = Liquid("chevron-right", L.T("تراجع · Ctrl+Z"), () => Travel(false));
        redoButton = Liquid("chevron-left", L.T("إعادة · Ctrl+Shift+Z"), () => Travel(true));
        var brand = Line(8, undoButton, redoButton, themeSwitch);
        // The mascot, a drop of color, sits beside the name it belongs to.
        using var portrait = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PickerHub/Assets/icon-64.png"));
        var mascot = new Image { Source = new Avalonia.Media.Imaging.Bitmap(portrait), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(mascot, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        var titlebar = BuildTitlebar(Line(8, mascot, wordmark, currentDot));
        brand.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 60, Margin = new Thickness(20, 0), FlowDirection = FlowDirection.LeftToRight };
        header.Children.Add(eyedropper); Grid.SetColumn(notice, 1); header.Children.Add(notice); Grid.SetColumn(brand, 2); header.Children.Add(brand);

        for (int i = 0; i < tools.Length; i++)
        {
            int index = i;
            AutomationProperties.SetAutomationId(spine.Add(tools[i].Symbol, tools[i].Title, () => Go(index)), "nav-" + i);
        }
        spine.Divide();
        spine.Add("settings", L.T("الإعدادات"), () => _ = Guard(Settings), false);
        spine.Margin = new Thickness(0, 0, 0, 16);

        // Title and its one-line purpose share a row, so the page keeps the height for its tools.
        pageDescription.VerticalAlignment = VerticalAlignment.Bottom; pageDescription.Margin = new Thickness(0, 0, 0, 9); pageDescription.TextWrapping = TextWrapping.Wrap;
        var heading = new WrapPanel { Orientation = Orientation.Horizontal, FlowDirection = L.Flow }; heading.Children.Add(pageTitle); pageDescription.Margin = new Thickness(14, 8, 0, 4); heading.Children.Add(pageDescription); heading.Margin = new Thickness(18, 0, 18, 6);
        var work = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = L.Flow,
            Margin = new Thickness(10, 0, Spine.Closed + 4, 0), MaxWidth = 1336 };
        work.Children.Add(heading);
        // Bottom breathing room belongs to the content extent, not presenter padding.
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); work.Children.Add(scroll);

        var body = new Panel(); body.Children.Add(work); body.Children.Add(spine); body.SizeChanged += (_, _) => spine.Fit(body.Bounds.Height);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        shell.Children.Add(header); Grid.SetRow(errorBanner, 1); shell.Children.Add(errorBanner); Grid.SetRow(body, 2); shell.Children.Add(body);
        // Dialogs cover the workbench while native window actions remain reachable.
        var room = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        Grid.SetRowSpan(backdrop, 2); room.Children.Add(backdrop); room.Children.Add(titlebar);
        Grid.SetRow(shell, 1); room.Children.Add(shell); Grid.SetRow(overlay, 1); room.Children.Add(overlay);
        Content = room;
    }
    // Changing tool answers at once: the marker moves on this frame and the page is built on the next.
    bool pageQueued;
    void Go(int index)
    {
        page = index; StyleShell();
        QueueRender();
    }
    void QueueRender()
    {
        if (pageQueued) return;
        pageQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { pageQueued = false; Render(); }, Avalonia.Threading.DispatcherPriority.Background);
    }
    void ApplyLanguage()
    {
        L.Language = state.Language; FlowDirection = L.Flow;
        DefineTools();
        spine.Relabel(tools.Select(t => (t.Symbol, t.Title)).ToArray());
        eyedropper.FlowDirection = L.Flow;
        eyedropper.Content = Line(9, new Icon("color-picker", 19), Text(L.T("القطّارة"), 13), Mono("F8", 11));
        AutomationProperties.SetName(eyedropper, L.T("القطّارة"));
        modes = [L.T("متجاورة"), L.T("متكاملة"), L.T("ثلاثية"), L.T("متكاملة منقسمة"), L.T("أحادية اللون"), L.T("عالية التباين")];
        status.FlowDirection = L.Flow; status.Text = Idle;
        if (content.Parent is ScrollViewer scroll && scroll.Parent is Grid work) work.FlowDirection = L.Flow;
        SetTrayMenu();
    }
    void StyleShell()
    {
        pageTitle.Text = tools[page].Title; pageDescription.Text = tools[page].About;
        UpdateWindowChrome();
        currentDot.Background = Paint(state.Color); ToolTip.SetTip(currentDot, state.Color);
        spine.Motion = Motion; spine.Activate(page);
        themeSwitch.Motion = Motion; themeSwitch.Sync(!dark);
    }
    async Task Settings()
    {
        var d = Dialog(L.T("الإعدادات")); d.Width = 440;
        var panel = Col(Text(L.T("الإعدادات"), 20, true), Label("PickerHub " + App.Version)); panel.Spacing = 12;
        var calm = new CheckBox { Content = L.T("تقليل الحركة"), IsChecked = state.ReduceMotion, IsEnabled = !damaged };
        calm.IsCheckedChanged += async (_, _) => await Guard(() => { Edit(() => state.ReduceMotion = calm.IsChecked == true); return Task.CompletedTask; });
        panel.Children.Add(Field(L.T("اللغة"), Choose(["العربية", "English"], state.Language == "ar" ? 0 : 1, i =>
        {
            Edit(() => state.Language = i == 0 ? "ar" : "en", false);
            d.Close(); ApplyLanguage(); Render();
            _ = Guard(Settings);
        })));
        panel.Children.Add(QuickSettings());
        var opaque = new CheckBox { Content = L.T("تقليل الشفافية"), IsChecked = state.ReduceTransparency };
        opaque.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.ReduceTransparency = opaque.IsChecked == true); return Task.CompletedTask; });
        var system = new CheckBox { Content = L.T("اتباع مظهر النظام"), IsChecked = state.FollowSystemTheme };
        system.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.FollowSystemTheme = system.IsChecked == true); return Task.CompletedTask; });
        panel.Children.Add(Col(Label(L.T("المظهر والحركة")), calm, opaque, system));
        if (Picker.UsesPortal)
        {
            var mode = new CheckBox { Content = L.T("استخدام عدسة PickerHub عبر مشاركة الشاشة"), IsChecked = state.UseWaylandScreenSharing, IsEnabled = !damaged };
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
            var section = Col(Label(L.T("القطّارة")), mode, Label(L.T("تستخدم القطّارة العادية منتقي ألوان النظام. فعّل المشاركة للعدسة الحيّة والتبديل بين النوافذ أثناء الالتقاط.")));
            if (WaylandCapture.IsSharing) section.Children.Add(stop = Btn(L.T("إنهاء مشاركة الشاشة"), async () =>
            {
                await Picker.RequestPermissionAgain();
                d.Close();
                status.Text = L.T("توقفت مشاركة الشاشة.");
            }));
            panel.Children.Add(section);
        }
        panel.Children.Add(Col(Label(L.T("المكتبة المحلية")),
            Btn("download", L.T("تنزيل نسخة احتياطية"), async () => { d.Close(); await WriteFile("PickerHub-backup.json", BackupBundle.Export(state)); }),
            Btn("upload", L.T("استعادة نسخة احتياطية"), async () => { d.Close(); await Restore(); })));
        var diagnostic = new CheckBox { Content = L.T("تسجيل أخطاء محلي اختياري"), IsChecked = state.DiagnosticsEnabled };
        diagnostic.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.DiagnosticsEnabled = diagnostic.IsChecked == true, false); SupportLog.Enabled = state.DiagnosticsEnabled; return Task.CompletedTask; });
        panel.Children.Add(diagnostic);
        panel.Children.Add(Label(L.T("يبقى السجل على جهازك؛ لا تُرسل أي بيانات تلقائياً.")));
        panel.Children.Add(Btn(L.T("تنزيل تقرير الدعم"), async () => { d.Close(); await WriteFile("PickerHub-diagnostics.json", Diagnostics()); }));
        panel.Children.Add(Label(L.T("تقرير الدعم لا يتضمن ألوانك أو صورك أو مسارات ملفاتك.")));
        panel.Children.Add(Choices(Btn(L.T("إغلاق"), () => d.Close())));
        foreach (var group in panel.Children.OfType<StackPanel>()) foreach (var b in group.Children.OfType<Button>()) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        d.Content = panel;
        await d.Show();
    }
}
