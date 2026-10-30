using Avalonia.Controls;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

public sealed partial class MessagesView : UserControl
{
    private MessagesViewModel? _messages;

    public MessagesView()
    {
        InitializeComponent();
    }

    /// <summary>Strg+N: Suchfeld der Unterhaltungen.</summary>
    public void FocusSearch() => SearchBox.Focus();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_messages is not null)
        {
            _messages.PopOutRequested -= OnPopOut;
        }

        _messages = DataContext as MessagesViewModel;
        if (_messages is not null)
        {
            _messages.PopOutRequested += OnPopOut;
        }
    }

    private static void OnPopOut(object? sender, ChatViewModel chat) => ChatWindow.Open(chat);
}
