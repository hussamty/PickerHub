using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PickerHub;

internal static class ProjectSearch
{
    internal sealed record Result(Palette Palette, Swatch? Color = null, string? Role = null);

    public static IEnumerable<Result> Find(IEnumerable<Palette> palettes, string query, bool favorites)
    {
        query = query.Trim();
        bool Matches(string value) => value.Contains(query, StringComparison.OrdinalIgnoreCase);
        foreach (var palette in palettes)
        {
            if (!favorites && (Matches(palette.Name) || Matches(palette.Collection)))
                yield return new(palette);
            if (query.Length == 0 && !favorites) continue;
            foreach (var color in palette.Colors)
                if ((!favorites || color.Favorite) && (Matches(color.Name) || Matches(color.Hex)))
                    yield return new(palette, color);
            if (favorites) continue;
            foreach (var (role, hex) in palette.Roles)
            {
                int index = Array.IndexOf(SemanticPalette.Roles, role);
                if (index >= 0 && (Matches(role) || Matches(L.T(SemanticPalette.Labels[index])) || Matches(hex)))
                    yield return new(palette, Role: role);
            }
        }
    }
}

internal sealed partial class MainWindow
{
    async Task FindInProjects()
    {
        const int limit = 30;
        var dialog = Dialog(L.T("البحث في المشاريع"));
        dialog.Width = 600;
        var query = new TextBox
        {
            Watermark = L.T("مشروع أو لوحة أو اسم لون أو HEX أو دور"),
            MaxLength = 100
        };
        AutomationProperties.SetName(query, L.T("البحث في المشاريع"));
        var favorites = new CheckBox { Content = L.T("المفضلة فقط") };
        var results = Col();
        var summary = Label("");
        ProjectSearch.Result[] hits = [];
        Button[] buttons = [];
        string? displayedQuery = null;
        bool displayedFavorites = false;
        void Refresh()
        {
            displayedQuery = query.Text ?? "";
            displayedFavorites = favorites.IsChecked == true;
            hits = ProjectSearch.Find(state.Palettes, displayedQuery, displayedFavorites).Take(limit + 1).ToArray();
            results.Children.Clear();
            summary.Text = hits.Length switch
            {
                0 => L.T("لا توجد نتائج مطابقة."),
                > limit => L.T("تظهر أول 30 نتيجة. ضيّق البحث لعرض نتائج أخرى."),
                _ => ""
            };
            summary.IsVisible = summary.Text.Length > 0;
            buttons = hits.Take(limit).Select((hit, i) =>
            {
                string place = hit.Palette.Collection.Length > 0
                    ? Isolate(hit.Palette.Collection) + " / " + Isolate(hit.Palette.Name)
                    : Isolate(hit.Palette.Name);
                string title = hit.Color is { } color ? Isolate(color.Name) + " · " + Isolate(color.Hex)
                    : hit.Role is { } role ? L.T(SemanticPalette.Labels[Array.IndexOf(SemanticPalette.Roles, role)]) + " · " + Isolate(hit.Palette.Roles[role])
                    : place;
                if (hit.Color != null || hit.Role != null) title += " · " + place;
                var button = Of(Btn(title, () => dialog.Close(hit)), "row");
                button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                button.Content = Text(title);
                ((TextBlock)button.Content).TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                AutomationProperties.SetName(button, title);
                button.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Up) { e.Handled = true; if (i == 0) query.Focus(); else buttons[i - 1].Focus(); }
                    if (e.Key == Key.Down && i + 1 < buttons.Length) { e.Handled = true; buttons[i + 1].Focus(); }
                };
                return button;
            }).ToArray();
            results.Children.AddRange(buttons);
        }
        bool queued = false;
        void QueueRefresh()
        {
            if (queued) return;
            queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (queued && query.GetVisualRoot() != null) Refresh();
                queued = false;
            }, DispatcherPriority.Background);
        }
        query.TextChanged += (_, _) => QueueRefresh();
        favorites.IsCheckedChanged += (_, _) => QueueRefresh();
        query.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Down or Key.Enter)) return;
            e.Handled = true;
            if (queued || displayedQuery != (query.Text ?? "") || displayedFavorites != (favorites.IsChecked == true))
            {
                queued = false;
                Refresh();
            }
            if (buttons.Length == 0) return;
            if (e.Key == Key.Down) buttons[0].Focus(); else dialog.Close(hits[0]);
        };
        dialog.Opened += () => query.Focus();
        dialog.Content = Col(Text(L.T("البحث في المشاريع"), Typography.Heading, true), query, favorites,
            summary, results, Choices(Btn(L.T("إغلاق"), () => dialog.Close())));
        Refresh();
        var result = await dialog.Show<ProjectSearch.Result>();
        if (result == null) return;
        paletteId = result.Palette.Id;
        selectedSwatches.Clear();
        libraryFavorites = false;
        libraryPage = paletteCard = 0;
        libraryQuery = result.Color?.Hex ?? "";
        if (result.Color != null && state.LibraryView == "deck")
            paletteCard = result.Palette.Colors.IndexOf(result.Color);
        page = 1; Render();
        if (content.Parent is ScrollViewer scroll) scroll.Offset = default;
        if (result.Role != null) await Roles(result.Palette);
    }
}
