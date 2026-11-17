using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;

namespace PickerHub;

internal sealed class MagnifierSession
{
    const int Radius = 12;
    readonly byte[] pixels = new byte[(Radius * 2 + 1) * (Radius * 2 + 1) * 4];
    readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Window owner;
    readonly Action<string>? collected;
    readonly Action? firstSample;
    readonly byte[] previousPixels = new byte[(Radius * 2 + 1) * (Radius * 2 + 1) * 4];
    bool painted;
    readonly Window? lens;
    PixelRect lensBounds;
    PixelPoint screenOrigin;
    readonly MagnifierView view;
    PixelPoint pointer, lastPhysical;
    bool hasPoint, hasSample, finished;
    bool selecting;
    long settleUntil;
    readonly long startedAt = Environment.TickCount64;
    nint input;
    PortalInput? portalInput;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly bool portal = Picker.UsesPortal;

    MagnifierSession(Window owner, Action<string>? collected, Action? firstSample)
    {
        this.owner = owner; this.collected = collected;
        this.firstSample = firstSample;
        view = new MagnifierView(pixels, Radius, owner.FontFamily, collected != null) { Width = MagnifierView.WidthDip, IsHitTestVisible = false };
        if (!portal) lens = new Window
        {
            Title = "PickerHub Magnifier", SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = MagnifierView.WidthDip,
            Height = view.LensHeight, CanResize = false, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, Content = view, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };
        timer.Tick += (_, _) => Run(Tick);
    }

    public static async Task<string?> Start(Window owner, Action<string>? collected = null, Action? firstSample = null)
    {
        var session = new MagnifierSession(owner, collected, firstSample);
        void OwnerClosed(object? sender, EventArgs e) => session.Cancel();
        owner.Closed += OwnerClosed;
        try
        {
            if (owner.Screens.All.Count == 0) throw new InvalidOperationException(L.T("تعذّر تحديد الشاشات المتصلة."));
            if (session.portal)
                session.portalInput = new PortalInput(owner, session.view, session.Move, p => session.Run(() => session.Pick(p)),
                    session.Cancel, session.Zoom, session.Nudge, () => session.Run(() => session.Pick(session.pointer)));
            else
            {
                session.input = NativePicker.pickerhub_input_open();
                if (session.input == 0) throw new InvalidOperationException(L.T("تعذّر بدء القطّارة. أغلق أداة الالتقاط الأخرى وأعد المحاولة."));
            }
            if (NativePicker.pickerhub_pointer(out int x, out int y) != 0) session.Move(new PixelPoint(x, y));
            session.timer.Start();
            return await session.result.Task;
        }
        finally
        {
            session.finished = true;
            owner.Closed -= OwnerClosed;
            session.timer.Stop();
            session.portalInput?.Dispose();
            if (session.input != 0) NativePicker.pickerhub_input_close(session.input);
            session.lens?.Close();
        }
    }
    void Run(Action action)
    {
        if (finished) return;
        try { action(); }
        catch (Exception ex) { finished = true; result.TrySetException(ex); }
    }
    void Tick()
    {
        if (!painted && Environment.TickCount64 - startedAt > 5000)
            throw new IOException(L.T("لم يصل إطار من الشاشة. أعد فتح القطّارة أو استخدم منتقي النظام."));
        if (!portal)
        {
            int action;
            while (!finished && (action = NativePicker.pickerhub_input_poll(input, out int x, out int y)) != 0)
            {
                switch (action)
                {
                    case 1: Pick(new PixelPoint(x, y)); break;
                    case 2: Cancel(); break;
                    case 3: Zoom(1); break;
                    case 4: Zoom(-1); break;
                    case 5: Pick(pointer); break;
                    case 6: Nudge(-1,0); break;
                    case 7: Nudge(1,0); break;
                    case 8: Nudge(0,-1); break;
                    case 9: Nudge(0,1); break;
                }
            }
            if (finished) return;
            if (NativePicker.pickerhub_pointer(out int px, out int py) != 0) Move(new PixelPoint(px, py));
        }
        if (hasPoint) Sample();
        view.Step();
    }
    void Move(PixelPoint p)
    {
        if (selecting || (hasPoint && lastPhysical == p)) return;
        lastPhysical = p;
        hasPoint = true;
        PositionAt(p);
    }
    void Nudge(int dx, int dy)
    {
        if (!hasPoint) return;
        var p = new PixelPoint(pointer.X + dx, pointer.Y + dy);
        if (owner.Screens.All.Any(s => s.Bounds.Contains(p))) PositionAt(p);
    }
    void PositionAt(PixelPoint p)
    {
        var screen = owner.Screens.ScreenFromPoint(p);
        if (screen == null) return;
        var bounds = screen.Bounds;
        var scale = screen.Scaling;
        int w = (int)Math.Ceiling(MagnifierView.WidthDip * scale);
        int h = (int)Math.Ceiling(view.LensHeight * scale);
        int gap = (int)Math.Ceiling(24 * scale);
        int Place(int pos, int size, int start, int end) =>
            Math.Clamp(pos + gap + size <= end ? pos + gap : pos - gap - size, start, Math.Max(start, end - size));
        PixelRect? oldLens = portalInput?.LensBounds;
        if (lens?.IsVisible == true) oldLens = new PixelRect(lens.Position, PixelSize.FromSize(lens.ClientSize, lens.RenderScaling));
        if (oldLens is { } visible && visible.Intersects(new PixelRect(p.X-Radius, p.Y-Radius, Radius*2+1, Radius*2+1)))
        {
            // Allow an exposed pixel to reach the stream before sampling it.
            lens?.Hide();
            portalInput?.HideLens();
            if (portal) WaylandCapture.DiscardFrame(screen.Bounds);
            settleUntil = Environment.TickCount64 + (portal ? 80 : 20);
        }
        pointer = p;
        hasSample = false;
        screenOrigin = bounds.Position;
        lensBounds = new PixelRect(Place(p.X, w, bounds.X, bounds.Right), Place(p.Y, h, bounds.Y, bounds.Bottom), w, h);
        if (lens != null) lens.Position = lensBounds.Position;
    }
    bool Sample()
    {
        if (Environment.TickCount64 < settleUntil) return false;
        bool read = portal ? WaylandCapture.Sample(owner, pointer, Radius, pixels)
            : NativePicker.pickerhub_sample(pointer.X, pointer.Y, Radius, pixels) != 0;
        if (!read) return false;
        if (!painted) firstSample?.Invoke();
        hasSample = true;
        if (!painted || !pixels.AsSpan().SequenceEqual(previousPixels))
        {
            pixels.CopyTo(previousPixels, 0); painted = true; view.InvalidateVisual();
        }
        if (portalInput != null) portalInput.ShowLens(lensBounds, screenOrigin);
        else if (lens != null && !lens.IsVisible) lens.Show();
        return true;
    }
    async void Pick(PixelPoint p)
    {
        if (finished || selecting) return;
        try
        {
            if (!hasPoint || (p != lastPhysical && p != pointer)) Move(p);
            selecting = true;
            // Wait only for the exposed region or a first frame; Esc stays responsive.
            for (int i = 0; !finished && !Sample() && !hasSample; i++)
            {
                if (i >= 90) throw new IOException(L.T("تعذّرت قراءة البكسل من الشاشة."));
                await Task.Delay(16);
            }
            if (!finished)
            {
                if (collected != null) { collected(view.Hex); view.Collected(view.Hex); }
                else { finished = true; result.TrySetResult(view.Hex); }
            }
        }
        catch (Exception ex) { finished = true; result.TrySetException(ex); }
        finally { selecting = false; }
    }
    void Zoom(int direction) { view.Zoom(direction); }
    void Cancel() { if (!finished) { finished = true; result.TrySetResult(null); } }
}

