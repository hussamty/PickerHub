using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Xml.Linq;

namespace PickerHub;

// A Tabler outline icon drawn in the inherited text color, so it follows its button's state.
internal sealed class Icon : Control
{
    static readonly Dictionary<string, Geometry[]> cache = [];
    readonly Geometry[] paths;
    static Icon() => AffectsRender<Icon>(TextElement.ForegroundProperty);
    public Icon(string name, double size = 19)
    {
        Width = Height = size; FlowDirection = FlowDirection.LeftToRight; VerticalAlignment = VerticalAlignment.Center;
        if (!cache.TryGetValue(name, out var found))
        {
            using var stream = AssetLoader.Open(new Uri("avares://PickerHub/Icons/" + name + ".svg"));
            found = XDocument.Load(stream).Descendants().Where(n => n.Name.LocalName == "path" && n.Attribute("stroke")?.Value != "none")
                .Select(n => n.Attribute("d")?.Value).OfType<string>().Select(Geometry.Parse).ToArray();
            cache[name] = found;
        }
        paths = found;
    }
    public override void Render(DrawingContext ctx)
    {
        var pen = new Pen(TextElement.GetForeground(this), 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (ctx.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
            foreach (var path in paths) ctx.DrawGeometry(null, pen, path);
    }
}

// Edge navigation: one pane of glass grown out of the window's right edge. At rest it is a slim
// rail of icons; under the pointer or keyboard focus it opens over the page and shows labels, and
// the grip pins it open until a click elsewhere or Esc. One pill follows the pointer from row to
// row and squashes on press; a second, filled pill marks the current tool and slides when it changes.
internal sealed class Spine : Glass
{
    public const double Closed = 58, Open = 236;
    const double Lip = 18;
    readonly StackPanel items = new() { Spacing = 3, FlowDirection = L.Flow, ClipToBounds = true };
    readonly Border hover = new() { Classes = { "pill", "hover" }, Opacity = 0 }, current = new() { Classes = { "pill", "active" }, Opacity = 0 };
    readonly List<Button> tools = [];
    readonly List<TextBlock> labels = [];
    bool keyboard, pinned, motion = true, motionInitialized, expanded;
    int active = -1;
    public Spine()
    {
        Radius = 26; Blur = 22; Shoulder = Lip; Width = Closed; Padding = new Thickness(7, Lip + 7, 7, Lip + 7);
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Center;
        var layers = new Panel(); layers.Children.AddRange([current, hover, items]); Child = layers;
        Add("dots", L.T("القائمة"), () => { pinned = !pinned; Expand(pinned || IsPointerOver); }, false);
        Divide();
        PointerEntered += (_, _) => Expand(true);
        PointerExited += (_, _) => { hover.Opacity = 0; if (!keyboard && !pinned) Expand(false); };
        AddHandler(PointerPressedEvent, (_, _) => hover.RenderTransform = TransformOperations.Parse("scale(0.97,0.86)"), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => hover.RenderTransform = TransformOperations.Parse("scale(1,1)"), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            // Showing a hidden window restores focus with Unspecified navigation.
            // Only intentional keyboard navigation should expand the rail.
            if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional)) return;
            keyboard = true; Expand(true); if (e.Source is Button row) Follow(row);
        }, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => { keyboard = false; if (!IsPointerOver && !pinned) { hover.Opacity = 0; Expand(false); } }, handledEventsToo: true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && pinned) { e.Handled = true; Release(); } };
        items.LayoutUpdated += (_, _) => Mark();
    }
    public void Fit(double availableHeight)
    {
        double height = availableHeight < 540 ? 36 : 44;
        foreach (var row in items.Children.OfType<Button>()) { row.Height = height; row.MinHeight = height; }
        current.Height = hover.Height = height;
    }
    public bool Motion
    {
        set
        {
            if (motionInitialized && motion == value) return;
            motionInitialized = true; motion = value;
            for (int i = 0; i < labels.Count; i++) labels[i].Transitions = Fade(i);
            foreach (var pill in new[] { hover, current }) pill.Transitions = value ? Travel() : null;
        }
    }
    Transitions? Fade(int row) => motion ? [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(130), Delay = TimeSpan.FromMilliseconds(row * 8) }] : null;
    static Transitions Travel() =>
    [
        new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(230), Easing = new BackEaseOut() },
        new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) },
        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new BackEaseOut() }
    ];
    // Opening overshoots like a drop let go; closing settles without a bounce so the icons are never clipped.
    void Expand(bool open)
    {
        if (expanded == open) return;
        expanded = open;
        Transitions = motion ? [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(open ? 280 : 190), Easing = open ? new BackEaseOut() : new CubicEaseOut() }] : null;
        Width = open ? Open : Closed; foreach (var label in labels) label.Opacity = open ? 1 : 0;
    }
    // Unpin after a click outside the spine.
    public void Release() { pinned = false; if (!IsPointerOver && !keyboard) Expand(false); }
    public void Collapse() { pinned = keyboard = false; hover.Opacity = 0; Expand(false); }
    // A pill that is not showing appears in place; one that is showing travels.
    void Slide(Border pill, double y)
    {
        if (pill.Opacity == 0) { var travel = pill.Transitions; pill.Transitions = null; pill.Margin = new Thickness(0, y, 0, 0); pill.Transitions = travel; }
        else pill.Margin = new Thickness(0, y, 0, 0);
        pill.Opacity = 1;
    }
    void Follow(Button row) { if (row.Bounds.Height > 0) Slide(hover, row.Bounds.Y); }
    void Mark() { if (active >= 0 && active < tools.Count && tools[active].Bounds.Height > 0 && current.Margin.Top != tools[active].Bounds.Y) Slide(current, tools[active].Bounds.Y); }
    public Button Add(string symbol, string label, Action act, bool tool = true)
    {
        var button = new Button(); button.Classes.Add("ph"); button.Classes.Add("nav");
        Label(button, symbol, label); button.Click += (_, _) => act();
        button.PointerEntered += (_, _) => Follow(button);
        if (tool) tools.Add(button);
        items.Children.Add(button); return button;
    }
    public void Relabel((string Symbol, string Title)[] titles)
    {
        items.FlowDirection = L.Flow;
        var rows = items.Children.OfType<Button>().ToArray();
        Label(rows[0], "dots", L.T("القائمة"));
        for (int i = 0; i < tools.Count; i++) Label(tools[i], titles[i].Symbol, titles[i].Title);
        Label(rows[^1], "settings", L.T("الإعدادات"));
    }
    public void Divide() => items.Children.Add(new Border { Classes = { "hairline" }, Margin = new Thickness(12, 5) });
    public void Label(Button button, string symbol, string label)
    {
        var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Opacity = Width > Closed ? 1 : 0, Margin = new Thickness(2, 0, 14, 0) };
        if (button.Content is Grid old) labels.Remove(old.Children.OfType<TextBlock>().First());
        labels.Add(text); text.Transitions = Fade(labels.Count - 1);
        var icon = new Icon(symbol, 20) { HorizontalAlignment = HorizontalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*") };
        grid.Children.Add(icon); Grid.SetColumn(text, 1); grid.Children.Add(text);
        button.Content = grid; AutomationProperties.SetName(button, label);
    }
    public void Activate(int index) { active = index; for (int i = 0; i < tools.Count; i++) tools[i].Classes.Set("active", i == index); Mark(); }
}

