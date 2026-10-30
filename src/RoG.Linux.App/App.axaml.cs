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
        desktop.Exit += (_, _) => services.Dispose();

        // Tray (StatusNotifierItem): nur wenn ein Panel es zeigt, geht „Schließen“ in den Infobereich
        var tray = new TrayService(services);
        services.Tray = tray;
        desktop.Exit += (_, _) => Task.Run(() => tray.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
        var trayVisible = await tray.StartAsync().WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default);
        window.CloseToTray = () => tray.IsVisible && services.Settings.CloseToTray;
        var trayHintShown = false;
        window.HiddenToTray += (_, _) =>
        {
            if (!trayHintShown && services.Settings.ToastNotifications)
            {
                trayHintShown = true;
                _ = services.Notifier.ShowAsync(services.T(RoG.Core.Localization.StringKeys.AppDisplayName), services.T(RoG.Core.Localization.StringKeys.TrayStillRunning));
            }
        };
        // SIGTERM (Abmelden, systemd, kill): Fenster ordentlich schließen, damit Größe/Lage gespeichert werden
        var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Dispatcher.UIThread.Post(() => desktop.TryShutdown());
        });
        desktop.Exit += (_, _) => sigterm.Dispose();

        // Autostart (--background): ohne Fenster im Infobereich – nur, wenn es ein sichtbares Tray gibt
        // Beim Anmelden startet das Panel oft erst nach der App – bis zu 15 s auf das Tray warten
        if (LaunchOptions.Current.Background && services.Settings.CloseToTray && !trayVisible)
        {
            trayVisible = await WaitForTrayAsync(tray, TimeSpan.FromSeconds(15));
        }

        var startHidden = LaunchOptions.Current.Background && trayVisible && services.Settings.CloseToTray;
        // MainWindow erst setzen, wenn das Fenster gezeigt wird – die Lebensdauer zeigt ein gesetztes MainWindow beim
        // Start selbst an (und würde den Hintergrund-Start sichtbar machen)
        if (startHidden)
        {
            window.Opened += (_, _) => desktop.MainWindow ??= window;
        }
        else
        {
            desktop.MainWindow = window;
            window.Show();
        }

        var shell = (ShellViewModel)window.DataContext!;
        tray.Invoked += (_, command) =>
        {
            switch (command)
            {
                case TrayCommand.Exit:
                    window.ExitRequested = true;
                    desktop.TryShutdown();
                    return;
                case TrayCommand.Notifications:
                    shell.NavigateTo("Nav_Notifications");
                    break;
                case TrayCommand.Messages:
                    shell.NavigateTo("Nav_Messages");
                    break;
                case TrayCommand.Servers:
                    shell.NavigateTo("Nav_Servers");
                    break;
            }

            BringToFront(window);
        };
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

    private static async Task<bool> WaitForTrayAsync(TrayService tray, TimeSpan timeout)
    {
        var visible = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e)
        {
            if (tray.IsVisible)
            {
                visible.TrySetResult(true);
            }
        }

        tray.VisibilityChanged += OnChanged;
        try
        {
            return tray.IsVisible || await visible.Task.WaitAsync(timeout).ContinueWith(t => t.IsCompletedSuccessfully, TaskScheduler.Default);
        }
        finally
        {
            tray.VisibilityChanged -= OnChanged;
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
        services.ServerControlOpener = server => Views.ServerControlWindow.Open(services, server);
        return new MainWindow(services) { DataContext = new ShellViewModel(services) };
    }

    /// <summary>
    /// Fenster zeigen und aktivieren. Unter X11 klappt das sofort; Wayland-Compositoren erlauben Fokuswechsel nur mit
    /// Aktivierungs-Token (xdg-activation) – ohne Token blinkt das Fenster bzw. die Taskleiste („will Aufmerksamkeit“).
    /// </summary>
    internal static void BringToFront(Window window)
    {
        (window as MainWindow)?.CheckAppLock(); // Tray/Deep Link landen hinter der Sperre
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
