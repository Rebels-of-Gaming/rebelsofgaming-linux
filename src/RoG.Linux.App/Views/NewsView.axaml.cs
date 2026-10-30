using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class NewsView : UserControl
{
    private NewsViewModel? _vm;

    public NewsView()
    {
        InitializeComponent();
    }

    // „Schreiben“/„Bearbeiten“ öffnen die Redaktion im eigenen Fenster; danach ggf. neu laden
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.EditorRequested -= OnEditorRequested;
        }

        _vm = DataContext as NewsViewModel;
        if (_vm is not null)
        {
            _vm.EditorRequested += OnEditorRequested;
        }
    }

    private static void OnEditorRequested(object? sender, NewsEditorRequest request) =>
        NewsEditorWindow.Open(request.Services, request.Rights, request.Slug, request.Closed);
}
