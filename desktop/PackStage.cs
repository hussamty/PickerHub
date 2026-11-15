using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using SkiaSharp;
using System.Diagnostics;
using System.Globalization;
using Space = System.Numerics.Matrix4x4;

namespace PickerHub;

// A value that is pulled toward a target by a damped spring. All motion on the stage is made of
// these: a cue only sets a target or gives a push, and the spring supplies the ease and the bounce.
internal struct Spring(double at)
{
    public double X = at, V, To = at;
    public void Step(double dt, double stiffness, double damping) { V += (-stiffness * (X - To) - damping * V) * dt; X += V * dt; }
    public readonly bool Still(double within) => Math.Abs(X - To) < within && Math.Abs(V) < within * 10;
    public void Snap(double value) { X = To = value; V = 0; }
}

internal sealed record CardSpec(string Hex, string Ink, string Label, string Code, string Name);

// One color of the palette as a card: the color itself edge to edge, its role and its code.
internal sealed class SwatchCard : Border
{
    public const double W = 150, H = 210;
    public readonly CardSpec Spec;
    readonly Border shine;
    readonly TranslateTransform sweep = new();
    public readonly MatrixTransform Pose = new();
    public SwatchCard(CardSpec spec, FontFamily code)
    {
        Spec = spec; Width = W; Height = H; CornerRadius = new CornerRadius(18); Focusable = true; IsHitTestVisible = false; RenderTransform = Pose;
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 30, Color = Color.Parse("#59000000") });
        AutomationProperties.SetName(this, spec.Label + " " + spec.Code);
        var ink = new SolidColorBrush(Color.Parse(spec.Ink));
        var role = new TextBlock { Text = spec.Label, FontSize = 16, FontWeight = FontWeight.Bold, FlowDirection = L.Flow, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 12) };
        var name = new TextBlock { Text = spec.Name, FontSize = 11, Opacity = .78, FlowDirection = L.Flow, HorizontalAlignment = HorizontalAlignment.Right };
        var value = new TextBlock { Text = spec.Code, FontSize = spec.Code.Length > 9 ? 10.5 : 13, FontWeight = FontWeight.Bold, FontFamily = code, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right };
        var foot = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 12) }; foot.Children.Add(name); foot.Children.Add(value);
        // A band of light that crosses the card as it turns.
        shine = new Border
        {
            Width = W * 2.2, Height = H * 1.6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-W * .6, -H * .3), Opacity = 0,
            RenderTransform = new TransformGroup { Children = [new RotateTransform(18), sweep] },
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = [new(Color.Parse("#00FFFFFF"), .36), new(Color.Parse("#52FFFFFF"), .5), new(Color.Parse("#00FFFFFF"), .64)]
            }
        };
        var face = new Panel(); face.Children.AddRange([role, foot, shine]);
        TextElement.SetForeground(face, ink);
        var body = new Border { Background = new SolidColorBrush(Color.Parse(spec.Hex)), CornerRadius = new CornerRadius(18), ClipToBounds = true, BorderThickness = new Thickness(1), Child = face };
        body[!BorderBrushProperty] = new DynamicResourceExtension("Rim");
        Child = body;
    }
    public void Gloss(double turn, double pitch)
    {
        sweep.X = turn / 22 * W; shine.Opacity = Math.Clamp((Math.Abs(turn) + Math.Abs(pitch)) / 16, 0, 1);
    }
}

// The envelope, drawn in two layers so the cards can sit between its back and its front pocket.
// The flap turns about its top edge: closed it lies over the pocket (front layer); past upright it
// stands behind the cards and shows its dark lining (back layer).
internal sealed class EnvelopeArt(bool front) : Control
{
    public const double W = 188, H = 244, Head = 118, Rim = 30, Flap = 114;
    static readonly Geometry sparkle = Geometry.Parse("M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m0 -12a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m-7 12a6 6 0 0 1 6 -6a6 6 0 0 1 -6 -6a6 6 0 0 1 -6 6a6 6 0 0 1 6 6");
    static readonly IBrush Lining = new SolidColorBrush(Color.Parse("#191917")), Ink = new SolidColorBrush(Color.Parse("#111111"));
    static readonly FontFamily code = new("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Menlo, Consolas, monospace");
    double angle, light;
    public string Serial = "";
    // Flap angle in degrees: 0 closed, 180 folded all the way back.
    public double Angle { get => angle; set { value = Math.Clamp(value, 0, 178); if (Math.Abs(value - angle) > .05) { angle = value; InvalidateVisual(); } } }
    // Where the light falls across the foil, -1 to 1.
    public double Light { get => light; set { if (Math.Abs(value - light) > .004) { light = value; InvalidateVisual(); } } }

