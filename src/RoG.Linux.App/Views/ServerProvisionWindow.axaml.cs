using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// „Neuer Gameserver“ bzw. Verwaltung eines über die Website angelegten Servers im eigenen Fenster (wie
/// ServerProvisionWindow der Windows-App); je Server höchstens eins. Escape schließt eine offene Rückfrage bzw. das
/// Fenster, Strg+W schließt.
/// </summary>
public sealed partial class ServerProvisionWindow : Window
{
    private static readonly Dictionary<string, ServerProvisionWindow> s_open = new(StringComparer.Ordinal);

    public ServerProvisionWindow()
    {
        InitializeComponent();
    }

    private ServerProvisionWindow(ServerProvisionViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        _ = viewModel.LoadAsync();
    }

    /// <summary>Offene Fenster je Schlüssel (Slug, leer = neuer Server) – Tests.</summary>
    internal static IReadOnlyDictionary<string, ServerProvisionWindow> OpenWindows => s_open;

    /// <summary>Fenster zur Anfrage öffnen bzw. nach vorn holen.</summary>
    public static ServerProvisionWindow OpenFor(ServerProvisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!s_open.TryGetValue(request.Key, out var window))
        {
            window = new ServerProvisionWindow(request.Create());
            s_open[request.Key] = window;
            window.Closed += (_, _) => s_open.Remove(request.Key);
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
            if (DataContext is ServerProvisionViewModel { Confirmation.IsOpen: true } vm)
            {
                vm.Confirmation.Cancel();
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
}
