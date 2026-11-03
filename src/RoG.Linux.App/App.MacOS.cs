using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Platform.Desktop;

namespace RoG.Linux.App;

/// <summary>
/// macOS-Start (Mac App Store): kein Tray und keine D-Bus-Dienste; Menüleiste mit „Über“, „Einstellungen …“ (⌘,) und den
/// Standardpunkten von Avalonia (Ausblenden, Beenden ⌘Q). Schließen des Fensters blendet es nur aus – die App bleibt im
/// Dock, ein Klick auf das Dock-Symbol holt es zurück (wie bei Mac-Apps üblich).
/// <c>rog://</c>-Links kommen als Apple Event (<c>CFBundleURLTypes</c> in <c>Info.plist</c>) über
/// <see cref="IActivatableLifetime"/>.
/// </summary>
public sealed partial class App
{
    private async Task StartMacAsync(IClassicDesktopStyleApplicationLifetime desktop, AppServices services, MainWindow window, MacActivation? activation)
    {
        window.CloseToTray = static () => true;
        desktop.MainWindow = window;
        window.Show();

        var shell = (ShellViewModel)window.DataContext!;
        NativeMenu.SetMenu(this, CreateMacMenu(services, window, shell));
        AttachRealtimeActivation(services, window, shell);
        await shell.InitializeAsync();
        StartupTrace.Mark("shell-initialized");
        if (LaunchOptions.Current.DeepLink is { } link)
        {
            shell.OpenDeepLink(link);
        }

        // erst jetzt zustellen: Links vom Kaltstart (Klick auf rog://… bei geschlossener App) kommen vor der Shell an
        activation?.Connect(request =>
        {
            BringToFront(window);
            if (request.DeepLink is { } deepLink)
            {
                shell.OpenDeepLink(deepLink);
            }
        });
        SmokeTest(desktop);
    }

    private static NativeMenu CreateMacMenu(AppServices services, MainWindow window, ShellViewModel shell)
    {
        void Open(string key)
        {
            BringToFront(window);
            shell.NavigateTo(key);
        }

        var about = new NativeMenuItem(services.T("Linux_MacMenuAbout"));
        about.Click += (_, _) => Open("Nav_Info");
        var settings = new NativeMenuItem(services.T("Linux_MacMenuSettings"))
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        settings.Click += (_, _) => Open("Nav_Settings");

        // Avalonia ergänzt dahinter Dienste, Ausblenden, Andere ausblenden, Alle einblenden und Beenden (⌘Q)
        return [about, new NativeMenuItemSeparator(), settings];
    }

    /// <summary>Rauchtest (<see cref="LaunchOptions.SmokeTestArgument"/>): Start bestätigen und gleich wieder beenden.</summary>
    private static void SmokeTest(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!LaunchOptions.Current.SmokeTest)
        {
            return;
        }

        Console.WriteLine($"rog-smoke-test: ok ({HostPlatform.UserAgent}, {Environment.OSVersion})");
        DispatcherTimer.RunOnce(() => desktop.TryShutdown(), TimeSpan.FromSeconds(2));
    }
}

/// <summary>
/// Apple Events von macOS über <see cref="IActivatableLifetime"/>: <c>rog://</c>-Links (<see cref="ActivationKind.OpenUri"/>)
/// und Klick aufs Dock-Symbol (<see cref="ActivationKind.Reopen"/>). Was vor der fertigen Shell kommt, wird gesammelt.
/// Angenommen werden nur <c>rog://</c>-Links (gleiche Prüfung wie unter Linux).
/// </summary>
public sealed class MacActivation
{
    private readonly Lock _lock = new();
    private readonly List<ActivationRequest> _pending = [];
    private Action<ActivationRequest>? _handler;

    private MacActivation()
    {
    }

    public static MacActivation Attach(Avalonia.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var activation = new MacActivation();
        if (application.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
        {
            lifetime.Activated += (_, e) =>
            {
                var request = e switch
                {
                    ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri } p => SingleInstance.TryLink(p.Uri?.OriginalString) is { } link ? new ActivationRequest(link) : null,
                    { Kind: ActivationKind.Reopen } => new ActivationRequest(null),
                    _ => null,
                };
                if (request is not null)
                {
                    Dispatcher.UIThread.Post(() => activation.Deliver(request));
                }
            };
        }

        return activation;
    }

    /// <summary>Ab jetzt zustellen (auf dem UI-Thread), Gesammeltes sofort.</summary>
    public void Connect(Action<ActivationRequest> handler)
    {
        ActivationRequest[] pending;
        lock (_lock)
        {
            _handler = handler;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var request in pending)
        {
            handler(request);
        }
    }

    private void Deliver(ActivationRequest request)
    {
        Action<ActivationRequest>? handler;
        lock (_lock)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Add(request);
                return;
            }
        }

        handler(request);
    }
}
