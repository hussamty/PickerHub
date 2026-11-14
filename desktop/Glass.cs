using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace PickerHub;

// A pane of frosted glass: a translucent tint, a lit rim, a soft shadow, and, when Blur is set,
// a blurred copy of whatever was painted behind it. Blur is only worth its cost where the pane
// overlaps other content; over the plain backdrop the tint alone looks the same.
internal class Glass : Decorator, ICustomHitTest
{
    public static readonly StyledProperty<Color> TintProperty = AvaloniaProperty.Register<Glass, Color>(nameof(Tint));
    public static readonly StyledProperty<Color> RimProperty = AvaloniaProperty.Register<Glass, Color>(nameof(Rim));
    public static readonly StyledProperty<Color> ShadeProperty = AvaloniaProperty.Register<Glass, Color>(nameof(Shade));
    public static readonly StyledProperty<Color> WashProperty = AvaloniaProperty.Register<Glass, Color>(nameof(Wash));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<Glass, double>(nameof(Radius), 24);
    public static readonly StyledProperty<double> BlurProperty = AvaloniaProperty.Register<Glass, double>(nameof(Blur));
    public static readonly StyledProperty<double> ShoulderProperty = AvaloniaProperty.Register<Glass, double>(nameof(Shoulder));
    static Glass() => AffectsRender<Glass>(TintProperty, RimProperty, ShadeProperty, WashProperty, RadiusProperty, BlurProperty, ShoulderProperty);

