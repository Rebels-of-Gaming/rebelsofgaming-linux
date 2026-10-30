using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class ServersView : UserControl
{
    private ServersViewModel? _vm;

    public ServersView()
    {
        InitializeComponent();
    }

    // „Neuer Gameserver“/„Verwalten“ öffnen ein eigenes Fenster
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.ProvisionRequested -= OnProvisionRequested;
        }

        _vm = DataContext as ServersViewModel;
        if (_vm is not null)
        {
            _vm.ProvisionRequested += OnProvisionRequested;
        }
    }

    private static void OnProvisionRequested(object? sender, ServerProvisionRequest request) => ServerProvisionWindow.OpenFor(request);
}
