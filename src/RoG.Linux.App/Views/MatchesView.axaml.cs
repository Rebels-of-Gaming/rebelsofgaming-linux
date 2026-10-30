using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class MatchesView : UserControl
{
    private MatchesViewModel? _vm;

    public MatchesView()
    {
        InitializeComponent();
    }

    // „Match eintragen“/„Bearbeiten“ öffnen den Editor im eigenen Fenster
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.EditorRequested -= OnEditorRequested;
        }

        _vm = DataContext as MatchesViewModel;
        if (_vm is not null)
        {
            _vm.EditorRequested += OnEditorRequested;
        }
    }

    private static void OnEditorRequested(object? sender, MatchEditorRequest request) =>
        MatchEditorWindow.Open(request.Services, request.MatchId, request.Closed);
}
