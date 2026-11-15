using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PickerHub;

internal static class LibraryOps
{
    public const int PageSize = 48;
    public static Swatch[] Filter(Palette p, string query, bool favorites) => p.Colors.Where(c => (!favorites || c.Favorite) &&
        (c.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || c.Hex.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
    public static int Deduplicate(Palette p)
    {
        int count = p.Colors.Count; var seen = new Dictionary<string, Swatch>(StringComparer.OrdinalIgnoreCase);
        p.Colors = p.Colors.Where(c => { if (!seen.TryGetValue(c.Hex, out var first)) { seen[c.Hex] = c; return true; } first.Favorite |= c.Favorite; return false; }).ToList();
        return count - p.Colors.Count;
    }
    public static void Move(Palette source, Palette target, HashSet<string> ids)
    {
        if (ReferenceEquals(source, target)) return;
        var colors = source.Colors.Where(c => ids.Contains(c.Id)).ToList();
        if (target.Colors.Count + colors.Count > 2048) throw new InvalidOperationException(L.T("الحد الأقصى للوحة 2048 لوناً."));
        foreach (var c in colors) { if (target.Colors.Any(t => t.Id == c.Id)) c.Id = Guid.NewGuid().ToString("N"); }
        source.Colors.RemoveAll(c => colors.Contains(c)); target.Colors.AddRange(colors);
    }
}
internal sealed partial class MainWindow
{
    string libraryQuery = "";
    bool libraryFavorites;
    int libraryPage;
    readonly HashSet<string> selectedSwatches = [];
    Control LibraryBrowser(Palette p)
    {
        selectedSwatches.IntersectWith(p.Colors.Select(c => c.Id));
        var query = new TextBox { Text = libraryQuery, Watermark = L.T("ابحث بالاسم أو HEX"), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(query, L.T("البحث في ألوان اللوحة"));
        var favorites = new CheckBox { Content = L.T("المفضلة فقط"), IsChecked = libraryFavorites };
        var results = new StackPanel { Spacing = 12 }; var count = Label("");
        var selectedCount = Label("");
        var batch = new WrapPanel();
        Swatch[] Selection() => p.Colors.Where(c => selectedSwatches.Contains(c.Id)).ToArray();
        void Sync() { selectedCount.Text = L.T("المحدد: ") + selectedSwatches.Count; batch.IsVisible = selectedSwatches.Count > 0; }
        batch.Children.Add(Btn(L.T("نسخ المحدد"), () => Copy(string.Join("\n", Selection().Select(c => PickValue(state, c.Hex))))));
        batch.Children.Add(Btn(L.T("تصدير المحدد"), () => Export(new Palette { Name = p.Name, Colors = Selection().ToList() })));
        batch.Children.Add(Btn(L.T("نقل المحدد"), async () =>
        {
            var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException(L.T("أنشئ لوحة ثانية أولاً."));
            var to = await SelectOption(L.T("نقل إلى لوحة"), others.Select(a => a.Name).ToArray());
            if (to != null) Edit(() => { LibraryOps.Move(p, others[to.Value], selectedSwatches); selectedSwatches.Clear(); });
        }));
        batch.Children.Add(Btn(L.T("حذف المحدد"), async () => { if (await Confirm(L.T("حذف الألوان المحددة؟ يمكنك التراجع."))) Edit(() => { p.Colors.RemoveAll(c => selectedSwatches.Contains(c.Id)); selectedSwatches.Clear(); }); }));
        void Refresh()
        {
            results.Children.Clear();
            var matches = LibraryOps.Filter(p, libraryQuery, libraryFavorites);
            int pages = Math.Max(1, (matches.Length + LibraryOps.PageSize - 1) / LibraryOps.PageSize);
            libraryPage = Math.Clamp(libraryPage, 0, pages - 1);
            var visible = matches.Skip(libraryPage * LibraryOps.PageSize).Take(LibraryOps.PageSize).ToArray();
            count.Text = matches.Length + " / " + p.Colors.Count; Sync();
            var tiles = new WrapPanel(); var lines = new StackPanel { Spacing = 6 };
            foreach (var color in visible)
            {
                var check = new CheckBox { Content = color.Name, IsChecked = selectedSwatches.Contains(color.Id), MaxWidth = 220 };
                AutomationProperties.SetName(check, L.T("تحديد ") + color.Name + " " + color.Hex);
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) selectedSwatches.Add(color.Id); else selectedSwatches.Remove(color.Id); Sync(); };
                var chip = Chip(color.Hex, () => { page = 0; Select(color.Hex); }, state.LibraryView == "list" ? "" : color.Hex);
                chip.Height = state.LibraryView == "list" ? 40 : 64; chip.CornerRadius = new CornerRadius(12);
                if (state.LibraryView == "list") chip.Width = 44;
                var star = Btn(color.Favorite ? "★" : "☆", () => Edit(() => color.Favorite = !color.Favorite));
                AutomationProperties.SetName(star, L.T("تبديل المفضلة: ") + color.Name);
                var actions = Line(4, star, Liquid("copy", L.T("نسخ اللون"), () => Copy(PickValue(state, color.Hex))), Liquid("pencil", L.T("تعديل الاسم"), async () => { var n = await Prompt(L.T("اسم اللون"), color.Name); if (n != null) Edit(() => color.Name = n); }));
                Control tile;
                if (state.LibraryView == "list")
                {
                    var line = new DockPanel(); DockPanel.SetDock(actions, Dock.Right); line.Children.Add(actions); DockPanel.SetDock(chip, Dock.Left); line.Children.Add(chip);
                    check.Margin = new Thickness(10, 0); line.Children.Add(check); tile = line;
                }
                else { tile = Col(chip, check, actions); tile.Width = 210; tile.Margin = new Thickness(0, 0, 12, 12); }
                if (state.LibraryView == "list") lines.Children.Add(tile); else tiles.Children.Add(tile);
            }
            if (matches.Length == 0) results.Children.Add(Label(L.T("لا توجد ألوان مطابقة. جرّب بحثاً آخر.")));
            else results.Children.Add(state.LibraryView == "list" ? lines : tiles);
            var prev = Btn(L.T("السابق"), () => { libraryPage--; Refresh(); }); prev.IsEnabled = libraryPage > 0;
            var next = Btn(L.T("التالي"), () => { libraryPage++; Refresh(); }); next.IsEnabled = libraryPage < pages - 1;
            results.Children.Add(Row(prev, Mono($"{libraryPage + 1} / {pages}"), next,
                Btn(L.T("تحديد الصفحة"), () => { selectedSwatches.UnionWith(visible.Select(c => c.Id)); Refresh(); }),
                Btn(L.T("إلغاء التحديد"), () => { selectedSwatches.Clear(); Refresh(); })));
        }
        query.TextChanged += (_, _) => { libraryQuery = query.Text ?? ""; libraryPage = 0; Refresh(); };
        favorites.IsCheckedChanged += (_, _) => { libraryFavorites = favorites.IsChecked == true; libraryPage = 0; Refresh(); };
        Refresh();
        return Section(L.T("ألوان اللوحة"), Col(Row(query, favorites, count), selectedCount, batch, results),
            Btn(L.T("إزالة المكرر"), () => { int removed = 0; Edit(() => removed = LibraryOps.Deduplicate(p)); status.Text = L.T("أزيلت الألوان المكررة: ") + removed; }));
    }
}
