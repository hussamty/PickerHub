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

    // Card front: the color itself. Card back: its measurements.
    Control CardFront(Specimen item, int number)
    {
        string ink = Core.Describe(item.Hex).Text("ink");
        var serial = Mono("no. " + (number + 1).ToString("00"), 11); serial.HorizontalAlignment = HorizontalAlignment.Left; serial.VerticalAlignment = VerticalAlignment.Top; serial.Opacity = .75;
        var caption = Col(Text(item.Name, 20, true), Mono(item.Hex, 12)); caption.Spacing = 0; caption.VerticalAlignment = VerticalAlignment.Bottom; caption.FlowDirection = FlowDirection.RightToLeft;
        var face = new Panel { Margin = new Thickness(20) }; face.Children.AddRange([serial, caption]);
        var card = new Border { Background = Paint(item.Hex), Child = face };
        Avalonia.Controls.Documents.TextElement.SetForeground(card, Paint(ink)); return card;
    }
    Control CardBack(Specimen item, int number)
    {
        var c = Core.Describe(item.Hex); var formats = c.GetProperty("formats");
        var head = new DockPanel { FlowDirection = FlowDirection.LeftToRight };
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Paint(item.Hex) }; DockPanel.SetDock(dot, Dock.Right); head.Children.Add(dot);
        head.Children.Add(Tone(Mono("no. " + (number + 1).ToString("00") + " / " + c.Text("english").ToLowerInvariant(), 11), "Muted"));
        int light = (int)Math.Round(Color.Parse(item.Hex).ToHsl().L * 100);
        var scale = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 10))), Height = 5, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < 10; i++)
        {
            var step = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0) };
            step[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(i < Math.Round(light / 10d) ? "Ink" : "Line");
            Grid.SetColumn(step, i); scale.Children.Add(step);
        }
        var lightness = new DockPanel(); var percent = Mono(light + "%", 11); DockPanel.SetDock(percent, Dock.Right); lightness.Children.Add(percent); lightness.Children.Add(Label("السطوع"));
        var rows = new StackPanel { Spacing = 5 };
        foreach (var key in new[] { "HEX", "RGB", "HSL", "CMYK" })
        {
            var row = new DockPanel { FlowDirection = FlowDirection.LeftToRight }; var value = Mono(formats.Text(key).Replace("rgb", "").Replace("hsl", "").Trim('(', ')'), 11.5);
            DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Tone(Mono(key.ToLowerInvariant(), 11), "Muted")); rows.Children.Add(row);
        }
        var shades = Core.Harmony(item.Hex, 4);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", shades.Select(_ => "*"))), Height = 40, FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < shades.Length; i++) { var shade = new Border { Background = Paint(shades[i]) }; Grid.SetColumn(shade, i); strip.Children.Add(shade); }
        var body = Col(head, Text(item.Name, 21, true), Col(lightness, scale), Of(new Border(), "hairline"), rows,
            new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = strip }, Label(c.Text("base") + "، " + c.Text("shade")));
        body.Spacing = 11; body.FlowDirection = FlowDirection.RightToLeft;
        var card = new Border { Padding = new Thickness(20, 18), Child = body };
        card[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Raised");
        Tone(card, "Ink"); return card;
    }
    // The deck with its pager and the actions for whichever card is on top.
    (Control View, Deck Deck) DeckView(IReadOnlyList<Specimen> items, int start, Action<int> moved, params Control[] actions)
    {
        var name = Text("", 15, true); name.HorizontalAlignment = HorizontalAlignment.Center; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var position = Tone(Mono("", 11), "Muted"); position.HorizontalAlignment = HorizontalAlignment.Center;
        var deck = new Deck(items.Count, start, Motion, (i, back) => back ? CardBack(items[i], i) : CardFront(items[i], i));
        void Sync(int i) { moved(i); name.Text = items[i].Name; position.Text = (i + 1).ToString("00") + " / " + items.Count.ToString("00"); }
        deck.Changed += Sync; Sync(start);
        var caption = Col(name, position); caption.Spacing = 0; caption.Width = 150; caption.VerticalAlignment = VerticalAlignment.Center;
        var pager = Line(8, Of(Liquid("chevron-right", "اللون السابق", deck.Previous), "still"), caption, Of(Liquid("chevron-left", "اللون التالي", deck.Next), "still"));
        var tools = Cluster([Liquid("refresh", "قلب البطاقة", deck.Flip), .. actions]);
        var view = Col(deck, pager, tools); view.Spacing = 14; view.HorizontalAlignment = HorizontalAlignment.Center;
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
        var manage = Line(8, Choose(state.Palettes.Select(a => a.Name), state.Palettes.IndexOf(p), i => { paletteId = state.Palettes[i].Id; paletteCard = 0; Render(); }),
            Cluster(Liquid("plus", "لوحة جديدة", async () => { var name = await Prompt("اسم اللوحة", ""); if (name != null) Edit(() => { var next = new Palette { Name = name }; state.Palettes.Add(next); paletteId = next.Id; paletteCard = 0; }); }),
                Liquid("pencil", "تسمية اللوحة", async () => { var n = await Prompt("اسم اللوحة", p.Name); if (n != null) Edit(() => p.Name = n); }),
                Liquid("download", "تصدير اللوحة", () => Export(p)),
                Liquid("trash", "حذف اللوحة", async () => { if (state.Palettes.Count == 1) throw new InvalidOperationException("احتفظ بلوحة واحدة على الأقل."); if (await Confirm("حذف «" + p.Name + "» بكل ألوانها؟")) Edit(() => { state.Palettes.Remove(p); paletteCard = 0; }); })));
        var add = Btn("plus", "إضافة اللون المحدد", () => Edit(() => { p.Colors.Add(Swatch.From(state.Color)); paletteCard = p.Colors.Count - 1; }), true);
        content.Children.Add(Row(manage, add));
        if (p.Colors.Count == 0) { content.Children.Add(Empty("ابدأ لوحتك الأولى", "أضف اللون المحدد أو احفظ مجموعة من الملتقط.", "palette")); return; }
        paletteCard = Math.Clamp(paletteCard, 0, p.Colors.Count - 1);
        Swatch Top() => p.Colors[paletteCard];
        void Move(int delta) { int i = paletteCard, j = i + delta; if (j >= 0 && j < p.Colors.Count) Edit(() => { (p.Colors[i], p.Colors[j]) = (p.Colors[j], p.Colors[i]); paletteCard = j; }); }
        var items = p.Colors.Select(c => new Specimen(c.Hex, c.Name)).ToArray();
        var (view, deck) = DeckView(items, paletteCard, i => paletteCard = i,
            Liquid("copy", "نسخ HEX", () => Copy(Top().Hex)),
            Liquid("color-picker", "فتح في الملتقط", () => { page = 0; Select(Top().Hex); }),
            Liquid("pencil", "تسمية اللون", async () => { var item = Top(); var n = await Prompt("اسم اللون", item.Name); if (n != null) Edit(() => item.Name = n); }),
            Liquid("arrow-up", "تقديم", () => Move(-1)),
            Liquid("arrow-down", "تأخير", () => Move(1)),
            Liquid("folder", "نقل إلى لوحة", async () =>
            {
                var item = Top(); var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException("أنشئ لوحة ثانية أولاً.");
                var dest = await SelectOption("نقل اللون إلى", others.Select(a => a.Name).ToArray()); if (dest != null) Edit(() => { p.Colors.Remove(item); others[dest.Value].Colors.Add(item); });
            }),
            Liquid("trash", "حذف اللون", () => { var item = Top(); Edit(() => p.Colors.Remove(item)); }));
        content.Children.Add(Pair(view, Sheet(p.Name, items, deck), 1, 1));
    }
    async Task Export(Palette p)
    {
        if (p.Colors.Count == 0) throw new InvalidOperationException("أضف لوناً إلى اللوحة أولاً.");
        string[] types = ["css", "scss", "json", "csv", "txt", "gpl"];
        var choice = await SelectOption("صيغة التصدير", types); if (choice == null) return; var ext = types[choice.Value];
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
        if (state.History.Count == 0) { content.Children.Add(Empty("سجلك يبدأ بلون", "اضغط القطّارة لاختيار لون من شاشتك.", "history")); content.Children.Add(HistoryLimit()); return; }
        historyCard = Math.Clamp(historyCard, 0, state.History.Count - 1);
        string Top() => state.History[historyCard];
        var items = state.History.Select(h => new Specimen(h, Core.Describe(h).Text("name"))).ToArray();
        var (view, deck) = DeckView(items, historyCard, i => historyCard = i,
            Liquid("copy", "نسخ HEX", () => Copy(Top())),
            Liquid("color-picker", "فتح في الملتقط", () => { page = 0; Select(Top()); }),
            Liquid("plus", "إضافة إلى لوحة", () => AddToPalette([Top()])),
            Liquid("trash", "حذف من السجل", () => { var h = Top(); Edit(() => state.History.Remove(h)); }));
        var clear = Of(Liquid("trash", "مسح السجل", async () => { if (await Confirm("مسح سجل الألوان؟")) Edit(() => { state.History.Clear(); historyCard = 0; }); }), "still");
        content.Children.Add(Pair(view, Col(Sheet("كل الألوان", items, deck, clear), HistoryLimit()), 1, 1));
    }
    Control HistoryLimit()
    {
        var limit = new NumericUpDown { Minimum = 1, Maximum = 256, Increment = 1, Value = state.Limit, FormatString = "0", Width = 150, MinHeight = 40, FlowDirection = FlowDirection.LeftToRight };
        AutomationProperties.SetName(limit, "عدد العناصر المحفوظة");
        var apply = Btn("تطبيق", () => Edit(() => { state.Limit = (int)(limit.Value ?? 32); state.History = state.History.Take(state.Limit).ToList(); })); apply.VerticalAlignment = VerticalAlignment.Bottom;
        return Surface(Line(10, Field("عدد العناصر المحفوظة", limit), apply));
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
        var sample = Text("تصميم واضح للجميع", 30, true);
        var normal = Text("النص المقروء يبدأ بتباين مناسب بين اللون والخلفية.", 14);
        var preview = new Border { Background = Paint(background), Padding = new Thickness(28, 52), CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Child = Col(sample, normal) };
        preview[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line");
        Avalonia.Controls.Documents.TextElement.SetForeground(preview, Paint(foreground));
        var swap = Of(Liquid("arrows-exchange", "تبديل اللونين", () => { (foreground, background) = (background, foreground); Render(); }), "still"); swap.VerticalAlignment = VerticalAlignment.Bottom;
        var pick = Line(10, Field("لون النص", EditHex("تعديل لون النص", foreground, h => foreground = h)), swap, Field("الخلفية", EditHex("تعديل لون الخلفية", background, h => background = h)));
        var use = Btn("color-picker", "استخدام اللون المحدد للنص", () => { foreground = state.Color; Render(); }); use.HorizontalAlignment = HorizontalAlignment.Left;
        var controls = Col(preview, Surface(Col(pick, use)));
        var ratio = Mono(r.GetProperty("ratio").GetDouble().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1", 50, true); ratio.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), RowDefinitions = new RowDefinitions("Auto,10,Auto") };
        var levels = new[] { ("aa", "AA", "نص عادي"), ("large", "AA", "نص كبير"), ("aaa", "AAA", "نص عادي"), ("aaaLarge", "AAA", "نص كبير") };
        for (int i = 0; i < levels.Length; i++)
        {
            var (key, level, size) = levels[i]; bool pass = r.GetProperty(key).GetBoolean();
            var mark = Tone(new Icon(pass ? "check" : "x", 18), pass ? "AccentText" : "Muted"); mark.HorizontalAlignment = HorizontalAlignment.Left;
            var verdict = Label(pass ? "ناجح" : "غير مناسب");
            var top = new DockPanel(); DockPanel.SetDock(mark, Dock.Right); top.Children.Add(mark); top.Children.Add(Mono(level, 15, true));
            var body = Col(top, Text(size, 13), verdict); body.Spacing = 2;
            var tile = Of(new Border { Child = body }, "tile"); Grid.SetColumn(tile, i % 2 * 2); Grid.SetRow(tile, i / 2 * 2); grid.Children.Add(tile);
        }
        var checks = Col(ratio, Label("نسبة التباين"), grid);
        if (!r.GetProperty("aa").GetBoolean()) { var fix = Btn("تحسين لون النص", () => { foreground = r.Text("suggestion"); Render(); }, true); fix.HorizontalAlignment = HorizontalAlignment.Stretch; fix.Margin = new Thickness(0, 6, 0, 0); checks.Children.Add(fix); }
        content.Children.Add(Pair(controls, Section("نتيجة الفحص", checks), 1.2, 1));
    }
}
