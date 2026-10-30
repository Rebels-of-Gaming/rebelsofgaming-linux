using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter der Verwaltung (<see cref="MinecraftOpsViewModel"/>): lädt beim ersten Anzeigen, Escape schließt
/// die Rückfrage.
/// </summary>
public sealed partial class MinecraftOpsView : UserControl
{
    public MinecraftOpsView()
    {
        InitializeComponent();
    }

    private MinecraftOpsViewModel? Tab => DataContext as MinecraftOpsViewModel;

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
    }
}
