using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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

    void ShowError(string message, Func<Task>? retry = null, Button? alternate = null, string? retryLabel = null)
    {
        // A new banner must own its own controls. Reparenting the previous message
        // while its StackPanel is still attached throws from Avalonia's visual tree.
        var errorText = Text(L.T(message), Typography.Body);
        var actions = Row(Btn(L.T("الإعدادات والاستعادة"), Settings), Btn(L.T("إغلاق الرسالة"), () => errorBanner.IsVisible = false));
        if (alternate != null) actions.Children.Insert(0, alternate);
        if (retry != null) actions.Children.Insert(0, Btn(retryLabel ?? L.T("إعادة المحاولة"), async () => { errorBanner.IsVisible = false; await retry(); }));
        errorBanner.Child = Col(errorText, actions); errorBanner.FlowDirection = L.Flow;
        errorBanner.IsVisible = true;
        AutomationProperties.SetLiveSetting(errorText, AutomationLiveSetting.Assertive);
    }
    Task pickCopies = Task.CompletedTask;
    Func<string, Task>? pickClipboard; // Injectable only by the in-process regression harness.
    sealed record PickCopy(long Session, string Hex, string Format, string Value, long Start, int Collected);
    long pickSession;
    PickCopy? pendingCopy;
    bool copyingPick;
    void QueuePickCopy(long session, string hex, long start, int collected)
    {
        // Only the latest pending clipboard value matters; every color is already
        // recorded in the library. A slow clipboard manager must not grow a task queue.
        pendingCopy = new(session, hex, state.CopyFormat, PickValue(state, hex), start, collected);
        if (copyingPick) return;
        copyingPick = true; pickCopies = DrainCopies();
    }
    async Task DrainCopies()
    {
        try
        {
            while (pendingCopy is { } copy)
            {
                pendingCopy = null;
                if (copy.Session != pickSession || closed) continue;
                try
                {
                    await (pickClipboard != null ? pickClipboard(copy.Value) : WriteClipboard(copy.Value)).WaitAsync(TimeSpan.FromSeconds(2));
                    SupportLog.Measure("pick-to-clipboard", copy.Start);
                    if (!closed && copy.Session == pickSession && pendingCopy == null)
                    {
                        string copied = string.Format(L.T("نُسخ {0} بصيغة {1}"), Isolate(copy.Hex), Isolate(copy.Format));
                        status.Text = copy.Collected == 0 ? copied : L.T("الألوان الملتقطة: ") + copy.Collected + " · " + copied;
                    }
                }
                catch (Exception ex)
                {
                    SupportLog.Record("pick-clipboard", ex);
                    if (!closed && copy.Session == pickSession && pendingCopy == null)
                        ShowError(L.T("حُفظ اللون، لكن تعذّر نسخه إلى الحافظة."), () =>
                        {
                            pendingCopy = copy with { Session = pickSession, Start = SupportLog.Measure() };
                            if (!copyingPick) { copyingPick = true; pickCopies = DrainCopies(); }
                            return pickCopies;
                        });
                }
            }
        }
        finally { copyingPick = false; }
    }
    void Travel(bool forward)
    {
        if (OverlayOpen || nativeDialogOpen || picking) return;
        if (damaged) throw new InvalidOperationException(L.T("استعد المكتبة أولاً."));
        bool oldGlobal = state.GlobalShortcut; string oldShortcut = state.Shortcut;
        lastSaved = edits.Travel(lastSaved, forward, writer.Enqueue);
        state = Workspace.Parse(lastSaved); SupportLog.Enabled = state.DiagnosticsEnabled; if (L.Language != state.Language) ApplyLanguage(); QueueRender();
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
            if (OverlayOpen || nativeDialogOpen || picking || e.Handled) return;
            if (e.Key == Key.F8 && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                _ = Guard(() => PickDesktop());
                return;
            }
            if (e.Source is TextBox) return;
            if (App.Preview && e.Key == Key.R && e.KeyModifiers == KeyModifiers.None && page == DailyTool) { e.Handled = true; _ = Guard(() => { ReplayEnvelope(); return Task.CompletedTask; }); return; }
            bool command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            if (command && (e.Key == Key.Z || e.Key == Key.Y))
            { e.Handled = true; _ = Guard(() => { Travel(e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift)); return Task.CompletedTask; }); }
        };
        // A preview window stays out of the tray: the real PickerHub may already be there.
        if (!App.Preview)
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PickerHub/Assets/icon-small-128.png"));
            tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "PickerHub", IsVisible = true };
            SetTrayMenu();
            tray.Clicked += (_, _) => BringForward();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        if (Application.Current?.PlatformSettings is { } platform)
        {
            EventHandler<Avalonia.Platform.PlatformColorValues> changed = (_, _) => Dispatcher.UIThread.Post(() => { if (!closed && state.FollowSystemTheme) Render(); });
            platform.ColorValuesChanged += changed; Closed += (_, _) => platform.ColorValuesChanged -= changed;
        }
        bool openedOnce = false;
        Opened += (_, _) => { if (openedOnce) return; openedOnce = true; if (state.GlobalShortcut) _ = Guard(ConfigureShortcut); };
        Closed += (_, _) => { closed = true; analysisCancellation?.Cancel(); shortcut?.Dispose(); tray?.Dispose(); dailyClock?.Stop(); };
    }
    void SetTrayMenu()
    {
        if (tray == null) return;
        var menu = new NativeMenu();
        void Item(string title, Func<Task> action) { var item = new NativeMenuItem(title); item.Click += (_, _) => _ = Guard(action); menu.Items.Add(item); }
        Item(L.T("التقاط ونسخ"), () => PickDesktop(true)); Item(L.T("التقاط وتحرير"), () => PickDesktop(false));
        Item(L.T("التقاط سريع"), () => PickDesktop(true, returnToWorkbench: false));
        Item(L.T("التقاط متعدد إلى اللوحة الحالية"), () => PickDesktop(null, true));
        Item(L.T("فتح PickerHub"), () => { BringForward(); return Task.CompletedTask; });
        Item(L.T("خروج"), () => { Close(); return Task.CompletedTask; }); tray.Menu = menu;
    }
    async Task ConfigureShortcut()
    {
        shortcut?.Dispose(); shortcut = null;
        if (!state.GlobalShortcut || App.Preview) { shortcutStatus = L.T("غير مفعّل"); return; }
        shortcutStatus = L.T("جارٍ التفعيل…");
        var next = new GlobalShortcut(); shortcut = next;
        try
        {
            await next.Start(this, state.Shortcut, () => _ = Guard(() =>
                PickDesktop(state.QuickPick ? true : null, returnToWorkbench: !state.QuickPick)));
            if (closed || shortcut != next) { next.Dispose(); return; }
            shortcutStatus = next.Description;
        }
        catch { next.Dispose(); if (shortcut == next) { shortcut = null; shortcutStatus = L.T("غير متاح؛ غيّر الاختصار أو استخدم F8 داخل التطبيق."); } throw; }
    }
    string Diagnostics() => Serialize(new
    {
        version = typeof(App).Assembly.GetName().Version?.ToString(),
        build = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
        binaryId = typeof(App).Module.ModuleVersionId,
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        picker = Picker.UsesPortal ? (state.UseWaylandScreenSharing ? "portal-live" : "system-color") : OperatingSystem.IsMacOS() ? "macos-native" : "native-lens",
        displays = Screens.All.Select(s => new { width = s.Bounds.Width, height = s.Bounds.Height, scale = s.Scaling }),
        events = SupportLog.Snapshot(),
        libraryReadOnly = damaged, globalShortcut = shortcut is { Description.Length: > 0 },
        sharing = Picker.UsesPortal && WaylandCapture.IsSharing,
        captureState = picking ? "picking" : Picker.UsesPortal && WaylandCapture.IsSharing ? "sharing" : "idle"
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
        var quiet = new CheckBox { Content = Label(L.T("ينسخ الاختصار العام دون تنشيط النافذة")), IsChecked = state.QuickPick, IsEnabled = state.GlobalShortcut };
        var info = Label(L.T(shortcutStatus));
        enabled.IsCheckedChanged += (_, _) => _ = Guard(async () => { Edit(() => state.GlobalShortcut = enabled.IsChecked == true, false); quiet.IsEnabled = state.GlobalShortcut; try { await ConfigureShortcut(); } finally { info.Text = shortcutStatus; } });
        quiet.IsCheckedChanged += (_, _) => _ = Guard(() => { Edit(() => state.QuickPick = quiet.IsChecked == true, false); return Task.CompletedTask; });
        string[] keys = ["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"];
        var key = Choose(keys, Array.IndexOf(keys, state.Shortcut), i => _ = Guard(async () => { Edit(() => state.Shortcut = keys[i], false); try { await ConfigureShortcut(); } finally { info.Text = shortcutStatus; } }));
        var capability = Picker.UsesPortal ? L.T("منتقي النظام: إذن لون واحد. عدسة PickerHub: مشاركة الشاشة اختيارية.") : OperatingSystem.IsMacOS() ? L.T("منتقي macOS الأصلي · ألوان sRGB") : L.T("عدسة حيّة · التنقل بين النوافذ · Esc للإلغاء");
        return Col(Text(L.T("الالتقاط السريع"), Typography.Body, true), auto, Field(L.T("صيغة النسخ"), Choose(formats, Array.IndexOf(formats, state.CopyFormat), i => Edit(() => state.CopyFormat = formats[i], false))), Row(upper, prefix), enabled, quiet, key, info, Label(capability));
    }
}
