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

    public Glass() : this("GlassTint") { }
    public Glass(string tint)
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
    static bool reduceTransparency;
    internal static bool ReduceTransparency
    {
        get => reduceTransparency;
        set
        {
            if (reduceTransparency == value) return;
            reduceTransparency = value;
            foreach (var weak in panes.ToArray())
            {
                if (weak.TryGetTarget(out var pane)) pane.InvalidateVisual(); else panes.Remove(weak);
            }
        }
    }
    static readonly List<WeakReference<Glass>> panes = [];
    internal static bool Dark = true;
    public override void Render(DrawingContext context) =>
        context.Custom(new Pane(new Rect(Bounds.Size), ReduceTransparency ? Color.Parse(Dark ? "#232321" : "#FFFFFF") : Tint, Rim, Shade, Wash, Radius, ReduceTransparency ? 0 : Blur, Shoulder, key));
    // The pane's backdrop copy lives on the render thread under this key. The control holds no
    // graphics memory itself, so nothing of it is ever freed by the garbage collector.
    static long last;
    readonly long key = Interlocked.Increment(ref last);
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); panes.Add(new(this)); Backing.Mark(key, true); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); panes.RemoveAll(w => !w.TryGetTarget(out var p) || p == this); Backing.Mark(key, false); }

    sealed record Pane(Rect Area, Color Tint, Color Rim, Color Shade, Color Wash, double Radius, double Blur, double Shoulder, long Key) : ICustomDrawOperation
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
            Backing.Sweep();
            using var path = Shape();
            float h = (float)Area.Height;

            if (Shade.A > 0)
            {
                using var mask = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Spread);
                using var shadow = new SKPaint { Color = Sk(Shade), IsAntialias = true, MaskFilter = mask };
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
            using (var sheen = new SKPaint { IsDither = true })
            {
                using var sheenShader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint((float)Area.Width * .7f, h * .7f),
                    [Sk(Rim, .22), Sk(Rim, 0)], null, SKShaderTileMode.Clamp);
                sheen.Shader = sheenShader; canvas.DrawPaint(sheen);
            }
            canvas.Restore();

            using var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            using var rimShader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), [Sk(Rim), Sk(Rim, .22)], null, SKShaderTileMode.Clamp);
            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            rim.Shader = rimShader; rim.StrokeWidth = 2;
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
            // A pane that has left the screen may still be painted once from the previous scene: it gets no surfaces.
            if (Backing.Of(Key) is not { } Behind) return;
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
    // A pane's private copy of what lies behind it, and its blurred form. These may be GPU
    // surfaces, which must be created, drawn and freed on the render thread while its context is
    // current. So they are owned by this table, never by a control: a control that goes away only
    // posts its key, and the render thread frees the surfaces on its next pass. Leaving them to the
    // garbage collector frees them from the finalizer thread, which crashes the graphics driver.
    sealed class Backing
    {
        static readonly Dictionary<long, Backing> live = [];
        static readonly HashSet<long> attached = [];
        static readonly System.Collections.Concurrent.ConcurrentQueue<(long Key, bool Shown)> changes = new();
        public SKSurface? Copy, Soft; public SKRectI Region;
        GRContext? owner; int wide, high;
        // Any thread: the pane with this key has entered or left the screen.
        public static void Mark(long key, bool shown) => changes.Enqueue((key, shown));
        // Render thread only, as is everything below.
        public static void Sweep()
        {
            while (changes.TryDequeue(out var change))
            {
                if (change.Shown) { attached.Add(change.Key); continue; }
                attached.Remove(change.Key);
                if (live.Remove(change.Key, out var left)) left.Free();
            }
        }
        public static Backing? Of(long key)
        {
            if (!attached.Contains(key)) return null;
            if (!live.TryGetValue(key, out var found)) live[key] = found = new Backing();
            return found;
        }
        void Free() { Copy?.Dispose(); Soft?.Dispose(); Copy = Soft = null; }
        // True when the surfaces were (re)made and hold nothing yet.
        public bool Fit(GRContext? gpu, int w, int h)
        {
            if (Copy != null && Soft != null && owner == gpu && w <= wide && h <= high) return false;
            Free();
            wide = Math.Max(wide, (w + 31) / 32 * 32); high = Math.Max(high, (h + 31) / 32 * 32); owner = gpu;
            var info = new SKImageInfo(wide, high, SKColorType.Rgba8888, SKAlphaType.Premul);
            Copy = (gpu != null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            Soft = (gpu != null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            return true;
        }
    }
}

// One connected glass silhouette for neighbouring actions. Explicit curved necks keep
// their thickness at every scale; alpha-threshold blur used to erase them almost entirely.
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
        drop.PropertyChanged += (_, change) =>
        {
            if (change.Property == RenderTransformProperty || change.Property == BoundsProperty ||
                change.Property == IsPointerOverProperty || change.Property == IsKeyboardFocusWithinProperty ||
                change.Property == IsVisibleProperty || change.Property == IsEnabledProperty) pool.InvalidateVisual();
        };
        row.Children.Add(drop);
    }
    sealed class Pool : Control
    {
        public static readonly StyledProperty<Color> BodyProperty = AvaloniaProperty.Register<Pool, Color>("Body");
        public static readonly StyledProperty<Color> RimProperty = AvaloniaProperty.Register<Pool, Color>("Rim");
        static Pool() => AffectsRender<Pool>(BodyProperty, RimProperty);
        readonly StackPanel row;
        public Pool(StackPanel row)
        {
            this.row = row; IsHitTestVisible = false;
            this[!BodyProperty] = new DynamicResourceExtension("GooBody");
            this[!RimProperty] = new DynamicResourceExtension("GlassRim");
        }
        public override void Render(DrawingContext context)
        {
            var drops = new List<Drop>();
            foreach (var child in row.Children)
            {
                if (!child.IsVisible) continue;
                var b = child.Bounds; var m = child.RenderTransform?.Value ?? Matrix.Identity;
                double w = b.Width * Math.Abs(m.M11), h = b.Height * Math.Abs(m.M22);
                drops.Add(new(new Rect(b.Center.X - w / 2, b.Center.Y - h / 2, w, h),
                    child.IsEffectivelyEnabled && (child.IsPointerOver || child.IsKeyboardFocusWithin)));
            }
            if (drops.Count > 0) context.Custom(new Blob(new Rect(Bounds.Size).Inflate(8), drops.ToArray(), GetValue(BodyProperty), GetValue(RimProperty)));
        }
    }
    readonly record struct Drop(Rect Area, bool Active);
    sealed class Blob(Rect bounds, Drop[] drops, Color color, Color rim) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => other is Blob b && b.Tone == color && b.Rim == rim && b.Drops.AsSpan().SequenceEqual(drops);
        Color Tone => color; Color Rim => rim; Drop[] Drops => drops;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var shape = new SKPath();
            foreach (var drop in drops)
            {
                var d = drop.Area; float r = (float)Math.Min(d.Width, d.Height) / 2;
                shape.AddRoundRect(new SKRect((float)d.X, (float)d.Y, (float)d.Right, (float)d.Bottom), r, r);
            }
            for (int i = 1; i < drops.Length; i++)
            {
                var a = drops[i - 1]; var b = drops[i];
                if (!a.Active && !b.Active) continue;
                var left = a.Area.X < b.Area.X ? a.Area : b.Area;
                var right = a.Area.X < b.Area.X ? b.Area : a.Area;
                if (right.Left - left.Right > 18 || Math.Abs(left.Center.Y - right.Center.Y) > 8) continue;
                float h = (float)Math.Min(left.Height, right.Height);
                float x0 = (float)(left.Right - left.Width * .20), x1 = (float)(right.Left + right.Width * .20);
                float mid = (float)(left.Right + right.Left) / 2, y = (float)(left.Center.Y + right.Center.Y) / 2;
                float shoulder = h * .36f, neck = h * .15f;
                using var bridge = new SKPath();
                bridge.MoveTo(x0, y - shoulder);
                bridge.CubicTo(mid - 5, y - neck, mid + 5, y - neck, x1, y - shoulder);
                bridge.LineTo(x1, y + shoulder);
                bridge.CubicTo(mid + 5, y + neck, mid - 5, y + neck, x0, y + shoulder);
                bridge.Close();
                // Union removes interior seams and paints the translucent body once.
                using var joined = shape.Op(bridge, SKPathOp.Union);
                if (joined != null) { shape.Reset(); shape.AddPath(joined); }
            }
            using var fill = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
            canvas.DrawPath(shape, fill);
            using var light = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, (float)bounds.Height),
                [new SKColor(rim.R, rim.G, rim.B, rim.A), new SKColor(rim.R, rim.G, rim.B, (byte)(rim.A / 5))], null, SKShaderTileMode.Clamp);
            using var edge = new SKPaint { Shader = light, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawPath(shape, edge);
        }
    }
}

