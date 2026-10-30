using Avalonia;

namespace RoG.Linux.App;

internal static class Program
{
    /// <summary>
    /// Einstieg. Argumente: <c>--environment=&lt;production|staging|local&gt;</c> (nur Entwicklung) und ein
    /// <c>rog://…</c>-Link (von der .desktop-Datei übergeben; im Spike nur gemerkt, siehe Bauplan Stufe 3).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        LaunchOptions.Current = LaunchOptions.Parse(args, Environment.GetEnvironmentVariable);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Auch vom Avalonia-Designer und den Headless-Tests benutzt.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = Platform.AppIdentity.AppId })
            .LogToTrace();
}
