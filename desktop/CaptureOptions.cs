using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PickerHub;

internal sealed partial class MainWindow
{
    Button captureMode = null!;
    TextBlock captureModeText = null!;
    Icon captureModeIcon = null!;
    bool captureDecisionPending;

    Button BuildCaptureMode()
    {
        captureModeText = Text("", Typography.Caption);
        captureModeIcon = new Icon("settings", 16);
        captureMode = Btn("", () => CaptureOptions());
        captureMode.Content = Line(6, captureModeIcon, captureModeText);
        captureMode.IsVisible = Picker.UsesPortal;
        captureMode.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(captureMode, "capture-mode");
        if (Picker.UsesPortal)
        {
            WaylandCapture.SharingChanged += SharingChanged;
            Closed += (_, _) => WaylandCapture.SharingChanged -= SharingChanged;
        }
        return captureMode;
    }

    void SharingChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!closed) { RefreshCaptureMode(); SetTrayMenu(); }
    });

    string CaptureModeLabel() => WaylandCapture.IsSharing ? L.T("مشاركة الشاشة نشطة")
        : state.UseWaylandScreenSharing ? L.T("العدسة الحية جاهزة للاتصال") : L.T("منتقي النظام");

    void RefreshCaptureMode()
    {
        if (!Picker.UsesPortal) return;
        string label = CaptureModeLabel();
        captureModeText.Text = label;
        captureModeIcon.Classes.Set("sharing", WaylandCapture.IsSharing);
        ToolTip.SetTip(captureMode, label);
        AutomationProperties.SetName(captureMode, L.T("وضع الالتقاط") + " · " + label);
    }

    void SetCaptureMode(bool live)
    {
        Edit(() => state.UseWaylandScreenSharing = live, false);
        if (!live) WaylandCapture.Stop();
        RefreshCaptureMode();
    }

    void StopSharing()
    {
        WaylandCapture.Stop();
        RefreshCaptureMode();
        status.Text = L.T("توقفت مشاركة الشاشة.");
    }

    void SetPicking(bool value)
    {
        picking = value;
        eyedropper.IsEnabled = captureMode.IsEnabled = !value;
        if (value) { captureDecisionPending = false; spine.Collapse(); }
    }

    async Task<bool> PrepareLiveCapture()
    {
        if (WaylandCapture.IsSharing) return await WaylandCapture.Prepare(this);

        // GNOME expects a focused, mapped parent for its consent dialog.
        // The parent stays transparent until consent completes.
        RaiseWorkbench(WindowState);
        double opacity = Opacity;
        try
        {
            Picker.SetPortalParentTransparent(this, true);
            await Task.Delay(60);
            return !closed && await WaylandCapture.Prepare(this);
        }
        finally
        {
            if (!closed)
            {
                Picker.SetPortalParentTransparent(this, false);
                Opacity = opacity;
            }
        }
    }

    async Task EnableLiveSharing()
    {
        if (picking || closed) return;
        if (OverlayOpen || nativeDialogOpen) { BringForward(); return; }
        if (damaged)
        {
            BringForward();
            return;
        }
        SetCaptureMode(true);
        SetPicking(true);
        try
        {
            if (await PrepareLiveCapture()) status.Text = L.T("مشاركة الشاشة نشطة");
            else if (!closed) status.Text = L.T("لم تبدأ المشاركة. يمكنك اختيار منتقي النظام من وضع الالتقاط.");
        }
        catch (Exception error)
        {
            SupportLog.Record("capture-live", error);
            if (!closed) ShowCaptureDecision(error.Message, EnableLiveSharing);
        }
        finally
        {
            SetPicking(false);
            if (!FocusCaptureDecision() && !closed) { BringForward(); captureMode.Focus(); }
        }
    }

    void ShowCaptureError(string message, PickAction action, bool systemPicker, bool allowSystem)
    {
        var fallback = allowSystem ? Btn(L.T("التقاط لون بمنتقي النظام"), async () =>
        {
            errorBanner.IsVisible = false;
            await PickDesktop(action == PickAction.Collect ? PickAction.Default : action, systemPicker: true);
        }) : null;
        ShowCaptureDecision(message, () => PickDesktop(action, systemPicker), fallback, allowSystem ? L.T("إعادة محاولة العدسة الحية") : null);
    }

    void ShowCaptureDecision(string message, Func<Task> retry, Button? alternate = null, string? retryLabel = null)
    {
        ShowError(message, retry, alternate, retryLabel);
        captureDecisionPending = true;
        if (!picking) FocusCaptureDecision();
    }

    bool FocusCaptureDecision()
    {
        if (!captureDecisionPending || closed) return false;
        captureDecisionPending = false;
        if (OverlayOpen || nativeDialogOpen) { BringForward(); return true; }
        RaiseWorkbench(WindowState);
        FocusErrorAction();
        return true;
    }

    bool FocusErrorAction()
    {
        if (!errorBanner.IsVisible) return false;
        Dispatcher.UIThread.Post(() => errorBanner.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)?.Focus(), DispatcherPriority.Loaded);
        return true;
    }

    async Task CaptureOptions(bool collect = false)
    {
        if (OverlayOpen || nativeDialogOpen || picking) return;
        BringForward();
        var dialog = Dialog(L.T("وضع الالتقاط"));
        var system = Btn(L.T("استخدام منتقي النظام"), () => dialog.Close("system"));
        var live = Btn(L.T(collect ? "استخدام العدسة الحية" : "بدء مشاركة الشاشة"), () => dialog.Close("live"), collect);
        system.IsEnabled = !damaged;
        live.IsEnabled = !damaged && !WaylandCapture.IsSharing;
        var panel = Col(Text(L.T("وضع الالتقاط"), Typography.Heading, true), Label(CaptureModeLabel()),
            Label(collect
                ? L.T("يتطلب الالتقاط المتعدد العدسة الحية ومشاركة الشاشة.")
                : L.T("يستقبل منتقي النظام لوناً واحداً. تبقى مشاركة الشاشة للعدسة الحية نشطة حتى توقفها.")),
            system, live);
        if (WaylandCapture.IsSharing)
            panel.Children.Add(Btn(L.T("إنهاء مشاركة الشاشة"), () => dialog.Close("stop")));
        panel.Children.Add(Choices(Btn(L.T("إغلاق"), () => dialog.Close())));
        dialog.Content = panel;
        switch (await dialog.Show<string>())
        {
            case "system": SetCaptureMode(false); break;
            case "live":
                if (collect)
                {
                    SetCaptureMode(true);
                    await PickDesktop(PickAction.Collect);
                }
                else await EnableLiveSharing();
                break;
            case "stop": StopSharing(); break;
        }
    }
}
