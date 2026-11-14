using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace PickerHub;

// Light and dark as one physical switch. A frosted knob slides in a recessed track; its pixel
// moon bursts apart and gathers again as a sun while a rim light travels round the knob.
// Clicking plays the change; dragging the knob scrubs it.
internal sealed class ThemeSwitch : Control
{
    const double W = 80, H = 44, Inset = 4, Knob = H - Inset * 2, Travel = W - Inset * 2 - Knob, Cell = 2;
    static readonly string[] Moon = ["...###...", "..###....", ".###...#.", ".###.....", ".###.....", ".####....", "..######.", "...####..", "........."];
    static readonly string[] Sun = ["....#....", ".#.....#.", "...###...", "..#####..", "#.#####.#", "..#####..", "...###...", ".#.....#.", "....#...."];
    static readonly (Point From, Point To, Vector Burst)[] Pixels = Plan();
    bool running;
    double t, from, to, pressX, pressT;
    DateTime started;
    bool light, dragging, moved, ring;

    public bool Motion { get; set; } = true;
    // Raised when the switch crosses its midpoint: true asks for the light theme.
    public event Action<bool>? Toggled;
    // Raised on every step of the travel, 0 (dark) to 1 (light).
    public event Action<double>? Scrubbed;
    // Raised when the knob has come to rest.
    public event Action? Settled;

