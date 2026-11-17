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
        // The stage is laid out left to right, so the reading start is named outright: right for Arabic, left for English.
        var start = L.Language == "ar" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var role = new TextBlock { Text = spec.Label, FontSize = Typography.Lead, FontWeight = FontWeight.SemiBold, FlowDirection = L.Flow, HorizontalAlignment = start, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 12) };
        var name = new TextBlock { Text = spec.Name, FontSize = Typography.Caption, Opacity = .78, FlowDirection = L.Flow, HorizontalAlignment = start };
        var value = new TextBlock { Text = spec.Code, FontSize = spec.Code.Length > 9 ? 10.5 : 13, FontWeight = FontWeight.Bold, FontFamily = code, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = start };
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
// Every outline is exact geometry: one radius for all outer corners, a V-shaped mouth with a filleted
// point, and straight flap edges that meet in a filleted tip. The flap turns about the top edge and is
// seen through the stage's lens: closed it lies over the pocket (front layer); past upright it stands
// behind the cards and shows its lining (back layer). The paper is painted with Skia, so each edge is
// anti-aliased once, shadows are true blurs and every gradient is dithered.
internal sealed class EnvelopeArt(bool front) : Control
{
    // Rim is how far the point of the mouth sits below the top edge; cards and confetti leave from there.
    public const double W = 188, H = 244, Head = 120, Rim = 62;
    const float Wf = (float)W, Hf = (float)H, Corner = 10, MouthFillet = 9, Apex = 128, TipFillet = 16, Lens = 900, SealR = 19, SealY = 92;
    static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#111111")), Emboss = new SolidColorBrush(Color.Parse("#66FFFFFF"));
    static readonly IPen Rule = new Pen(new SolidColorBrush(Color.Parse("#2E111111")), 1);
    double angle, light;
    public string Serial = "";
    // Flap angle in degrees: 0 closed, 180 folded all the way back.
    public double Angle { get => angle; set { value = Math.Clamp(value, 0, 178); if (Math.Abs(value - angle) > .05) { angle = value; InvalidateVisual(); } } }
    // Where the light falls across the paper, -1 to 1.
    public double Light { get => light; set { if (Math.Abs(value - light) > .004) { light = value; InvalidateVisual(); } } }

