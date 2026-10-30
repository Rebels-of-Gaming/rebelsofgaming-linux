using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RoG.Linux.App.Controls;
using RoG.Linux.App.Localization;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Platform.Desktop;

namespace RoG.Linux.App;

public sealed partial class App : Application
{
    /// <summary>Single-Instance aus <c>Main</c> (in Tests <c>null</c>).</summary>
    internal static SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var services = await AppServices.CreateAsync();
        var window = CreateMainWindow(services);
        WindowPlacementService.Restore(window, services.Settings.Window);
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => services.Dispose();
        // SIGTERM (Abmelden, systemd, kill): Fenster ordentlich schließen, damit Größe/Lage gespeichert werden
        var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Dispatcher.UIThread.Post(() => desktop.TryShutdown());
        });
        desktop.Exit += (_, _) => sigterm.Dispose();
        window.Show();

        var shell = (ShellViewModel)window.DataContext!;
        // Desktop-Benachrichtigungen nur, wenn das Fenster nicht im Vordergrund ist; Klick → Fenster nach vorn + Ziel
        services.Realtime.IsAppFocused = () => window.IsActive;
        services.Realtime.ActivationRequested += (_, target) =>
        {
            BringToFront(window);
            switch (target)
            {
                case RoG.Core.Navigation.AppRoute route when shell.Navigate(route):
                    break;
                case Uri url:
                    _ = services.Browser.OpenAsync(url);
                    break;
            }
        };
        await shell.InitializeAsync();
        if (LaunchOptions.Current.DeepLink is { } link)
        {
            shell.OpenDeepLink(link);
        }

        // Zweiter Start: Fenster nach vorn holen, Link öffnen (kommt auf einem Hintergrund-Thread)
        if (Instance is { } instance)
        {
            instance.Activated += (_, request) => Dispatcher.UIThread.Post(() =>
            {
                BringToFront(window);
                if (request.DeepLink is { } deepLink)
                {
                    shell.OpenDeepLink(deepLink);
                }
            });
        }
    }

    /// <summary>Hauptfenster mit Shell – auch von den Headless-Tests (Screenshots) benutzt.</summary>
    public static MainWindow CreateMainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Loc.Current = services.Localizer;
        RichContentView.LinkOpener = uri => services.Browser.OpenAsync(uri);
        RichContentView.ImageSource = (uri, width) => services.Images.LoadAsync(uri, width);
        ThemeService.Apply(services.LinuxSettings.EffectiveTheme);
        services.LinuxSettingsChanged += (_, _) => Dispatcher.UIThread.Post(() => ThemeService.Apply(services.LinuxSettings.EffectiveTheme));
        return new MainWindow(services) { DataContext = new ShellViewModel(services) };
    }

    /// <summary>
    /// Fenster zeigen und aktivieren. Unter X11 klappt das sofort; Wayland-Compositoren erlauben Fokuswechsel nur mit
    /// Aktivierungs-Token (xdg-activation) – ohne Token blinkt das Fenster bzw. die Taskleiste („will Aufmerksamkeit“).
    /// </summary>
    internal static void BringToFront(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
