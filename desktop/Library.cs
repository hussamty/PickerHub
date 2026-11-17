using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickerHub;

internal sealed partial class MainWindow
{
    internal sealed record Specimen(string Hex, string Name);

    // A color specimen is the color itself. Chrome and measurements use the
    // contrast engine's ink; no translucent material changes the sampled fill.
    Control CardFront(Specimen item, int number)
    {
        var info = Core.Describe(item.Hex); var ink = Paint(info.Text("ink"));
        var serial = Mono((number + 1).ToString("00"), Typography.Label); serial.HorizontalAlignment = HorizontalAlignment.Left;
        var code = Mono(item.Hex, 14, true); code.HorizontalAlignment = HorizontalAlignment.Right;
        var head = new Grid(); head.Children.Add(serial); head.Children.Add(code); head.VerticalAlignment = VerticalAlignment.Top;
        var name = Text(item.Name, Typography.Feature, true); name.MaxLines = 3; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var family = Text(info.Text("base"), 13);
        var caption = Col(name, family); caption.Spacing = 4; caption.VerticalAlignment = VerticalAlignment.Bottom; caption.FlowDirection = L.Flow;
        var face = new Panel(); face.Children.Add(head); face.Children.Add(caption);
        foreach (var label in new[] { serial, code, name, family }) label.Foreground = ink;
        return new Border { Background = Paint(item.Hex), BorderBrush = Paint(info.Text("ink"), .18), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26), Padding = new Thickness(22), Child = face };
    }
    Control CardBack(Specimen item, int number)
    {
        var c = Core.Describe(item.Hex); var formats = c.GetProperty("formats");
        var head = new DockPanel { FlowDirection = FlowDirection.LeftToRight };
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Paint(item.Hex) }; DockPanel.SetDock(dot, Dock.Right); head.Children.Add(dot);
        head.Children.Add(Tone(Mono("no. " + (number + 1).ToString("00") + " / " + c.Text("english").ToLowerInvariant(), Typography.Caption), "Muted"));
        int light = (int)Math.Round(Color.Parse(item.Hex).ToHsl().L * 100);
        var scale = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 10))), Height = 5, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < 10; i++)
        {
            var step = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0) };
            step[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(i < Math.Round(light / 10d) ? "Ink" : "Line");
            Grid.SetColumn(step, i); scale.Children.Add(step);
        }
        var lightness = new DockPanel(); var percent = Mono(light + "%", Typography.Caption); DockPanel.SetDock(percent, Dock.Right); lightness.Children.Add(percent); lightness.Children.Add(Label(L.T("السطوع")));
        var rows = new StackPanel { Spacing = 5 };
        foreach (var key in new[] { "HEX", "RGB", "HSL", "CMYK" })
        {
            var row = new DockPanel { FlowDirection = FlowDirection.LeftToRight }; var value = Mono(formats.Text(key).Replace("rgb", "").Replace("hsl", "").Trim('(', ')'), 11.5);
            DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Tone(Mono(key.ToLowerInvariant(), Typography.Caption), "Muted")); rows.Children.Add(row);
        }
        var shades = Core.Harmony(item.Hex, 4);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", shades.Select(_ => "*"))), Height = 40, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < shades.Length; i++) { var shade = new Border { Background = Paint(shades[i]) }; Grid.SetColumn(shade, i); strip.Children.Add(shade); }
        var body = Col(head, Text(item.Name, Typography.Heading, true), Col(lightness, scale), Of(new Border(), "hairline"), rows,
            new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = strip }, Label(c.Text("base") + L.T("، ") + c.Text("shade")));
        body.Spacing = 12; body.FlowDirection = L.Flow;
        // The reverse is denser glass, so the measurements stay readable over the cards beneath.
        return Tone(new Glass("DenseTint") { Radius = 26, Blur = 18, Padding = new Thickness(20, 18), Child = body }, "Ink");
    }
    // The deck with its pager and the actions for whichever card is on top.
    (Control View, Deck Deck) DeckView(IReadOnlyList<Specimen> items, int start, Action<int> moved, params Control[] actions)
    {
        var name = Text("", Typography.Lead, true); name.HorizontalAlignment = HorizontalAlignment.Center; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var position = Tone(Mono("", Typography.Caption), "Muted"); position.HorizontalAlignment = HorizontalAlignment.Center;
        var deck = new Deck(items.Count, start, Motion, (i, back) => back ? CardBack(items[i], i) : CardFront(items[i], i));
        void Sync(int i) { moved(i); name.Text = items[i].Name; position.Text = (i + 1).ToString("00") + " / " + items.Count.ToString("00"); }
        deck.Changed += Sync; Sync(start);
        var caption = Col(name, position); caption.Spacing = 0; caption.Width = 150; caption.VerticalAlignment = VerticalAlignment.Center;
        // Previous points back along the reading direction: right in Arabic, left in English.
        bool arabic = L.Language == "ar";
        var pager = Line(8, Of(Liquid(arabic ? "chevron-right" : "chevron-left", L.T("اللون السابق"), deck.Previous), "still"), caption, Of(Liquid(arabic ? "chevron-left" : "chevron-right", L.T("اللون التالي"), deck.Next), "still"));
        var tools = Cluster([Liquid("refresh", L.T("قلب البطاقة"), deck.Flip), .. actions]);
        var view = Col(deck, pager, tools); view.Spacing = 12; view.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var child in view.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        return (view, deck);
    }
    Control Sheet(string title, IReadOnlyList<Specimen> items, Deck deck, Control? actions = null)
    {
        var dots = new WrapPanel();
        for (int i = 0; i < items.Count; i++) { int index = i; var dot = Dot(items[i].Hex, () => deck.Show(index), 38); dot.Margin = new Thickness(0, 0, 8, 8); ToolTip.SetTip(dot, items[i].Name + "  " + items[i].Hex); dots.Children.Add(dot); }
        var sheet = Section(title, dots, actions); sheet.VerticalAlignment = VerticalAlignment.Top; return sheet;
    }
    void Palettes()
    {
        var p = Current;
        var manage = Line(8, Choose(state.Palettes.Select(a => (a.Collection.Length > 0 ? a.Collection + " / " : "") + a.Name), state.Palettes.IndexOf(p), i => { paletteId = state.Palettes[i].Id; paletteCard = 0; Render(); }),
            Cluster(Liquid("plus", L.T("لوحة جديدة"), async () => { var name = await Prompt(L.T("اسم اللوحة"), ""); if (name != null) Edit(() => { var next = new Palette { Name = name }; state.Palettes.Add(next); paletteId = next.Id; paletteCard = 0; }); }),
                Liquid("pencil", L.T("تسمية اللوحة"), async () => { var n = await Prompt(L.T("اسم اللوحة"), p.Name); if (n != null) Edit(() => p.Name = n); }),
                Liquid("download", L.T("تصدير اللوحة"), () => Export(p)),
                Liquid("trash", L.T("حذف اللوحة"), async () => { if (state.Palettes.Count == 1) throw new InvalidOperationException(L.T("احتفظ بلوحة واحدة على الأقل.")); if (await Confirm(L.T("حذف «") + p.Name + L.T("» بكل ألوانها؟"))) Edit(() => { state.Palettes.Remove(p); paletteCard = 0; }); })));
        var add = Btn("plus", L.T("إضافة اللون المحدد"), () => Edit(() => { p.Colors.Add(Swatch.From(state.Color)); paletteCard = p.Colors.Count - 1; }), true);
        string[] views = ["grid", "list", "deck"];
        var viewChoice = Choose([L.T("شبكة"), L.T("قائمة"), L.T("بطاقات")], Array.IndexOf(views, state.LibraryView), i => Edit(() => state.LibraryView = views[i]));
        var collection = Btn(L.T("المشروع: ") + (p.Collection.Length == 0 ? L.T("عام") : p.Collection), async () => { var name = await Prompt(L.T("اسم المشروع"), p.Collection); if (name != null) Edit(() => p.Collection = name); });
        content.Children.Add(Row(manage, add, Btn(L.T("التقاط متعدد"), () => PickDesktop(null, true)), viewChoice, collection, Btn(L.T("أدوار الألوان"), () => Roles(p)), Btn(L.T("مصفوفة التباين"), () => PaletteContrast(p))));
        if (state.LibraryView != "deck") { content.Children.Add(LibraryBrowser(p)); return; }
        if (p.Colors.Count == 0) { content.Children.Add(Empty(L.T("ابدأ لوحتك الأولى"), L.T("أضف اللون المحدد أو احفظ مجموعة من الملتقط."), "palette")); return; }
        paletteCard = Math.Clamp(paletteCard, 0, p.Colors.Count - 1);
        Swatch Top() => p.Colors[paletteCard];
        void Move(int delta) { int i = paletteCard, j = i + delta; if (j >= 0 && j < p.Colors.Count) Edit(() => { (p.Colors[i], p.Colors[j]) = (p.Colors[j], p.Colors[i]); paletteCard = j; }); }
        var items = p.Colors.Select(c => new Specimen(c.Hex, c.Name)).ToArray();
        var (view, deck) = DeckView(items, paletteCard, i => paletteCard = i,
            Liquid("copy", L.T("نسخ اللون"), () => Copy(PickValue(state, Top().Hex))),
            Liquid("color-picker", L.T("فتح في الملتقط"), () => { page = 0; Select(Top().Hex); }),
            Liquid("pencil", L.T("تسمية اللون"), async () => { var item = Top(); var n = await Prompt(L.T("اسم اللون"), item.Name); if (n != null) Edit(() => item.Name = n); }),
            Liquid("arrow-up", L.T("تقديم"), () => Move(-1)),
            Liquid("arrow-down", L.T("تأخير"), () => Move(1)),
            Liquid("folder", L.T("نقل إلى لوحة"), async () =>
            {
                var item = Top(); var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException(L.T("أنشئ لوحة ثانية أولاً."));
                var dest = await SelectOption(L.T("نقل اللون إلى"), others.Select(a => a.Name).ToArray()); if (dest != null) Edit(() => { p.Colors.Remove(item); others[dest.Value].Colors.Add(item); });
            }),
            Liquid("trash", L.T("حذف اللون"), () => { var item = Top(); Edit(() => p.Colors.Remove(item)); }));
        content.Children.Add(Pair(view, Sheet(p.Name, items, deck), 1, 1));
    }
    async Task Export(Palette p)
    {
        if (p.Colors.Count == 0) throw new InvalidOperationException(L.T("أضف لوناً إلى اللوحة أولاً."));
        string[] types = ["css", "scss", "json", "csv", "txt", "gpl"];
        var choice = await SelectOption(L.T("صيغة التصدير"), types); if (choice == null) return; var ext = types[choice.Value];
        string Token(Swatch c, int i) => $"color-{i + 1}-" + Regex.Replace(c.Name, @"[^\p{L}\p{N}-]+", "-").Trim('-');
        string Csv(string s) { if (Regex.IsMatch(s, @"^\s*[=+@-]|^[\t\r\n]")) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var text = ext switch
        {
            "css" => ":root {\n" + string.Join("\n", p.Colors.Select((c, i) => "  --" + Token(c, i) + ": " + c.Hex + ";")) + "\n}\n",
            "scss" => string.Join("\n", p.Colors.Select((c, i) => "$" + Token(c, i) + ": " + c.Hex + ";")),
            "json" => Serialize(new { name = p.Name, colors = p.Colors.Select(c => new { name = c.Name, hex = c.Hex }) }),
            "csv" => "\uFEFFname,hex\r\n" + string.Join("\r\n", p.Colors.Select(c => Csv(c.Name) + "," + Csv(c.Hex))),
            "gpl" => "GIMP Palette\nName: " + p.Name.ReplaceLineEndings(" ") + "\nColumns: 8\n#\n" + string.Join("\n", p.Colors.Select(c => string.Join(" ", Enumerable.Range(0, 3).Select(i => Convert.ToInt32(c.Hex.Substring(1 + i * 2, 2), 16))) + "\t" + c.Name.ReplaceLineEndings(" "))),
            _ => p.Name + "\n" + string.Join("\n", p.Colors.Select(c => c.Name + "  " + c.Hex))
        };
        await WriteFile(Regex.Replace(p.Name, @"[\\/:*?""<>|\p{Cc}]", "-") + "." + ext, text);
    }
    void History()
    {
        if (state.History.Count == 0) { content.Children.Add(Empty(L.T("سجلك يبدأ بلون"), L.T("اضغط القطّارة لاختيار لون من شاشتك."), "history")); content.Children.Add(HistoryLimit()); return; }
        historyCard = Math.Clamp(historyCard, 0, state.History.Count - 1);
        string Top() => state.History[historyCard];
        var items = state.History.Select(h => new Specimen(h, Core.Describe(h).Text("name"))).ToArray();
        var (view, deck) = DeckView(items, historyCard, i => historyCard = i,
            Liquid("copy", L.T("نسخ اللون"), () => Copy(PickValue(state, Top()))),
            Liquid("color-picker", L.T("فتح في الملتقط"), () => { page = 0; Select(Top()); }),
            Liquid("plus", L.T("إضافة إلى لوحة"), () => AddToPalette([Top()])),
            Liquid("trash", L.T("حذف من السجل"), () => { var h = Top(); Edit(() => state.History.Remove(h)); }));
        var clear = Of(Liquid("trash", L.T("مسح السجل"), async () => { if (await Confirm(L.T("مسح سجل الألوان؟"))) Edit(() => { state.History.Clear(); historyCard = 0; }); }), "still");
        content.Children.Add(Pair(view, Col(Sheet(L.T("كل الألوان"), items, deck, clear), HistoryLimit()), 1, 1));
    }
    Control HistoryLimit()
    {
        var limit = new NumericUpDown { Minimum = 1, Maximum = 256, Increment = 1, Value = state.Limit, FormatString = "0", Width = 150, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight };
        AutomationProperties.SetName(limit, L.T("عدد العناصر المحفوظة"));
        var apply = Btn(L.T("تطبيق"), () => Edit(() => { state.Limit = (int)(limit.Value ?? 32); state.History = state.History.Take(state.Limit).ToList(); })); apply.VerticalAlignment = VerticalAlignment.Bottom;
        return Surface(Line(10, Field(L.T("عدد العناصر المحفوظة"), limit), apply));
    }
    Button EditHex(string caption, string hex, Action<string> commit)
    {
        var button = Btn("", async () => { var next = await Prompt(caption, hex, true); if (next != null) { commit(next); Render(); } });
        var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Paint(hex), BorderThickness = new Thickness(1) };
        dot[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line");
        button.Content = Line(8, dot, Mono(hex, 13)); button.FlowDirection = FlowDirection.LeftToRight;
        AutomationProperties.SetName(button, caption + " " + hex); ToolTip.SetTip(button, caption); ToolTip.SetPlacement(button, PlacementMode.Top);
        return button;
    }
    void ContrastPage()
    {
        var r = Core.Contrast(foreground, background);
        var sample = Text(L.T("تصميم واضح للجميع"), Typography.Display, true);
        var normal = Text(L.T("النص المقروء يبدأ بتباين مناسب بين اللون والخلفية."), Typography.Body);
        var preview = new Border { Background = Paint(background), Padding = new Thickness(28, 52), CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Child = Col(sample, normal) };
        preview[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line");
        Avalonia.Controls.Documents.TextElement.SetForeground(preview, Paint(foreground));
        var swap = Of(Liquid("arrows-exchange", L.T("تبديل اللونين"), () => { (foreground, background) = (background, foreground); Render(); }), "still"); swap.VerticalAlignment = VerticalAlignment.Bottom;
        var pick = Line(10, Field(L.T("لون النص"), EditHex(L.T("تعديل لون النص"), foreground, h => foreground = h)), swap, Field(L.T("الخلفية"), EditHex(L.T("تعديل لون الخلفية"), background, h => background = h)));
        var use = Btn("color-picker", L.T("استخدام اللون المحدد للنص"), () => { foreground = state.Color; Render(); }); use.HorizontalAlignment = HorizontalAlignment.Left;
        var controls = Col(preview, Surface(Col(pick, use)));
        var ratio = Mono(r.GetProperty("ratio").GetDouble().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1", 50, true); ratio.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), RowDefinitions = new RowDefinitions("Auto,10,Auto") };
        var levels = new[] { ("aa", "AA", L.T("نص عادي")), ("large", "AA", L.T("نص كبير")), ("aaa", "AAA", L.T("نص عادي")), ("aaaLarge", "AAA", L.T("نص كبير")) };
        for (int i = 0; i < levels.Length; i++)
        {
            var (key, level, size) = levels[i]; bool pass = r.GetProperty(key).GetBoolean();
            var mark = Tone(new Icon(pass ? "check" : "x", 18), pass ? "AccentText" : "Muted"); mark.HorizontalAlignment = HorizontalAlignment.Left;
            var verdict = Label(pass ? L.T("ناجح") : L.T("غير مناسب"));
            var top = new DockPanel(); DockPanel.SetDock(mark, Dock.Right); top.Children.Add(mark); top.Children.Add(Mono(level, Typography.Lead, true));
            var body = Col(top, Text(size, Typography.Control), verdict); body.Spacing = 2;
            var tile = Of(new Border { Child = body }, "tile"); Grid.SetColumn(tile, i % 2 * 2); Grid.SetRow(tile, i / 2 * 2); grid.Children.Add(tile);
        }
        var checks = Col(ratio, Label(L.T("نسبة التباين")), grid);
        if (!r.GetProperty("aa").GetBoolean()) { var fix = Btn(L.T("تحسين لون النص"), () => { foreground = r.Text("suggestion"); Render(); }, true); fix.HorizontalAlignment = HorizontalAlignment.Stretch; fix.Margin = new Thickness(0, 6, 0, 0); checks.Children.Add(fix); }
        content.Children.Add(Pair(controls, Section(L.T("نتيجة الفحص"), checks), 1.2, 1));
    }
}