    // ---- Geometry, in the body's own frame: x from 0 to W, y from 0 (the top edge and hinge) to H. ----
    static SKPath Build(Action<SKPath> draw) { var path = new SKPath(); draw(path); return path; }
    static readonly SKPath BodyShape = Build(p => p.AddRoundRect(new SKRect(0, 0, Wf, Hf), Corner, Corner));
    // The pocket is the body below the mouth: from just under each top corner down to a filleted V.
    static readonly SKPath PocketShape = Build(p =>
    {
        p.MoveTo(0, Corner);
        p.ArcTo(new SKPoint(Wf / 2, (float)Rim), new SKPoint(Wf, Corner), MouthFillet);
        p.LineTo(Wf, Corner);
        p.ArcTo(new SKPoint(Wf, Hf), new SKPoint(0, Hf), Corner);
        p.ArcTo(new SKPoint(0, Hf), new SKPoint(0, 0), Corner);
        p.Close();
    });
    static readonly SKPath LipLine = Build(p =>
    {
        p.MoveTo(0, Corner);
        p.ArcTo(new SKPoint(Wf / 2, (float)Rim), new SKPoint(Wf, Corner), MouthFillet);
        p.LineTo(Wf, Corner);
    });
    static readonly SKPath PocketRim = Build(p =>
    {
        p.MoveTo(Wf, Corner);
        p.ArcTo(new SKPoint(Wf, Hf), new SKPoint(0, Hf), Corner);
        p.ArcTo(new SKPoint(0, Hf), new SKPoint(0, 0), Corner);
        p.LineTo(0, Corner);
    });
    // The inside of the back, seen through the mouth. It runs 1.5 px under the pocket, so the two leave no seam.
    // Its top corners follow the flap's (see Fold).
    static SKPath Inside(float corner) => Build(p =>
    {
        p.MoveTo(0, Corner + 1.5f);
        p.ArcTo(new SKPoint(0, 0), new SKPoint(Wf, 0), corner);
        p.ArcTo(new SKPoint(Wf, 0), new SKPoint(Wf, Hf), corner);
        p.LineTo(Wf, Corner + 1.5f);
        p.ArcTo(new SKPoint(Wf / 2, (float)Rim + 1.5f), new SKPoint(0, Corner + 1.5f), MouthFillet);
        p.Close();
    });
    static readonly SKPath InsideShape = Inside(Corner);
    // The flap shares the body's top edge and corners exactly; its sides run straight into a filleted tip.
    static SKPath Flap(float corner) => Build(p =>
    {
        p.MoveTo(0, corner);
        p.ArcTo(new SKPoint(0, 0), new SKPoint(Wf, 0), corner);
        p.ArcTo(new SKPoint(Wf, 0), new SKPoint(Wf, Hf), corner);
        p.ArcTo(new SKPoint(Wf / 2, Apex), new SKPoint(0, corner), TipFillet);
        p.Close();
    });
    static readonly SKPath FlapShape = Flap(Corner);
    // The flap's free edges, without the fold.
    static SKPath FlapEdge(float corner) => Build(p =>
    {
        p.MoveTo(corner, 0);
        p.ArcTo(new SKPoint(0, 0), new SKPoint(0, Hf), corner);
        p.ArcTo(new SKPoint(Wf / 2, Apex), new SKPoint(Wf, corner), TipFillet);
        p.LineTo(Wf, corner);
        p.ArcTo(new SKPoint(Wf, 0), new SKPoint(0, 0), corner);
    });
    static readonly SKPath FlapRim = FlapEdge(Corner);
    // Closed, the envelope's top corners are the flap's and the back's together, rounded as one. Opened, two
    // rounded corners would pinch the fold into ears, so both square off while the flap stands past upright.
    static float Fold(double degrees) { double t = Math.Clamp((degrees - 90) / 60, 0, 1); return (float)(Corner * (1 - t * t * (3 - 2 * t))); }
    // The PickerHub mark from the app icon: an eyedropper bent into a P. Its glass body wraps the lens, its collar
    // and grooved rubber bulb sit where the bowl meets the stem, and the stem narrows into the tip.
    static readonly SKPath[] Mark =
    [
        SKPath.ParseSvgPathData("M524 668 H592 A170 170 0 0 0 762 498 V334 A170 170 0 0 0 592 164 H432 A170 170 0 0 0 262 334 V692 Q262 700 266.4 706.7 L291.6 745.3 Q296 752 296 760 V836 A14 14 0 0 0 324 836 V760 Q324 752 328.4 745.3 L353.6 706.7 Q358 700 358 692 V334 A74 74 0 0 1 432 260 H592 A74 74 0 0 1 666 334 V498 A74 74 0 0 1 592 572 H524 Z"),
        Build(p => p.AddRoundRect(new SKRect(500, 550, 528, 690), 12, 12)),
        Build(p => { p.AddPath(SKPath.ParseSvgPathData("M470 556 Q486 556 486 572 V668 Q486 684 470 684 H438 A64 64 0 0 1 438 556 Z M446 555 h8 v130 h-8 Z M462 555 h8 v130 h-8 Z")); p.FillType = SKPathFillType.EvenOdd; }),
    ];

    // The flap turns about the hinge and is seen through the same lens as the cards, so its tip widens as it comes forward.
    static SKMatrix Hinge(double degrees)
    {
        double turn = degrees * Math.PI / 180;
        var lens = new SKMatrix { ScaleX = 1, ScaleY = (float)Math.Cos(turn), Persp1 = (float)(-Math.Sin(turn) / Lens), Persp2 = 1 };
        return SKMatrix.CreateTranslation(Wf / 2, 0).PreConcat(lens).PreConcat(SKMatrix.CreateTranslation(-Wf / 2, 0));
    }
    static uint Mix(uint a, uint b, float t)
    {
        uint Channel(int shift) => (uint)Math.Round(((a >> shift) & 255) + ((((b >> shift) & 255) - (double)((a >> shift) & 255)) * Math.Clamp(t, 0, 1))) << shift;
        return Channel(24) | Channel(16) | Channel(8) | Channel(0);
    }
    static SKShader Linear(SKPoint from, SKPoint to, params (uint Color, float At)[] stops) =>
        SKShader.CreateLinearGradient(from, to, stops.Select(s => new SKColor(s.Color)).ToArray(), stops.Select(s => s.At).ToArray(), SKShaderTileMode.Clamp);