// The lens: a block of brand black holding the magnified pixels, the pixel under the pointer
// framed in signal yellow, and its value beneath. It is drawn opaque so it reads the same with
// or without a compositor.
internal sealed class MagnifierView(byte[] pixels, int radius, FontFamily font, bool multiple = false) : Control
{
    internal const int WidthDip = 232, HeightDip = 314;
    internal int LensHeight => multiple ? 390 : HeightDip;
    readonly Queue<string> recent = new();
    internal int Count { get; private set; }
    public void Collected(string hex)
    {
        Count++; recent.Enqueue(hex); if (recent.Count > 6) recent.Dequeue();
        InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize) => new(WidthDip, LensHeight);
    const double Pad = 14, Side = WidthDip - 2 * Pad;
    readonly Typeface face = new(font), bold = new(font, FontStyle.Normal, FontWeight.Bold);
    readonly Typeface code = new(new FontFamily("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Menlo, Consolas, monospace"), FontStyle.Normal, FontWeight.Bold);
    static readonly IBrush Body = Brush("#111111"), Rim = Brush("#3DFFFFFF"), Ink = Brushes.White, Muted = Brush("#ADADA5"), Signal = Brush("#FFF000"), Well = Brush("#1C1C1A");
    int zoom = 12;
    double shown = 12;
    Color Pixel(int x, int y)
    {
        int i = ((y + radius) * (2 * radius + 1) + x + radius) * 4;
        return Color.FromRgb(pixels[i], pixels[i+1], pixels[i+2]);
    }
    public string Hex { get { var c = Pixel(0, 0); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; } }
    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public void Zoom(int direction) => zoom = Math.Clamp(zoom + 2 * direction, 6, 24);
    // Called once per frame: the magnification glides to its new level instead of jumping.
    public void Step()
    {
        if (Math.Abs(zoom - shown) < .02) { if (shown != zoom) { shown = zoom; InvalidateVisual(); } return; }
        shown += (zoom - shown) * .28; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var color = Pixel(0, 0);
        ctx.DrawRectangle(Body, null, new Rect(0, 0, WidthDip, LensHeight), 24, 24);
        ctx.DrawRectangle(null, new Pen(Rim, 1), new Rect(.5, .5, WidthDip - 1, LensHeight - 1), 23.5, 23.5);

        // The number of pixels across is fractional while the zoom glides, so cells are laid out from the centre.
        double across = 25 - shown / 2, cell = Side / across, centre = Pad + Side / 2;
        var well = new Rect(Pad, Pad, Side, Side);
        using (ctx.PushClip(new RoundedRect(well, 14)))
        {
            ctx.DrawRectangle(Well, null, well);
            for (int j = -radius; j <= radius; j++) for (int i = -radius; i <= radius; i++)
            {
                double x = centre + (i - .5) * cell, y = centre + (j - .5) * cell;
                if (x > Pad + Side || y > Pad + Side || x + cell < Pad || y + cell < Pad) continue;
                ctx.DrawRectangle(new SolidColorBrush(Pixel(i, j)), null, new Rect(x, y, cell + .5, cell + .5));
            }
            if (cell >= 9)
            {
                var grid = new Pen(Brush("#26111111"), 1);
                for (int i = -radius; i <= radius + 1; i++)
                {
                    double at = Math.Round(centre + (i - .5) * cell) + .5;
                    ctx.DrawLine(grid, new Point(at, Pad), new Point(at, Pad + Side)); ctx.DrawLine(grid, new Point(Pad, at), new Point(Pad + Side, at));
                }
            }
        }
        ctx.DrawRectangle(null, new Pen(Rim, 1), well.Deflate(.5), 13.5, 13.5);
        // The chosen pixel: yellow between two black lines, so it shows on any color.
        var target = new Rect(centre - cell / 2, centre - cell / 2, cell, cell);
        ctx.DrawRectangle(null, new Pen(Brushes.Black, 1), target.Inflate(3), 4, 4);
        ctx.DrawRectangle(null, new Pen(Signal, 2), target.Inflate(1.5), 3, 3);
        ctx.DrawRectangle(null, new Pen(Brushes.Black, 1), target, 1.5, 1.5);

        double row = Pad + Side + 16;
        ctx.DrawEllipse(new SolidColorBrush(color), new Pen(Ink, 2), new Point(Pad + 19, row + 19), 18, 18);
        Draw(ctx, Hex, code, 20, Ink, Pad + 50, row - 3);
        Draw(ctx, $"RGB {color.R}, {color.G}, {color.B}", code, 11, Muted, Pad + 50, row + 23);
        // Keys, as small caps beside what they do.
        if (multiple)
        {
            ctx.DrawLine(new Pen(Rim, 1), new Point(Pad, 283), new Point(WidthDip - Pad, 283));
            var label = Count == 0 ? L.T("كل نقرة تضيف لوناً إلى اللوحة") : L.T("أُضيف إلى اللوحة: ") + Count;
            var feedback = new FormattedText(label, CultureInfo.InvariantCulture, L.Flow, bold, 12, Count == 0 ? Muted : Signal);
            ctx.DrawText(feedback, new Point((WidthDip - feedback.Width) / 2, 291));
            int n = 0;
            foreach (var hex in recent)
            {
                ctx.DrawRectangle(Brush(hex), new Pen(Rim, 1), new Rect(Pad + n * 34, 321, 28, 22), 7, 7); n++;
            }
        }
        double hint = LensHeight - 30, x0 = WidthDip - Pad;
        x0 = Key(ctx, "Esc", L.T(multiple ? "إنهاء" : "إلغاء"), x0, hint); Key(ctx, "+ −", L.T("تكبير"), x0 - 12, hint);
    }
    void Draw(DrawingContext ctx, string value, Typeface type, double size, IBrush brush, double x, double y) =>
        ctx.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, type, size, brush), new Point(x, y));
    // Draws "label [key]" ending at the given right edge; returns where it started.
    double Key(DrawingContext ctx, string key, string label, double right, double y)
    {
        var cap = new FormattedText(key, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold, 10, Ink);
        var text = new FormattedText(label, CultureInfo.InvariantCulture, L.Flow, face, 11, Muted);
        double capWidth = cap.Width + 12, left = right - text.Width - 6 - capWidth;
        ctx.DrawText(text, new Point(right - text.Width, y + 1));
        ctx.DrawRectangle(Brush("#24FFFFFF"), null, new Rect(left, y, capWidth, 20), 6, 6);
        ctx.DrawText(cap, new Point(left + 6, y + 3));
        return left;
    }
}
