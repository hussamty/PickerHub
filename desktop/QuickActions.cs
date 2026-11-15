using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace PickerHub;

internal sealed partial class MainWindow
{
    readonly EditHistory edits = new();
    readonly Border errorBanner = new() { IsVisible = false, Margin = new Thickness(24, 0, 24, 8), Padding = new Thickness(14) };
    Button? undoButton, redoButton;
    TrayIcon? tray;
    GlobalShortcut? shortcut;
    string shortcutStatus = L.T("غير مفعّل");
    bool closed;

    void ShowError(string message, Func<Task>? retry = null)
    {
        // A new banner must own its own controls. Reparenting the previous message
        // while its StackPanel is still attached throws from Avalonia's visual tree.
        var errorText = Text(L.T(message), 13);
        var actions = Row(Btn(L.T("الإعدادات والاستعادة"), Settings), Btn(L.T("إغلاق الرسالة"), () => errorBanner.IsVisible = false));
        if (retry != null) actions.Children.Insert(0, Btn(L.T("إعادة المحاولة"), async () => { errorBanner.IsVisible = false; await retry(); }));
        errorBanner.Child = Col(errorText, actions);
        errorBanner.IsVisible = true;
        AutomationProperties.SetLiveSetting(errorText, AutomationLiveSetting.Assertive);
    }
    Task pickCopies = Task.CompletedTask;
    string? pendingCopy;
    bool copyingPick;
    void QueuePickCopy(string value)
    {
        // Only the latest pending clipboard value matters; every color is already
        // recorded in the library. A slow clipboard manager must not grow a task queue.
        pendingCopy = value;
        if (copyingPick) return;
        copyingPick = true; pickCopies = DrainCopies();
    }
    async Task DrainCopies()
    {
        try
        {
            while (pendingCopy is { } value)
            {
                pendingCopy = null;
                await Guard(() => Copy(value).WaitAsync(TimeSpan.FromSeconds(2)));
            }
        }
        finally { copyingPick = false; }
    }
    void Travel(bool forward)
    {
        if (damaged) throw new InvalidOperationException(L.T("استعد المكتبة أولاً."));
        bool oldGlobal = state.GlobalShortcut; string oldShortcut = state.Shortcut;
        lastSaved = edits.Travel(lastSaved, forward, writer.Enqueue);
        state = Workspace.Parse(lastSaved); if (L.Language != state.Language) ApplyLanguage(); QueueRender();
        if (oldGlobal != state.GlobalShortcut || oldShortcut != state.Shortcut) _ = Guard(ConfigureShortcut);
        if (Picker.UsesPortal && !state.UseWaylandScreenSharing) WaylandCapture.Stop();
        status.Text = forward ? L.T("أُعيد التعديل") : L.T("تُراجع عن التعديل");
    }
    static string PickValue(Workspace w, string hex)
    {
        if (w.CopyFormat != "HEX") return Core.Describe(hex).GetProperty("formats").Text(w.CopyFormat);
        string value = w.HexUppercase ? hex.ToUpperInvariant() : hex.ToLowerInvariant();
        return w.HexPrefix ? value : value.TrimStart('#');
    }
    void SetupQuickActions()
    {
        KeyDown += (_, e) =>
        {
            if (e.Source is TextBox || overlay.Children.Count > 0 || picking) return;
            bool command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            if (command && (e.Key == Key.Z || e.Key == Key.Y))
            { e.Handled = true; _ = Guard(() => { Travel(e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift)); return Task.CompletedTask; }); }
        };
        // A small opaque icon stays readable in either system theme.
        using var pixels = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(pixels))
        {
            canvas.Clear(new SKColor(17, 17, 17)); using var paint = new SKPaint { Color = new SKColor(255, 240, 0), IsAntialias = true };
            canvas.DrawCircle(16, 16, 10, paint);
        }
        using var png = pixels.Encode(SKEncodedImageFormat.Png, 100); using var stream = png.AsStream();
        tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "PickerHub", IsVisible = true };
        SetTrayMenu();
        tray.Clicked += (_, _) => { Show(); Activate(); };
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        if (Application.Current?.PlatformSettings is { } platform)
        {
            EventHandler<Avalonia.Platform.PlatformColorValues> changed = (_, _) => Dispatcher.UIThread.Post(() => { if (!closed && state.FollowSystemTheme) Render(); });
            platform.ColorValuesChanged += changed; Closed += (_, _) => platform.ColorValuesChanged -= changed;
        }
        bool openedOnce = false;
        Opened += (_, _) => { if (openedOnce) return; openedOnce = true; if (state.GlobalShortcut) _ = Guard(ConfigureShortcut); };
        Closed += (_, _) => { closed = true; analysisCancellation?.Cancel(); shortcut?.Dispose(); tray.Dispose(); dailyClock?.Stop(); };
    }
    void SetTrayMenu()
    {
        if (tray == null) return;
        var menu = new NativeMenu();
        void Item(string title, Func<Task> action) { var item = new NativeMenuItem(title); item.Click += (_, _) => _ = Guard(action); menu.Items.Add(item); }
        Item(L.T("التقاط ونسخ"), () => PickDesktop(true)); Item(L.T("التقاط وتحرير"), () => PickDesktop(false));
        Item(L.T("التقاط متعدد إلى اللوحة الحالية"), () => PickDesktop(null, true));
        Item(L.T("فتح PickerHub"), () => { Show(); Activate(); return Task.CompletedTask; });
        Item(L.T("خروج"), () => { Close(); return Task.CompletedTask; }); tray.Menu = menu;
    }
    async Task ConfigureShortcut()
    {
        shortcut?.Dispose(); shortcut = null;
        if (!state.GlobalShortcut) { shortcutStatus = L.T("غير مفعّل"); return; }
        shortcutStatus = L.T("جارٍ التفعيل…");
        var next = new GlobalShortcut(); shortcut = next;
        try
        {
            await next.Start(this, state.Shortcut, () => { if (!closed && !picking && overlay.Children.Count == 0) _ = Guard(() => PickDesktop()); });
            if (closed || shortcut != next) { next.Dispose(); return; }
            shortcutStatus = next.Description;
        }
        catch { next.Dispose(); if (shortcut == next) { shortcut = null; shortcutStatus = L.T("غير متاح؛ غيّر الاختصار أو استخدم F8 داخل التطبيق."); } throw; }
    }
    string Diagnostics() => Serialize(new
    {
        version = typeof(App).Assembly.GetName().Version?.ToString(),
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        picker = Picker.UsesPortal ? (state.UseWaylandScreenSharing ? "portal-live" : "system-color") : OperatingSystem.IsMacOS() ? "macos-native" : "native-lens",
        displays = Screens.All.Select(s => new { width = s.Bounds.Width, height = s.Bounds.Height, scale = s.Scaling }),
        events = SupportLog.Snapshot(),
        libraryReadOnly = damaged, globalShortcut = shortcut != null, sharing = Picker.UsesPortal && WaylandCapture.IsSharing
    });
    Control QuickSettings()
    {
        var auto = new CheckBox { Content = L.T("نسخ اللون تلقائياً بعد الالتقاط"), IsChecked = state.AutoCopy };
        auto.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.AutoCopy = auto.IsChecked == true, false); return Task.CompletedTask; });
        string[] formats = ["HEX", "RGB", "HSL"];
        var upper = new CheckBox { Content = L.T("أحرف HEX كبيرة"), IsChecked = state.HexUppercase };
        var prefix = new CheckBox { Content = L.T("تضمين #"), IsChecked = state.HexPrefix };
        upper.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.HexUppercase = upper.IsChecked == true, false); return Task.CompletedTask; });
        prefix.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.HexPrefix = prefix.IsChecked == true, false); return Task.CompletedTask; });
        var enabled = new CheckBox { Content = L.T("اختصار عام من أي نافذة"), IsChecked = state.GlobalShortcut };
        var info = Label(L.T(shortcutStatus));
        enabled.IsCheckedChanged += (_, _) => _ = Guard(async () => { Edit(() => state.GlobalShortcut = enabled.IsChecked == true, false); try { await ConfigureShortcut(); } finally { info.Text = shortcutStatus; } });
        string[] keys = ["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"];
        var key = Choose(keys, Array.IndexOf(keys, state.Shortcut), i => _ = Guard(async () => { Edit(() => state.Shortcut = keys[i], false); try { await ConfigureShortcut(); } finally { info.Text = shortcutStatus; } }));
        var capability = Picker.UsesPortal ? L.T("منتقي النظام: إذن لون واحد. عدسة PickerHub: مشاركة الشاشة اختيارية.") : OperatingSystem.IsMacOS() ? L.T("منتقي macOS الأصلي · ألوان sRGB") : L.T("عدسة حيّة · التنقل بين النوافذ · Esc للإلغاء");
        return Col(Label(L.T("الالتقاط السريع")), auto, Field(L.T("صيغة النسخ"), Choose(formats, Array.IndexOf(formats, state.CopyFormat), i => Edit(() => state.CopyFormat = formats[i], false))), Row(upper, prefix), enabled, key, info, Label(capability));
    }
}
