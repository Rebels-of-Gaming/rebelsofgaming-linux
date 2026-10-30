using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Chat-Ansicht: Enter sendet, Umschalt+Enter fügt eine Zeile ein, Escape hebt das Zitat auf; neue Nachrichten scrollen
/// nach unten; Bildvorschauen laden, sobald der Anhang angezeigt wird.
/// </summary>
public sealed partial class ChatView : UserControl
{
    private ChatViewModel? _chat;

    public ChatView()
    {
        InitializeComponent();
    }

    /// <summary>Fokus in die Eingabe (Pop-out-Fenster, Antworten).</summary>
    public void FocusComposer() => Composer.Focus();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_chat is not null)
        {
            _chat.MessageAppended -= OnAppended;
            _chat.PropertyChanged -= OnChatChanged;
        }

        _chat = DataContext as ChatViewModel;
        if (_chat is not null)
        {
            _chat.MessageAppended += OnAppended;
            _chat.PropertyChanged += OnChatChanged;
        }
    }

    private void OnChatChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.QuoteTarget) && _chat?.HasQuote == true)
        {
            Composer.Focus();
        }
    }

    private void OnAppended(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => Scroll.ScrollToEnd(), DispatcherPriority.Background);

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_chat is null)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Return && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (_chat.SendCommand.CanExecute(null))
            {
                _chat.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Escape && _chat.HasQuote)
        {
            e.Handled = true;
            _chat.QuoteTarget = null;
        }
    }

    private void OnAttachmentLoaded(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ChatAttachmentViewModel attachment)
        {
            _ = attachment.LoadPreviewAsync();
        }
    }
}
