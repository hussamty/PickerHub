using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace PickerHub;

internal sealed partial class MainWindow
{
    Button? minimizeWindow, maximizeWindow, closeWindow;
    bool? lastMaximized;
    readonly Dictionary<WindowEdge, Cursor> resizeCursors = [];
    bool CustomChrome => !OperatingSystem.IsMacOS();

    Control BuildTitlebar(Control identity)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 38, Background = Brushes.Transparent, FlowDirection = FlowDirection.LeftToRight };
        identity.Margin = new Thickness(10, 0); identity.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(identity);
        if (CustomChrome)
        {
            SystemDecorations = SystemDecorations.None;
            ExtendClientAreaToDecorationsHint = true; ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
            Button WindowAction(string icon, Action act, string id)
            {
                var button = Of(Btn("", act), "window-control"); button.Content = new Icon(icon, 16);
                AutomationProperties.SetAutomationId(button, id); return button;
            }
            minimizeWindow = WindowAction("minus", () => WindowState = WindowState.Minimized, "window-minimize");
            maximizeWindow = WindowAction("square", ToggleMaximize, "window-maximize");
            closeWindow = WindowAction("x", Close, "window-close"); closeWindow.Classes.Add("close");
            var buttons = Line(6, minimizeWindow, maximizeWindow, closeWindow); buttons.VerticalAlignment = VerticalAlignment.Center;
            buttons.Margin = new Thickness(8, 0); Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                if (e.Source is Visual hit && (hit is Button || hit.GetVisualAncestors().Any(v => v is Button))) return;
                if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
                e.Handled = true;
            };
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ResizeEdge(e.GetPosition(this)) is { } edge)
                { e.Handled = true; BeginResizeDrag(edge, e); }
            }, RoutingStrategies.Tunnel);
            PointerMoved += (_, e) =>
            {
                if (ResizeEdge(e.GetPosition(this)) is not { } edge) { Cursor = Cursor.Default; return; }
                if (!resizeCursors.TryGetValue(edge, out var cursor))
                {
                    var type = edge switch
                    {
                        WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
                        WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
                        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
                        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
                        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
                        _ => StandardCursorType.BottomRightCorner
                    };
                    resizeCursors[edge] = cursor = new Cursor(type);
                }
                Cursor = cursor;
            };
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateWindowChrome(); };
            Closed += (_, _) => { foreach (var cursor in resizeCursors.Values) cursor.Dispose(); resizeCursors.Clear(); };
        }
        return new Glass("GlassTint") { Child = row, Margin = new Thickness(8, 8, 8, 2), Radius = 18, Shade = Colors.Transparent };
    }

    WindowEdge? ResizeEdge(Point p)
    {
        if (!CustomChrome || !CanResize || WindowState != WindowState.Normal) return null;
        const double grip = 6;
        bool left = p.X < grip, right = p.X >= Bounds.Width - grip, top = p.Y < grip, bottom = p.Y >= Bounds.Height - grip;
        if (top) return left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North;
        if (bottom) return left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South;
        return left ? WindowEdge.West : right ? WindowEdge.East : null;
    }
    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void UpdateWindowChrome()
    {
        if (maximizeWindow == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        if (lastMaximized != maximized) { maximizeWindow.Content = new Icon(maximized ? "copy" : "square", 16); lastMaximized = maximized; }
        foreach (var (button, label) in new[] { (minimizeWindow!, L.T("تصغير النافذة")), (maximizeWindow, L.T(maximized ? "استعادة حجم النافذة" : "تكبير النافذة")), (closeWindow!, L.T("إغلاق التطبيق")) })
        { AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); }
    }
}
