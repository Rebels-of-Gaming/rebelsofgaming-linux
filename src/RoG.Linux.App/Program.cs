using Avalonia;

namespace RoG.Linux.App;

internal static class Program
{
    /// <summary>
    /// Einstieg. Argumente: <c>--environment=&lt;production|staging|local&gt;</c> (nur Entwicklung), <c>--x11</c> /
    /// <c>--wayland</c>, <c>--gamemode</c> (Spielmodus erzwingen) und ein <c>rog://…</c>-Link (von der .desktop-Datei übergeben). Läuft die App schon, gehen die
    /// Argumente an die laufende Instanz (Single-Instance, siehe <see cref="Platform.Desktop.SingleInstance"/>) und dieser
    /// Prozess endet sofort.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Services.StartupTrace.Mark("main");
        LaunchOptions.Current = LaunchOptions.Parse(args, Environment.GetEnvironmentVariable);
        if (OperatingSystem.IsMacOS())
        {
            // macOS: LaunchServices hält die App ohnehin einmalig; rog://-Links kommen als Apple Event (App.MacOS.cs)
            return BuildAvaloniaApp(DisplayBackend.X11).StartWithClassicDesktopLifetime(args);
        }

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
            // macOS: Avalonia setzt den Prozessnamen sonst über eine private LaunchServices-Schnittstelle – in der
            // App Sandbox (Mac App Store) ist launchservicesd gesperrt und die App bricht beim Start ab. Den Namen in
            // Menüleiste und Dock liefert ohnehin das Bundle (CFBundleName).
            .With(new MacOSPlatformOptions { DisableSetProcessName = true })
            .LogToTrace();
        return backend == DisplayBackend.Wayland ? builder.UseWaylandWithFallback() : builder;
    }
}
