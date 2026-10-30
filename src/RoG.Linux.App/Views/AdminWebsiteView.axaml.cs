using Avalonia.Controls;
using RoG.Core.Features.Admin;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltung – Website: „Bereich bearbeiten“ öffnet den Editor im eigenen Fenster (eins je Bereich).</summary>
public sealed partial class AdminWebsiteView : UserControl
{
    private AdminWebsiteViewModel? _vm;

    public AdminWebsiteView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.EditorRequested -= OnEditorRequested;
            }

            _vm = DataContext as AdminWebsiteViewModel;
            if (_vm is not null)
            {
                _vm.EditorRequested += OnEditorRequested;
            }
        };
    }

    private void OnEditorRequested(object? sender, AdminArea area)
    {
        if (sender is AdminWebsiteViewModel vm)
        {
            AdminEditorWindow.OpenFor(vm.Services, area);
        }
    }
}
