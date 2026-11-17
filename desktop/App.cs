using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace PickerHub;

internal class App : Application
{
    internal static SingleInstance? Instance;
    // --preview-envelope: the daily envelope in a throwaway library, ready to be opened again and again.
    internal static bool Preview;
    internal static long LaunchTime { get; private set; }
    internal static string Version => typeof(App).Assembly.GetName().Version!.ToString(3);
    [STAThread]
    public static int Main(string[] args)
    {
        LaunchTime = System.Diagnostics.Stopwatch.GetTimestamp();
        if (args.Length == 2 && args[0] == "--instance-probe")
        {
            Workspace.RootOverride = args[1];
            using var probe = SingleInstance.Open(); return probe == null ? 0 : 3;
        }
        if (args.Contains("--self-test")) return SelfTests.Run();
        if (args.Contains("--version")) { Console.WriteLine("PickerHub " + Version); return 0; }
        if (args.Contains("--help"))
        {
            Console.WriteLine("PickerHub " + Version + "\n\nUsage: pickerhub [--preview-envelope [--english] [--light]] [--version]\n\n" +
                "  --preview-envelope  Open the palette-of-the-day envelope in a temporary library. Replay it as often as\n" +
                "                      you like; nothing touches your own library, and the temporary one is deleted on close.\n" +
                "  --english, --light  Start the preview in English or in the light theme.");
            return 0;
        }
        if (args.Contains("--check-core")) { Console.WriteLine(Core.Describe("#87CEEB").Text("hex")); return 0; }
        string? testRoot = null;
        if (args.Contains("--ui-test") || args.Contains("--preview-envelope"))
        {
            // Both run in a library of their own, so they can never touch the user's colors or lock them.
            Preview = !args.Contains("--ui-test");
            testRoot = Path.Combine(Path.GetTempPath(), (Preview ? "pickerhub-preview-" : "pickerhub-ui-") + Guid.NewGuid().ToString("N")); Workspace.RootOverride = testRoot;
            new Workspace { Language = args.Contains("--english") ? "en" : "ar", Theme = args.Contains("--light") ? "light" : "dark" }.Save();
        }
        try { Instance = SingleInstance.Open(); if (Instance == null) return 0; }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => SupportLog.Record("unhandled", e.ExceptionObject as Exception);
        if (OperatingSystem.IsLinux()) NativePicker.pickerhub_clipboard_prepare();
        try
        {
            int code = AppBuilder.Configure<App>()
                .With(new X11PlatformOptions { WmClass = "pickerhub", OverlayPopups = true })
                .With(new Win32PlatformOptions { OverlayPopups = true })
                .With(new AvaloniaNativePlatformOptions { OverlayPopups = true })
                .UsePlatformDetect().StartWithClassicDesktopLifetime(args);
            return code != 0 ? code : Environment.ExitCode;
        }
        finally { Instance?.Dispose(); if (testRoot != null && Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
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
        var app = Current!;
        // Separate native popup roots must follow the same theme as the workbench.
        app.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var tone = Avalonia.Media.Color.Parse(dark ? "#FFF000" : "#111111");
        if (app.Resources.TryGetValue("SystemAccentColor", out var now) && now is Avalonia.Media.Color c && c == tone) return;
        app.Resources["SystemAccentColor"] = tone;
        string[] steps = dark ? ["#FFF233", "#FFF566", "#FFF899", "#E6D800", "#CCC000", "#B3A800"] : ["#2B2B2B", "#444444", "#5E5E5E", "#000000", "#000000", "#000000"];
        string[] keys = ["Light1", "Light2", "Light3", "Dark1", "Dark2", "Dark3"];
        for (int i = 0; i < keys.Length; i++) app.Resources["SystemAccentColor" + keys[i]] = Avalonia.Media.Color.Parse(steps[i]);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose; desktop.MainWindow = new MainWindow();
            Instance?.OnActivate(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => ((MainWindow)desktop.MainWindow).BringForward()));
            if (desktop.Args?.Contains("--ui-test") == true)
            {
                bool started = false;
                desktop.MainWindow.Opened += (_, _) => { if (started) return; started = true; _ = ((MainWindow)desktop.MainWindow).Smoke(); };
            } }
        base.OnFrameworkInitializationCompleted();
    }
}
