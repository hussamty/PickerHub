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
    readonly TextBlock pageTitle = Text("", Typography.Display, true);
    readonly TextBlock pageDescription = Label("");
    readonly ThemeSwitch themeSwitch = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Backdrop backdrop = new();
    StackPanel history = new();
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

        // The eyedropper is the one global action: it leads the bar, straight after the identity.
        eyedropper.Height = 40; eyedropper.CornerRadius = new CornerRadius(20); eyedropper.HorizontalAlignment = HorizontalAlignment.Left; eyedropper.VerticalAlignment = VerticalAlignment.Center;
        eyedropper.Content = Line(9, new Icon("color-picker", 19), new TextBlock { Text = L.T("القطّارة"), VerticalAlignment = VerticalAlignment.Center },
            Of(new Border { VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "F8", FontSize = Typography.Caption, FontFamily = Code } }, "keycap"));
        eyedropper.FlowDirection = L.Flow;
        AutomationProperties.SetName(eyedropper, L.T("القطّارة"));

        // Status news no longer lives in the top bar: it rises as a toast (Toast.cs).
        status.Text = Idle;
        var toasts = BuildToast();

        // The wordmark follows the TrineHub rule: only the capital H takes the signal color.
        var wordmark = new TextBlock { FontSize = Typography.Title, FontWeight = FontWeight.Bold, LetterSpacing = -.2, VerticalAlignment = VerticalAlignment.Center };
        var signal = new Avalonia.Controls.Documents.Run("H"); signal[!Avalonia.Controls.Documents.TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentText");
        wordmark.Inlines = [new Avalonia.Controls.Documents.Run("Picker"), signal, new Avalonia.Controls.Documents.Run("ub")];
        undoButton = Liquid("arrow-back-up", "", () => Travel(false));
        redoButton = Liquid("arrow-forward-up", "", () => Travel(true));
        foreach (var step in new[] { undoButton, redoButton }) ToolTip.SetPlacement(step, PlacementMode.Bottom);
        history = Line(4, undoButton, redoButton); history.VerticalAlignment = VerticalAlignment.Center;
        DirectHistory();
        // The mark sits beside the name it belongs to.
        using var portrait = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PickerHub/Assets/icon-small-128.png"));
        var mark = new Image { Source = new Avalonia.Media.Imaging.Bitmap(portrait), Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(mark, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);

        // One row, read in order: who this is and the one global action, then free space to move the
        // window by, then the quieter controls for history and theme, then the window itself.
        var lead = Line(16, Line(9, mark, wordmark), eyedropper); lead.VerticalAlignment = VerticalAlignment.Center;
        var trail = Line(12, history, themeSwitch); trail.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Height = 56, Background = Brushes.Transparent, FlowDirection = FlowDirection.LeftToRight };
        lead.Margin = new Thickness(14, 0, 0, 0); header.Children.Add(lead);
        Grid.SetColumn(trail, 2); header.Children.Add(trail);
        var titlebar = BuildTitlebar(header, 3);
        // A narrow window drops the name; the actions always stay.
        header.SizeChanged += (_, _) => wordmark.IsVisible = header.Bounds.Width >= 640;
        Closed += (_, _) => (mark.Source as IDisposable)?.Dispose();

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
        // The heading takes its direction from the page column, so it follows every change of language.
        var heading = new WrapPanel { Orientation = Orientation.Horizontal }; heading.Children.Add(pageTitle); pageDescription.Margin = new Thickness(16, 8, 0, 6); heading.Children.Add(pageDescription); heading.Margin = new Thickness(22, 2, 22, 10);
        var work = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = L.Flow,
            Margin = new Thickness(10, 0, Spine.Closed + 4, 0), MaxWidth = 1336 };
        work.Children.Add(heading);
        // Bottom breathing room belongs to the content extent, not presenter padding.
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); work.Children.Add(scroll);

        var body = new Panel(); body.Children.Add(work); body.Children.Add(spine); body.SizeChanged += (_, _) => spine.Fit(body.Bounds.Height);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        shell.Children.Add(errorBanner); Grid.SetRow(body, 1); shell.Children.Add(body);
        // Dialogs cover the workbench while native window actions remain reachable.
        var room = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), FlowDirection = FlowDirection.LeftToRight };
        Grid.SetRowSpan(backdrop, 2); room.Children.Add(backdrop); room.Children.Add(titlebar);
        Grid.SetRow(shell, 1); room.Children.Add(shell); Grid.SetRow(overlay, 1); room.Children.Add(overlay);
        Grid.SetRow(toasts, 1); room.Children.Add(toasts);
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
        eyedropper.Content = Line(9, new Icon("color-picker", 19), new TextBlock { Text = L.T("القطّارة"), VerticalAlignment = VerticalAlignment.Center },
            Of(new Border { VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "F8", FontSize = Typography.Caption, FontFamily = Code } }, "keycap"));
        AutomationProperties.SetName(eyedropper, L.T("القطّارة"));
        modes = [L.T("متجاورة"), L.T("متكاملة"), L.T("ثلاثية"), L.T("متكاملة منقسمة"), L.T("أحادية اللون"), L.T("عالية التباين")];
        status.Text = Idle; PlaceToast();
        DirectHistory(); ToolTip.SetTip(themeSwitch, L.T("المظهر"));
        // Everything that reads takes the language's direction: the page column (and its heading), the error banner and the toast.
        if (content.Parent is ScrollViewer scroll && scroll.Parent is Grid work) work.FlowDirection = L.Flow;
        errorBanner.FlowDirection = L.Flow;
        SetTrayMenu();
    }
    // Undo points back along the reading direction, so the pair mirrors with the language.
    void DirectHistory()
    {
        bool mirrored = L.Flow == FlowDirection.RightToLeft;
        history.FlowDirection = L.Flow;
        foreach (var (button, back, label) in new[] { (undoButton!, true, L.T("تراجع · Ctrl+Z")), (redoButton!, false, L.T("إعادة · Ctrl+Shift+Z")) })
        {
            button.Content = new Icon(back != mirrored ? "arrow-back-up" : "arrow-forward-up", 18) { HorizontalAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label);
        }
    }
    void StyleShell()
    {
        pageTitle.Text = tools[page].Title; pageDescription.Text = tools[page].About; pageTitle.LetterSpacing = Typography.Tracking(pageTitle.Text, Typography.Display);
        pageTitle.LineHeight = Typography.Leading(Typography.Display); pageDescription.LineHeight = Typography.Leading(Typography.Label);
        UpdateWindowChrome();
        spine.Motion = Motion; spine.Activate(page);
        themeSwitch.Motion = Motion; themeSwitch.Sync(!dark);
    }
    async Task Settings()
    {
        var d = Dialog(L.T("الإعدادات")); d.Width = 440;
        var panel = Col(Text(L.T("الإعدادات"), Typography.Heading, true), Label("PickerHub " + App.Version)); panel.Spacing = 12;
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
