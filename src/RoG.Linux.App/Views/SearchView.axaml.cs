using Avalonia.Controls;

namespace RoG.Linux.App.Views;

public sealed partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        // Suchseite geöffnet (Strg+K, Knopf): sofort tippen können
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => QueryBox.Focus());
    }

    public void FocusQuery() => QueryBox.Focus();
}
