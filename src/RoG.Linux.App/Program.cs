using Avalonia;

namespace RoG.Linux.App;

internal static class Program
{
    /// <summary>
    /// Einstieg. Argumente: <c>--environment=&lt;production|staging|local&gt;</c> (nur Entwicklung), <c>--x11</c> /
    /// <c>--wayland</c> und ein <c>rog://…</c>-Link (von der .desktop-Datei übergeben). Läuft die App schon, gehen die
    /// Argumente an die laufende Instanz (Single-Instance, siehe <see cref="Platform.Desktop.SingleInstance"/>) und dieser
    /// Prozess endet sofort.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        LaunchOptions.Current = LaunchOptions.Parse(args, Environment.GetEnvironmentVariable);
        using var instance = Platform.Desktop.SingleInstance.Acquire(args);
        if (!instance.IsPrimary)
        {
            return 0;
        }

        App.Instance = instance;
        return BuildAvaloniaApp(LaunchOptions.Current.Backend).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Vom Avalonia-Designer benutzt (X11 bzw. was die Plattform erkennt).</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(DisplayBackend.X11);

    public static AppBuilder BuildAvaloniaApp(DisplayBackend backend)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // App-ID als WM-Klasse (X11) bzw. xdg_toplevel-App-ID (Wayland): Symbol und .desktop-Zuordnung im Dock
            .With(new X11PlatformOptions { WmClass = Platform.AppIdentity.AppId })
            .With(new WaylandPlatformOptions { AppId = Platform.AppIdentity.AppId })
            .LogToTrace();
        return backend == DisplayBackend.Wayland ? builder.UseWaylandWithFallback() : builder;
    }
}
