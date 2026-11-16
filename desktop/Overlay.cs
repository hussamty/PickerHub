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
using Avalonia.VisualTree;

namespace PickerHub;

// Everything that opens over the page lives in one layer inside the window: sheets (settings,
// prompts, confirmations) and the lists that drop from select buttons. Being in the window lets
// them be real glass over the page, open with the same liquid motion as the rest, and follow the theme.
internal sealed partial class MainWindow
{
    bool OverlayOpen => overlay.Children.Any(c => c.IsVisible && c.IsHitTestVisible);
    readonly Panel overlay = new() { FlowDirection = FlowDirection.LeftToRight };

    // Grow a pane out of a point: it starts small and clear, then springs to size.
    void Emerge(Control pane, string from, RelativePoint origin, int ms = 240)
    {
        if (!Motion) return;
        pane.RenderTransformOrigin = origin; pane.Opacity = 0; pane.RenderTransform = TransformOperations.Parse(from);
        Dispatcher.UIThread.Post(() =>
        {
            pane.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new BackEaseOut() }];
            pane.Opacity = 1; pane.RenderTransform = TransformOperations.Parse("scale(1,1)");
        }, DispatcherPriority.Render);
    }
    void Dismiss(Control layer, Control pane)
    {
        layer.IsHitTestVisible = false;
        if (!Motion) { overlay.Children.Remove(layer); return; }
        layer.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) }];
        layer.Opacity = 0; pane.RenderTransform = TransformOperations.Parse("scale(0.97,0.97)");
        DispatcherTimer.RunOnce(() => overlay.Children.Remove(layer), TimeSpan.FromMilliseconds(130));
    }

    // A modal pane of glass over a dimmed page. Esc or a click on the dimmed page closes it.
    internal sealed class Modal(MainWindow window, string title)
    {
        readonly TaskCompletionSource<object?> done = new();
        Panel? layer; Glass? pane; IInputElement? before;
        public double Width { get; set; } = 400;
        public Control? Content { get; set; }
        public event Action? Opened;
        public void Close(object? result = null)
        {
            if (layer == null || pane == null || done.Task.IsCompleted) return;
            window.SizeChanged -= FitPane;
            window.Dismiss(layer, pane); before?.Focus(); done.TrySetResult(result);
        }
        void FitPane(object? sender = null, SizeChangedEventArgs? args = null)
        {
            if (pane == null) return;
            pane.Width = Math.Min(Width, Math.Max(240, window.overlay.Bounds.Width - 32));
            pane.MaxHeight = Math.Max(160, window.overlay.Bounds.Height - 32);
        }
        public async Task<T?> Show<T>()
        {
            before = window.FocusManager?.GetFocusedElement();
            var dim = new Border(); dim[!Border.BackgroundProperty] = new DynamicResourceExtension("Scrim");
            dim.PointerPressed += (_, e) => { e.Handled = true; Close(); };
            pane = new Glass("SheetTint")
            {
                Focusable = true, Radius = 26, Blur = 26, Padding = new Thickness(16, 18, 16, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Width = Math.Min(Width, Math.Max(280, window.Bounds.Width - 32)), MaxHeight = Math.Max(240, window.Bounds.Height - 48), FlowDirection = L.Flow,
                Child = new ScrollViewer { Content = new Border { Padding = new Thickness(8, 6, 8, 24), Child = Content }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
            };
            AutomationProperties.SetName(pane, title);
            layer = new Panel(); layer.Children.Add(dim); layer.Children.Add(pane);
            KeyboardNavigation.SetTabNavigation(layer, KeyboardNavigationMode.Cycle);
            layer.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            window.overlay.Children.Add(layer); FitPane(); window.SizeChanged += FitPane;
            if (window.Motion) { dim.Opacity = 0; dim.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }]; Dispatcher.UIThread.Post(() => dim.Opacity = 1, DispatcherPriority.Render); }
            window.Emerge(pane, "translateY(14px) scale(0.94,0.94)", new RelativePoint(.5, .5, RelativeUnit.Relative), 280);
            Dispatcher.UIThread.Post(() =>
            {
                Opened?.Invoke();
                if (window.FocusManager?.GetFocusedElement() is not Visual focused || !pane.IsVisualAncestorOf(focused))
                {
                    var first = pane.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
                    // Do not scroll a long sheet to its bottom just to focus its only button.
                    if (first?.TranslatePoint(new Point(), pane) is Point pos && pos.Y >= 0 && pos.Y + first.Bounds.Height <= pane.Bounds.Height) first.Focus();
                    else pane.Focus();
                }
            }, DispatcherPriority.Loaded);
            return await done.Task is T value ? value : default;
        }
        public Task Show() => Show<object>();
    }
    Modal Dialog(string title) => new(this, title);

    // A select button. Its list is a pane of glass that grows out of the button and flips above it when there is no room below.
    Button Choose(IEnumerable<string> options, int index, Action<int> changed)
    {
        var items = options.Select(L.T).ToArray(); int selected = Math.Clamp(index, 0, Math.Max(0, items.Length - 1));
        var label = new TextBlock { Text = items.Length > 0 ? items[selected] : "", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        var restingChevron = TransformOperations.Parse(L.Language == "ar" ? "rotate(-90deg)" : "rotate(90deg)");
        var openChevron = TransformOperations.Parse(L.Language == "ar" ? "rotate(90deg)" : "rotate(-90deg)");
        var chevron = new Icon("chevron-right", 16) { RenderTransform = restingChevron };
        if (Motion) chevron.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new BackEaseOut() }];
        var face = new DockPanel(); DockPanel.SetDock(chevron, Dock.Right); face.Children.Add(chevron); face.Children.Add(label);
        var button = Of(new Button { Content = face, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center }, "ph", "select");
        button.Click += (_, _) =>
        {
            chevron.RenderTransform = openChevron;
            Drop(button, items, selected, i => { selected = i; label.Text = items[i]; changed(i); }, () => chevron.RenderTransform = restingChevron);
        };
        return button;
    }
    // Where a control sits in the overlay. Walks the tree by hand because a right-to-left subtree is
    // mirrored about its own centre wherever the direction flips, which point translation does not follow.
    Rect Locate(Control anchor)
    {
        double x0 = 0, x1 = anchor.Bounds.Width, y = 0;
        for (Visual? v = anchor; v != null && v != overlay.GetVisualParent(); v = v.GetVisualParent())
        {
            if (v.GetVisualParent() is { } parent && v.FlowDirection != parent.FlowDirection) (x0, x1) = (v.Bounds.Width - x1, v.Bounds.Width - x0);
            x0 += v.Bounds.X; x1 += v.Bounds.X; y += v.Bounds.Y;
        }
        // That is the position in the overlay's parent; the overlay is itself mirrored inside it.
        if (overlay.GetVisualParent() is { } room && overlay.FlowDirection != room.FlowDirection) (x0, x1) = (overlay.Bounds.Width - x1, overlay.Bounds.Width - x0);
        return new Rect(x0 - overlay.Bounds.X, y - overlay.Bounds.Y, x1 - x0, anchor.Bounds.Height);
    }
    void Drop(Control anchor, string[] items, int selected, Action<int> pick, Action closed)
    {
        var layer = new Panel();
        var catcher = new Border { Background = Brushes.Transparent };
        var list = new StackPanel { Spacing = 2 };
        var pane = new Glass("SheetTint")
        {
            Radius = 20, Blur = 22, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, FlowDirection = L.Flow,
            Child = new ScrollViewer { Content = list, MaxHeight = 304, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
        };
        void Close(bool refocus)
        {
            if (!layer.IsHitTestVisible) return;
            SizeChanged -= Reposition;
            Dismiss(layer, pane); closed(); if (refocus) anchor.Focus();
        }
        var rows = new List<Button>();
        for (int i = 0; i < items.Length; i++)
        {
            int at = i;
            var text = new TextBlock { Text = items[i], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            var mark = Tone(new Icon("check", 16), "AccentText"); mark.Opacity = i == selected ? 1 : 0;
            var line = new DockPanel(); DockPanel.SetDock(mark, Dock.Right); line.Children.Add(mark); line.Children.Add(text);
            var row = Of(new Button { Content = line }, "ph", "row", "option"); if (i == selected) row.Classes.Add("chosen");
            row.Click += (_, _) => { Close(true); if (at != selected) _ = Guard(() => { pick(at); return Task.CompletedTask; }); };
            rows.Add(row); list.Children.Add(row);
        }
        catcher.PointerPressed += (_, e) => { e.Handled = true; Close(false); };
        layer.KeyDown += (_, e) =>
        {
            int now = rows.FindIndex(r => r.IsFocused);
            switch (e.Key)
            {
                case Key.Escape: Close(true); break;
                case Key.Down: rows[Math.Min(rows.Count - 1, now + 1)].Focus(NavigationMethod.Directional); break;
                case Key.Up: rows[Math.Max(0, now - 1)].Focus(NavigationMethod.Directional); break;
                case Key.Tab: Close(true); break;
                default: return;
            }
            e.Handled = true;
        };
        bool above = false;
        void Reposition(object? sender = null, SizeChangedEventArgs? args = null)
        {
            var spot = Locate(anchor);
            double width = Math.Min(Math.Max(spot.Width, 180), Math.Max(160, overlay.Bounds.Width - 16));
            double height = Math.Min(304, items.Length * 42) + 12;
            double belowRoom = Math.Max(40, overlay.Bounds.Height - spot.Bottom - 14), aboveRoom = Math.Max(40, spot.Top - 14);
            above = height > belowRoom && aboveRoom > belowRoom;
            height = Math.Min(height, above ? aboveRoom : belowRoom);
            ((ScrollViewer)pane.Child!).MaxHeight = Math.Max(28, height - 12);
            pane.Width = width;
            pane.Margin = new Thickness(Math.Clamp(spot.X, 8, Math.Max(8, overlay.Bounds.Width - width - 8)),
                Math.Clamp(above ? spot.Top - 6 - height : spot.Bottom + 6, 8, Math.Max(8, overlay.Bounds.Height - height - 8)), 0, 0);
        }
        Reposition(); SizeChanged += Reposition;
        layer.Children.Add(catcher); layer.Children.Add(pane); overlay.Children.Add(layer);
        Emerge(pane, "scale(0.92,0.55)", new RelativePoint(.5, above ? 1 : 0, RelativeUnit.Relative));
        Dispatcher.UIThread.Post(() => { if (rows.Count > 0) rows[Math.Clamp(selected, 0, rows.Count - 1)].Focus(NavigationMethod.Directional); }, DispatcherPriority.Loaded);
    }
}