// Shared color arithmetic for the pieces that draw themselves.
internal static class Tones
{
    // Mix two ARGB colors: 0 gives the first, 1 the second.
    public static Color Blend(uint from, uint to, double t)
    {
        Color a = Color.FromUInt32(from), b = Color.FromUInt32(to); t = Math.Clamp(t, 0, 1);
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(Channel(a.A, b.A), Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }
}

// The room behind the glass: brand black or its light inverse, with pools of light for the
// panes to catch. Mix runs from 0 (dark) to 1 (light) so the theme switch can scrub it.
// Wide, faint gradients show steps on an 8-bit screen, and a pool that stops at a hard radius
// shows its edge. So each pool fades along an eased curve, is dithered, and a fine grain is laid
// over the whole room: the steps break up below what the eye can follow.
internal sealed class Backdrop : Control
{
    public static readonly StyledProperty<double> MixProperty = AvaloniaProperty.Register<Backdrop, double>(nameof(Mix));
    static Backdrop() => AffectsRender<Backdrop>(MixProperty);
    public double Mix { get => GetValue(MixProperty); set => SetValue(MixProperty, value); }
    public override void Render(DrawingContext context) => context.Custom(new Room(new Rect(Bounds.Size), Math.Clamp(Mix, 0, 1)));

    sealed record Room(Rect Area, double Mix) : ICustomDrawOperation
    {
        const int Steps = 12;
        static readonly float[] stops = Enumerable.Range(0, Steps + 1).Select(i => i / (float)Steps).ToArray();
        static readonly SKShader grain = Grain();
        public Rect Bounds => Area;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => other is Room room && room == this;
        public void Dispose() { }
        // A tile of faint light and dark specks, the same every run.
        static SKShader Grain()
        {
            const int size = 128;
            using var tile = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var random = new Random(11);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                byte tone = random.Next(2) == 0 ? (byte)0 : (byte)255;
                tile.SetPixel(x, y, new SKColor(tone, tone, tone, (byte)random.Next(0, 9)));
            }
            return SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        }
        void Pool(SKCanvas canvas, SKRect area, Color tone, float x, float y, float radius)
        {
            // Full at the centre, flat again at the rim: no edge where the pool ends.
            var colors = stops.Select(p => new SKColor(tone.R, tone.G, tone.B, (byte)Math.Round(tone.A * (1 - p * p * (3 - 2 * p))))).ToArray();
            using var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), radius, colors, stops, SKShaderTileMode.Clamp, SKMatrix.CreateScale(area.Width, area.Height));
            using var paint = new SKPaint { Shader = shader, IsDither = true };
            canvas.DrawRect(area, paint);
        }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas; var area = new SKRect(0, 0, (float)Area.Width, (float)Area.Height);
            var ground = Tones.Blend(0xFF111111, 0xFFF4F3EE, Mix);
            using (var fill = new SKPaint { Color = new SKColor(ground.R, ground.G, ground.B) }) canvas.DrawRect(area, fill);
            // Yellow light from the top corner where the eyedropper lives; a neutral pool opposite it.
            Pool(canvas, area, Tones.Blend(0x3DFFF000, 0x6BFFF000, Mix), .12f, -.05f, .72f);
            Pool(canvas, area, Tones.Blend(0x1FFFFFFF, 0x1A111111, Mix), .95f, 1.05f, .8f);
            Pool(canvas, area, Tones.Blend(0x17FFF000, 0x38FFF000, Mix), .62f, .6f, .42f);
            using var speck = new SKPaint { Shader = grain };
            canvas.DrawRect(area, speck);
        }
    }
}
