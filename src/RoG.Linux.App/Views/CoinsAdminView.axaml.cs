using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter der Verwaltung (<see cref="CoinsAdminViewModel"/>): lädt beim ersten Anzeigen, Enter im Suchfeld sucht, Escape schließt
/// die Rückfrage bzw. die gewählte Wallet.
/// </summary>
public sealed partial class CoinsAdminView : UserControl
{
    public CoinsAdminView()
    {
        InitializeComponent();
    }

    private CoinsAdminViewModel? Tab => DataContext as CoinsAdminViewModel;

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
        else if (e.Key == Key.Enter && e.Source is TextBox { Name: "SearchBox" })
        {
            e.Handled = true;
            tab.SearchNowCommand.Execute(null);
        }
    }
}
