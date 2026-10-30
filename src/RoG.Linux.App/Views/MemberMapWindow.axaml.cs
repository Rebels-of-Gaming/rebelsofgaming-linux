using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Mitgliederkarte im eigenen Fenster (höchstens eins). Escape schließt die Namensliste bzw. das Fenster, Strg+W schließt.</summary>
public sealed partial class MemberMapWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(5) };

    public MemberMapWindow()
    {
        InitializeComponent();
    }

    private MemberMapWindow(MemberMapViewModel map)
        : this()
    {
        DataContext = map;
        map.ProfileOpened += OnProfileOpened;

        // Signierte Kachel-Vorlage gilt 2 Stunden – rechtzeitig neu holen
        _refresh.Tick += (_, _) => _ = map.RefreshIfExpiringAsync(DateTimeOffset.UtcNow);
        _refresh.Start();
        Opened += (_, _) => Map.Focus();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            map.ProfileOpened -= OnProfileOpened;
            if (Current == this)
            {
                Current = null;
            }
        };
        _ = map.LoadAsync();
    }

    /// <summary>Offenes Kartenfenster (Tests).</summary>
    internal static MemberMapWindow? Current { get; private set; }

    internal Controls.MemberMapView MapView => Map;

    public static MemberMapWindow Open(MemberMapViewModel map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Current is not { } window)
        {
            window = new MemberMapWindow(map);
            Current = window;
            window.Show();
        }

        RoG.Linux.App.App.BringToFront(window);
        return window;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (DataContext is MemberMapViewModel { IsPanelOpen: true } map)
            {
                map.ClosePanel();
                Map.Focus();
            }
            else
            {
                Close();
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            e.Handled = true;
            Close();
        }
    }

    // Profil öffnet sich im Hauptfenster – das nach vorn holen
    private static void OnProfileOpened(object? sender, string slug)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
        {
            RoG.Linux.App.App.BringToFront(main);
        }
    }
}
