using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PickerHub;

internal sealed class ImageSampler : Control
{
    readonly Bitmap preview;
    readonly PixelSize source;
    readonly Action<PixelPoint> choose;
    PixelPoint? cursor;

    static ImageSampler() => AffectsRender<ImageSampler>(IsFocusedProperty);

    public ImageSampler(Bitmap preview, PixelSize source, PixelPoint? selected, Action<PixelPoint> choose)
    {
        this.preview = preview;
        this.source = source;
        this.choose = choose;
        cursor = selected;
        Focusable = true;
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = new Cursor(StandardCursorType.Cross);
        MaxHeight = 360;
        AutomationProperties.SetName(this, L.T("اختيار بكسل من الصورة"));
    }

    internal static Rect ImageBounds(Size viewport, Size bitmap)
    {
        double scale = Math.Min(viewport.Width / bitmap.Width, viewport.Height / bitmap.Height);
        double width = bitmap.Width * scale, height = bitmap.Height * scale;
        return new Rect((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
    }

    internal static PixelPoint? PixelAt(Point point, Rect image, PixelSize source)
    {
        if (point.X < image.X || point.Y < image.Y || point.X >= image.Right || point.Y >= image.Bottom) return null;
        return new PixelPoint(Math.Clamp((int)((point.X - image.X) / image.Width * source.Width), 0, source.Width - 1),
            Math.Clamp((int)((point.Y - image.Y) / image.Height * source.Height), 0, source.Height - 1));
    }

    protected override Size MeasureOverride(Size available)
    {
        double scale = Math.Min(1, Math.Min(available.Width / preview.Size.Width, available.Height / preview.Size.Height));
        return new Size(preview.Size.Width * scale, preview.Size.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        var image = ImageBounds(Bounds.Size, preview.Size);
        if (image.Width <= 0 || image.Height <= 0) return;
        context.DrawImage(preview, new Rect(preview.Size), image);
        if (IsFocused) context.DrawRectangle(null, new Pen(Brushes.White, 2), image.Deflate(2));
        if (cursor is not { } pixel) return;
        var point = new Point(image.X + image.Width * (pixel.X + .5) / source.Width, image.Y + image.Height * (pixel.Y + .5) / source.Height);
        context.DrawEllipse(null, new Pen(Brushes.Black, 4), point, 6, 6);
        context.DrawEllipse(null, new Pen(Brushes.White, 2), point, 6, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            PixelAt(e.GetPosition(this), ImageBounds(Bounds.Size, preview.Size), source) is not { } pixel) return;
        Focus();
        cursor = pixel;
        InvalidateVisual();
        choose(pixel);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var pixel = cursor ?? new PixelPoint(source.Width / 2, source.Height / 2);
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 16 : 1;
        switch (e.Key)
        {
            case Key.Left: pixel = new PixelPoint(Math.Max(0, pixel.X - step), pixel.Y); break;
            case Key.Right: pixel = new PixelPoint(Math.Min(source.Width - 1, pixel.X + step), pixel.Y); break;
            case Key.Up: pixel = new PixelPoint(pixel.X, Math.Max(0, pixel.Y - step)); break;
            case Key.Down: pixel = new PixelPoint(pixel.X, Math.Min(source.Height - 1, pixel.Y + step)); break;
            case Key.Enter: choose(pixel); break;
            default: return;
        }
        cursor = pixel;
        InvalidateVisual();
        e.Handled = true;
    }
}