    public ThemeSwitch()
    {
        Width = W; Height = H; Focusable = true; FlowDirection = FlowDirection.LeftToRight; Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "تبديل المظهر بين الفاتح والداكن");
        PointerPressed += (_, e) => { e.Pointer.Capture(this); running = false; dragging = true; moved = false; pressX = e.GetPosition(this).X; pressT = t; e.Handled = true; };
        PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            double dx = e.GetPosition(this).X - pressX; if (Math.Abs(dx) > 3) moved = true;
            if (moved) Set(Math.Clamp(pressT + dx / Travel, 0, 1));
        };
        PointerReleased += (_, _) => { if (dragging) Release(); };
        PointerCaptureLost += (_, _) => { if (dragging) Release(); };
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Glide(light ? 0 : 1); } };
    }
    static (Point, Point, Vector)[] Plan()
    {
        static List<Point> Cells(string[] art) { var list = new List<Point>(); for (int y = 0; y < art.Length; y++) for (int x = 0; x < art[y].Length; x++) if (art[y][x] == '#') list.Add(new Point(x, y)); return list; }
        var moon = Cells(Moon); var sun = Cells(Sun); var random = new Random(7);
        return Enumerable.Range(0, Math.Max(moon.Count, sun.Count)).Select(i =>
        {
            double angle = random.NextDouble() * Math.PI * 2, reach = 2.5 + random.NextDouble() * 4;
            return (moon[i % moon.Count], sun[i * 5 % sun.Count], new Vector(Math.Cos(angle), Math.Sin(angle)) * reach);
        }).ToArray();
    }
    // The saved theme, as the window knows it. Mid-gesture the switch keeps its own position.
    public void Sync(bool isLight)
    {
        light = isLight;
        if (!dragging && !running && t != (light ? 1 : 0)) { t = light ? 1 : 0; Scrubbed?.Invoke(t); InvalidateVisual(); }
    }
    void Set(double value)
    {
        bool before = t >= .5; t = value; Scrubbed?.Invoke(t); InvalidateVisual();
        if (t >= .5 != before) Toggled?.Invoke(t >= .5);
    }
    void Release()
    {
        dragging = false;
        Glide(moved ? (t >= .5 ? 1 : 0) : (light ? 0 : 1));
    }
    void Glide(double target)
    {
        if (!Motion) { Set(target); Settle(); return; }
        from = t; to = target; started = DateTime.UtcNow; if (!running) { running = true; Frame(); }
    }
    // One step per display frame, so the knob moves with the screen and not with a timer.
    void Frame()
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(_ => Step());
        else { running = false; Set(to); Settle(); }
    }
    void Step()
    {
        if (!running) return;
        double p = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / (150 + 230 * Math.Abs(to - from)));
        double eased = 1 - Math.Pow(1 - p, 3);
        Set(from + (to - from) * eased);
        if (p >= 1) { running = false; Settle(); } else Frame();
    }
    // If the theme could not be saved, the knob returns to the theme that is really showing.
    void Settle() { if (t != (light ? 1 : 0)) { t = light ? 1 : 0; Scrubbed?.Invoke(t); InvalidateVisual(); } Settled?.Invoke(); }

    static Color Blend(uint dark, uint lightColor, double k)
    {
        Color a = Color.FromUInt32(dark), b = Color.FromUInt32(lightColor);
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * Math.Clamp(k, 0, 1));
        return Color.FromArgb(Channel(a.A, b.A), Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }
    static LinearGradientBrush Fall(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new(top, 0), new(bottom, 1)]
    };
    public override void Render(DrawingContext context)
    {
        // The well: darker at the top lip, with a thin catch of light along the bottom.
        var track = new Rect(0, 0, W, H);
        context.DrawRectangle(Fall(Blend(0xFF000000, 0xFFC9C7BE, t), Blend(0xFF1D1D1B, 0xFFE9E7E0, t)), null, track, 15, 15);
        context.DrawRectangle(null, new Pen(Fall(Blend(0x00FFFFFF, 0x24111111, t), Blend(0x38FFFFFF, 0xFFFFFFFF, t)), 1), track.Deflate(.5), 14.5, 14.5);

        // The rim light starts under the knob in the dark, and climbs round its right side to the top.
        double angle = (90 - 180 * t) * Math.PI / 180;
        var toward = new Vector(Math.Cos(angle), Math.Sin(angle));
        var knob = new Rect(Inset + Travel * t, Inset, Knob, Knob);
        var glow = Blend(0xFFF2F2EA, 0xFFFFF000, t);
        var shadows = new BoxShadows(
            new BoxShadow { OffsetX = toward.X * 4, OffsetY = toward.Y * 4, Blur = 12, Spread = 0, Color = Color.FromArgb(Blend(0x80000000, 0xE6000000, t).A, glow.R, glow.G, glow.B) },
            [new BoxShadow { OffsetX = 0, OffsetY = 3, Blur = 8, Color = Blend(0x99000000, 0x38111111, t) }]);
        context.DrawRectangle(Fall(Blend(0xFF45464A, 0xFFFFFFFF, t), Blend(0xFF2B2C2F, 0xFFEFEEE8, t)), null, knob, 11, 11, shadows);
        var lit = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5 + toward.X * .5, .5 + toward.Y * .5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.5 - toward.X * .5, .5 - toward.Y * .5, RelativeUnit.Relative),
            GradientStops = [new(glow, 0), new(Color.FromArgb(0, glow.R, glow.G, glow.B), .55)]
        };
        context.DrawRectangle(null, new Pen(lit, 1.5), knob.Deflate(.75), 10.25, 10.25);

        // Each pixel leaves the moon, is thrown outward at the midpoint, and lands in the sun.
        double eased = t * t * (3 - 2 * t), burst = Math.Sin(Math.PI * t);
        var ink = new SolidColorBrush(Blend(0xFFF4F4EE, 0xFF111111, t));
        var origin = knob.Center - new Point(Moon.Length * Cell / 2, Moon.Length * Cell / 2);
        foreach (var (start, end, fling) in Pixels)
        {
            var cell = start + (end - start) * eased;
            var at = origin + new Point(cell.X * Cell, cell.Y * Cell) + fling * burst;
            context.DrawRectangle(ink, null, new Rect(Math.Round(at.X * 2) / 2, Math.Round(at.Y * 2) / 2, Cell, Cell));
        }
        if (ring) context.DrawRectangle(null, new Pen(new SolidColorBrush(Blend(0xFFFFFFFF, 0xFF111111, t)), 2), track.Inflate(3), 18, 18);
    }
    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); ring = e.NavigationMethod != NavigationMethod.Pointer; InvalidateVisual(); }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { base.OnLostFocus(e); ring = false; InvalidateVisual(); }
}