    public Glass(string tint = "GlassTint")
    {
        this[!TintProperty] = new DynamicResourceExtension(tint);
        this[!RimProperty] = new DynamicResourceExtension("GlassRim");
        this[!ShadeProperty] = new DynamicResourceExtension("GlassShade");
    }
    public Color Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public Color Rim { get => GetValue(RimProperty); set => SetValue(RimProperty, value); }
    public Color Shade { get => GetValue(ShadeProperty); set => SetValue(ShadeProperty, value); }
    // A faint cast of a sampled color through the glass.
    public Color Wash { get => GetValue(WashProperty); set => SetValue(WashProperty, value); }
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Blur { get => GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    // When set, the pane grows out of the window's right edge through two concave shoulders of this radius.
    public double Shoulder { get => GetValue(ShoulderProperty); set => SetValue(ShoulderProperty, value); }

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
    public override void Render(DrawingContext context) =>
        context.Custom(new Pane(new Rect(Bounds.Size), Tint, Rim, Shade, Wash, Radius, Blur, Shoulder, behind));
    Backing behind = Backing.Take();
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); behind.Return(); behind = Backing.Take(); }

    sealed record Pane(Rect Area, Color Tint, Color Rim, Color Shade, Color Wash, double Radius, double Blur, double Shoulder, Backing Behind) : ICustomDrawOperation
    {
        const float Spread = 14, Drop = 10;
        public Rect Bounds => Area.Inflate(Shade.A == 0 ? 1 : Spread * 3);
        public bool HitTest(Point p) => Area.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => other is Pane pane && pane == this;
        public void Dispose() { }
        static SKColor Sk(Color c, double alpha = 1) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0, 255));

        SKPath Shape()
        {
            float w = (float)Area.Width, h = (float)Area.Height, s = (float)Shoulder;
            float r = (float)Math.Min(Radius, Math.Min(w, h - 2 * s) / 2);
            var path = new SKPath();
            if (s <= 0) { path.AddRoundRect(new SKRect(0, 0, w, h), r, r); return path; }
            path.MoveTo(w, 0);
            path.ArcTo(new SKRect(w - 2 * s, -s, w, s), 0, 90, false);
            path.LineTo(r, s);
            path.ArcTo(new SKRect(0, s, 2 * r, s + 2 * r), 270, -90, false);
            path.LineTo(0, h - s - r);
            path.ArcTo(new SKRect(0, h - s - 2 * r, 2 * r, h - s), 180, -90, false);
            path.LineTo(w - s, h - s);
            path.ArcTo(new SKRect(w - 2 * s, h - s, w, h + s), 270, 90, false);
            path.Close();
            return path;
        }
        public void Render(ImmediateDrawingContext context)
        {
            if (Area.Width < 1 || Area.Height < 1 || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var path = Shape();
            float h = (float)Area.Height;

            if (Shade.A > 0)
            {
                using var shadow = new SKPaint { Color = Sk(Shade), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Spread) };
                canvas.Save();
                if (Shoulder > 0) { canvas.ClipPath(path, SKClipOperation.Difference, true); canvas.Translate(0, Drop); canvas.DrawPath(path, shadow); }
                else
                {
                    // A plain rounded rectangle takes Skia's fast blurred-shadow path; an arbitrary path is rasterized on every repaint.
                    float r = (float)Math.Min(Radius, Math.Min(Area.Width, Area.Height) / 2);
                    using var round = new SKRoundRect(new SKRect(0, 0, (float)Area.Width, (float)Area.Height), r, r);
                    canvas.ClipRoundRect(round, SKClipOperation.Difference, true); canvas.Translate(0, Drop); canvas.DrawRoundRect(round, shadow);
                }
                canvas.Restore();
            }

            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            if (Blur > 0 && lease.SkSurface is { } surface) Frost(canvas, surface, lease.GrContext);
            using (var fill = new SKPaint { Color = Sk(Tint) }) canvas.DrawPaint(fill);
            if (Wash.A > 0) { using var wash = new SKPaint { Color = Sk(Wash) }; canvas.DrawPaint(wash); }
            // Light falls from the top-left corner and fades across the pane.
            using (var sheen = new SKPaint())
            {
                sheen.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint((float)Area.Width * .7f, h * .7f),
                    [Sk(Rim, .22), Sk(Rim, 0)], null, SKShaderTileMode.Clamp);
                canvas.DrawPaint(sheen);
            }
            canvas.Restore();

            using var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            rim.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), [Sk(Rim), Sk(Rim, .22)], null, SKShaderTileMode.Clamp);
            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            rim.StrokeWidth = 2;
            canvas.DrawPath(path, rim);
            canvas.Restore();
        }
        // Draw the pixels painted under the pane back, blurred. A repaint may cover only part of the
        // pane, and outside that part the window already shows the finished glass, so the pane keeps
        // its own copy of the backdrop and refreshes only what was just repainted beneath it.
        // The copy is a quarter of the size: a blur hides the difference and costs a sixteenth of the work.
        const int Down = 4;
        void Frost(SKCanvas canvas, SKSurface surface, GRContext? gpu)
        {
            var matrix = canvas.TotalMatrix;
            float scale = MathF.Sqrt(MathF.Abs(matrix.ScaleX * matrix.ScaleY - matrix.SkewX * matrix.SkewY));
            float sigma = (float)Blur * scale;
            if (sigma < .5f || !canvas.GetDeviceClipBounds(out var fresh)) return;
            var own = SKRectI.Ceiling(matrix.MapRect(new SKRect(0, 0, (float)Area.Width, (float)Area.Height)));
            int reach = (int)MathF.Ceiling(sigma * 2.5f);
            var region = new SKRectI(Math.Max(0, own.Left - reach), Math.Max(0, own.Top - reach), own.Right + reach, own.Bottom + reach);
            if (region.Width <= 0 || region.Height <= 0) return;
            using var seen = surface.Snapshot(region);
            if (seen == null) return;
            int w = (region.Width + Down - 1) / Down, h = (region.Height + Down - 1) / Down;
            bool whole = Behind.Fit(gpu, w, h) || Behind.Region != region;
            if (Behind.Copy == null || Behind.Soft == null) return;
            Behind.Region = region;

            var copy = Behind.Copy.Canvas;
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.Low };
            copy.Save();
            copy.ClipRect(new SKRect(0, 0, w, h));
            copy.Scale(1f / Down); copy.Translate(-region.Left, -region.Top);
            if (whole) copy.DrawImage(seen, region.Left, region.Top, replace);
            else
            {
                // Around the pane nothing of it was ever painted; inside it, only the repainted part is the true backdrop.
                copy.Save(); copy.ClipRect(own, SKClipOperation.Difference); copy.DrawImage(seen, region.Left, region.Top, replace); copy.Restore();
                copy.ClipRect(fresh); copy.DrawImage(seen, region.Left, region.Top, replace);
            }
            copy.Restore();

            var part = new SKRectI(0, 0, w, h);
            using (var small = Behind.Copy.Snapshot(part))
            using (var blur = SKImageFilter.CreateBlur(sigma / Down, sigma / Down, SKShaderTileMode.Clamp))
            using (var soften = new SKPaint { ImageFilter = blur, BlendMode = SKBlendMode.Src })
                if (small != null) Behind.Soft.Canvas.DrawImage(small, 0, 0, soften);
            using var blurred = Behind.Soft.Snapshot(part);
            if (blurred == null) return;
            using var enlarge = new SKPaint { FilterQuality = SKFilterQuality.Low };
            canvas.Save();
            canvas.ResetMatrix(); canvas.Translate(region.Left, region.Top); canvas.Scale(Down);
            canvas.DrawImage(blurred, 0, 0, enlarge);
            canvas.Restore();
        }
    }
    // A pane's private copy of what lies behind it, and its blurred form. Only the render thread draws
    // into them. They are pooled rather than left to the finalizer, which must not free GPU surfaces.
    sealed class Backing
    {
        static readonly System.Collections.Concurrent.ConcurrentBag<Backing> spare = [];
        public SKSurface? Copy, Soft; public SKRectI Region;
        GRContext? owner; int wide, high;
        public static Backing Take() => spare.TryTake(out var found) ? found : new Backing();
        public void Return() { Region = default; spare.Add(this); }
        // True when the surfaces were (re)made and hold nothing yet.
        public bool Fit(GRContext? gpu, int w, int h)
        {
            if (Copy != null && Soft != null && owner == gpu && w <= wide && h <= high) return false;
            Copy?.Dispose(); Soft?.Dispose();
            wide = Math.Max(wide, (w + 31) / 32 * 32); high = Math.Max(high, (h + 31) / 32 * 32); owner = gpu;
            var info = new SKImageInfo(wide, high, SKColorType.Rgba8888, SKAlphaType.Premul);
            Copy = (gpu != null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            Soft = (gpu != null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            return true;
        }
    }
}

// A run of liquid buttons that share one body. Each button's outline is drawn into a blurred
// layer and cut back to a hard edge, so shapes that come close flow into each other.
internal sealed class Goo : Panel
{
    readonly StackPanel row = new() { Orientation = Avalonia.Layout.Orientation.Horizontal };
    readonly Pool pool;
    public Goo(double spacing = 7)
    {
        row.Spacing = spacing; pool = new Pool(row);
        Children.Add(pool); Children.Add(row);
    }
    public void Add(Control drop)
    {
        drop.PropertyChanged += (_, change) => { if (change.Property == RenderTransformProperty || change.Property == BoundsProperty) pool.InvalidateVisual(); };
        row.Children.Add(drop);
    }
    sealed class Pool : Control
    {
        public static readonly StyledProperty<Color> BodyProperty = AvaloniaProperty.Register<Pool, Color>("Body");
        static Pool() => AffectsRender<Pool>(BodyProperty);
        readonly StackPanel row;
        public Pool(StackPanel row) { this.row = row; IsHitTestVisible = false; this[!BodyProperty] = new DynamicResourceExtension("GooBody"); }
        public override void Render(DrawingContext context)
        {
            var drops = new List<Rect>();
            foreach (var child in row.Children)
            {
                if (!child.IsVisible) continue;
                var b = child.Bounds; var m = child.RenderTransform?.Value ?? Matrix.Identity;
                double w = b.Width * Math.Abs(m.M11), h = b.Height * Math.Abs(m.M22);
                drops.Add(new Rect(b.Center.X - w / 2, b.Center.Y - h / 2, w, h));
            }
            if (drops.Count > 0) context.Custom(new Blob(new Rect(Bounds.Size).Inflate(12), drops.ToArray(), GetValue(BodyProperty)));
        }
    }
    sealed class Blob(Rect bounds, Rect[] drops, Color color) : ICustomDrawOperation
    {
        // Alpha below the midpoint is dropped and above it is filled: the blur becomes a crisp, merged outline.
        static readonly byte[] Edge = Enumerable.Range(0, 256).Select(a => (byte)Math.Clamp((a - 112) * 8, 0, 255)).ToArray();
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => other is Blob b && b.Tone == color && b.Drops.AsSpan().SequenceEqual(drops);
        Color Tone => color; Rect[] Drops => drops;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var blur = SKImageFilter.CreateBlur(4.5f, 4.5f);
            using var table = SKColorFilter.CreateTable(Edge, null, null, null);
            using var merge = SKImageFilter.CreateColorFilter(table, blur);
            using var layer = new SKPaint { ImageFilter = merge, Color = new SKColor(255, 255, 255, color.A) };
            using var fill = new SKPaint { Color = new SKColor(color.R, color.G, color.B), IsAntialias = true };
            canvas.SaveLayer(new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom), layer);
            foreach (var d in drops)
            {
                float r = (float)Math.Min(d.Width, d.Height) / 2;
                canvas.DrawRoundRect(new SKRect((float)d.X, (float)d.Y, (float)d.Right, (float)d.Bottom), r, r, fill);
            }
            canvas.Restore();
        }
    }
}

