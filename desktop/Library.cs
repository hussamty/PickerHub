using Avalonia.Controls;
using Avalonia.Media;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickerHub;

internal sealed partial class MainWindow
{
    void Palettes()
    {
        var p = Current;
        content.Children.Add(Row(Choose(state.Palettes.Select(a => a.Name), state.Palettes.IndexOf(p), i => { paletteId = state.Palettes[i].Id; Render(); }),
            Btn("لوحة جديدة", async () => { var name = await Prompt("اسم اللوحة", ""); if (name != null) Edit(() => { var next = new Palette { Name = name }; state.Palettes.Add(next); paletteId = next.Id; }); }),
            IconButton("pencil", "تسمية اللوحة", async () => { var n = await Prompt("اسم اللوحة", p.Name); if (n != null) Edit(() => p.Name = n); }),
            IconButton("trash", "حذف اللوحة", async () => { if (state.Palettes.Count == 1) throw new InvalidOperationException("احتفظ بلوحة واحدة على الأقل."); if (await Confirm("حذف «" + p.Name + "» بكل ألوانها؟")) Edit(() => state.Palettes.Remove(p)); })));
        content.Children.Add(Row(Btn("إضافة اللون المحدد", () => Edit(() => p.Colors.Add(Swatch.From(state.Color)))), Btn("تصدير اللوحة", () => Export(p))));
        if (p.Colors.Count > 0)
        {
            var strip = new Avalonia.Controls.Grid { ColumnDefinitions = new(string.Join(",", p.Colors.Select(_ => "*"))), Height = 76, FlowDirection = Avalonia.Media.FlowDirection.LeftToRight };
            for (int j = 0; j < p.Colors.Count; j++) { var col = p.Colors[j]; var sample = Chip(col.Hex, () => { page = 0; Select(col.Hex); }); sample.Margin = new Avalonia.Thickness(0); sample.CornerRadius = new Avalonia.CornerRadius(0); sample.BorderThickness = new Avalonia.Thickness(0); Avalonia.Controls.Grid.SetColumn(sample, j); strip.Children.Add(sample); }
            content.Children.Add(new Avalonia.Controls.Border { CornerRadius = new Avalonia.CornerRadius(12), ClipToBounds = true, Child = strip });
        }
        foreach (var item in p.Colors)
        {
            var actions = new Button { Content = Icon("dots"), Background = Paper };
            ToolTip.SetTip(actions, "خيارات اللون");
            var menu = new ContextMenu();
            void Menu(string text, Func<Task> action) { var entry = new MenuItem { Header = text }; entry.Click += async (_, _) => await Guard(action); menu.Items.Add(entry); }
            Menu("تسمية", async () => { var n = await Prompt("اسم اللون", item.Name); if (n != null) Edit(() => item.Name = n); });
            Menu("تقديم", () => { Move(-1); return Task.CompletedTask; });
            Menu("تأخير", () => { Move(1); return Task.CompletedTask; });
            Menu("نقل إلى لوحة", async () =>
            {
                var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException("أنشئ لوحة ثانية أولاً.");
                var dest = await SelectOption("نقل اللون إلى", others.Select(a => a.Name).ToArray()); if (dest != null) Edit(() => { p.Colors.Remove(item); others[dest.Value].Colors.Add(item); });
            });
            Menu("حذف", () => { Edit(() => p.Colors.Remove(item)); return Task.CompletedTask; });
            actions.Click += (_, _) => menu.Open(actions);
            var line = new DockPanel();
            var tools = Row(IconButton("copy", "نسخ HEX", () => Copy(item.Hex)), actions);
            DockPanel.SetDock(tools, Dock.Left); line.Children.Add(tools);
            var chip = Chip(item.Hex, () => { page = 0; Select(item.Hex); }, " "); chip.Width = 46; chip.Height = 46;
            DockPanel.SetDock(chip, Dock.Right); line.Children.Add(chip);
            line.Children.Add(Col(Text(item.Name, 14, true), Label(item.Hex)));
            content.Children.Add(Card(line));
            void Move(int delta) { int i = p.Colors.IndexOf(item), j = i + delta; if (j >= 0 && j < p.Colors.Count) Edit(() => (p.Colors[i], p.Colors[j]) = (p.Colors[j], p.Colors[i])); }
        }
        if (p.Colors.Count == 0) content.Children.Add(Empty("ابدأ لوحتك الأولى", "أضف اللون المحدد أو احفظ مجموعة من الملتقط.", "palette"));
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
        var limit = new NumericUpDown { Minimum = 1, Maximum = 256, Increment = 1, Value = state.Limit, FormatString = "0", Width = 140, FlowDirection = FlowDirection.LeftToRight };
        content.Children.Add(Row(Field("عدد العناصر المحفوظة", limit), Btn("تطبيق", () => Edit(() => { state.Limit = (int)(limit.Value ?? 32); state.History = state.History.Take(state.Limit).ToList(); })),
            IconButton("trash", "مسح السجل", async () => { if (await Confirm("مسح سجل الألوان؟")) Edit(state.History.Clear); })));
        var tiles = new WrapPanel();
        foreach (var h in state.History)
        {
            var swatch = Chip(h, () => { page = 0; Select(h); }); swatch.Height = 94; swatch.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            var tile = Card(Col(swatch, Text(Core.Describe(h).Text("name"), 13, true), Row(IconButton("copy", "نسخ HEX", () => Copy(h)), IconButton("trash", "حذف", () => Edit(() => state.History.Remove(h))))));
            tile.Width = 184; tile.Margin = new Avalonia.Thickness(0, 0, 10, 10); tiles.Children.Add(tile);
        }
        content.Children.Add(tiles);
        if (state.History.Count == 0) content.Children.Add(Empty("سجلك يبدأ بلون", "اضغط القطّارة لاختيار لون من شاشتك.", "history"));
    }
    Button EditHex(string caption, string hex, Action<string> commit)
    {
        var button = Btn(caption, async () => { var next = await Prompt(caption, hex, true); if (next != null) { commit(next); Render(); } });
        var code = Text(hex, 13); code.FlowDirection = FlowDirection.LeftToRight;
        button.Content = Row(Text(caption, 12), code);
        return button;
    }
    void ContrastPage()
    {
        var r = Core.Contrast(foreground, background);
        var sample = Text("تصميم واضح للجميع", 28, true); sample.Foreground = Paint(foreground);
        var normal = Text("النص المقروء يبدأ بتباين مناسب بين اللون والخلفية.", 14); normal.Foreground = Paint(foreground);
        var preview = new Avalonia.Controls.Border { Background = Paint(background), Padding = new Avalonia.Thickness(25, 42), CornerRadius = new Avalonia.CornerRadius(12), Child = Col(sample, normal) };
        var controls = Col(preview, Row(Field("لون النص", EditHex("تعديل", foreground, h => foreground = h)), IconButton("arrows-exchange", "تبديل اللونين", () => { (foreground, background) = (background, foreground); Render(); }), Field("الخلفية", EditHex("تعديل", background, h => background = h))), Btn("استخدام اللون المحدد للنص", () => { foreground = state.Color; Render(); }));
        var ratio = Text(r.GetProperty("ratio").GetDouble().ToString("0.00") + ":1", 45, true); ratio.FlowDirection = Avalonia.Media.FlowDirection.LeftToRight;
        var checks = Col(ratio, Label("نسبة التباين"), new Separator());
        foreach (var (key, label) in new[] { ("aa", "AA للنص العادي"), ("large", "AA للنص الكبير"), ("aaa", "AAA للنص العادي"), ("aaaLarge", "AAA للنص الكبير") })
        {
            bool pass = r.GetProperty(key).GetBoolean();
            checks.Children.Add(Row(Icon(pass ? "check" : "x", pass ? Accent : Muted), Text(label, 13), Label(pass ? "ناجح" : "غير مناسب")));
        }
        if (!r.GetProperty("aa").GetBoolean()) checks.Children.Add(Btn("تحسين لون النص", () => { foreground = r.Text("suggestion"); Render(); }, true));
        content.Children.Add(Pair(controls, Section("نتيجة الفحص", checks), 1.2, 1));
    }
}
