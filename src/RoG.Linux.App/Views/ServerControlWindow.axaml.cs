using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Core.Features.Servers;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Server-Steuerung im eigenen Fenster (wie <c>ServerConsoleWindow</c> der Windows-App); je Server höchstens eins – erneutes
/// Öffnen holt es nach vorn. Titel = Servername. Escape bricht eine offene Rückfrage ab bzw. schließt, Strg+W schließt.
/// Schließen trennt die Live-Konsole.
/// </summary>
public sealed partial class ServerControlWindow : Window
{
    private static readonly Dictionary<string, ServerControlWindow> s_open = new(StringComparer.Ordinal);

    public ServerControlWindow()
    {
        InitializeComponent();
    }

    /// <summary>Fenster zu einem ViewModel (Tests, <see cref="Open"/>); startet Ressourcen und Live-Konsole beim Öffnen.</summary>
    internal ServerControlWindow(ServerControlViewModel control)
        : this()
    {
        DataContext = control;
        TitleText.Text = control.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Opened += (_, _) =>
        {
            _ = control.StartAsync();
            Dispatcher.UIThread.Post(ConsolePanel.FocusCommand, DispatcherPriority.Background);
        };
        Closed += (_, _) =>
        {
            if (s_open.TryGetValue(control.Slug, out var open) && open == this)
            {
                s_open.Remove(control.Slug);
            }

            _ = control.DisposeAsync().AsTask();
        };
    }

    /// <summary>Offene Steuer-Fenster (Tests).</summary>
    internal static IReadOnlyCollection<ServerControlWindow> OpenWindows => s_open.Values;

    internal ServerControlViewModel? Control => DataContext as ServerControlViewModel;

    /// <summary>„Steuern“ auf der Server-Karte: Fenster öffnen bzw. nach vorn holen (nur mit Steuerungsrechten).</summary>
    public static ServerControlWindow? Open(AppServices services, GameServer server)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(server);
        if (!s_open.TryGetValue(server.Slug, out var window))
        {
            var control = new ServerControlViewModel(services, server);
            if (!control.Access.Any)
            {
                return null;
            }

            window = new ServerControlWindow(control);
            s_open[server.Slug] = window;
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
            if (Control?.HandleEscape() != true)
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

    private void OnRconKeyDown(object? sender, KeyEventArgs e)
    {
        if (Control?.Rcon is not { } rcon)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when rcon.SendCommand.CanExecute(null):
                e.Handled = true;
                rcon.SendCommand.Execute(null);
                break;
            case Key.Up when rcon.PreviousCommand() is not null:
                e.Handled = true;
                RconBox.CaretIndex = rcon.Command.Length;
                break;
            case Key.Down:
                e.Handled = true;
                rcon.NextCommand();
                RconBox.CaretIndex = rcon.Command.Length;
                break;
        }
    }

    private void OnWhitelistKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Control?.Whitelist.AddCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            Control.Whitelist.AddCommand.Execute(null);
        }
    }
}
