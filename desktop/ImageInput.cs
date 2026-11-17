using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace PickerHub;

internal sealed partial class MainWindow
{
    const int ImageByteLimit = 32 << 20;
    bool analyzing;
    CancellationTokenSource? analysisCancellation;
    readonly Border imageDropCue = new() { IsVisible = false, IsHitTestVisible = false, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(2), Margin = new Thickness(12) };
    static bool ImageFile(IStorageItem file) => file is IStorageFile && Path.GetExtension(file.Name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
    static bool ImageData(IDataTransfer data) => data.Contains(DataFormat.Bitmap) || data.TryGetFiles() is { Length: 1 } files && ImageFile(files[0]);

    void SetupImageInput()
    {
        imageDropCue[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentText");
        if (Content is Panel room) { Grid.SetRow(imageDropCue, 1); room.Children.Add(imageDropCue); }
        DragDrop.SetAllowDrop(content, true);
        content.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            bool accept = page == 3 && !analyzing && !OverlayOpen && ImageData(e.DataTransfer);
            e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            imageDropCue.IsVisible = accept; e.Handled = true;
        });
        content.AddHandler(DragDrop.DragLeaveEvent, (_, _) => imageDropCue.IsVisible = false);
        content.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            imageDropCue.IsVisible = false;
            if (page != 3 || analyzing || OverlayOpen) return;
            e.Handled = true; e.DragEffects = DragDropEffects.Copy;
            // Snapshot borrowed drag pixels before the event's data transfer expires.
            if (e.DataTransfer.TryGetBitmap() is { } bitmap)
            {
                try { var bytes = ImagePixels(bitmap); _ = Guard(() => ImportImage(() => Task.FromResult<Stream>(new MemoryStream(bytes)), L.T("صورة ملصقة"))); }
                catch (Exception ex) { ShowError(ex.Message); }
            }
            else if (e.DataTransfer.TryGetFiles() is { Length: 1 } files && files[0] is IStorageFile file && ImageFile(file))
                _ = Guard(() => ImportImage(file.OpenReadAsync, file.Name));
            else ShowError(L.T("اختر صورة واحدة بصيغة PNG أو JPG أو WEBP."));
        });
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (page != 3 || e.Key != Key.V || e.KeyModifiers != modifier || OverlayOpen) return;
            if (FocusManager?.GetFocusedElement() is Visual focused && (focused is TextBox || focused.GetVisualAncestors().Any(v => v is TextBox))) return;
            e.Handled = true; _ = Guard(PasteImage);
        }, RoutingStrategies.Tunnel);
    }

    async Task Analyze()
    {
        if (analyzing) return;
        var files = await OpenFiles(new() { Title = L.T("اختيار صورة"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG · JPG · WEBP") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }] });
        if (files.Count > 0) await ImportImage(files[0].OpenReadAsync, files[0].Name);
    }

    async Task PasteImage()
    {
        if (analyzing) return;
        if (Clipboard == null) throw new InvalidOperationException(L.T("الحافظة غير متاحة."));
        // In-process values belong to the clipboard owner. Disposing a borrowed
        // bitmap makes the next paste fail; only remote decoded pixels are ours.
        var local = await Clipboard.TryGetInProcessDataAsync();
        using var remote = local == null ? await Clipboard.TryGetDataAsync() : null;
        var data = local ?? remote;
        if (data != null && await data.TryGetBitmapAsync() is { } bitmap)
        {
            byte[] bytes;
            try { bytes = ImagePixels(bitmap); }
            finally { if (local == null) bitmap.Dispose(); }
            await ImportImage(() => Task.FromResult<Stream>(new MemoryStream(bytes)), L.T("صورة ملصقة"));
            return;
        }
        if (data != null && await data.TryGetFilesAsync() is { Length: 1 } files && files[0] is IStorageFile file && ImageFile(file))
        { await ImportImage(file.OpenReadAsync, file.Name); return; }
        status.Text = L.T("لا توجد صورة في الحافظة. انسخ صورة أو ملف صورة أولاً.");
    }

    static byte[] ImagePixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width < 1 || size.Height < 1 || (long)size.Width * size.Height > 40_000_000)
            throw new InvalidDataException(L.T("الحد الأقصى 40 مليون بكسل."));
        double scale = Math.Min(1, 768d / Math.Max(size.Width, size.Height));
        using var sample = bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))), BitmapInterpolationMode.None);
        using var bytes = new MemoryStream(); sample.Save(bytes); return bytes.ToArray();
    }

    async Task ImportImage(Func<Task<Stream>> open, string name)
    {
        if (analyzing) return;
        analyzing = true;
        using var cancellation = new CancellationTokenSource(); analysisCancellation = cancellation;
        var progress = Dialog(L.T("تحليل الصورة"));
        progress.Content = Col(Text(L.T("جارٍ تحليل الصورة…")), new ProgressBar { IsIndeterminate = true }, Btn(L.T("إلغاء"), () => progress.Close()));
        bool finished = false;
        async Task WatchClose() { await progress.Show(); if (!finished) cancellation.Cancel(); }
        var visible = WatchClose();
        status.Text = L.T("جارٍ تحليل الصورة…");
        try
        {
            using var source = await open();
            if (source.CanSeek && source.Length > ImageByteLimit) throw new InvalidDataException(L.T("اختر صورة أصغر من 32 ميغابايت."));
            using var bytes = new MemoryStream(); var buffer = new byte[65536]; int read;
            while ((read = await source.ReadAsync(buffer, cancellation.Token)) > 0)
            {
                if (bytes.Length + read > ImageByteLimit) throw new InvalidDataException(L.T("اختر صورة أصغر من 32 ميغابايت."));
                bytes.Write(buffer, 0, read);
            }
            bytes.Position = 0;
            var result = await Task.Run(() => ImageAnalysis.Analyze(bytes, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed) return;
            using var png = new MemoryStream(result.Preview); var next = new Bitmap(png);
            var previous = preview; preview = next;
            recentImage = result.Colors; percentages = result.Percentages;
            imageName = Path.GetFileNameWithoutExtension(name); if (imageName.Length == 0) imageName = L.T("لوحة الصورة"); if (imageName.Length > 100) imageName = imageName[..100];
            if (page == 3) Render(); previous?.Dispose();
            status.Text = L.T("استُخرجت ألوان الصورة");
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("أُلغي التحليل"); }
        finally { finished = true; progress.Close(); await visible; analysisCancellation = null; analyzing = false; }
    }
}
