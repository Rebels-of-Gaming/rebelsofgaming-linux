using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Reiter „Backups“: lädt beim ersten Anzeigen, Escape schließt die Rückfrage, Eingabe im Namensfeld legt an.</summary>
public sealed partial class ServerBackupsView : UserControl
{
    public ServerBackupsView()
    {
        InitializeComponent();
    }

    private ServerBackupsViewModel? Backups => DataContext as ServerBackupsViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = Backups?.EnsureLoadedAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            _ = Backups?.EnsureLoadedAsync();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Backups is not { } backups)
        {
            return;
        }

        if (e.Key == Key.Escape && backups.CloseOverlay())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is TextBox && backups.CreateCommand.CanExecute(null))
        {
            e.Handled = true;
            _ = backups.CreateCommand.ExecuteAsync(null);
        }
    }
}