    static LinearGradientBrush Across(double tilt, params (uint Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, .35 - tilt, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .65 + tilt, RelativeUnit.Relative) };
        foreach (var (color, at) in stops) brush.GradientStops.Add(new GradientStop(Color.FromUInt32(color), Math.Clamp(at, 0, 1)));
        return brush;
    }
    // Glossy foil: the brand yellow, an iridescent wash, one bright streak, and darker edges where it curves away.
    void Foil(DrawingContext context, Geometry shape, double lift)
    {
        double at = light * .2;
        context.DrawGeometry(new SolidColorBrush(Tones.Blend(0xFFF5E600, 0xFFFFF96B, lift)), null, shape);
        context.DrawGeometry(Across(.3, (0x00FF5FA2, .05 + at), (0x66FF5FA2, .26 + at), (0x668B5CF6, .44 + at), (0x593B9DFF, .6 + at), (0x4034D399, .76 + at), (0x0034D399, .95 + at)), null, shape);
        double streak = .36 + light * .24;
        context.DrawGeometry(Across(.12, (0x00FFFFFF, streak - .16), (0xB3FFFFFF, streak), (0x00FFFFFF, streak + .16)), null, shape);
        context.DrawGeometry(Across(0, (0x47111111, 0), (0x00111111, .16), (0x00111111, .84), (0x52111111, 1)), new Pen(new SolidColorBrush(Color.Parse("#40111111")), 1), shape);
    }
    static StreamGeometry Tongue(double tip)
    {
        var shape = new StreamGeometry();
        using var draw = shape.Open();
        double reach = tip - Head;
        draw.BeginFigure(new Point(0, Head), true);
        draw.LineTo(new Point(W, Head));
        draw.CubicBezierTo(new Point(W, Head + reach * .56), new Point(W * .64, tip), new Point(W / 2, tip));
        draw.CubicBezierTo(new Point(W * .36, tip), new Point(0, Head + reach * .56), new Point(0, Head));
        draw.EndFigure(true);
        return shape;
    }
    public override void Render(DrawingContext context)
    {
        double turn = angle * Math.PI / 180, fold = Math.Cos(turn), tip = Head + Flap * fold;
        if (!front)
        {
            if (fold < 0)
            {
                // Seen from inside: the lining, with the foil showing as a thin edge.
                var open = Tongue(tip);
                context.DrawGeometry(Lining, new Pen(new SolidColorBrush(Color.Parse("#FFF000")), 1.5), open);
                context.DrawGeometry(Across(0, (0x33FFFFFF, 0), (0x00FFFFFF, .5), (0x40000000, 1)), null, open);
            }
            var back = new Rect(0, Head, W, H);
            context.DrawRectangle(Lining, null, back, 16, 16);
            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new(Color.Parse("#2EFFFFFF"), 0), new(Color.Parse("#00FFFFFF"), .2), new(Color.Parse("#66000000"), 1)]
            }, null, back, 16, 16);
            return;
        }
        // The pocket: its top edge dips in the middle, where a thumb would pull the cards.
        var pocket = new StreamGeometry();
        using (var draw = pocket.Open())
        {
            double top = Head + Rim, bottom = Head + H, r = 16;
            draw.BeginFigure(new Point(0, top), true);
            draw.QuadraticBezierTo(new Point(W / 2, top + 40), new Point(W, top));
            draw.LineTo(new Point(W, bottom - r));
            draw.ArcTo(new Point(W - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise);
            draw.LineTo(new Point(r, bottom));
            draw.ArcTo(new Point(0, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            draw.EndFigure(true);
        }
        Foil(context, pocket, 0);
        var face = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold);
        var title = new FormattedText(L.T("باقة اليوم"), CultureInfo.InvariantCulture, L.Flow, face, 21, Ink);
        context.DrawText(title, new Point((W - title.Width) / 2, Head + H - 92));
        var mono = new Typeface(code, FontStyle.Normal, FontWeight.Bold);
        context.DrawText(new FormattedText("PickerHub", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, 10, Ink), new Point(16, Head + H - 34));
        var serial = new FormattedText(Serial, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, 10, Ink);
        context.DrawText(serial, new Point(W - 16 - serial.Width, Head + H - 34));
        if (fold < 0) return;
        // The closed flap catches more light as it lifts toward the viewer.
        Foil(context, Tongue(tip), Math.Sin(turn) * .8);
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#80FFFFFF")), 1), new Point(4, Head + .5), new Point(W - 4, Head + .5));
        var seal = new Point(W / 2, Head + Flap * fold * .72);
        context.DrawEllipse(Ink, null, seal, 19, 19 * fold);
        using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(1.05, 1.05 * fold) * Matrix.CreateTranslation(seal.X, seal.Y)))
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#FFF000")), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), sparkle);
    }
}

