using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;

namespace PickerHub;

// Palette of the day. The Rust core owns the catalog, the choice, the cooldown and the state file;
// this page only shows what it returns and reports what the user did with it.
internal sealed partial class MainWindow
{
    int dailyFormat;
    bool? dailyDark;
    bool dailyStaged;
    DispatcherTimer? dailyClock;
    static readonly string[] dailyFormats = ["HEX", "RGB", "HSL"];

    static string Span(long seconds) => FormattableString.Invariant($"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    static string Local(JsonElement at) => at.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(at.GetString(), out var when)
        ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "";

    void DailyPage()
    {
        dailyClock?.Stop();
        JsonElement d;
        try { d = Core.Daily("status"); }
        catch (Exception e) { content.Children.Add(Empty(L.T("تعذّر فتح باقة اليوم"), e.Message, "sparkles")); return; }
        string phase = d.Text("state");
        bool has = d.GetProperty("palette").ValueKind == JsonValueKind.Object;
        if (App.Preview) content.Children.Add(PreviewBar());
        if (phase == "sealed") Sealed(d, has);
        else if (has) Revealed(d);
        else Waiting(d);
        content.Children.Add(Row(Btn(L.T("أرشيف الباقات"), DailyArchive), Btn(L.T("ألوان العلامة المثبتة"), BrandColors)));
    }

    // The preview's own controls: replay the opening from a sealed envelope, with or without motion.
    Control PreviewBar()
    {
        var calm = new CheckBox { Content = L.T("تقليل الحركة"), IsChecked = state.ReduceMotion, VerticalAlignment = VerticalAlignment.Center };
        calm.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.ReduceMotion = calm.IsChecked == true); return Task.CompletedTask; });
        var note = Label(L.T("معاينة: مكتبة مؤقتة تُحذف عند الإغلاق ولا تمسّ مكتبتك. R لإعادة المعاينة."));
        note.VerticalAlignment = VerticalAlignment.Center;
        var replay = Btn("refresh", L.T("إعادة المعاينة"), ReplayEnvelope);
        AutomationProperties.SetAutomationId(replay, "preview-replay");
        return Row(replay, calm, note);
    }
    // Forget today's palette so the core seals a fresh envelope, then build the page again.
    void ReplayEnvelope()
    {
        if (!App.Preview) return;
        dailyClock?.Stop();
        File.Delete(Path.Combine(Workspace.Folder, "daily_palette_state.json"));
        pack = null; packKey = ""; dailyStaged = false;
        Render(); status.Text = L.T("الظرف مغلق من جديد");
    }

    PackStage? pack;
    string packKey = "";
    static string PackKey(JsonElement p, bool shade, int format) => $"{p.Text("id")}|{p.GetProperty("serial").GetInt64()}|{shade}|{format}|{p.GetProperty("adjust").GetProperty("light").GetInt64()}|{p.GetProperty("adjust").GetProperty("vivid").GetInt64()}";
    IEnumerable<CardSpec> Cards(JsonElement p, bool shade)
    {
        string key = dailyFormats[dailyFormat].ToLowerInvariant();
        return p.GetProperty(shade ? "dark" : "light").EnumerateArray().Select(r => new CardSpec(r.Text("hex"), r.Text("ink"), r.Text("label"), r.Text(key), L.Language == "en" ? Core.Describe(r.Text("hex")).Text("english") : r.Text("name"))).ToArray();
    }

    // A sealed envelope in the middle of the page, one action beneath it, and the previous palette within reach.
    void Sealed(JsonElement d, bool has)
    {
        var stage = new PackStage(Motion, "No. " + (d.GetProperty("serial").GetInt64() + 1).ToString("00"));
        pack = stage; packKey = "";
        var words = Col(Text(has ? L.T("باقة جديدة بانتظارك") : L.T("اكتشاف اليوم"), Typography.Display, true),
            Label(L.T("باقة ألوان واحدة كل 24 ساعة، مستوحاة من مشهد طبيعي، بأدوار جاهزة للتصميم.")));
        words.Spacing = 4; words.HorizontalAlignment = HorizontalAlignment.Center;
        var open = Btn("sparkles", L.T("افتح الباقة"), () => Unseal(stage, words), true);
        words.Children.Add(open); open.Margin = new Thickness(0, 10, 0, 0);
        if (has)
        {
            var last = d.GetProperty("palette");
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var role in last.GetProperty(dark ? "dark" : "light").EnumerateArray())
            {
                string hex = role.Text("hex"); var dot = Dot(hex, () => _ = Guard(() => Copy(hex)), 26);
                ToolTip.SetTip(dot, role.Text("label") + "  " + hex); dots.Children.Add(dot);
            }
            words.Children.Add(Label(L.T("الباقة السابقة: ") + last.Text("name"))); words.Children.Add(dots);
        }
        foreach (var child in words.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        stage.OpenRequested += () => _ = Guard(() => Unseal(stage, words));
        content.Children.Add(stage); content.Children.Add(words);
    }

    // Ask the core for the palette first: the envelope only opens for a pack that really exists.
    async Task Unseal(PackStage stage, Control words)
    {
        if (stage.Opening || stage.HasCards) return;
        var d = Core.Daily("reveal");
        if (!d.GetProperty("fresh").GetBoolean() || d.GetProperty("palette").ValueKind != JsonValueKind.Object) { Render(); return; }
        var p = d.GetProperty("palette"); bool shade = dailyDark ?? dark;
        words.IsHitTestVisible = false;
        if (Motion) words.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        words.Opacity = 0;
        packKey = PackKey(p, shade, dailyFormat); status.Text = L.T("فُتحت باقة اليوم: ") + p.Text("name");
        await stage.Open(Cards(p, shade));
        dailyStaged = Motion;
        if (page == DailyTool) Render();
    }

    void Revealed(JsonElement d)
    {
        var p = d.GetProperty("palette"); RememberDaily(p); var flags = d.GetProperty("flags");
        bool shade = dailyDark ?? dark; string phase = d.Text("state");
        var roles = p.GetProperty(shade ? "dark" : "light").EnumerateArray().ToArray();
        string title = p.Text("name") + (shade ? L.T(" (داكن)") : L.T(" (فاتح)"));
        Palette Make() => new() { Name = title, Colors = roles.Select(r => new Swatch { Name = r.Text("label"), Hex = r.Text("hex") }).ToList(), Roles = SemanticPalette.Read(p, shade ? "dark" : "light") };

        // The cards that flew out of the envelope are the ones shown here; later visits build them at rest.
        string key = PackKey(p, shade, dailyFormat);
        if (pack == null || packKey != key || !pack.HasCards || pack.Opening) { pack = PackStage.Resting(Motion, Cards(p, shade)); packKey = key; }
        pack.Copied = card => _ = Guard(async () => { await Copy(card.Code); status.Text = L.T("نُسخ ") + card.Label + ": " + card.Code; });
        content.Children.Add(pack);

        // The story: what this palette is, and when the next one comes.
        var moods = Line(6, [.. p.GetProperty("moods").EnumerateArray().Select(m => (Control)Of(new Border { Child = Text(m.Text("label"), Typography.Label), Padding = new Thickness(12, 3) }, "status"))]);
        var serial = Tone(Mono("no. " + p.GetProperty("serial").GetInt64().ToString("00") + " / " + p.Text("english").ToLowerInvariant(), Typography.Caption), "Muted");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; Grid.SetColumn(serial, 2); top.Children.Add(moods); top.Children.Add(serial);
        var heading = Col(Text(p.Text("name"), Typography.Display, true), Text(p.Text("story"), Typography.Lead)); heading.Spacing = 0;

        var remaining = Mono(Span(d.GetProperty("remaining").GetInt64()), Typography.Heading, true);
        Control timing;
        if (phase == "clock")
        {
            string retry = Local(d.GetProperty("retryAt"));
            timing = Col(Of(Text(L.T("وقت الجهاز غير صالح"), Typography.Lead, true), "danger"),
                Label(L.T("ساعة الجهاز الآن أقدم من آخر وقت سجّله التطبيق، لذلك توقّف العدّ. صحّح الساعة، أو انتظر حتى ") + retry + L.T(" لتفتح باقة جديدة. باقتك الحالية ما زالت متاحة.")));
        }
        else
        {
            timing = Line(10, Tone(new Icon("clock", 18), "Muted"), Label(L.T("الباقة التالية بعد")), remaining);
            Count(d.GetProperty("remaining").GetInt64(), remaining);
        }
        if (d.GetProperty("altered").GetBoolean()) timing = Col(timing, Label(L.T("تغيّر ملف حالة الباقة خارج التطبيق، فبدأ العدّ من جديد.")));
        var story = Col(top, heading, timing); story.Spacing = 12;

        // What to do with it.
        bool saved = flags.GetProperty("saved").GetBoolean(), applied = flags.GetProperty("applied").GetBoolean(), dismissed = flags.GetProperty("dismissed").GetBoolean();
        var save = Btn(saved ? "check" : "device-floppy", saved ? L.T("محفوظة في لوحاتك") : L.T("حفظ كلوحة"), () =>
        {
            var made = Make();
            Edit(() => { state.Palettes.Add(made); paletteId = made.Id; paletteCard = 0; }); Core.Daily("save"); Render(); status.Text = L.T("حُفظت الباقة كلوحة جديدة");
        }, !saved);
        var apply = Btn(applied ? "check" : "plus", applied ? L.T("أُضيفت إلى اللوحة الحالية") : L.T("تطبيق على اللوحة الحالية"), () =>
        {
            var target = Current;
            Edit(() => target.Colors.AddRange(Make().Colors)); Core.Daily("apply"); Render(); status.Text = L.T("أُضيفت ألوان الباقة إلى «") + target.Name + "»";
        });
        save.IsEnabled = !saved; apply.IsEnabled = !applied;
        string format = dailyFormats[dailyFormat].ToLowerInvariant();
        var more = Cluster(Liquid("download", L.T("تصدير الباقة"), () => Export(Make())),
            Liquid("copy", L.T("نسخ كل الأكواد"), () => Copy(string.Join("\n", roles.Select(r => r.Text("role") + ": " + r.Text(format))))),
            Liquid("x", dismissed ? L.T("استُبعدت") : L.T("لا تعجبني هذه الباقة"), () => { Core.Daily("dismiss"); status.Text = L.T("سنقلّل ظهور الباقات المشابهة"); Render(); }));
        var views = Line(8, Choose([L.T("داكن"), L.T("فاتح")], shade ? 0 : 1, i => { dailyDark = i == 0; Render(); }), Choose(dailyFormats, dailyFormat, i => { dailyFormat = i; Render(); }));
        ((Control)views.Children[0]).MinWidth = 104; ((Control)views.Children[1]).MinWidth = 104;
        Control Tune(string label, string direction) => Btn(label, () => { Core.Daily("adjust", direction); Render(); });
        var controls = Col(Row(save, apply, more), Row(views, Tune(L.T("أفتح"), "lighter"), Tune(L.T("أغمق"), "darker"), Tune(L.T("أهدأ"), "muted"), Tune(L.T("أزهى"), "vivid")));
        controls.Spacing = 2; controls.VerticalAlignment = VerticalAlignment.Center;

        var pane = Surface(Pair(story, controls, 1, 1.25));
        content.Children.Add(pane);
        var themes = DailyThemes(p); var checks = themes.SelectMany(t => SemanticPalette.Checks(t.Value)).ToArray();
        content.Children.Add(Row(Btn(L.T("تصدير أدوار الفاتح والداكن"), () => ExportThemes(themes)),
            Label(checks.All(c => c.AA) ? L.T("كل أزواج النص المحددة تحقق AA") : L.T("بعض الألوان لا تحقق AA كنص عادي. صدّر تقرير التباين."))));
        if (dailyStaged) { dailyStaged = false; Stage([pane]); }
    }

    // First opening only: the story arrives once the cards have landed.
    void Stage(List<Control> parts)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i]; var settled = part.Transitions;
            part.Transitions = null; part.Opacity = 0; part.RenderTransform = TransformOperations.Parse("translateY(12px) scale(0.98,0.98)");
            var delay = TimeSpan.FromMilliseconds(60 + i * 70);
            Dispatcher.UIThread.Post(() =>
            {
                part.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Delay = delay },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(300), Delay = delay, Easing = new BackEaseOut() }];
                part.Opacity = 1; part.RenderTransform = TransformOperations.Parse("scale(1,1)");
                DispatcherTimer.RunOnce(() => part.Transitions = settled, delay + TimeSpan.FromMilliseconds(340));
            }, DispatcherPriority.Render);
        }
    }

    // No palette to show and none due: a state file that failed its check starts a full wait.
    void Waiting(JsonElement d)
    {
        var remaining = Mono(Span(d.GetProperty("remaining").GetInt64()), 26, true);
        string why = d.Text("state") == "clock" ? L.T("ساعة الجهاز الآن أقدم من آخر وقت سجّله التطبيق. صحّح الساعة أو انتظر حتى ") + Local(d.GetProperty("retryAt")) + "."
            : d.GetProperty("altered").GetBoolean() ? L.T("تغيّر ملف حالة الباقة خارج التطبيق، فبدأ انتظار جديد مدته 24 ساعة.") : L.T("الباقة التالية تُفتح بعد انتهاء العدّ.");
        content.Children.Add(Empty(L.T("لا توجد باقة الآن"), why, "clock", remaining));
        if (d.Text("state") != "clock") Count(d.GetProperty("remaining").GetInt64(), remaining);
    }
    // The display counts down on a monotonic stopwatch; when it reaches zero the core is asked again.
    void Count(long from, TextBlock display)
    {
        var passed = Stopwatch.StartNew();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; dailyClock = clock;
        clock.Tick += (_, _) =>
        {
            if (dailyClock != clock || page != DailyTool) { clock.Stop(); return; }
            long left = from - (long)passed.Elapsed.TotalSeconds;
            if (left > 0) { display.Text = Span(left); return; }
            clock.Stop(); Render();
        };
        clock.Start();
    }
}
