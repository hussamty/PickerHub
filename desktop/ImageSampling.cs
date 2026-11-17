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
    Action? refreshImageSample;
    CancellationTokenSource? imageSampleRequest;
    readonly SemaphoreSlim imageSamplerGate = new(1);

    Control ImageSamplingControls()
    {
        var picker = new ImageSampler(preview!, imageSourceSize, sampledPixel, pixel => _ = Guard(() => SampleImage(pixel)));
        var picture = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = picker };
        var help = Label(L.T(imagePreviewOnly
            ? "الصورة الملصقة كبيرة، لذا يؤخذ اللون من المعاينة المصغّرة."
            : "انقر على بكسل أو استخدم الأسهم ثم Enter لأخذ لونه."));
        var value = Mono("");
        AutomationProperties.SetLiveSetting(value, AutomationLiveSetting.Polite);
        var copy = Liquid("copy", L.T("نسخ اللون"), () => sampledColor is { } color ? Copy(PickValue(state, color)) : Task.CompletedTask);
        var actions = Liquid("dots", L.T("إجراءات اللون"), () => sampledColor is { } color ? ColorActions(color) : Task.CompletedTask);
        var edit = Liquid("color-picker", L.T("فتح في الملتقط"), () =>
        {
            if (sampledColor is { } color) { page = 0; Select(color); }
        });
        var result = Row(value, copy, edit, actions);
        refreshImageSample = () =>
        {
            result.IsVisible = sampledPixel.HasValue;
            bool ready = sampledColor != null;
            copy.IsVisible = edit.IsVisible = actions.IsVisible = ready;
            value.Text = sampledColor ?? L.T(imageSampleRequest != null ? "جارٍ أخذ لون البكسل…" : "هذا البكسل شفاف.");
        };
        refreshImageSample();
        return Col(picture, help, result);
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