    // Brand-yellow paper: lighter where it faces the light, with a soft specular band that follows the pointer
    // and a faint pearl tint at the band's edges.
    static void Paper(SKCanvas canvas, SKPath shape, SKRect area, float lift, float light)
    {
        using (var fill = new SKPaint { IsAntialias = true, IsDither = true })
        {
            fill.Shader = Linear(new SKPoint(0, area.Top), new SKPoint(0, area.Bottom), (Mix(0xFFFFF45E, 0xFFFFF9A6, lift), 0), (Mix(0xFFFFF000, 0xFFFFF462, lift), .5f), (Mix(0xFFF5DD00, 0xFFFBEB3A, lift), 1));
            canvas.DrawPath(shape, fill);
        }
        float at = .5f + Math.Clamp(light, -1, 1) * .22f;
        canvas.Save(); canvas.ClipPath(shape, SKClipOperation.Intersect, true);
        using (var band = new SKPaint { IsDither = true })
        {
            band.Shader = Linear(new SKPoint(area.Left - 60, area.Top), new SKPoint(area.Right + 60, area.Bottom),
                (0x00FFFFFF, 0), (0x00FFFFFF, at - .2f), (0x24FFE4F1, at - .08f), (0x5CFFFFFF, at), (0x24D8F1FF, at + .08f), (0x00FFFFFF, at + .2f), (0x00FFFFFF, 1));
            canvas.DrawRect(area, band);
        }
        canvas.Restore();
    }
    // A line along an outline, clipped inside the shape: half a stroke wide, and it never doubles the outer edge.
    static void Edge(SKCanvas canvas, SKPath shape, SKPath line, uint color, float width)
    {
        canvas.Save(); canvas.ClipPath(shape, SKClipOperation.Intersect, true);
        using (var pen = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round, Color = new SKColor(color) })
            canvas.DrawPath(line, pen);
        canvas.Restore();
    }
    // The lining: deep charcoal with a fine yellow pinstripe, as good stationery is lined.
    static void Lining(SKCanvas canvas, SKPath shape, SKRect area)
    {
        using (var fill = new SKPaint { IsAntialias = true, IsDither = true, Shader = Linear(new SKPoint(0, area.Top), new SKPoint(0, area.Bottom), (0xFF2A2A27, 0), (0xFF161615, 1)) })
            canvas.DrawPath(shape, fill);
        canvas.Save(); canvas.ClipPath(shape, SKClipOperation.Intersect, true);
        using (var stripe = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0x14FFF000) })
            for (float k = area.Left - area.Height; k < area.Right; k += 6) canvas.DrawLine(k, area.Bottom, k + area.Height, area.Top, stripe);
        canvas.Restore();
    }
    // The seal: a disc of brand black with a fine yellow ring and the PickerHub mark.
    static void Seal(SKCanvas canvas, float x, float y)
    {
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f))
        using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0x66000000), MaskFilter = blur })
            canvas.DrawCircle(x, y + 1.5f, SealR, shadow);
        using (var disc = new SKPaint { IsAntialias = true, IsDither = true })
        {
            disc.Shader = SKShader.CreateRadialGradient(new SKPoint(x - SealR * .35f, y - SealR * .45f), SealR * 1.7f, [new SKColor(0xFF41413C), new SKColor(0xFF161615), new SKColor(0xFF0A0A0A)], [0, .5f, 1], SKShaderTileMode.Clamp);
            canvas.DrawCircle(x, y, SealR, disc);
        }
        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0x99FFF000) })
            canvas.DrawCircle(x, y, SealR - 3.5f, ring);
        canvas.Save();
        canvas.Translate(x, y); canvas.Scale(SealR * 1.08f / 686); canvas.Translate(-512, -507);   // the mark is 686 units tall, centred on (512, 507)
        using (var mark = new SKPaint { IsAntialias = true, Color = new SKColor(0xFFFFF000) })
            foreach (var part in Mark) canvas.DrawPath(part, mark);
        using (var pixel = new SKPaint { IsAntialias = true, Color = SKColors.White }) canvas.DrawRoundRect(new SKRect(442, 336, 582, 476), 30, 30, pixel);
        canvas.Restore();
    }

    // ---- The layers ----
    static void Back(SKCanvas canvas, double degrees, bool dark)
    {
        canvas.Translate(0, (float)Head);
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9))
        using (var drop = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, (byte)(dark ? 115 : 40)), MaskFilter = blur })
        { canvas.Save(); canvas.Translate(0, 7); canvas.DrawPath(BodyShape, drop); canvas.Restore(); }
        // Closed, the flap and pocket cover the inside exactly; drawing it would only darken their edges.
        if (degrees < .5) return;
        float corner = Fold(degrees);
        using (var inside = corner < Corner ? Inside(corner) : null) Lining(canvas, inside ?? InsideShape, new SKRect(0, 0, Wf, (float)Rim + 2));
        if (degrees <= 90) return;
        var hinge = Hinge(degrees); canvas.Concat(ref hinge);
        // Opened, the flap reaches 1.5 px back across the fold, so the fold leaves no seam either.
        using var face = Flap(corner); using var edge = FlapEdge(corner);
        using var lining = Build(p => { p.AddPath(face); p.AddRect(new SKRect(corner, -1.5f, Wf - corner, .5f)); });
        Lining(canvas, lining, new SKRect(0, -2, Wf, Apex));
        Edge(canvas, face, edge, 0xF2FFF000, 1.8f);   // the yellow outside shows only as the paper's edge
    }
    static void Pocket(SKCanvas canvas, double degrees, float light, bool dark)
    {
        canvas.Translate(0, (float)Head);
        if (degrees > 2)
        {
            // The pocket's lip shades whatever stands just inside it.
            canvas.Save(); canvas.ClipPath(InsideShape, SKClipOperation.Intersect, true); canvas.Translate(0, -3);
            using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5))
            using (var shade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12, Color = new SKColor(0x6B000000), MaskFilter = blur })
                canvas.DrawPath(LipLine, shade);
            canvas.Restore();
        }
        Paper(canvas, PocketShape, new SKRect(0, 0, Wf, Hf), 0, light);
        Edge(canvas, PocketShape, PocketRim, dark ? 0x1F000000u : 0x33000000u, 1.4f);
        Edge(canvas, PocketShape, LipLine, 0xC2FFFFFF, 1.8f);   // the lip catches the light
        if (degrees >= 88) return;
        // The closed flap lifts off the pocket a little, and its shadow lengthens as it rises.
        float k = (float)Math.Cos(degrees * Math.PI / 180);
        canvas.Save(); canvas.ClipPath(PocketShape, SKClipOperation.Intersect, true); canvas.Translate(0, 2.2f + 5 * (1 - k));
        var hinge = Hinge(degrees); canvas.Concat(ref hinge);
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.2f + 4 * (1 - k)))
        using (var shade = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, (byte)(78 * k)), MaskFilter = blur })
            canvas.DrawPath(FlapShape, shade);
        canvas.Restore();
    }
    static void Flap(SKCanvas canvas, double degrees, float light)
    {
        canvas.Translate(0, (float)Head);
        var hinge = Hinge(degrees); canvas.Concat(ref hinge);
        // It catches more light as it lifts toward the viewer.
        Paper(canvas, FlapShape, new SKRect(0, 0, Wf, Apex), (float)(Math.Sin(degrees * Math.PI / 180) * .8), light + .18f);
        Edge(canvas, FlapShape, FlapRim, 0x9EFFFFFF, 1.8f);
        Seal(canvas, Wf / 2, SealY);
    }
    // The printed face: the name of the pack, debossed, and a stamp line with the maker and the number.
    void Labels(DrawingContext context)
    {
        var family = TextElement.GetFontFamily(this);
        string name = L.T("باقة اليوم");
        FormattedText Title(double size, IBrush brush) => new(name, CultureInfo.InvariantCulture, L.Flow, new Typeface(family, FontStyle.Normal, FontWeight.SemiBold), size, brush);
        double size = 21; while (size > 15 && Title(size, Ink).Width > W - 36) size -= .5;
        var title = Title(size, Ink); var at = new Point((W - title.Width) / 2, Head + H * .655 - title.Height / 2);
        context.DrawText(Title(size, Emboss), at + new Point(0, .9));
        context.DrawText(title, at);
        var stamp = new Typeface(family, FontStyle.Normal, FontWeight.Bold);
        var maker = new FormattedText("PickerHub", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, stamp, 10.5, Ink);
        var number = new FormattedText(Serial, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, FontWeight.SemiBold), 10.5, Ink);
        double line = Head + H - 21;
        context.DrawText(maker, new Point(18, line - maker.Height / 2));
        context.DrawText(number, new Point(W - 18 - number.Width, line - number.Height / 2));
        context.DrawLine(Rule, new Point(18, line - 14.5), new Point(W - 18, line - 14.5));
    }
    public override void Render(DrawingContext context)
    {
        // The paint passes run on the render thread, so they capture plain values and never this control.
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        double degrees = angle; float shine = (float)light;
        var reach = new Rect(-48, -24, W + 96, H + Head + 72);
        if (!front) { context.Custom(new Sheet(reach, canvas => Back(canvas, degrees, dark))); return; }
        context.Custom(new Sheet(reach, canvas => Pocket(canvas, degrees, shine, dark)));
        Labels(context);
        if (degrees < 90) context.Custom(new Sheet(reach, canvas => Flap(canvas, degrees, shine)));
    }
    // One Skia pass recorded for the render thread.
    sealed class Sheet(Rect bounds, Action<SKCanvas> paint) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas; int saved = canvas.Save();
            try { paint(canvas); } finally { canvas.RestoreToCount(saved); }
        }
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
    // The first role sits where reading starts: on the right in Arabic, on the left in English.
    int Place(int card) => L.Language == "ar" ? cards.Count - 1 - card : card;
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
        double fall = dropAt < 0 ? 0 : Math.Clamp((clock - dropAt) / .5, 0, 1), bob = phase == Phase.Sealed ? Math.Sin(idle * 1.7) * 5 : 0;
        double squeeze = 1 - push.X * .05 + (phase == Phase.Opening && clock < .3 ? -.05 * Math.Sin(clock / .3 * Math.PI) : 0);
        // Its work done, the envelope sinks a little and fades. It is gone before it could reach the stage's edge.
        double away = fall * fall * (3 - 2 * fall);
        lean.Matrix = Pose(pitch.X, turn.X + (phase == Phase.Sealed ? Math.Sin(idle * 1.1) * 3 : 0), shake + away * 4, squeeze * (1 - .06 * away), away * 6, bob + away * 26);
        back.Opacity = front.Opacity = Math.Pow(1 - fall, 1.6);
        back.Angle = front.Angle = flap.X;
        back.Light = front.Light = turn.X / 17 + (phase == Phase.Sealed ? Math.Sin(idle * 1.1) * .35 : 0) + flap.X / 400;
        var body = Envelope;
        far.LightAt = new Point(Bounds.Width / 2, body.Y + EnvelopeArt.Head + 70); far.LightUp = 1; far.LightReach = 190;
        far.ShadowAt = new Point(Bounds.Width / 2 + away * 6, Tall - 22); far.ShadowUp = (1 - fall) * (1 - bob / 30);
        far.InvalidateVisual();
    }
}
