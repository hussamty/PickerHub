using Avalonia;
using SkiaSharp;

namespace PickerHub;

internal static class ImageAnalysis
{
    internal sealed record Result(byte[] Preview, string[] Colors, double[] Percentages, PixelSize SourceSize);
    internal static Result Analyze(Stream stream, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException(L.T("تعذّر قراءة الصورة."));
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 40_000_000) throw new InvalidDataException(L.T("الحد الأقصى 40 مليون بكسل."));
        float scale = Math.Min(1, 768f / Math.Max(info.Width, info.Height));
        var decoded = codec.GetScaledDimensions(scale);
        using var srgb = SKColorSpace.CreateSrgb();
        // Codecs such as JPEG can downsample during decode instead of allocating the full image.
        var decodedInfo = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb);
        using var src = SKBitmap.Decode(codec, decodedInfo) ?? throw new InvalidDataException(L.T("الصورة تالفة."));
        cancellation.ThrowIfCancellationRequested();
        using var small = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(info.Width * scale)), Math.Max(1, (int)Math.Round(info.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb));
        if (!src.ScalePixels(small, SKFilterQuality.None)) throw new InvalidDataException(L.T("تعذّر تصغير الصورة."));
        // Allocate, invoke and read on this same worker: the core owns thread-local buffers.
        var result = Core.Extract(small.Bytes);
        cancellation.ThrowIfCancellationRequested();
        if (result.GetArrayLength() == 0) throw new InvalidDataException(L.T("الصورة شفافة بالكامل."));
        var colors = result.EnumerateArray().ToArray();
        using var image = SKImage.FromBitmap(small); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(png.ToArray(), colors.Select(c => c.Text("hex")).ToArray(),
            colors.Select(c => c.GetProperty("percent").GetDouble()).ToArray(), new PixelSize(info.Width, info.Height));
    }

    internal static string? Sample(byte[] data, PixelPoint point, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(data, writable: false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException(L.T("تعذّر قراءة الصورة."));
        var info = codec.Info;
        if (point.X < 0 || point.X >= info.Width || point.Y < 0 || point.Y >= info.Height)
            throw new ArgumentOutOfRangeException(nameof(point));
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb))
            ?? throw new InvalidDataException(L.T("الصورة تالفة."));
        cancellation.ThrowIfCancellationRequested();
        var pixel = bitmap.GetPixel(point.X, point.Y);
        return pixel.Alpha == 0 ? null : $"#{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}";
    }
}
