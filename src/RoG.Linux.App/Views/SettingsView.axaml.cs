using Avalonia.Controls;

namespace RoG.Linux.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>F1: zu den Tastenkürzeln scrollen.</summary>
    public void ShowShortcuts() => ShortcutsCard.BringIntoView();
}
