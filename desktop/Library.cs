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
            Btn("تسمية اللوحة", async () => { var n = await Prompt("اسم اللوحة", p.Name); if (n != null) Edit(() => p.Name = n); }),
            Btn("حذف اللوحة", async () => { if (state.Palettes.Count == 1) throw new InvalidOperationException("احتفظ بلوحة واحدة على الأقل."); if (await Confirm("حذف «" + p.Name + "» بكل ألوانها؟")) Edit(() => state.Palettes.Remove(p)); })));
        content.Children.Add(Row(Btn("إضافة اللون المحدد", () => Edit(() => p.Colors.Add(Swatch.From(state.Color)))), Btn("تصدير اللوحة", () => Export(p))));
        foreach (var item in p.Colors)
        {
            var buttons = Row(Chip(item.Hex, () => { page = 0; Select(item.Hex); }), Text(item.Name), Btn("نسخ", () => Copy(item.Hex)),
                Btn("تسمية", async () => { var n = await Prompt("اسم اللون", item.Name); if (n != null) Edit(() => item.Name = n); }),
                Btn("↑", () => Move(-1)), Btn("↓", () => Move(1)), Btn("نقل", async () =>
                {
                    var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException("أنشئ لوحة ثانية أولاً.");
                    var dest = await SelectOption("نقل اللون إلى", others.Select(a => a.Name).ToArray()); if (dest != null) Edit(() => { p.Colors.Remove(item); others[dest.Value].Colors.Add(item); });
                }), Btn("حذف", () => Edit(() => p.Colors.Remove(item))));
            content.Children.Add(Card(buttons));
            void Move(int delta) { int i = p.Colors.IndexOf(item), j = i + delta; if (j >= 0 && j < p.Colors.Count) Edit(() => (p.Colors[i], p.Colors[j]) = (p.Colors[j], p.Colors[i])); }
        }
        if (p.Colors.Count == 0) content.Children.Add(Text("لوحتك جاهزة. أضف اللون المحدد أو احفظ مجموعة متناسقة من الملتقط."));
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
        var limit = new NumericUpDown { Minimum = 1, Maximum = 256, Increment = 1, Value = state.Limit, FormatString = "0" };
        content.Children.Add(Row(Text("عدد العناصر المحفوظة"), limit, Btn("تطبيق", () => Edit(() => { state.Limit = (int)(limit.Value ?? 32); state.History = state.History.Take(state.Limit).ToList(); })),
            Btn("مسح السجل", async () => { if (await Confirm("مسح سجل الألوان؟")) Edit(state.History.Clear); })));
        foreach (var h in state.History) content.Children.Add(Row(Chip(h, () => { page = 0; Select(h); }), Text(Core.Describe(h).Text("name")), Btn("نسخ", () => Copy(h)), Btn("حذف", () => Edit(() => state.History.Remove(h)))));
        if (state.History.Count == 0) content.Children.Add(Text("سيظهر هنا أول لون تلتقطه."));
    }
    Button EditHex(string caption, string hex, Action<string> commit) => Btn(caption + "  " + hex, async () => { var next = await Prompt(caption, hex, true); if (next != null) { commit(next); Render(); } });
    void ContrastPage()
    {
        var r = Core.Contrast(foreground, background);
        content.Children.Add(Row(EditHex("لون النص", foreground, h => foreground = h), EditHex("الخلفية", background, h => background = h),
            Btn("استخدام اللون المحدد", () => { foreground = state.Color; Render(); }), Btn("تبديل", () => { (foreground, background) = (background, foreground); Render(); })));
        var sample = Text("معاينة النص الكبير", 30, true); sample.Foreground = Paint(foreground); var normal = Text("هكذا يبدو النص العادي فوق الخلفية المختارة."); normal.Foreground = Paint(foreground);
        content.Children.Add(new Border { Background = Paint(background), Padding = new Avalonia.Thickness(23), Child = Col(sample, normal) });
        content.Children.Add(Text(r.GetProperty("ratio").GetDouble().ToString("0.00") + ":1", 32, true));
        foreach (var (key, label) in new[] { ("aa", "AA · النص العادي"), ("large", "AA · النص الكبير"), ("aaa", "AAA · النص العادي"), ("aaaLarge", "AAA · النص الكبير") })
            content.Children.Add(Text(label + "   " + (r.GetProperty(key).GetBoolean() ? "✓ ناجح" : "× غير مناسب")));
        if (!r.GetProperty("aa").GetBoolean()) content.Children.Add(Btn("تطبيق لون نص مقترح   " + r.Text("suggestion"), () => { foreground = r.Text("suggestion"); Render(); }));
    }
}
