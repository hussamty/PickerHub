using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PickerHub;

internal sealed partial class MainWindow
{
    async Task ColorActions(string color, Func<Task>? rename = null)
    {
        var dialog = Dialog(L.T("إجراءات اللون"));
        // Copies in the format chosen in Settings, as every other copy action does.
        var copy = Btn(L.T("نسخ رمز اللون"), () => dialog.Close("copy"));
        var text = Btn(L.T("استخدام كلون للنص"), () => dialog.Close("text"));
        var backdrop = Btn(L.T("استخدام كلون للخلفية"), () => dialog.Close("background"));
        AutomationProperties.SetAutomationId(copy, "copy-color-code");
        AutomationProperties.SetAutomationId(text, "use-as-text");
        AutomationProperties.SetAutomationId(backdrop, "use-as-background");
        var actions = Col(Text(L.T("إجراءات اللون"), Typography.Heading, true), Mono(color), copy, text, backdrop);
        if (rename != null) actions.Children.Add(Btn(L.T("تعديل الاسم"), () => dialog.Close("rename")));
        actions.Children.Add(Btn(L.T("إلغاء"), () => dialog.Close()));
        dialog.Content = actions;
        switch (await dialog.Show<string>())
        {
            case "copy": await Copy(PickValue(state, color)); break;
            case "text": SetContrastColor(color, asText: true); break;
            case "background": SetContrastColor(color, asText: false); break;
            case "rename" when rename != null: await rename(); break;
        }
    }

    void SetContrastColor(string color, bool asText)
    {
        if (asText) foreground = color;
        else background = color;
        page = 2;
        Render();
        var id = asText ? "contrast-text" : "contrast-background";
        Dispatcher.UIThread.Post(() => content.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == id)?.Focus(), DispatcherPriority.Loaded);
    }

    async Task SaveContrastRoles()
    {
        if (damaged) return;
        var paletteIndex = await SelectOption(L.T("حفظ النص والخلفية في لوحة"),
            state.Palettes.Select(palette => palette.Name).ToArray(), state.Palettes.IndexOf(Current));
        if (paletteIndex == null) return;
        var palette = state.Palettes[paletteIndex.Value];
        bool Replaces(string role, string hex) => palette.Roles.TryGetValue(role, out var old) &&
            !hex.Equals(old, StringComparison.OrdinalIgnoreCase);
        if ((Replaces("text", foreground) || Replaces("background", background)) &&
            !await Confirm(L.T("استبدال دوري النص والخلفية في اللوحة المختارة؟"))) return;
        Edit(() =>
        {
            palette.Roles["text"] = foreground;
            palette.Roles["background"] = background;
        }, false);
        status.Text = L.T("حُفظ دوران في ") + Isolate(palette.Name);
    }
}
