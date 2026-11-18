using System.Text;
using System.Text.Json;
using Avalonia.Controls;

namespace PickerHub;

internal static class PaletteExchange
{
    public static Palette Parse(byte[] bytes)
    {
        const string invalid = "ملف اللوحة غير صالح. اختر ملف JSON صدّرته من PickerHub.";
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
            var root = document.RootElement;
            string name = ReadName(root);
            if (!root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Array ||
                colors.GetArrayLength() is < 1 or > Palette.MaxColors)
                throw new InvalidDataException(L.T(invalid));

            var palette = new Palette { Name = name };
            foreach (var item in colors.EnumerateArray())
            {
                string swatchName = ReadName(item);
                if (!item.TryGetProperty("hex", out var code) || code.ValueKind != JsonValueKind.String || !Workspace.Hex(code.GetString()))
                    throw new InvalidDataException(L.T(invalid));
                palette.Colors.Add(new Swatch { Name = swatchName, Hex = code.GetString()!.ToUpperInvariant() });
            }
            Workspace.RefreshAutomaticNames(palette.Colors);
            return palette;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(L.T(invalid), ex);
        }

        static string ReadName(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String && name.GetString() is { Length: <= 100 } value &&
                !string.IsNullOrWhiteSpace(value))
                return value;
            throw new InvalidDataException(L.T(invalid));
        }
    }

    public static Swatch[] UniqueAgainst(Palette incoming, IEnumerable<Swatch> existing)
    {
        var seen = new HashSet<string>(existing.Select(color => color.Hex), StringComparer.OrdinalIgnoreCase);
        return incoming.Colors.Where(color => seen.Add(color.Hex)).ToArray();
    }
}

internal sealed partial class MainWindow
{
    async Task ImportPalette(Palette destination)
    {
        var bytes = await ReadFile(L.T("استيراد لوحة JSON"), ["*.json"], 2 << 20);
        if (bytes == null) return;
        await PreviewPaletteImport(PaletteExchange.Parse(bytes), destination);
    }

    async Task PreviewPaletteImport(Palette incoming, Palette destination)
    {
        var unique = PaletteExchange.UniqueAgainst(incoming, destination.Colors);
        int duplicates = incoming.Colors.Count - unique.Length;
        var preview = new WrapPanel();
        foreach (var color in incoming.Colors.Take(12))
        {
            var chip = new Border { Width = 22, Height = 22, Background = Paint(color.Hex), CornerRadius = new Avalonia.CornerRadius(5), Margin = new Avalonia.Thickness(2) };
            ToolTip.SetTip(chip, color.Name + " · " + Isolate(color.Hex));
            preview.Children.Add(chip);
        }
        var dialog = Dialog(L.T("استيراد لوحة JSON"));
        var create = Btn(L.T("إنشاء لوحة جديدة"), () => dialog.Close("new"), true);
        create.IsEnabled = state.Palettes.Count < Workspace.MaxPalettes;
        var merge = Btn(L.T("دمج في اللوحة الحالية"), () => dialog.Close("merge"));
        merge.IsEnabled = unique.Length > 0 && destination.Colors.Count + unique.Length <= Palette.MaxColors;
        dialog.Content = Col(Text(incoming.Name, Typography.Heading, true), preview,
            Label(string.Format(L.T("{0} لون في الملف"), incoming.Colors.Count)),
            Label(string.Format(L.T("الدمج في {0}: إضافة {1} وتجاوز {2} مكررة"), Isolate(destination.Name), unique.Length, duplicates)),
            Row(create, merge, Btn(L.T("إلغاء"), () => dialog.Close())));
        string? action = await dialog.Show<string>();
        if (action == null) return;
        Edit(() =>
        {
            if (action == "new") { state.Palettes.Add(incoming); paletteId = incoming.Id; }
            else destination.Colors.AddRange(unique);
            libraryQuery = ""; libraryFavorites = false; libraryPage = paletteCard = 0;
            selectedSwatches.Clear();
        });
        if (content.Parent is ScrollViewer scroll) scroll.Offset = default;
        status.Text = L.T("استُوردت اللوحة.");
    }
}
