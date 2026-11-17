using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace PickerHub;

// A monitor-sized input surface is needed on Wayland: the compositor only
// delivers pointer and keyboard events to a client which owns a visible surface.
internal sealed class PortalInput : IDisposable
{
    readonly Dictionary<PixelPoint, Window> windows = [];
    readonly MagnifierView view;
    Canvas? lensParent;
    public PixelRect? LensBounds { get; private set; }
    readonly DispatcherTimer focusTimer;
    Window? keyboardWindow;
    public PortalInput(Window owner, MagnifierView view, Action<PixelPoint> move, Action<PixelPoint> pick, Action cancel,
        Action<int> zoom, Action<int, int> nudge, Action accept)
    {
        this.view = view;
        focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        focusTimer.Tick += (_, _) =>
        {
            if (keyboardWindow is { } window && windows.Values.All(w => !w.IsActive))
                Focus(window);
        };
        try
        {
            foreach (var screen in owner.Screens.All)
            {
                var surface = new Canvas { Background = Brushes.Transparent, Focusable = true };
                var w = new Window
                {
                    Title = "PickerHub Magnifier", SystemDecorations = SystemDecorations.None,
                    WindowStartupLocation = WindowStartupLocation.Manual, Position = screen.Bounds.Position,
                    Width = screen.Bounds.Width / screen.Scaling, Height = screen.Bounds.Height / screen.Scaling,
                    CanResize = false, ShowInTaskbar = false, Topmost = true,
                    Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    Cursor = new Cursor(StandardCursorType.Cross), Content = surface
                };
                w.PointerMoved += (_, e) =>
                {
                    keyboardWindow = w;
                    move(w.PointToScreen(e.GetPosition(w)));
                };
                w.PointerPressed += (_, e) =>
                {
                    var state = e.GetCurrentPoint(w).Properties;
                    if (state.IsRightButtonPressed) cancel();
                    else if (state.IsLeftButtonPressed) pick(w.PointToScreen(e.GetPosition(w)));
                    e.Handled = true;
                };
                w.PointerWheelChanged += (_, e) => { zoom(Math.Sign(e.Delta.Y)); e.Handled = true; };
                w.AddHandler(InputElement.KeyDownEvent, (_, e) =>
                {
                    switch (e.Key)
                    {
                        case Key.Escape: cancel(); break;
                        case Key.Enter: accept(); break;
                        case Key.Left: nudge(-1, 0); break;
                        case Key.Right: nudge(1, 0); break;
                        case Key.Up: nudge(0, -1); break;
                        case Key.Down: nudge(0, 1); break;
                        default: return;
                    }
                    e.Handled = true;
                }, RoutingStrategies.Tunnel, handledEventsToo: true);
                w.Activated += (_, _) => surface.Focus();
                w.Closed += (_, _) => cancel();
                windows.Add(screen.Bounds.Position, w);
                w.Show();
                w.Position = screen.Bounds.Position;
                keyboardWindow = w;
            }
            if (keyboardWindow != null) Focus(keyboardWindow);
            focusTimer.Start();
        }
        catch { Dispose(); throw; }
    }
    public void HideLens()
    {
        view.IsVisible = false;
        LensBounds = null;
    }
    public void ShowLens(PixelRect bounds, PixelPoint screenOrigin)
    {
        var w = windows[screenOrigin];
        var canvas = (Canvas)w.Content!;
        if (canvas != lensParent)
        {
            lensParent?.Children.Remove(view);
            canvas.Children.Add(view);
            lensParent = canvas;
        }
        var at = w.PointToClient(bounds.Position);
        Canvas.SetLeft(view, at.X);
        Canvas.SetTop(view, at.Y);
        view.IsVisible = true;
        LensBounds = bounds;
    }
    static void Focus(Window window)
    {
        window.Activate();
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor == "XID") NativePicker.pickerhub_focus_window((nuint)handle.Handle);
        if (window.Content is Canvas surface) surface.Focus();
    }
    public void Dispose()
    {
        focusTimer.Stop();
        keyboardWindow = null;
        foreach (var w in windows.Values) w.Close();
        windows.Clear();
    }
}

internal static class NativePicker
{
    [DllImport("picker_capture")] internal static extern void pickerhub_clipboard_prepare();
    [DllImport("picker_capture")] internal static extern void pickerhub_focus_window(nuint window);
    [DllImport("picker_capture")] internal static extern nuint pickerhub_active_window();
    [DllImport("picker_capture")] internal static extern nint pickerhub_input_open();
    [DllImport("picker_capture")] internal static extern void pickerhub_input_close(nint input);
    [DllImport("picker_capture")] internal static extern int pickerhub_input_poll(nint input, out int x, out int y);
    [DllImport("picker_capture")] internal static extern int pickerhub_pointer(out int x, out int y);
    [DllImport("picker_capture")] internal static extern int pickerhub_sample(int x, int y, int radius, [Out] byte[] rgba);
}
