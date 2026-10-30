using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Verdrahtung der Moderations-Reiter: lädt beim ersten Anzeigen (<see cref="ILazyTab"/>) und schließt mit Escape
/// zuerst offene Bereiche/Rückfragen (<see cref="ModerationTabViewModel.CloseOverlay"/>).
/// </summary>
internal static class ModerationTabHost
{
    public static void Attach(UserControl view)
    {
        view.AttachedToVisualTree += (_, _) => _ = (view.DataContext as ILazyTab)?.EnsureLoadedAsync();
        view.DataContextChanged += (_, _) =>
        {
            if (TopLevel.GetTopLevel(view) is not null && view.DataContext is ILazyTab tab)
            {
                _ = tab.EnsureLoadedAsync();
            }
        };
        view.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!e.Handled && e.Key == Key.Escape && view.DataContext is ModerationTabViewModel vm && vm.CloseOverlay())
            {
                e.Handled = true;
            }
        });
    }
}
