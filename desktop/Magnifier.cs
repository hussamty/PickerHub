using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PickerHub;

internal sealed class DesktopFrame : IDisposable
{
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Left { get; }
    public int Top { get; }
    public bool FromPortal { get; init; }
    public WriteableBitmap Image { get; }
    public DesktopFrame(byte[] rgba, int width, int height, int left, int top)
    {
        Pixels = rgba; Width = width; Height = height; Left = left; Top = top;
        Image = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = Image.Lock();
        for (int y = 0; y < height; y++) Marshal.Copy(rgba, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);
    }
    public static DesktopFrame FromFile(string path)
    {
        try
        {
            using var input = File.OpenRead(path);
            using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("تعذّرت قراءة لقطة الشاشة.");
            var info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 150_000_000) throw new InvalidDataException("أبعاد لقطة الشاشة غير مدعومة.");
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("لقطة الشاشة غير مكتملة.");
            return new DesktopFrame(bitmap.Bytes, info.Width, info.Height, 0, 0) { FromPortal = true };
        }
        finally
        {
            // The portal creates this temporary file for this request.
            if (Path.GetFullPath(path).StartsWith(Path.GetTempPath(), StringComparison.Ordinal)) File.Delete(path);
        }
    }
    public Color Pixel(int x, int y)
    {
        int i = (Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)) * 4;
        return Color.FromRgb(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }
    public void Dispose() { Image.Dispose(); Array.Clear(Pixels); }
}

internal sealed class MagnifierSession
{
    readonly List<Window> windows = [];
    readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool finished;
    public static async Task<string?> Start(Window owner, DesktopFrame frame)
    {
        var session = new MagnifierSession();
        var screens = owner.Screens.All;
        if (screens.Count == 0) throw new InvalidOperationException("تعذّر تحديد الشاشات المتصلة.");
        int left = frame.FromPortal ? screens.Min(s => s.Bounds.X) : frame.Left;
        int top = frame.FromPortal ? screens.Min(s => s.Bounds.Y) : frame.Top;
        int width = frame.FromPortal ? screens.Max(s => s.Bounds.Right) - left : frame.Width;
        int height = frame.FromPortal ? screens.Max(s => s.Bounds.Bottom) - top : frame.Height;
        try
        {
            foreach (var screen in screens)
            {
                var view = new MagnifierView(frame, screen.Bounds, left, top, width, height, owner.FontFamily, session.Complete);
                var overlay = new Window
                {
                    Title = "PickerHub Magnifier",
                    SystemDecorations = SystemDecorations.None,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = screen.Bounds.Position,
                    Width = screen.Bounds.Width / screen.Scaling,
                    Height = screen.Bounds.Height / screen.Scaling,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Content = view,
                    Background = Brushes.Black
                };
                session.windows.Add(overlay);
                overlay.Closed += (_, _) => session.Complete(null);
                overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; session.Complete(null); } };
                overlay.Show();
                overlay.Position = screen.Bounds.Position;
                overlay.WindowState = WindowState.FullScreen;
            }
            return await session.result.Task;
        }
        finally { session.Complete(null); }
    }
    void Complete(string? hex)
    {
        if (finished) return; finished = true;
        foreach (var window in windows) window.Close();
        result.TrySetResult(hex);
    }
}