// The room behind the glass: brand black or its light inverse, with two pools of light for the
// panes to catch. Mix runs from 0 (dark) to 1 (light) so the theme switch can scrub it.
internal sealed class Backdrop : Control
{
    public static readonly StyledProperty<double> MixProperty = AvaloniaProperty.Register<Backdrop, double>(nameof(Mix));
    static Backdrop() => AffectsRender<Backdrop>(MixProperty);
    public double Mix { get => GetValue(MixProperty); set => SetValue(MixProperty, value); }
    static Color Blend(uint dark, uint light, double t)
    {
        Color a = Color.FromUInt32(dark), b = Color.FromUInt32(light);
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(Channel(a.A, b.A), Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }
    static RadialGradientBrush Pool(Color color, double x, double y, double radius) => new()
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative), GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(radius, RelativeUnit.Relative), RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
        GradientStops = [new(color, 0), new(Color.FromArgb(0, color.R, color.G, color.B), 1)]
    };
    public override void Render(DrawingContext context)
    {
        double t = Math.Clamp(Mix, 0, 1); var area = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Blend(0xFF111111, 0xFFF4F3EE, t)), null, area);
        // Yellow light from the top corner where the eyedropper lives; a neutral pool opposite it.
        context.DrawRectangle(Pool(Blend(0x3DFFF000, 0x6BFFF000, t), .12, -.05, .62), null, area);
        context.DrawRectangle(Pool(Blend(0x1FFFFFFF, 0x1A111111, t), .95, 1.05, .7), null, area);
        context.DrawRectangle(Pool(Blend(0x17FFF000, 0x38FFF000, t), .62, .6, .34), null, area);
    }
}
