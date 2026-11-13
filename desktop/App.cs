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
        Resources["SystemAccentColor"] = Avalonia.Media.Color.Parse("#D03A1F");
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://PickerHub/")) { Source = new Uri("avares://PickerHub/Theme.axaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose; desktop.MainWindow = new MainWindow(); }
        base.OnFrameworkInitializationCompleted();
    }
}
