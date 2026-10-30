using Avalonia.Controls;

namespace RoG.Linux.App.Views;

public sealed partial class MembersView : UserControl
{
    public MembersView()
    {
        InitializeComponent();
    }

    /// <summary>Strg+F: Suchfeld fokussieren.</summary>
    public void FocusSearch() => SearchBox.Focus();
}
