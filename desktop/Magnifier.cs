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
    readonly Window? lens;
    PixelRect lensBounds;
    PixelPoint screenOrigin;
    readonly MagnifierView view;
    PixelPoint pointer, lastPhysical;
    bool hasPoint, hasSample, finished;
    bool selecting;
    long settleUntil;
    nint input;
    PortalInput? portalInput;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    readonly bool portal = Picker.UsesPortal;

    MagnifierSession(Window owner)
    {
        this.owner = owner;
        view = new MagnifierView(pixels, Radius, owner.FontFamily) { Width = MagnifierView.WidthDip, Height = MagnifierView.HeightDip, IsHitTestVisible = false };
        if (!portal) lens = new Window
        {
            Title = "PickerHub Magnifier", SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = MagnifierView.WidthDip,
            Height = MagnifierView.HeightDip, CanResize = false, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, Content = view, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };
        timer.Tick += (_, _) => Run(Tick);
    }

    public static async Task<string?> Start(Window owner)
    {
        var session = new MagnifierSession(owner);
        try
        {
            if (owner.Screens.All.Count == 0) throw new InvalidOperationException("تعذّر تحديد الشاشات المتصلة.");
            if (session.portal)
                session.portalInput = new PortalInput(owner, session.view, session.Move, p => session.Run(() => session.Pick(p)),
                    session.Cancel, session.Zoom, session.Nudge, () => session.Run(() => session.Pick(session.pointer)));
            else
            {
                session.input = NativePicker.pickerhub_input_open();
                if (session.input == 0) throw new InvalidOperationException("تعذّر بدء القطّارة. أغلق أداة الالتقاط الأخرى وأعد المحاولة.");
            }
            if (NativePicker.pickerhub_pointer(out int x, out int y) != 0) session.Move(new PixelPoint(x, y));
            session.timer.Start();
            return await session.result.Task;
        }
        finally
        {
            session.finished = true;
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
        int h = (int)Math.Ceiling(MagnifierView.HeightDip * scale);
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
        hasSample = true;
        view.InvalidateVisual();
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
                if (i >= 90) throw new IOException("تعذّرت قراءة البكسل من الشاشة.");
                await Task.Delay(16);
            }
            if (!finished) { finished = true; result.TrySetResult(view.Hex); }
        }
        catch (Exception ex) { finished = true; result.TrySetException(ex); }
        finally { selecting = false; }
    }
    void Zoom(int direction) { view.Zoom(direction); }
    void Cancel() { if (!finished) { finished = true; result.TrySetResult(null); } }
}

internal sealed class MagnifierView(byte[] pixels, int radius, FontFamily font) : Control
{
    internal const int WidthDip = 228, HeightDip = 310;
    readonly Typeface typeface = new(font);
    int zoom = 12;
    Color Pixel(int x, int y)
    {
        int i = ((y + radius) * (2 * radius + 1) + x + radius) * 4;
        return Color.FromRgb(pixels[i], pixels[i+1], pixels[i+2]);
    }
    public string Hex { get { var c = Pixel(0, 0); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; } }
    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public void Zoom(int direction) { zoom = Math.Clamp(zoom + 2 * direction, 6, 24); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        var color = Pixel(0, 0);
        const double width = WidthDip, height = HeightDip, pad = 14;
        ctx.DrawRectangle(Brush("#172332"), new Pen(Brush("#4B6078"), 1), new Rect(0, 0, width, height), 14, 14);
        const double side = width - 2 * pad;
        int count = (25 - zoom / 2) | 1;
        double cell = side / count;
        int mid = count / 2;
        using (ctx.PushClip(new RoundedRect(new Rect(pad, pad, side, side), 8)))
        {
            for (int j = 0; j < count; j++) for (int i = 0; i < count; i++)
                ctx.DrawRectangle(new SolidColorBrush(Pixel(i-mid,j-mid)), null, new Rect(pad+i*cell,pad+j*cell,cell+.1,cell+.1));
            var grid = new Pen(Brush("#500E1A28"), .6);
            for (int i = 1; i < count; i++)
            {
                ctx.DrawLine(grid, new Point(pad+i*cell,pad), new Point(pad+i*cell,pad+side));
                ctx.DrawLine(grid, new Point(pad,pad+i*cell), new Point(pad+side,pad+i*cell));
            }
        }
        var center = new Rect(pad+mid*cell, pad+mid*cell, cell, cell);
        ctx.DrawRectangle(null, new Pen(Brushes.Black, 3), center);
        ctx.DrawRectangle(null, new Pen(Brushes.White, 1.5), center);
        ctx.DrawRectangle(new SolidColorBrush(color), new Pen(Brush("#B9C8D8"), 1), new Rect(pad,side+26,28,28),6,6);
        Text(ctx, Hex, 20, Brushes.White, 53, side+20);
        var muted = Brush("#BFCDDC");
        Text(ctx, $"RGB  {color.R}, {color.G}, {color.B}", 11, muted, 53, side+52);
        Text(ctx, "Alt+Tab للتبديل   ·   Esc للإلغاء", 11, muted, pad, height-29);
    }
    void Text(DrawingContext ctx,string value,double size,IBrush brush,double x,double y) =>
        ctx.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,typeface,size,brush),new Point(x,y));
}