// Confetti in two depths. Pieces far from the viewer are small, slow and soft; the few that fly
// close are large and out of focus, and are drawn over everything. The far layer also carries the
// stage light and the shadow the envelope casts on the floor.
internal sealed class Confetti(bool near) : Control
{
    struct Piece { public float X, Y, VX, VY, Depth, Size, Angle, Spin, Flip, Tumble, Sway, Age, Life; public byte Shape; public uint Tone; }
    static readonly uint[] tones = [0xFFFF5FA2, 0xFF8B5CF6, 0xFF3B9DFF, 0xFFFFF000, 0xFF34D399, 0xFFFF8A3D];
    readonly List<Piece> pieces = [];
    public Point LightAt, ShadowAt;
    // LightReach is how far the pool of light may spread up and down before it must have faded out.
    public double LightUp, ShadowUp, LightReach = 140;
    public bool Busy => pieces.Count > 0;
    public Confetti() : this(false) { }

    // Throw pieces up and out of a point. Each layer keeps only the pieces at its own depth.
    public void Burst(Point from, int count, Random random, double force = 1)
    {
        for (int i = 0; i < count; i++)
        {
            double depth = .55 + random.NextDouble() * 1.05, angle = -Math.PI / 2 + (random.NextDouble() - .5) * 2.2, speed = (360 + random.NextDouble() * 620) * force * depth;
            var piece = new Piece
            {
                X = (float)(from.X + (random.NextDouble() - .5) * 90), Y = (float)from.Y, VX = (float)(Math.Cos(angle) * speed), VY = (float)(Math.Sin(angle) * speed),
                Depth = (float)depth, Size = (float)(6 + random.NextDouble() * 8), Angle = (float)(random.NextDouble() * 360), Spin = (float)((random.NextDouble() - .5) * 520),
                Flip = (float)(random.NextDouble() * 6.28), Tumble = (float)(4 + random.NextDouble() * 9), Sway = (float)(random.NextDouble() * 6.28), Life = (float)(2.3 + random.NextDouble() * 1.1),
                Shape = (byte)random.Next(4), Tone = tones[random.Next(tones.Length)]
            };
            if (depth > 1.34 == near) pieces.Add(piece);
        }
    }
    public void Step(double seconds)
    {
        float dt = (float)seconds, slow = MathF.Exp(-1.9f * dt);
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var p = pieces[i];
            p.Age += dt;
            if (p.Age >= p.Life || p.Y > Bounds.Height + 30) { pieces.RemoveAt(i); continue; }
            // Air takes the speed off quickly, then the piece drifts down, swaying as it tumbles.
            p.VX *= slow; p.VY = p.VY * slow + 520 * p.Depth * dt;
            p.X += (p.VX + MathF.Sin(p.Age * 3.1f + p.Sway) * 34 * p.Depth) * dt; p.Y += p.VY * dt;
            p.Angle += p.Spin * dt; p.Flip += p.Tumble * dt;
            pieces[i] = p;
        }
        InvalidateVisual();
    }
    public void Clear() { pieces.Clear(); InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        context.Custom(new Scene(new Rect(Bounds.Size), pieces.ToArray(), near, dark, LightAt, LightUp, Math.Min(LightReach, Math.Min(LightAt.Y, Bounds.Height - LightAt.Y)) - 2, ShadowAt, ShadowUp));
    }
    sealed class Scene(Rect area, Piece[] pieces, bool near, bool dark, Point lightAt, double lightUp, double reach, Point shadowAt, double shadowUp) : ICustomDrawOperation
    {
        static readonly float[] stops = Enumerable.Range(0, 11).Select(i => i / 10f).ToArray();
        public Rect Bounds => area;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            if (!near && lightUp > .01 && reach > 8)
            {
                // A soft pool of light behind the subject. It reaches nothing before the stage ends, so it has no edge.
                byte peak = (byte)((dark ? 30 : 150) * lightUp);
                var colors = stops.Select(p => new SKColor(255, 255, 255, (byte)(peak * (1 - p * p * (3 - 2 * p))))).ToArray();
                using var glow = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, colors, stops, SKShaderTileMode.Clamp, SKMatrix.CreateScale((float)Math.Min(reach * 2.6, area.Width / 2 - 2), (float)reach).PostConcat(SKMatrix.CreateTranslation((float)lightAt.X, (float)lightAt.Y)));
                using var lit = new SKPaint { Shader = glow, IsDither = true };
                canvas.DrawRect(new SKRect(0, 0, (float)area.Width, (float)area.Height), lit);
            }
            if (!near && shadowUp > .01)
            {
                using var soft = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 11);
                using var shade = new SKPaint { Color = new SKColor(0, 0, 0, (byte)((dark ? 150 : 60) * shadowUp)), MaskFilter = soft, IsAntialias = true };
                canvas.DrawOval((float)shadowAt.X, (float)shadowAt.Y, 96, 11, shade);
            }
            if (pieces.Length == 0) return;
            // Out of focus at both ends of the depth range.
            using var far = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.3f);
            using var close = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.8f);
            using var paint = new SKPaint { IsAntialias = true };
            using var triangle = new SKPath();
            triangle.MoveTo(0, -.6f); triangle.LineTo(.56f, .44f); triangle.LineTo(-.56f, .44f); triangle.Close();
            foreach (var p in pieces)
            {
                // Pieces fade out with age and toward the stage's top and bottom, so none is cut off at an edge.
                float fade = Math.Clamp((p.Life - p.Age) / .55f, 0, 1) * Math.Clamp(((float)area.Height - p.Y) / 46f, 0, 1) * Math.Clamp(p.Y / 34f, 0, 1) * Math.Clamp(p.Age / .05f, 0, 1);
                if (fade <= 0) continue;
                float turn = MathF.Cos(p.Flip), size = p.Size * p.Depth;
                var tone = Color.FromUInt32(p.Tone);
                // The underside of a piece is a little darker than its face.
                float shade = turn < 0 ? .8f : 1;
                paint.Color = new SKColor((byte)(tone.R * shade), (byte)(tone.G * shade), (byte)(tone.B * shade), (byte)(255 * fade * (p.Depth < .8f ? .8f : 1)));
                paint.MaskFilter = near ? close : p.Depth < .8f ? far : null;
                canvas.Save();
                canvas.Translate(p.X, p.Y); canvas.RotateDegrees(p.Angle); canvas.Scale(1, MathF.Max(.12f, MathF.Abs(turn)));
                switch (p.Shape)
                {
                    case 0: canvas.DrawRoundRect(-size / 2, -size * .31f, size, size * .62f, 1.5f, 1.5f, paint); break;
                    case 1: canvas.DrawCircle(0, 0, size * .4f, paint); break;
                    case 2: canvas.DrawRoundRect(-size * .8f, -size * .13f, size * 1.6f, size * .26f, 1, 1, paint); break;
                    default: canvas.Scale(size); canvas.DrawPath(triangle, paint); break;
                }
                canvas.Restore();
            }
        }
    }
}

