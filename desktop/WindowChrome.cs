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
    Glass? titlebarSurface;
    readonly Dictionary<WindowEdge, Cursor> resizeCursors = [];
    bool CustomChrome => !OperatingSystem.IsMacOS();

    // The bar is the titlebar: its empty space moves the window, and the window actions close the row.
    Control BuildTitlebar(Grid row, int column)
    {
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
            var divider = new Border { Width = 1, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            divider[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line");
            var buttons = Line(2, divider, minimizeWindow, maximizeWindow, closeWindow); buttons.VerticalAlignment = VerticalAlignment.Center;
            buttons.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(buttons, column); row.Children.Add(buttons);
            PointerPressedEventArgs? pendingMove = null;
            Point moveOrigin = default;
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                if (e.Source is Visual hit && (hit is Button || hit.GetVisualAncestors().Any(v => v is Button))) return;
                pendingMove = null;
                if (e.ClickCount == 2) ToggleMaximize();
                else { pendingMove = e; moveOrigin = e.GetPosition(this); }
                e.Handled = true;
            };
            // Start a native move only after movement. Starting it on the first
            // press lets some window managers consume the subsequent double-click.
            PointerMoved += (_, e) =>
            {
                if (pendingMove is not { } press) return;
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { pendingMove = null; return; }
                var delta = e.GetPosition(this) - moveOrigin;
                if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
                pendingMove = null; BeginMoveDrag(press);
            };
            PointerReleased += (_, _) => pendingMove = null;
            Deactivated += (_, _) => pendingMove = null;
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
        else row.ColumnDefinitions[column].Width = new GridLength(14);
        return titlebarSurface = new Glass("GlassTint") { Child = row, Margin = new Thickness(12, 8, 12, 8), Radius = 20, Shade = Colors.Transparent };
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
    void ToggleMaximize() => WindowState = WindowState is WindowState.Maximized or WindowState.FullScreen ? WindowState.Normal : WindowState.Maximized;
    // The middle action always answers the current size: maximize from a normal window, restore from a
    // maximized one. Minimize keeps its own slot and only ever minimizes.
    void UpdateWindowChrome()
    {
        if (maximizeWindow == null) return;
        bool maximized = WindowState is WindowState.Maximized or WindowState.FullScreen;
        maximizeWindow.IsVisible = CanResize;
        string glyph = maximized ? "restore" : "square";
        if (maximizeWindow.Tag as string != glyph) { maximizeWindow.Tag = glyph; maximizeWindow.Content = new Icon(glyph, 16); }
        Cursor = Cursor.Default;
        foreach (var (button, label) in new[] { (minimizeWindow!, L.T("تصغير النافذة")), (maximizeWindow, maximized ? L.T("استعادة حجم النافذة") : L.T("تكبير النافذة")), (closeWindow!, L.T("إغلاق التطبيق")) })
        { AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); }
    }
}
