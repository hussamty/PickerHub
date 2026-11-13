using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace PickerHub;

internal class App : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine("PickerHub 0.3.0"); return 0; }
        if (args.Contains("--check-core")) { Console.WriteLine(Core.Describe("#87CEEB").Text("hex")); return 0; }
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
    public override void Initialize() { Resources["SystemAccentColor"] = Avalonia.Media.Color.Parse("#2856B4"); Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