// The whole scene of the daily palette: a sealed envelope that opens, deals its cards and falls
// away, and afterwards the same cards at rest in a row. Cards are real controls posed with a
// perspective matrix, so the ones that fly out are the ones that stay.
internal sealed class PackStage : Panel
{
    public const double Tall = 400, Short = 300;
    const double Top = 44, Body = 14, Lens = 900;
    enum Phase { Sealed, Opening, Resting }
    sealed class Card(SwatchCard view)
    {
        public readonly SwatchCard View = view;
        public Spring X = new(0), Y = new(0), Pitch = new(0), Turn = new(0), Roll = new(0), Size = new(1);
        public double Stiff = 150, Damp = 15;
        public bool Still => X.Still(.05) && Y.Still(.05) && Pitch.Still(.05) && Turn.Still(.05) && Roll.Still(.05) && Size.Still(.001);
    }
    readonly EnvelopeArt back = new(false) { ZIndex = 1, IsHitTestVisible = false }, front = new(true) { ZIndex = 50, IsHitTestVisible = false };
    readonly MatrixTransform lean = new();
    readonly Confetti far = new(false) { ZIndex = 0, IsHitTestVisible = false }, near = new(true) { ZIndex = 80, IsHitTestVisible = false };
    readonly List<Card> cards = [];
    readonly List<(double At, Action Do)> cues = [];
    readonly Stopwatch watch = new();
    readonly Random random = new(7);
    readonly bool motion;
    static readonly FontFamily code = new("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Menlo, Consolas, monospace");
    Phase phase;
    Spring pitch = new(0), turn = new(0), flap = new(0), push = new(0);
    double clock, idle, dropAt = -1, last;
    bool looping, pressed, placed;
    int hovered = -1;
    TaskCompletionSource? done;
    public event Action? OpenRequested;
    public Action<CardSpec>? Copied;
    public bool Opening => phase == Phase.Opening;
    public bool HasCards => cards.Count > 0;

