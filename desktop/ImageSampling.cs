using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace PickerHub;

internal sealed partial class MainWindow
{
    byte[]? imageSource;
    PixelSize imageSourceSize;
    bool imagePreviewOnly;
    PixelPoint? sampledPixel;
    string? sampledColor;
    // Where each extracted color lives in the image, and which analysis the guide last animated.
    Point[] imagePoints = [];
    int imageGuideRevision, imageGuideShown = -1;
    // Every color of the picture is shown, rather than the compact view of its five most dominant.
    bool imageShowAll;
    Action? refreshImageSample;
    CancellationTokenSource? imageSampleRequest;
    readonly SemaphoreSlim imageSamplerGate = new(1);

    // The picture that takes a click or the arrow keys, and the line beneath it that reports the pixel.
    (ImageSampler Picture, Control Details) ImageSamplingControls()
    {
        var picker = new ImageSampler(preview!, imageSourceSize, sampledPixel, pixel => _ = Guard(() => SampleImage(pixel)));
        var help = Label(L.T(imagePreviewOnly
            ? "الصورة الملصقة كبيرة، لذا يؤخذ اللون من المعاينة المصغّرة."
            : "انقر على بكسل أو استخدم الأسهم ثم Enter لأخذ لونه."));
        var value = Mono("");
        AutomationProperties.SetLiveSetting(value, AutomationLiveSetting.Polite);
        // The pixel's name, from the same classifier as every other name in the app.
        var name = Label("");
        var copy = Liquid("copy", L.T("نسخ اللون"), () => sampledColor is { } color ? Copy(PickValue(state, color)) : Task.CompletedTask);
        var actions = Liquid("dots", L.T("إجراءات اللون"), () => sampledColor is { } color ? ColorActions(color) : Task.CompletedTask);
        var edit = Liquid("color-picker", L.T("فتح في الملتقط"), () =>
        {
            if (sampledColor is { } color) { page = 0; Select(color); }
        });
        var result = Row(value, name, copy, edit, actions);
        refreshImageSample = () =>
        {
            result.IsVisible = sampledPixel.HasValue;
            bool ready = sampledColor != null;
            copy.IsVisible = edit.IsVisible = actions.IsVisible = name.IsVisible = ready;
            value.Text = sampledColor ?? L.T(imageSampleRequest != null ? "جارٍ أخذ لون البكسل…" : "هذا البكسل شفاف.");
            name.Text = sampledColor is { } color ? Core.Describe(color).Text("name") : "";
        };
        refreshImageSample();
        return (picker, Col(help, result));
    }

    async Task SampleImage(PixelPoint pixel)
    {
        if (imageSource is not { } source || closed) return;
        imageSampleRequest?.Cancel();
        using var request = new CancellationTokenSource();
        imageSampleRequest = request;
        sampledPixel = pixel;
        sampledColor = null;
        refreshImageSample?.Invoke();
        try
        {
            await imageSamplerGate.WaitAsync(request.Token);
            try
            {
                var color = await Task.Run(() => ImageAnalysis.Sample(source, pixel, request.Token), request.Token);
                if (imageSampleRequest == request) sampledColor = color;
            }
            finally { imageSamplerGate.Release(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch
        {
            if (imageSampleRequest == request) sampledPixel = null;
            throw;
        }
        finally
        {
            if (imageSampleRequest == request)
            {
                imageSampleRequest = null;
                refreshImageSample?.Invoke();
            }
        }
    }
}
