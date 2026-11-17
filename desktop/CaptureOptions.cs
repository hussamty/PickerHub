using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace PickerHub;

internal sealed partial class MainWindow
{
    Button captureMode = null!;
    TextBlock captureModeText = null!;
    Icon captureModeIcon = null!;

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
        if (!closed) RefreshCaptureMode();
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

    async Task CaptureOptions(bool collect = false, bool? copy = null)
    {
        if (OverlayOpen || nativeDialogOpen || picking) return;
        var dialog = Dialog(L.T("وضع الالتقاط"));
        var system = Btn(L.T("استخدام منتقي النظام"), () => dialog.Close("system"));
        var live = Btn(L.T("استخدام العدسة الحية"), () => dialog.Close("live"), collect);
        system.IsEnabled = live.IsEnabled = !damaged;
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
                SetCaptureMode(true);
                if (collect) await PickDesktop(copy, collect: true);
                break;
            case "stop": StopSharing(); break;
        }
    }
}