    PackStage(bool motion, Phase phase)
    {
        this.motion = motion; this.phase = phase; ClipToBounds = true; Background = Brushes.Transparent; FlowDirection = FlowDirection.LeftToRight;
        Height = phase == Phase.Resting ? Short : Tall; Focusable = phase == Phase.Sealed;
        back.RenderTransform = lean; front.RenderTransform = lean;
        Children.AddRange([far, back, front, near]);
        PointerMoved += (_, e) => Track(e.GetPosition(this));
        PointerExited += (_, _) => { pitch.To = turn.To = 0; pressed = false; push.To = 0; Hover(-1, default); Cursor = Cursor.Default; Wake(); };
        PointerPressed += (_, e) => Press(e.GetPosition(this));
        PointerReleased += (_, e) => Release(e.GetPosition(this));
        KeyDown += (_, e) => { if (this.phase == Phase.Sealed && e.Key is Key.Enter or Key.Space) { e.Handled = true; OpenRequested?.Invoke(); } };
    }
    // A sealed envelope waiting to be opened.
    public PackStage(bool motion, string serial) : this(motion, Phase.Sealed)
    {
        front.Serial = serial; AutomationProperties.SetName(this, L.T("افتح باقة اليوم"));
    }
    // The cards already at rest, for every visit after the opening.
    public static PackStage Resting(bool motion, IEnumerable<CardSpec> specs)
    {
        var stage = new PackStage(motion, Phase.Resting);
        stage.back.IsVisible = stage.front.IsVisible = false;
        stage.Deal(specs, true);
        return stage;
    }
    void Deal(IEnumerable<CardSpec> specs, bool shown)
    {
        int index = 0;
        foreach (var spec in specs)
        {
            int at = index++;
            var view = new SwatchCard(spec, code) { ZIndex = 10 + at, Opacity = shown ? 1 : 0 };
            view.GotFocus += (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer && phase == Phase.Resting) { Hover(at, new Point(.5, .5)); Wake(); } };
            view.LostFocus += (_, _) => { if (hovered == at) { Hover(-1, default); Wake(); } };
            view.KeyDown += (_, e) => { if (phase == Phase.Resting && e.Key is Key.Enter or Key.Space) { e.Handled = true; Copy(at); } };
            cards.Add(new Card(view)); Children.Add(view);
        }
        InvalidateArrange();
    }

    // ---- Layout: cards are arranged at their resting places and posed away from them. ----
    int Place(int card) => cards.Count - 1 - card;                       // first role on the right, as Arabic reads
    double Step => StepFor(Bounds.Width);
    double Mid => (cards.Count - 1) / 2d;
    double StepFor(double width) => cards.Count < 2 ? 0 : Math.Min(SwatchCard.W + 14, (width - 24 - SwatchCard.W) / (cards.Count - 1));
    Rect SlotFor(double width, int card)
    {
        int place = Place(card); double step = StepFor(width), total = step * (cards.Count - 1) + SwatchCard.W;
        // A shallow arc: the outer cards sit a little lower and lean a little outward.
        return new Rect((width - total) / 2 + place * step, Top + Math.Pow(Math.Abs(place - Mid), 2) * 1.5, SwatchCard.W, SwatchCard.H);
    }
    Rect Slot(int card) => SlotFor(Bounds.Width, card);
    double RestRoll(int card) => (Place(card) - Mid) * 1.3;
    Rect Envelope => new((Bounds.Width - EnvelopeArt.W) / 2, Tall - 30 - EnvelopeArt.H - EnvelopeArt.Head, EnvelopeArt.W, EnvelopeArt.H + EnvelopeArt.Head);
    Point Mouth => new(Bounds.Width / 2, Envelope.Y + EnvelopeArt.Head + EnvelopeArt.Rim);
    protected override Size MeasureOverride(Size available)
    {
        foreach (var child in Children) child.Measure(available);
        return new Size(double.IsInfinity(available.Width) ? 900 : available.Width, Height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var all = new Rect(size);
        far.Arrange(all); near.Arrange(all);
        var envelope = new Rect((size.Width - EnvelopeArt.W) / 2, Tall - 30 - EnvelopeArt.H - EnvelopeArt.Head, EnvelopeArt.W, EnvelopeArt.H + EnvelopeArt.Head);
        back.Arrange(envelope); front.Arrange(envelope);
        for (int i = 0; i < cards.Count; i++) cards[i].View.Arrange(SlotFor(size.Width, i));
        // Poses depend on the places just arranged, so set them once layout has finished.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (phase == Phase.Resting) { Rest(!placed); placed = true; } else Apply(); }, Avalonia.Threading.DispatcherPriority.Render);
        return size;
    }

    // ---- Pointer ----
    void Track(Point at)
    {
        if (phase == Phase.Sealed)
        {
            var body = Envelope; var centre = new Point(body.Center.X, body.Y + EnvelopeArt.Head + EnvelopeArt.H / 2);
            bool over = new Rect(body.X, body.Y + EnvelopeArt.Head, body.Width, EnvelopeArt.H).Inflate(18).Contains(at);
            // The envelope leans toward the pointer, a little, from anywhere on the stage.
            turn.To = Math.Clamp((at.X - centre.X) / 260, -1, 1) * 17; pitch.To = -Math.Clamp((at.Y - centre.Y) / 220, -1, 1) * 12;
            Cursor = over ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
        else if (phase == Phase.Resting)
        {
            // Hit-test the resting places, not the lifted cards, so a card does not flee the pointer.
            int found = -1;
            for (int i = cards.Count - 1; i >= 0 && found < 0; i--) if (Slot(i).Contains(at)) found = i;
            // The lifted card keeps the pointer while it is over the strip the card rose from.
            if (found < 0 && hovered >= 0 && Slot(hovered).Inflate(new Thickness(0, 18, 0, 0)).Contains(at)) found = hovered;
            var slot = found >= 0 ? Slot(found) : default;
            Hover(found, found >= 0 ? new Point((at.X - slot.X) / slot.Width, (at.Y - slot.Y) / slot.Height) : default);
            Cursor = found >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
        Wake();
    }
    void Hover(int card, Point within)
    {
        if (phase != Phase.Resting) return;
        if (card != hovered) { hovered = card; pressed = false; }
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i]; c.Stiff = 190; c.Damp = 19;
            if (i == card)
            {
                c.View.ZIndex = 60; c.Y.To = -16; c.Size.To = pressed ? 1.02 : 1.08; c.Roll.To = 0; c.X.To = 0;
                c.Turn.To = (Math.Clamp(within.X, 0, 1) - .5) * 26; c.Pitch.To = -(Math.Clamp(within.Y, 0, 1) - .5) * 20;
            }
            else
            {
                // Neighbours step aside so the lifted card is seen whole.
                int away = card < 0 ? 0 : Math.Sign(Place(i) - Place(card)); double room = card < 0 ? 0 : (Step < SwatchCard.W ? 22 : 8) / Math.Abs(Place(i) - Place(card));
                c.View.ZIndex = 10 + i; c.X.To = away * room; c.Y.To = 0; c.Size.To = 1; c.Roll.To = RestRoll(i); c.Turn.To = 0; c.Pitch.To = 0;
            }
        }
    }
    void Press(Point at)
    {
        if (phase == Phase.Sealed && new Rect(Envelope.X, Envelope.Y + EnvelopeArt.Head, EnvelopeArt.W, EnvelopeArt.H).Inflate(18).Contains(at)) { pressed = true; push.To = 1; Focus(); Wake(); }
        else if (phase == Phase.Resting && hovered >= 0) { pressed = true; cards[hovered].Size.To = 1.02; Wake(); }
    }
    void Release(Point at)
    {
        if (!pressed) return;
        pressed = false; push.To = 0;
        if (phase == Phase.Sealed) OpenRequested?.Invoke();
        else if (phase == Phase.Resting && hovered >= 0) Copy(hovered);
        Wake();
    }
    void Copy(int card)
    {
        // The card nods once to say it was taken.
        var c = cards[card]; c.Size.To = hovered == card ? 1.08 : 1; if (motion) c.Turn.V += 520;
        Copied?.Invoke(c.View.Spec); Wake();
    }

    // ---- The opening ----
    // Plays the whole opening and completes when the cards have settled in their row.
    public Task Open(IEnumerable<CardSpec> specs)
    {
        if (phase != Phase.Sealed) return Task.CompletedTask;
        Deal(specs, false); UpdateLayout();
        phase = Phase.Opening; Focusable = false; Cursor = Cursor.Default; done = new TaskCompletionSource();
        if (!motion || VisualRoot == null) { Finish(); return done.Task; }
        clock = 0; pitch.To = turn.To = 0; push.To = 0; cues.Clear();
        // The flap swings open and overshoots a little before it rests.
        cues.Add((.26, () => flap.To = 166));
        cues.Add((.56, () => { far.Burst(Mouth, 96, random); near.Burst(Mouth, 96, random); }));
        for (int i = 0; i < cards.Count; i++)
        {
            int card = i;
            cues.Add((.66 + i * .1, () => Launch(card)));
            cues.Add((1.86 + (cards.Count - 1 - i) * .05, () => Settle(card)));
        }
        cues.Add((1.72, () => dropAt = clock));
        cues.Sort((a, b) => a.At.CompareTo(b.At));
        Wake();
        return done.Task;
    }
    // A card is pushed up out of the pocket and joins the fan held in its mouth.
    void Launch(int card)
    {
        var c = cards[card]; var slot = Slot(card); var body = Envelope;
        double fan = (Place(card) - Mid) * 6, radians = fan * Math.PI / 180, pivot = 150;
        c.View.Opacity = 1;
        c.X.Snap(body.Center.X - slot.Center.X); c.Y.Snap(body.Y + EnvelopeArt.Head + EnvelopeArt.H / 2 + 10 - slot.Center.Y);
        c.Size.Snap(.84); c.Turn.Snap(card % 2 == 0 ? 34 : -34); c.Pitch.Snap(-14); c.Roll.Snap(0);
        // The fan turns about a point below the cards, so their feet stay inside the pocket.
        c.X.To = body.Center.X + pivot * Math.Sin(radians) - slot.Center.X; c.Y.To = 150 + pivot * (1 - Math.Cos(radians)) - slot.Center.Y;
        c.Size.To = 1; c.Turn.To = 0; c.Pitch.To = 0; c.Roll.To = fan;
        c.Y.V = -520; c.Stiff = 120; c.Damp = 13.5;
        far.Burst(Mouth, 9, random, .8); near.Burst(Mouth, 9, random, .8);
    }
    // The fan opens into the row. Each card turns a little on its way and lands with a soft bounce.
    void Settle(int card)
    {
        var c = cards[card];
        c.X.To = 0; c.Y.To = 0; c.Roll.To = RestRoll(card); c.Turn.V += card % 2 == 0 ? 230 : -230; c.Pitch.V += 90;
        c.Stiff = 132; c.Damp = 14;
    }
    void Finish()
    {
        if (phase == Phase.Resting) return;
        phase = Phase.Resting; cues.Clear(); dropAt = -1;
        back.IsVisible = front.IsVisible = false; far.ShadowUp = 0;
        Height = Short;
        Rest(true);
        done?.TrySetResult();
    }
    void Rest(bool snap)
    {
        if (hovered >= 0) return;
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i]; c.View.Opacity = 1; c.View.ZIndex = 10 + i; c.X.To = 0; c.Y.To = 0; c.Size.To = 1; c.Turn.To = 0; c.Pitch.To = 0; c.Roll.To = RestRoll(i);
            if (snap || !motion) { c.X.Snap(0); c.Y.Snap(0); c.Size.Snap(1); c.Turn.Snap(0); c.Pitch.Snap(0); c.Roll.Snap(RestRoll(i)); }
        }
        Apply(); Wake();
    }

    // ---- Frames ----
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Wake(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        looping = false; far.Clear(); near.Clear();
        if (phase == Phase.Opening) Finish();
    }
    void Wake()
    {
        if (looping || VisualRoot == null) return;
        looping = true; watch.Restart(); last = 0;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Frame());
    }
    void Frame()
    {
        if (!looping || VisualRoot == null) { looping = false; return; }
        double now = watch.Elapsed.TotalSeconds, dt = Math.Min(now - last, 1 / 24d); last = now;
        bool busy = Advance(dt);
        Apply();
        if (busy) TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Frame()); else looping = false;
    }
    // Move everything forward by dt. Returns whether anything is still in motion.
    bool Advance(double dt)
    {
        if (!motion)
        {
            pitch.Snap(pitch.To); turn.Snap(turn.To); push.Snap(push.To);
            foreach (var c in cards) { c.X.Snap(c.X.To); c.Y.Snap(c.Y.To); c.Size.Snap(c.Size.To); c.Roll.Snap(c.Roll.To); c.Turn.Snap(0); c.Pitch.Snap(0); }
            return false;
        }
        if (phase == Phase.Opening)
        {
            clock += dt;
            while (cues.Count > 0 && cues[0].At <= clock) { var cue = cues[0]; cues.RemoveAt(0); cue.Do(); }
        }
        if (phase == Phase.Sealed) idle += dt;
        // Small steps keep the stiff springs stable at any frame rate.
        for (double left = dt; left > 0; left -= 1 / 240d)
        {
            double step = Math.Min(left, 1 / 240d);
            pitch.Step(step, 120, 14); turn.Step(step, 120, 14); push.Step(step, 260, 20); flap.Step(step, 58, 7.2);
            foreach (var c in cards) { c.X.Step(step, c.Stiff, c.Damp); c.Y.Step(step, c.Stiff, c.Damp); c.Size.Step(step, c.Stiff, c.Damp); c.Roll.Step(step, c.Stiff, c.Damp); c.Turn.Step(step, 90, 10); c.Pitch.Step(step, 90, 10); }
        }
        far.Step(dt); near.Step(dt);
        bool still = cards.All(c => c.Still) && !far.Busy && !near.Busy;
        if (phase == Phase.Opening && cues.Count == 0 && clock > 2.5 && cards.All(c => c.Still)) Finish();
        return phase switch
        {
            Phase.Sealed => IsEffectivelyVisible,                       // the envelope breathes while it waits
            Phase.Opening => true,
            _ => !still
        };
    }
    static Matrix Pose(double pitch, double turn, double roll, double size, double x, double y)
    {
        const float degree = MathF.PI / 180;
        var m = Space.CreateScale((float)size) * Space.CreateRotationZ((float)roll * degree) * Space.CreateRotationX((float)pitch * degree) * Space.CreateRotationY((float)turn * degree);
        var lens = Space.Identity; lens.M34 = -1f / (float)Lens;
        m *= lens;
        // Each card is seen straight on from its own place, then moved: flying far does not skew it.
        return new Matrix(m.M11, m.M12, m.M14, m.M21, m.M22, m.M24, m.M41, m.M42, m.M44) * Matrix.CreateTranslation(x, y);
    }
    void Apply()
    {
        foreach (var c in cards)
        {
            c.View.Pose.Matrix = Pose(c.Pitch.X, c.Turn.X, c.Roll.X, c.Size.X, c.X.X, c.Y.X);
            c.View.Gloss(c.Turn.X, c.Pitch.X);
        }
        if (phase == Phase.Resting)
        {
            far.LightAt = new Point(Bounds.Width / 2, Top + SwatchCard.H / 2); far.LightUp = 1; far.LightReach = 150; far.InvalidateVisual();
            return;
        }
        // Before the cue: a quick shake, as if something inside wants out.
        double shake = phase == Phase.Opening && clock < .7 ? 6.5 * Math.Sin(clock * 29) * Math.Exp(-clock * 5.2) : 0;
        double fall = dropAt < 0 ? 0 : Math.Clamp((clock - dropAt) / .55, 0, 1), bob = phase == Phase.Sealed ? Math.Sin(idle * 1.7) * 5 : 0;
        double squeeze = 1 - push.X * .05 + (phase == Phase.Opening && clock < .3 ? -.05 * Math.Sin(clock / .3 * Math.PI) : 0);
        lean.Matrix = Pose(pitch.X, turn.X + (phase == Phase.Sealed ? Math.Sin(idle * 1.1) * 3 : 0), shake + fall * 13, squeeze, fall * fall * 40, bob + fall * fall * 330);
        back.Opacity = front.Opacity = 1 - Math.Pow(fall, 1.4);
        back.Angle = front.Angle = flap.X;
        back.Light = front.Light = turn.X / 17 + (phase == Phase.Sealed ? Math.Sin(idle * 1.1) * .35 : 0) + flap.X / 400;
        var body = Envelope;
        far.LightAt = new Point(Bounds.Width / 2, body.Y + EnvelopeArt.Head + 70); far.LightUp = 1; far.LightReach = 190;
        far.ShadowAt = new Point(Bounds.Width / 2 + fall * 30, Tall - 22); far.ShadowUp = (1 - fall) * (1 - bob / 30);
        far.InvalidateVisual();
    }
}
