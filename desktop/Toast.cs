using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using System.Text.RegularExpressions;

namespace PickerHub;

// News arrives as a toast, never in the top bar: a small pane of frosted glass that rises from the bottom
// corner where reading starts (bottom-right in Arabic, bottom-left in English), says one thing and leaves on
// its own. A message that carries a color shows that color beside it. Pointing at the toast holds it;
// clicking it sends it away. Every message still goes through `status`, which screen readers announce.
internal sealed partial class MainWindow
{
    Glass? toast;
    Grid? toastRow;
    readonly Border toastSwatch = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    readonly Border toastSignal = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer toastTimer = new();
    bool toastShown;
    static readonly Regex HexCode = new("#[0-9A-Fa-f]{6}(?![0-9A-Fa-f])");
    static readonly TimeSpan ToastLeaving = TimeSpan.FromMilliseconds(240);

    // Wraps a name or code set inside a sentence, so its own direction cannot reorder the words around it.
    static string Isolate(string text) => "⁨" + text + "⁩";

    Control BuildToast()
    {
        status.FontSize = Typography.Control; status.FontWeight = FontWeight.Medium; status.LineHeight = Typography.Leading(Typography.Control);
        status.TextWrapping = TextWrapping.Wrap; status.MaxLines = 2; status.TextTrimming = TextTrimming.CharacterEllipsis; status.VerticalAlignment = VerticalAlignment.Center;
        Tone(status, "Ink");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        toastSwatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        toastSignal[!Border.BackgroundProperty] = new DynamicResourceExtension("Accent");
        var mark = new Panel { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center }; mark.Children.AddRange([toastSwatch, toastSignal]);
        status.Margin = new Thickness(10, 0, 2, 0);
        toastRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; toastRow.Children.Add(mark); Grid.SetColumn(status, 1); toastRow.Children.Add(status);
        toast = new Glass("SheetTint")
        {
            Child = toastRow, Radius = 18, Blur = 18, Padding = new Thickness(14, 11, 16, 11), MinHeight = 44, MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false, Opacity = 0, Cursor = new Cursor(StandardCursorType.Hand),
            RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative)
        };
        AutomationProperties.SetAutomationId(toast, "toast");
        toast.PointerEntered += (_, _) => toastTimer.Stop();
        toast.PointerExited += (_, _) => { if (toastShown) { toastTimer.Interval = TimeSpan.FromSeconds(1.6); toastTimer.Start(); } };
        toast.PointerPressed += (_, e) => { e.Handled = true; HideToast(); };
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); HideToast(); };
        status.PropertyChanged += (_, change) => { if (change.Property == TextBlock.TextProperty) Announce(); };
        PlaceToast();
        var host = new Panel(); host.Children.Add(toast);
        return host;
    }
    // The toast takes the reading direction of the language, and its corner with it.
    void PlaceToast()
    {
        if (toast == null || toastRow == null) return;
        bool arabic = L.Language == "ar";
        toastRow.FlowDirection = L.Flow;
        toast.HorizontalAlignment = arabic ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        // On the right it stands clear of the navigation rail; on the left, of the window's edge.
        toast.Margin = arabic ? new Thickness(24, 0, Spine.Closed + 18, 22) : new Thickness(24, 0, 24, 22);
    }
    void Announce()
    {
        string text = status.Text ?? "";
        if (toast == null || closed || text.Length == 0 || text == Idle) return;
        var hex = HexCode.Match(text);
        toastSwatch.IsVisible = hex.Success; toastSignal.IsVisible = !hex.Success;
        if (hex.Success) toastSwatch.Background = Paint(hex.Value);
        ShowToast(text.Length);
    }
    static Transitions Moving(bool arriving) =>
    [
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(arriving ? 180 : 200), Easing = arriving ? new CubicEaseOut() : new CubicEaseIn() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(arriving ? 360 : 220), Easing = arriving ? new BackEaseOut() : new CubicEaseIn() }
    ];
    void ShowToast(int length)
    {
        var pane = toast!;
        toastTimer.Stop();
        // Long messages stay a little longer: about three seconds, up to six.
        toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(2.6 + length / 30.0, 3, 6));
        if (!toastShown)
        {
            toastShown = true; pane.IsVisible = true;
            if (Motion)
            {
                pane.Transitions = null; pane.Opacity = 0; pane.RenderTransform = TransformOperations.Parse("translateY(18px) scale(0.96,0.96)");
                Dispatcher.UIThread.Post(() =>
                {
                    if (!toastShown) return;
                    pane.Transitions = Moving(true); pane.Opacity = 1; pane.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1,1)");
                }, DispatcherPriority.Render);
            }
            else { pane.Transitions = null; pane.Opacity = 1; pane.RenderTransform = null; }
        }
        else if (Motion)
        {
            // Fresh news while it is showing: the same pane takes it with a small nod.
            pane.Transitions = Moving(true); pane.Opacity = 1; pane.RenderTransform = TransformOperations.Parse("translateY(-3px) scale(1.02,1.02)");
            DispatcherTimer.RunOnce(() => { if (toastShown) pane.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1,1)"); }, TimeSpan.FromMilliseconds(130));
        }
        toastTimer.Start();
    }
    void HideToast()
    {
        if (!toastShown || toast is not { } pane) return;
        toastShown = false; toastTimer.Stop();
        if (Motion) { pane.Transitions = Moving(false); pane.Opacity = 0; pane.RenderTransform = TransformOperations.Parse("translateY(10px) scale(0.98,0.98)"); }
        else { pane.Transitions = null; pane.Opacity = 0; }
        DispatcherTimer.RunOnce(() =>
        {
            if (toastShown) return;
            pane.IsVisible = false;
            // Rest the line, so the same message can be announced again.
            status.Text = Idle;
        }, Motion ? ToastLeaving : TimeSpan.FromMilliseconds(1));
    }
}
