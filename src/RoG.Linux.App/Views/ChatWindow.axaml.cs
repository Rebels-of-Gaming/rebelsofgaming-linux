using Avalonia.Controls;
using Avalonia.Interactivity;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Chat in eigenem Fenster; je Unterhaltung höchstens eins (erneutes Öffnen holt es nach vorn).</summary>
public sealed partial class ChatWindow : Window
{
    private static readonly Dictionary<string, ChatWindow> s_open = new(StringComparer.Ordinal);

    public ChatWindow()
    {
        InitializeComponent();
    }

    private ChatWindow(ChatViewModel chat)
        : this()
    {
        DataContext = chat;
        TitleText.Text = chat.Title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Closed += (_, _) =>
        {
            s_open.Remove(chat.ConversationId);
            chat.Dispose();
        };
        _ = chat.LoadAsync();
    }

    /// <summary>Offene Chat-Fenster (Tests).</summary>
    internal static IReadOnlyCollection<ChatWindow> OpenWindows => s_open.Values;

    public static ChatWindow Open(ChatViewModel chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        if (s_open.TryGetValue(chat.ConversationId, out var window))
        {
            chat.Dispose();
        }
        else
        {
            window = new ChatWindow(chat);
            s_open[chat.ConversationId] = window;
            window.Show();
        }

        RoG.Linux.App.App.BringToFront(window);
        window.Chat.FocusComposer();
        return window;
    }

    /// <summary>Strg+W schließt das Chat-Fenster.</summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Avalonia.Input.Key.W)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnTopChanged(object? sender, RoutedEventArgs e) => Topmost = OnTop.IsChecked == true;
}
