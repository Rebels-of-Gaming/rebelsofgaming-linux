using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltungs-Reiter: lädt beim ersten Anzeigen, Escape schließt Rückfrage bzw. Detail, Eingabe im Suchfeld sucht.</summary>
public sealed partial class ApplicationsAdminView : UserControl
{
    public ApplicationsAdminView()
    {
        InitializeComponent();
    }

    private ApplicationsAdminViewModel? Tab => DataContext as ApplicationsAdminViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = Tab?.EnsureLoadedAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            _ = Tab?.EnsureLoadedAsync();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Tab is not { } tab)
        {
            return;
        }

        if (e.Key == Key.Escape && tab.CloseOverlay())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source == SearchBox)
        {
            e.Handled = true;
            tab.SearchNowCommand.Execute(null);
        }
        else if (e.Key == Key.Enter && e.Source == TaskBox && tab.AddTaskCommand.CanExecute(null))
        {
            e.Handled = true;
            tab.AddTaskCommand.Execute(null);
        }
    }
}
