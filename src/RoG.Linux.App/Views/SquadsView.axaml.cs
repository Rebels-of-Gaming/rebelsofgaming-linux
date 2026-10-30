using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class SquadsView : UserControl
{
    private SquadDetailViewModel? _detail;

    public SquadsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_detail is not null)
        {
            _detail.ManageRequested -= OnManageRequested;
        }

        _detail = (DataContext as SquadsViewModel)?.Detail;
        if (_detail is not null)
        {
            _detail.ManageRequested += OnManageRequested;
        }
    }

    /// <summary>„Verwalten“: Squad-Verwaltung im eigenen Fenster (je Squad höchstens eins).</summary>
    private static void OnManageRequested(object? sender, SquadAdminViewModel admin) => SquadAdminWindow.Open(admin);
}
