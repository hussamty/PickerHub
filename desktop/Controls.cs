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

// Edge navigation. At rest it is a slim capsule of icons; under the pointer or keyboard
// focus it widens over the page to show labels. Every tool stays one click away.
internal sealed class Spine : Border
{
    public const double Closed = 60, Open = 224;
    readonly StackPanel items = new() { Spacing = 3, FlowDirection = FlowDirection.RightToLeft };
    readonly List<Button> tools = [];
    readonly List<TextBlock> labels = [];
    bool keyboard;
    protected override Type StyleKeyOverride => typeof(Border);
    public Spine()
    {
        Classes.Add("spine"); Width = Closed; ClipToBounds = true; Child = items;
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Center;
        PointerEntered += (_, _) => Expand(true);
        PointerExited += (_, _) => { if (!keyboard) Expand(false); };
        AddHandler(GotFocusEvent, (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer) { keyboard = true; Expand(true); } }, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => { keyboard = false; if (!IsPointerOver) Expand(false); }, handledEventsToo: true);
    }
    public bool Motion
    {
        set
        {
            Transitions = value ? [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseOut() }] : null;
            foreach (var label in labels) label.Transitions = value ? [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }] : null;
        }
    }
    void Expand(bool open) { Width = open ? Open : Closed; foreach (var label in labels) label.Opacity = open ? 1 : 0; }
    public Button Add(string symbol, string label, Action act, bool tool = true)
    {
        var button = new Button(); button.Classes.Add("ph"); button.Classes.Add("nav");
        Label(button, symbol, label); button.Click += (_, _) => act();
        if (tool) tools.Add(button);
        items.Children.Add(button); return button;
    }
    public void Divide() => items.Children.Add(new Border { Classes = { "hairline" }, Margin = new Thickness(12, 5) });
    public void Label(Button button, string symbol, string label)
    {
        var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Opacity = Width > Closed ? 1 : 0, Margin = new Thickness(2, 0, 14, 0) };
        if (button.Content is Grid old) labels.Remove(old.Children.OfType<TextBlock>().First());
        labels.Add(text);
        var icon = new Icon(symbol, 20) { HorizontalAlignment = HorizontalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*") };
        grid.Children.Add(icon); Grid.SetColumn(text, 1); grid.Children.Add(text);
        button.Content = grid; AutomationProperties.SetName(button, label);
    }
    public void Activate(int index) { for (int i = 0; i < tools.Count; i++) tools[i].Classes.Set("active", i == index); }
}

// A fanned stack of cards. The top card flips to its reverse; stepping deals the next one forward.
internal sealed class Deck : Panel
{
    public const double CardWidth = 264, CardHeight = 372;
    const int Depth = 4;
    readonly Func<int, bool, Control> face;
    readonly int count;
    readonly bool motion;
    int index;
    bool flipped, busy;
    Border? top;
    public event Action<int>? Changed;
    public Deck(int count, int index, bool motion, Func<int, bool, Control> face)
    {
        this.count = count; this.index = index; this.motion = motion; this.face = face;
        Width = CardWidth + 110; Height = CardHeight + 24; FlowDirection = FlowDirection.LeftToRight;
        Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "بطاقات الألوان");
        KeyDown += (_, e) =>
        {
            switch (e.Key) { case Key.Left: Next(); break; case Key.Right: Previous(); break; case Key.Space: case Key.Enter: Flip(); break; default: return; }
            e.Handled = true;
        };
        Deal(0);
    }
    // The pile leans away from the reading edge: right-to-left pages fan to the left.
    static TransformOperations Pose(double depth, double flip = 1) => TransformOperations.Parse(
        FormattableString.Invariant($"translate({-depth * 30}px,{depth * 3}px) rotate({-depth * 5}deg) scale({(1 - depth * .03) * flip},{1 - depth * .03})"));
    static readonly TransformOperations Away = TransformOperations.Parse("translate(150px,10px) rotate(9deg) scale(0.97,0.97)");
    Border Card(int item, bool back, TransformOperations start, double opacity = 1) => new()
    {
        Width = CardWidth, Height = CardHeight, CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = face(item, back),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = opacity,
        RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative), RenderTransform = start,
        BoxShadow = BoxShadows.Parse("0 10 28 0 #2A000000"),
        Transitions = motion ? [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(460), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) }] : null
    };
    // step: +1 after moving forward, -1 after moving back, 0 for a plain layout.
    void Deal(int step, int previous = -1)
    {
        Children.Clear();
        int shown = Math.Min(Depth, count);
        var settle = new List<Action>();
        for (int depth = shown - 1; depth >= 0; depth--)
        {
            int d = depth;
            bool entering = step < 0 && d == 0;
            var card = Card((index + d) % count, false, !motion || step == 0 ? Pose(d) : entering ? Away : Pose(d + step), entering && motion ? 0 : 1);
            if (d == 0) { top = card; card.PointerPressed += (_, e) => { e.Handled = true; Focus(); Flip(); }; }
            Children.Add(card); settle.Add(() => { card.RenderTransform = Pose(d); card.Opacity = 1; });
        }
        if (motion && step > 0 && previous >= 0)
        {
            var leaving = Card(previous, false, Pose(0)); leaving.IsHitTestVisible = false; Children.Add(leaving);
            settle.Add(() => { leaving.RenderTransform = Away; leaving.Opacity = 0; });
            DispatcherTimer.RunOnce(() => Children.Remove(leaving), TimeSpan.FromMilliseconds(480));
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
            Transitions Turn(Easing easing) => [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = easing }];
            card.Transitions = Turn(new CubicEaseIn()); card.RenderTransform = Pose(0, 0.02); await Task.Delay(180);
            card.Child = face(index, flipped);
            card.Transitions = Turn(new CubicEaseOut()); card.RenderTransform = Pose(0); await Task.Delay(180);
        }
        finally { busy = false; }
    }
}
