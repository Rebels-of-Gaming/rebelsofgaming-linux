using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class MembersView : UserControl
{
    private MembersViewModel? _members;

    public MembersView()
    {
        InitializeComponent();
    }

    /// <summary>Strg+F: Suchfeld fokussieren.</summary>
    public void FocusSearch() => SearchBox.Focus();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_members is not null)
        {
            _members.MapRequested -= OnMapRequested;
        }

        _members = DataContext as MembersViewModel;
        if (_members is not null)
        {
            _members.MapRequested += OnMapRequested;
        }
    }

    private static void OnMapRequested(object? sender, MemberMapViewModel map) => MemberMapWindow.Open(map);
}
