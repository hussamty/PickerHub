using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;

namespace PickerHub;
internal sealed partial class MainWindow
{
    async Task Roles(Palette p)
    {
        var dialog = Dialog(L.T("أدوار الألوان")); dialog.Width = 520;
        var fields = new Dictionary<string, TextBox>(); var rows = new StackPanel { Spacing = 8 };
        var feedback = Label("");
        Dictionary<string, string> Read()
        {
            var result = fields.Where(k => !string.IsNullOrWhiteSpace(k.Value.Text)).ToDictionary(k => k.Key, k => "#" + k.Value.Text!.Trim().TrimStart('#').ToUpperInvariant());
            if (!SemanticPalette.Valid(result)) throw new InvalidDataException(L.T("أدخل ست خانات HEX.")); return result;
        }
        for (int i = 0; i < SemanticPalette.Roles.Length; i++)
        {
            string role = SemanticPalette.Roles[i];
            var input = new TextBox { Text = p.Roles.GetValueOrDefault(role, ""), Watermark = "#RRGGBB", MaxLength = 7, FontFamily = Code, FlowDirection = FlowDirection.LeftToRight, MinWidth = 145 };
            AutomationProperties.SetName(input, L.T(SemanticPalette.Labels[i])); fields[role] = input;
            rows.Children.Add(Row(Field(L.T(SemanticPalette.Labels[i]), input), Btn(L.T("استخدام اللون المحدد"), () => input.Text = state.Color)));
        }
        bool isDark = dark;
        Dictionary<string, Dictionary<string, string>> Themes() => new() { [isDark ? "dark" : "light"] = Read() };
        var actions = Row(Btn(L.T("حفظ"), () => { var roles = Read(); Edit(() => p.Roles = roles); dialog.Close(); }, true),
            Btn("CSS", async () => { var value = SemanticPalette.Css(Themes()); dialog.Close(); await WriteFile("palette.css", value); }),
            Btn("Design tokens", async () => { var value = SemanticPalette.Tokens(Themes()); dialog.Close(); await WriteFile("palette.tokens.json", value); }),
            Btn(L.T("فحص التباين"), () => { var checks = SemanticPalette.Checks(Read()); feedback.Text = checks.Length == 0 ? L.T("عيّن النص والخلفية لفحص التباين.") : string.Join("\n", checks.Select(c => $"{c.Foreground} / {c.Background}: {c.Ratio:0.00}:1 · AA {(c.AA ? "✓" : "×")}")); }));
        dialog.Content = Col(Text(L.T("أدوار الألوان"), Typography.Heading, true), Label(L.T("أدوار محفوظة مستقلة. اترك الدور فارغاً لاستبعاده.")),
            Choose([L.T("فاتح"), L.T("داكن")], isDark ? 1 : 0, i => isDark = i == 1), rows, feedback, actions, Btn(L.T("إغلاق"), () => dialog.Close()));
        await dialog.Show();
    }
    async Task ExportThemes(Dictionary<string, Dictionary<string, string>> themes)
    {
        var type = await SelectOption(L.T("صيغة التصدير"), ["CSS · light + dark", "Design tokens · JSON", "WCAG contrast · JSON"]);
        if (type == null) return;
        await WriteFile(type == 0 ? "palette.css" : type == 1 ? "palette.tokens.json" : "palette.contrast.json",
            type == 0 ? SemanticPalette.Css(themes) : type == 1 ? SemanticPalette.Tokens(themes) : SemanticPalette.Report(themes));
    }
    Dictionary<string, Dictionary<string, string>> DailyThemes(JsonElement p) => new()
    {
        ["light"] = SemanticPalette.WithLocks(SemanticPalette.Read(p, "light"), state.BrandLocks),
        ["dark"] = SemanticPalette.WithLocks(SemanticPalette.Read(p, "dark"), state.BrandLocks)
    };
    void RememberDaily(JsonElement p)
    {
        if (damaged) return;
        var entry = SemanticPalette.Archive(p);
        if (state.Discoveries.Any(d => d.Id == entry.Id)) return;
        Edit(() => { state.Discoveries.Insert(0, entry); state.Discoveries = state.Discoveries.Take(90).ToList(); }, false);
    }
    async Task DailyArchive()
    {
        var dialog = Dialog(L.T("أرشيف الباقات")); dialog.Width = 560;
        var entries = new StackPanel { Spacing = 14 };
        foreach (var entry in state.Discoveries)
        {
            var dots = Row([.. entry.Light.Select(r => (Control)Dot(r.Value, () => _ = Guard(() => Copy(r.Value)), 26))]);
            var themes = new Dictionary<string, Dictionary<string, string>> { ["light"] = entry.Light, ["dark"] = entry.Dark };
            entries.Children.Add(Col(Text(entry.Name, Typography.Title, true), Label(entry.CreatedAt), dots, Row(
                Btn(L.T("تصدير"), async () => { dialog.Close(); await ExportThemes(themes); }),
                Btn(L.T("حفظ كلوحة"), () =>
                {
                    var colors = (dark ? entry.Dark : entry.Light).Select(r => new Swatch { Name = r.Key, Hex = r.Value }).ToList();
                    Edit(() => state.Palettes.Add(new Palette { Name = entry.Name, Colors = colors, Roles = new(dark ? entry.Dark : entry.Light) })); dialog.Close();
                }))));
        }
        if (entries.Children.Count == 0) entries.Children.Add(Label(L.T("ستظهر هنا الباقات التي تفتحها، حتى 90 باقة.")));
        dialog.Content = Col(Text(L.T("أرشيف الباقات"), Typography.Heading, true), entries, Btn(L.T("إغلاق"), () => dialog.Close())); await dialog.Show();
    }
    async Task BrandColors()
    {
        var dialog = Dialog(L.T("ألوان العلامة المثبتة")); var rows = new StackPanel { Spacing = 12 };
        foreach (string role in new[] { "primary", "accent" })
        {
            rows.Children.Add(Col(Mono(role), Label(state.BrandLocks.GetValueOrDefault(role, "—")), Row(
                Btn(L.T("تثبيت اللون المحدد"), () => { Edit(() => state.BrandLocks[role] = state.Color); dialog.Close(); }),
                Btn(L.T("إلغاء التثبيت"), () => { Edit(() => state.BrandLocks.Remove(role)); dialog.Close(); }))));
        }
        dialog.Content = Col(Text(L.T("ألوان العلامة المثبتة"), Typography.Heading, true), Label(L.T("تُطبّق على تصدير باقة اليوم. افحص التباين بعد التثبيت.")), rows, Btn(L.T("إغلاق"), () => dialog.Close()));
        await dialog.Show();
    }
    async Task PaletteContrast(Palette p)
    {
        // Bound the matrix: users can export a selected subset to inspect other colors.
        var colors = p.Colors.Where(c => selectedSwatches.Count == 0 || selectedSwatches.Contains(c.Id)).Take(12).ToArray();
        if (colors.Length == 0) return;
        var dialog = Dialog(L.T("مصفوفة التباين")); dialog.Width = Math.Min(900, Bounds.Width - 40);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("90", colors.Length + 1))), RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("42", colors.Length + 1))), FlowDirection = FlowDirection.LeftToRight };
        void Cell(Control child, int row, int column) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
        for (int i = 0; i < colors.Length; i++)
        {
            Cell(Mono(colors[i].Hex, Typography.Caption), i + 1, 0); Cell(Mono(colors[i].Hex, Typography.Caption), 0, i + 1);
            for (int j = 0; j < colors.Length; j++)
            {
                var r = Core.Contrast(colors[i].Hex, colors[j].Hex); bool aa = r.GetProperty("aa").GetBoolean();
                var value = Mono(r.GetProperty("ratio").GetDouble().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + (aa ? " ✓" : " ×"), Typography.Caption);
                value.Foreground = Paint(Core.Describe(colors[j].Hex).Text("ink")); value.HorizontalAlignment = HorizontalAlignment.Center;
                var tile = new Border { Background = Paint(colors[j].Hex), Child = value, Margin = new Thickness(1), CornerRadius = new CornerRadius(5) };
                ToolTip.SetTip(tile, colors[i].Hex + " / " + colors[j].Hex + (aa ? " · AA" : L.T(" · لا يحقق AA"))); Cell(tile, i + 1, j + 1);
            }
        }
        var simulation = new StackPanel { Spacing = 8 };
        string[] modes = [L.T("عمى الأحمر (Protanopia)"), L.T("عمى الأخضر (Deuteranopia)"), L.T("عمى الأزرق (Tritanopia)")];
        for (int kind = 0; kind < modes.Length; kind++)
        {
            var dots = new WrapPanel();
            foreach (var color in colors)
            {
                string preview = ColorVision.Simulate(color.Hex, kind);
                var swatch = new Border { Width = 34, Height = 34, Background = Paint(preview), CornerRadius = new CornerRadius(8), Margin = new Thickness(3) };
                ToolTip.SetTip(swatch, color.Hex + " → " + preview); dots.Children.Add(swatch);
            }
            simulation.Children.Add(Col(Text(modes[kind], Typography.Control, true), dots));
        }
        dialog.Content = Col(Text(L.T("مصفوفة التباين"), Typography.Heading, true), Label(L.T("أول 12 لوناً من التحديد أو اللوحة. ✓ يحقق AA للنص العادي.")),
            new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, Label(L.T("محاكاة رؤية الألوان تقريبية. قياسات التباين للألوان الأصلية.")), simulation, Btn(L.T("إغلاق"), () => dialog.Close()));
        await dialog.Show();
    }
}
