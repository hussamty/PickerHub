using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace PickerHub;

internal class App : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine("PickerHub 0.3.5"); return 0; }
        if (args.Contains("--check-core")) { Console.WriteLine(Core.Describe("#87CEEB").Text("hex")); return 0; }
        return AppBuilder.Configure<App>().With(new X11PlatformOptions{WmClass="pickerhub"}).UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
    public override void Initialize()
    {
        Accent(true);
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://PickerHub/")) { Source = new Uri("avares://PickerHub/Theme.axaml") });
    }
    // Stock controls take their accent from here: brand yellow on dark, brand black on light,
    // because yellow marks on a white ground are too faint to read.
    public static void Accent(bool dark)
    {
        var app = Current!; var tone = Avalonia.Media.Color.Parse(dark ? "#FFF000" : "#111111");
        if (app.Resources.TryGetValue("SystemAccentColor", out var now) && now is Avalonia.Media.Color c && c == tone) return;
        app.Resources["SystemAccentColor"] = tone;
        string[] steps = dark ? ["#FFF233", "#FFF566", "#FFF899", "#E6D800", "#CCC000", "#B3A800"] : ["#2B2B2B", "#444444", "#5E5E5E", "#000000", "#000000", "#000000"];
        string[] keys = ["Light1", "Light2", "Light3", "Dark1", "Dark2", "Dark3"];
        for (int i = 0; i < keys.Length; i++) app.Resources["SystemAccentColor" + keys[i]] = Avalonia.Media.Color.Parse(steps[i]);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose; desktop.MainWindow = new MainWindow(); }
        base.OnFrameworkInitializationCompleted();
    }
}