internal sealed class MagnifierView : Control
{
    readonly DesktopFrame frame;
    readonly PixelRect screen;
    readonly int left, top, desktopWidth, desktopHeight;
    readonly Typeface typeface;
    readonly Action<string?> complete;
    Point pointer;
    bool active;
    int zoom = 12;
    int X => Math.Clamp((int)Math.Floor(((screen.X - left) + pointer.X / Bounds.Width * screen.Width) * frame.Width / desktopWidth), 0, frame.Width - 1);
    int Y => Math.Clamp((int)Math.Floor(((screen.Y - top) + pointer.Y / Bounds.Height * screen.Height) * frame.Height / desktopHeight), 0, frame.Height - 1);
    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public MagnifierView(DesktopFrame frame, PixelRect screen, int left, int top, int width, int height, FontFamily font, Action<string?> complete)
    {
        this.frame = frame; this.screen = screen; this.left = left; this.top = top; desktopWidth = width; desktopHeight = height;
        this.complete = complete; typeface = new Typeface(font);
        Focusable = true; FlowDirection = FlowDirection.LeftToRight; Cursor = new Cursor(StandardCursorType.Cross);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        PointerEntered += (_, e) => { active = true; pointer = e.GetPosition(this); Focus(); InvalidateVisual(); };
        PointerMoved += (_, e) => { active = true; pointer = e.GetPosition(this); InvalidateVisual(); };
        PointerExited += (_, _) => { active = false; InvalidateVisual(); };
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right) { complete(null); return; }
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            pointer = e.GetPosition(this); var c = frame.Pixel(X, Y); complete($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        };
        PointerWheelChanged += (_, e) => { zoom = Math.Clamp(zoom + (e.Delta.Y > 0 ? 2 : -2), 6, 24); e.Handled = true; InvalidateVisual(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { var c = frame.Pixel(X, Y); complete($"#{c.R:X2}{c.G:X2}{c.B:X2}"); e.Handled = true; return; }
            int dx = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0, dy = e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0;
            if (dx == 0 && dy == 0) return;
            pointer = new Point(Math.Clamp(pointer.X + dx * desktopWidth / (double)frame.Width * Bounds.Width / screen.Width, 0, Bounds.Width - 1),
                Math.Clamp(pointer.Y + dy * desktopHeight / (double)frame.Height * Bounds.Height / screen.Height, 0, Bounds.Height - 1));
            active = true; e.Handled = true; InvalidateVisual();
        };
    }
    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var src = new Rect((screen.X - left) * (double)frame.Width / desktopWidth, (screen.Y - top) * (double)frame.Height / desktopHeight,
            screen.Width * (double)frame.Width / desktopWidth, screen.Height * (double)frame.Height / desktopHeight);
        ctx.DrawImage(frame.Image, src, new Rect(Bounds.Size));
        if (!active) return;
        var color = frame.Pixel(X, Y); string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        const double width = 228, height = 310, pad = 14;
        double lx = pointer.X + width + 28 < Bounds.Width ? pointer.X + 24 : pointer.X - width - 24;
        double ly = pointer.Y + height + 28 < Bounds.Height ? pointer.Y + 24 : pointer.Y - height - 24;
        lx = Math.Clamp(lx, 4, Math.Max(4, Bounds.Width - width - 4)); ly = Math.Clamp(ly, 4, Math.Max(4, Bounds.Height - height - 4));
        var outer = new Rect(lx, ly, width, height);
        ctx.DrawRectangle(Brush("#172332"), new Pen(Brush("#4B6078"), 1), outer, 14, 14);
        double gx = lx + pad, gy = ly + pad, side = width - 2 * pad;
        int count = 13; double cell = side / count;
        // Zoom level controls the number of source pixels visible in the lens.
        count = Math.Max(7, 25 - zoom / 2); if (count % 2 == 0) count++;
        cell = side / count; int radius = count / 2;
        using (ctx.PushClip(new RoundedRect(new Rect(gx, gy, side, side), 8)))
        {
            for (int j = 0; j < count; j++) for (int i = 0; i < count; i++)
            {
                var c = frame.Pixel(X + i - radius, Y + j - radius);
                ctx.DrawRectangle(new SolidColorBrush(c), null, new Rect(gx + i * cell, gy + j * cell, cell + 0.1, cell + 0.1));
            }
            var gridPen = new Pen(Brush("#500E1A28"), .6);
            for (int i = 1; i < count; i++) { ctx.DrawLine(gridPen, new Point(gx + i * cell, gy), new Point(gx + i * cell, gy + side)); ctx.DrawLine(gridPen, new Point(gx, gy + i * cell), new Point(gx + side, gy + i * cell)); }
        }
        var selected = new Rect(gx + radius * cell, gy + radius * cell, cell, cell);
        ctx.DrawRectangle(null, new Pen(Brushes.Black, 3), selected); ctx.DrawRectangle(null, new Pen(Brushes.White, 1.5), selected);
        ctx.DrawRectangle(new SolidColorBrush(color), new Pen(Brush("#B9C8D8"), 1), new Rect(lx + 14, ly + side + 26, 28, 28), 6, 6);
        DrawText(ctx, hex, 20, Brushes.White, new Point(lx + 53, ly + side + 20));
        DrawText(ctx, $"RGB  {color.R}, {color.G}, {color.B}", 11, Brush("#BFCDDC"), new Point(lx + 53, ly + side + 52));
        DrawText(ctx, "انقر للاختيار   •   Esc للإلغاء", 11, Brush("#BFCDDC"), new Point(lx + 14, ly + height - 29));
        // Keep the exact source pixel visible even while moving by keyboard.
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 3), pointer, 4, 4);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 1), pointer, 4, 4);
    }
    void DrawText(DrawingContext ctx, string value, double size, IBrush brush, Point point)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
        ctx.DrawText(text, point);
    }
}
