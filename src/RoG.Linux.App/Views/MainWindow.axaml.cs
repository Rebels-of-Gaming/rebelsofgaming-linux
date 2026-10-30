using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Hauptfenster. Tastatur: Strg+1 … Strg+9 öffnen die ersten Bereiche der Seitenleiste, Strg+, die Einstellungen,
/// Strg+K die Suche, Strg+F das Suchfeld der Mitglieder,
/// F5 aktualisiert, Escape schließt das Menü (schmale Ansicht) bzw. das Detail (Liste-Detail-Seiten), Tab/Umschalt+Tab
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
    }

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
        if (e.Handled || Shell is not { IsShellVisible: true } shell)
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
        else if (e.Key == Key.F5 && shell.CurrentPage is IRefreshablePage refreshable)
        {
            _ = refreshable.RefreshAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && shell.CurrentPage is LoadableViewModel page)
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
