using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Hauptfenster. Tastatur: Strg+1 … Strg+9 öffnen die ersten Bereiche der Seitenleiste, Strg+, die Einstellungen,
/// Strg+K die Suche, Strg+F das Suchfeld der Mitglieder, Strg+N neue Nachricht bzw. neues Thema, Alt+←/→ zurück/vor,
/// Strg+W schließen (Infobereich), Strg+Q beenden, F1 Tastenkürzel, F5/Strg+R aktualisiert, Escape schließt das Menü (schmale Ansicht) bzw. das Detail (Liste-Detail-Seiten), Tab/Umschalt+Tab
/// und Pfeile wie gewohnt. Merkt Größe, Lage und „maximiert“ beim Schließen (<see cref="WindowPlacementService"/>).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppServices? _services;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(AppServices? services)
    {
        _services = services;
        InitializeComponent();
        PositionChanged += (_, e) => RememberNormal(e.Point, null);
        // App-Sperre: Vordergrund verloren → Zeit merken; zurück → ggf. sperren (wie OnWindowActivated unter Windows)
        Deactivated += (_, _) => services?.AppLock.OnLeft();
        Activated += (_, _) => services?.AppLock.OnReturned();
    }

    /// <summary>Vor dem Zeigen (Tray, Deep Link, zweiter Start): sperren, falls lange genug weg – kein Aufblitzen.</summary>
    public void CheckAppLock() => _services?.AppLock.OnReturned();

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    /// <summary>„Beenden“ aus dem Tray-Menü: wirklich schließen statt in den Infobereich.</summary>
    public bool ExitRequested { get; set; }

    /// <summary>Soll „Schließen“ nur ausblenden? (Tray sichtbar und „Beim Schließen in den Infobereich“ an.)</summary>
    public Func<bool>? CloseToTray { get; set; }

    /// <summary>Fenster wurde in den Infobereich ausgeblendet (für den einmaligen Hinweis).</summary>
    public event EventHandler? HiddenToTray;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Shell?.UpdateWidth(Bounds.Width);
            RememberNormal(null, Bounds.Size);
        }
        else if (change.Property == DataContextProperty)
        {
            Shell?.UpdateWidth(Bounds.Width > 0 ? Bounds.Width : Width);
        }
    }

    /// <summary>Lage/Größe nur im normalen Zustand merken – maximiert soll die vorherige Größe zurückkommen.</summary>
    private void RememberNormal(PixelPoint? position, Size? size)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        if (position is { } p)
        {
            _normalPosition = p;
        }

        if (size is { Width: > 0, Height: > 0 } s)
        {
            _normalSize = s;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_services is not null && _normalSize.Width > 0)
        {
            var placement = WindowPlacementService.Capture(_normalPosition, _normalSize, WindowState == WindowState.Maximized);
            _services.UpdateSettingsNow(s => s with { Window = placement });
        }

        // Schließen per Knopf/Alt+F4 → in den Infobereich (Benachrichtigungen kommen weiter); Beenden der App, Abmelden
        // vom Desktop und SIGTERM schließen wirklich
        if (!ExitRequested && e.CloseReason is WindowCloseReason.WindowClosing or WindowCloseReason.Undefined && CloseToTray?.Invoke() == true)
        {
            e.Cancel = true;
            _services?.AppLock.OnLeft();
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        Shell?.CloseOverlayCommand.Execute(null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        // Gesperrt: Tastatur bleibt auf dem Sperrbildschirm – nur Schließen/Beenden
        if (_services?.AppLock.IsLocked == true)
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.W or Key.Q)
            {
                ExitRequested = e.Key == Key.Q;
                Close();
                e.Handled = true;
            }

            return;
        }

        if (Shell is not { IsShellVisible: true } shell)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < shell.MainItems.Count)
            {
                shell.Navigate(shell.MainItems[index]);
                e.Handled = true;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemComma)
        {
            e.Handled = shell.NavigateTo("Nav_Settings");
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K)
        {
            e.Handled = shell.NavigateTo("Nav_Search");
            this.GetVisualDescendants().OfType<SearchView>().FirstOrDefault()?.FocusQuery();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F && shell.CurrentPage is MembersViewModel)
        {
            this.GetVisualDescendants().OfType<MembersView>().FirstOrDefault()?.FocusSearch();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left)
        {
            e.Handled = shell.GoBack();
        }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right)
        {
            e.Handled = shell.GoForward();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N)
        {
            // Strg+N: im Forum mit Schreibrecht neues Thema, sonst Nachrichten (Suche fokussiert)
            if (shell.CurrentPage is ForumViewModel { CanCreateThread: true } forum)
            {
                forum.StartNewThreadCommand.Execute(null);
            }
            else if (shell.NavigateTo("Nav_Messages"))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<MessagesView>().FirstOrDefault()?.FocusSearch(), Avalonia.Threading.DispatcherPriority.Background);
            }

            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            Close(); // in den Infobereich, wenn eingestellt
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Q)
        {
            ExitRequested = true;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = shell.NavigateTo("Nav_Settings");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<SettingsView>().FirstOrDefault()?.ShowShortcuts(), Avalonia.Threading.DispatcherPriority.Background);
        }
        else if ((e.Key == Key.F5 || (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R)) && shell.CurrentPage is IRefreshablePage refreshable)
        {
            _ = refreshable.RefreshAsync();
            e.Handled = true;
        }
        else if ((e.Key == Key.F5 || (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R)) && shell.CurrentPage is LoadableViewModel page)
        {
            _ = page.LoadAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (shell.IsOverlayOpen)
            {
                shell.IsOverlayOpen = false;
                e.Handled = true;
                return;
            }

            if (shell.CurrentPage is IDetailPage { IsDetailOpen: true } detail)
            {
                detail.CloseDetail();
                e.Handled = true;
            }
        }
    }
}
