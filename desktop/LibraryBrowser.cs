using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PickerHub;

internal static class LibraryOps
{
    public const int PageSize = 24;
    public static Swatch[] Filter(Palette p, string query, bool favorites)
    {
        query = query.Trim();
        return p.Colors.Where(c => (!favorites || c.Favorite) &&
            (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Hex.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
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
        if (target.Colors.Count + colors.Count > Palette.MaxColors) throw new InvalidOperationException(L.T("الحد الأقصى للوحة 2048 لوناً."));
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
        AutomationProperties.SetAutomationId(query, "palette-search");
        var favorites = new CheckBox { Content = L.T("المفضلة فقط"), IsChecked = libraryFavorites };
        var results = new StackPanel { Spacing = 12 }; var count = Label("");
        var selectedCount = Label("");
        var batch = new WrapPanel();
        var position = Mono("");
        Swatch[] matches = LibraryOps.Filter(p, libraryQuery, libraryFavorites);
        Swatch[] visible = [];
        var visibleChecks = new List<CheckBox>();
        var previous = Of(new Button { Content = L.T("السابق") }, "ph");
        var next = Of(new Button { Content = L.T("التالي") }, "ph");
        AutomationProperties.SetAutomationId(next, "palette-next-page");
        var selectPage = Btn(L.T("تحديد الصفحة"), () =>
        {
            foreach (var check in visibleChecks) check.IsChecked = true;
        });
        previous.Click += (_, _) => { libraryPage--; Refresh(); };
        next.Click += (_, _) => { libraryPage++; Refresh(); };
        var clearSelection = Btn(L.T("إلغاء التحديد"), () =>
        {
            selectedSwatches.Clear();
            foreach (var check in visibleChecks) check.IsChecked = false;
            Sync();
        });
        Swatch[] Selection() => p.Colors.Where(c => selectedSwatches.Contains(c.Id)).ToArray();
        void Sync()
        {
            selectedCount.Text = L.T("المحدد: ") + selectedSwatches.Count;
            selectedCount.IsVisible = batch.IsVisible = selectedSwatches.Count > 0;
        }
        batch.Children.Add(Btn(L.T("نسخ المحدد"), () => Copy(string.Join("\n", Selection().Select(c => PickValue(state, c.Hex))))));
        batch.Children.Add(Btn(L.T("تصدير المحدد"), () => Export(new Palette { Name = p.Name, Colors = Selection().ToList() })));
        batch.Children.Add(Btn(L.T("نقل المحدد"), async () =>
        {
            var others = state.Palettes.Where(a => a != p).ToArray(); if (others.Length == 0) throw new InvalidOperationException(L.T("أنشئ لوحة ثانية أولاً."));
            var to = await SelectOption(L.T("نقل إلى لوحة"), others.Select(a => a.Name).ToArray());
            if (to != null) Edit(() => { LibraryOps.Move(p, others[to.Value], selectedSwatches); selectedSwatches.Clear(); });
        }));
        batch.Children.Add(Btn(L.T("حذف المحدد"), async () => { if (await Confirm(L.T("حذف الألوان المحددة؟ يمكنك التراجع."))) Edit(() => { p.Colors.RemoveAll(c => selectedSwatches.Contains(c.Id)); selectedSwatches.Clear(); }); }));
        batch.Children.Add(clearSelection);
        void Refresh()
        {
            int pages = Math.Max(1, (matches.Length + LibraryOps.PageSize - 1) / LibraryOps.PageSize);
            libraryPage = Math.Clamp(libraryPage, 0, pages - 1);
            var nextColors = matches.Skip(libraryPage * LibraryOps.PageSize).Take(LibraryOps.PageSize).ToArray();
            count.Text = matches.Length + " / " + p.Colors.Count; Sync();
            position.Text = $"{libraryPage + 1} / {pages}";
            previous.IsEnabled = libraryPage > 0; next.IsEnabled = libraryPage < pages - 1;
            selectPage.IsEnabled = nextColors.Length > 0;
            if (results.Children.Count > 0 && visible.SequenceEqual(nextColors)) return;
            visible = nextColors;
            results.Children.Clear();
            visibleChecks.Clear();
            var tiles = new WrapPanel(); var lines = new StackPanel { Spacing = 6 };
            foreach (var color in visible)
            {
                var check = new CheckBox { Content = color.Name, IsChecked = selectedSwatches.Contains(color.Id), MaxWidth = 220 };
                visibleChecks.Add(check);
                AutomationProperties.SetName(check, L.T("تحديد ") + color.Name + " " + color.Hex);
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) selectedSwatches.Add(color.Id); else selectedSwatches.Remove(color.Id); Sync(); };
                var chip = Chip(color.Hex, () => { page = 0; Select(color.Hex); }, state.LibraryView == "list" ? "" : color.Hex);
                if (visibleChecks.Count == 1) AutomationProperties.SetAutomationId(chip, "first-palette-swatch");
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
                else { tile = Col(chip, check, actions); }
                var ink = Core.Describe(color.Hex).Text("ink");
                var swatchFill = Paint(color.Hex);
                var inkBrush = Paint(ink);
                var checkOutline = Paint(ink, .8);
                check.Foreground = inkBrush;
                // Selection and hover must remain readable on the swatch, even
                // when its contrast differs from the surrounding app theme.
                foreach (var selected in new[] { "Unchecked", "Checked" })
                    foreach (var interaction in new[] { "", "PointerOver", "Pressed", "Disabled" })
                    {
                        string suffix = selected + interaction;
                        check.Resources["CheckBoxForeground" + suffix] = inkBrush;
                        check.Resources["CheckBoxCheckBackgroundStroke" + suffix] = checkOutline;
                        check.Resources["CheckBoxCheckBackgroundFill" + suffix] = selected == "Checked" ? inkBrush : Brushes.Transparent;
                        check.Resources["CheckBoxCheckGlyphForeground" + suffix] = swatchFill;
                    }
                var actionFill = Paint(ink, .10);
                var actionOutline = Paint(ink, .22);
                foreach (var action in actions.Children.OfType<Button>())
                { action.Foreground = inkBrush; action.Background = actionFill; action.BorderBrush = actionOutline; }
                var card = new Border { Background = swatchFill, CornerRadius = new CornerRadius(18), Padding = new Thickness(12),
                    BorderBrush = Paint(ink, .18), BorderThickness = new Thickness(1), Child = tile };
                if (state.LibraryView == "list") lines.Children.Add(card);
                else { card.Width = 174; card.Margin = new Thickness(0, 0, 10, 10); tiles.Children.Add(card); }
            }
            if (matches.Length == 0) results.Children.Add(Label(L.T("لا توجد ألوان مطابقة. جرّب بحثاً آخر.")));
            else results.Children.Add(state.LibraryView == "list" ? lines : tiles);
        }
        bool refreshQueued = false;
        long searchStart = 0;
        void FilterResults()
        {
            searchStart = SupportLog.Measure();
            if (refreshQueued) return;
            refreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                refreshQueued = false;
                if (query.GetVisualRoot() == null) return;
                matches = LibraryOps.Filter(p, libraryQuery, libraryFavorites);
                libraryPage = 0; Refresh();
                MeasureFrame("palette-search-to-frame", searchStart);
            }, DispatcherPriority.Background);
        }
        query.TextChanged += (_, _) => { libraryQuery = query.Text ?? ""; FilterResults(); };
        favorites.IsCheckedChanged += (_, _) => { libraryFavorites = favorites.IsChecked == true; FilterResults(); };
        Refresh();
        var filters = Col(Row(query, favorites, count), Row(previous, position, next, selectPage));
        filters.Spacing = 4;
        return Surface(Col(filters, selectedCount, batch, results));
    }
}
