using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PickerHub;

internal sealed partial class MainWindow
{
    async Task Analyze()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "اختيار صورة", FileTypeFilter = [new FilePickerFileType("PNG · JPG · WEBP") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }] }); if (files.Count == 0) return;
        using var stream = await files[0].OpenReadAsync();
        if (stream.CanSeek && stream.Length > 32L << 20) throw new InvalidDataException("اختر صورة أصغر من 32 ميغابايت.");
        var (data, rgba) = await Task.Run(() =>
        {
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("تعذّر قراءة الصورة.");
            var i = codec.Info; if (i.Width < 1 || i.Height < 1 || (long)i.Width * i.Height > 40_000_000) throw new InvalidDataException("الحد الأقصى 40 مليون بكسل.");
            using var src = SKBitmap.Decode(codec) ?? throw new InvalidDataException("الصورة تالفة.");
            double scale = Math.Min(1, 768d / Math.Max(i.Width, i.Height));
            using var small = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(i.Width * scale)), Math.Max(1, (int)Math.Round(i.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (!src.ScalePixels(small, SKFilterQuality.None)) throw new InvalidDataException("تعذّر تصغير الصورة.");
            using var image = SKImage.FromBitmap(small); using var png = image.Encode(SKEncodedImageFormat.Png, 100); return (png.ToArray(), small.Bytes);
        });
        var result = Core.Extract(rgba); if (result.GetArrayLength() == 0) throw new InvalidDataException("الصورة شفافة بالكامل.");
        var colors = result.EnumerateArray().ToArray(); recentImage = colors.Select(c => c.Text("hex")).ToArray(); percentages = colors.Select(c => c.GetProperty("percent").GetDouble()).ToArray();
        imageName = Path.GetFileNameWithoutExtension(files[0].Name); if (imageName.Length == 0) imageName = "لوحة الصورة"; if (imageName.Length > 100) imageName = imageName[..100];
        preview?.Dispose(); using var ms = new MemoryStream(data); preview = new Bitmap(ms); Render();
    }
    void Images()
    {
        content.Children.Add(Btn("اختيار صورة   PNG · JPG · WEBP", Analyze, true)); if (preview == null) return;
        content.Children.Add(new Image { Source = preview, MaxHeight = 190, Stretch = Stretch.Uniform });
        Palette Make() => new() { Name = imageName, Colors = recentImage.Select(Swatch.From).ToList() };
        content.Children.Add(Row(Text(imageName, 18, true), Btn("حفظ كلوحة", async () => { var p = Make(); var name = await Prompt("اسم اللوحة", p.Name); if (name != null) { p.Name = name; Edit(() => { state.Palettes.Add(p); paletteId = p.Id; }); } }), Btn("تصدير", () => Export(Make()))));
        for (int i = 0; i < recentImage.Length; i++) { var h = recentImage[i]; content.Children.Add(Row(Chip(h, () => { page = 0; Select(h); }), Text(Core.Describe(h).Text("name")), Text($"{percentages[i]:0.0}%"))); }
    }
    static string Css(Gradient g) => (g.Type == "linear" ? $"linear-gradient({g.Angle}deg, " : "radial-gradient(circle, ") + string.Join(", ", g.Stops.OrderBy(s => s.Pos).Select(s => $"{s.Hex} {s.Pos}%")) + ")";
    static IBrush GradientBrush(Gradient g)
    {
        var stops = new GradientStops(); foreach (var s in g.Stops.OrderBy(s => s.Pos)) stops.Add(new GradientStop(Avalonia.Media.Color.Parse(s.Hex), s.Pos / 100d));
        double angle = g.Angle * Math.PI / 180, dx = Math.Sin(angle) / 2, dy = -Math.Cos(angle) / 2;
        return g.Type == "linear" ? new LinearGradientBrush { StartPoint = new RelativePoint(.5 - dx, .5 - dy, RelativeUnit.Relative), EndPoint = new RelativePoint(.5 + dx, .5 + dy, RelativeUnit.Relative), GradientStops = stops } : new RadialGradientBrush { GradientStops = stops };
    }
    void Gradients()
    {
        var preview = new Border { Height = 115, CornerRadius = new CornerRadius(10), Background = GradientBrush(gradient) };
        var code = Text(Css(gradient), 13); code.FlowDirection = FlowDirection.LeftToRight;
        void Refresh() { preview.Background = GradientBrush(gradient); code.Text = Css(gradient); }
        var angle = new NumericUpDown { Minimum = 0, Maximum = 360, Value = gradient.Angle, Increment = 5, IsEnabled = gradient.Type == "linear" };
        angle.ValueChanged += (_, _) => { gradient.Angle = (int)(angle.Value ?? 0); Refresh(); };
        content.Children.Add(Row(Choose(["خطي", "دائري"], gradient.Type == "linear" ? 0 : 1, i => { gradient.Type = i == 0 ? "linear" : "radial"; Render(); }), Text("الزاوية"), angle, Btn("تدرج جديد", () => { gradient = new(); Render(); })));
        content.Children.Add(preview);
        foreach (var stop in gradient.Stops)
        {
            var pos = new NumericUpDown { Minimum = 0, Maximum = 100, Value = stop.Pos, Increment = 1 };
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = stop.Pos, Width = 125, TickFrequency = 1, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { stop.Pos = (int)Math.Round(slider.Value); pos.Value = stop.Pos; Refresh(); };
            pos.ValueChanged += (_, _) => { stop.Pos = (int)(pos.Value ?? 0); slider.Value = stop.Pos; Refresh(); };
            content.Children.Add(Row(EditHex("لون النقطة", stop.Hex, h => stop.Hex = h), slider, pos, Text("%"), Btn("إزالة", () => { if (gradient.Stops.Count > 2) { gradient.Stops.Remove(stop); Render(); } })));
        }
        content.Children.Add(code);
        content.Children.Add(Row(Btn("إضافة نقطة", () => { if (gradient.Stops.Count < 32) { gradient.Stops.Add(new Stop { Hex = state.Color, Pos = 50 }); Render(); } }), Btn("نسخ CSS", () => Copy("background: " + Css(gradient) + ";")),
            Btn("تصدير CSS", () => WriteFile("gradient.css", ".gradient { background: " + Css(gradient) + "; }")), Btn("حفظ التدرج", async () =>
            {
                var name = await Prompt("اسم التدرج", gradient.Name); if (name == null) return; gradient.Name = name; var saved = JsonSerializer.Deserialize<Gradient>(Serialize(gradient), Workspace.Json)!;
                Edit(() => { var i = state.Gradients.FindIndex(g => g.Id == saved.Id); if (i < 0) state.Gradients.Add(saved); else state.Gradients[i] = saved; });
            }, true)));
        content.Children.Add(Text("مكتبة التدرجات", 18, true)); foreach (var g in state.Gradients) content.Children.Add(Row(new Border { Width = 81, Height = 30, Background = GradientBrush(g) }, Text(g.Name),
            Btn("فتح", () => { gradient = JsonSerializer.Deserialize<Gradient>(Serialize(g), Workspace.Json)!; Render(); }), Btn("حذف", async () => { if (await Confirm("حذف التدرج؟")) Edit(() => state.Gradients.Remove(g)); })));
    }
}