// A fanned stack of cards. The top card flips to its reverse; stepping deals the next one forward.
internal sealed class Deck : Panel
{
    public const double CardWidth = 264, CardHeight = 372;
    const int Depth = 4;
    readonly Func<int, bool, Control> face;
    readonly int count;
    readonly bool motion;
    readonly List<(Border Card, int Depth)> pile = [];
    int index;
    double spread = 1;
    bool flipped, busy;
    Border? top;
    public event Action<int>? Changed;
    public Deck(int count, int index, bool motion, Func<int, bool, Control> face)
    {
        this.count = count; this.index = index; this.motion = motion; this.face = face;
        Width = CardWidth + 150; Height = CardHeight + 40; FlowDirection = FlowDirection.LeftToRight;
        // The pile opens a little under the pointer, so the cards beneath can be read through the glass.
        PointerEntered += (_, _) => Fan(1.35); PointerExited += (_, _) => Fan(1);
        Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, L.T("بطاقات الألوان"));
        KeyDown += (_, e) =>
        {
            switch (e.Key) { case Key.Left: Next(); break; case Key.Right: Previous(); break; case Key.Space: case Key.Enter: Flip(); break; default: return; }
            e.Handled = true;
        };
        Deal(0);
    }
    // The pile leans away from the reading edge: right-to-left pages fan to the left.
    TransformOperations Pose(double depth, double flip = 1) => TransformOperations.Parse(
        FormattableString.Invariant($"translate({-depth * 30 * spread}px,{depth * 3}px) rotate({-depth * 5 * spread}deg) scale({(1 - depth * .03) * flip},{1 - depth * .03})"));
    static readonly TransformOperations Away = TransformOperations.Parse("translate(150px,10px) rotate(9deg) scale(0.97,0.97)");
    Border Card(int item, bool back, TransformOperations start, double opacity = 1) => new()
    {
        Width = CardWidth, Height = CardHeight, Child = face(item, back),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = opacity,
        RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative), RenderTransform = start,
        Transitions = motion ? [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }] : null
    };
    void Fan(double amount) { spread = amount; if (busy) return; foreach (var (card, depth) in pile) card.RenderTransform = Pose(depth); }
    // step: +1 after moving forward, -1 after moving back, 0 for a plain layout.
    void Deal(int step, int previous = -1)
    {
        Children.Clear(); pile.Clear();
        int shown = Math.Min(Depth, count);
        var settle = new List<Action>();
        for (int depth = shown - 1; depth >= 0; depth--)
        {
            int d = depth;
            bool entering = step < 0 && d == 0;
            var card = Card((index + d) % count, false, !motion || step == 0 ? Pose(d) : entering ? Away : Pose(d + step), entering && motion ? 0 : 1);
            if (d == 0) { top = card; card.PointerPressed += (_, e) => { e.Handled = true; Focus(); Flip(); }; }
            Children.Add(card); pile.Add((card, d)); settle.Add(() => { card.RenderTransform = Pose(d); card.Opacity = 1; });
        }
        if (motion && step > 0 && previous >= 0)
        {
            var leaving = Card(previous, false, Pose(0)); leaving.IsHitTestVisible = false; Children.Add(leaving);
            settle.Add(() => { leaving.RenderTransform = Away; leaving.Opacity = 0; });
            DispatcherTimer.RunOnce(() => Children.Remove(leaving), TimeSpan.FromMilliseconds(320));
        }
        if (motion && step != 0) Dispatcher.UIThread.Post(() => { foreach (var move in settle) move(); }, DispatcherPriority.Background);
    }
    void Go(int step)
    {
        if (count < 2 || busy) return;
        int previous = index; index = (index + step + count) % count; flipped = false;
        Deal(step, previous); Changed?.Invoke(index);
    }
    public void Next() => Go(1);
    public void Previous() => Go(-1);
    public void Show(int item) { if (item == index || busy) return; int previous = index; flipped = false; index = item; Deal(1, previous); Changed?.Invoke(index); }
    public async void Flip()
    {
        if (busy || top == null) return;
        var card = top; flipped = !flipped;
        if (!motion) { card.Child = face(index, flipped); return; }
        busy = true;
        try
        {
            Transitions Turn(Easing easing) => [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = easing }];
            card.Transitions = Turn(new CubicEaseIn()); card.RenderTransform = Pose(0, 0.02); await Task.Delay(125);
            card.Child = face(index, flipped);
            card.Transitions = Turn(new CubicEaseOut()); card.RenderTransform = Pose(0); await Task.Delay(125);
        }
        finally { busy = false; }
    }
}
