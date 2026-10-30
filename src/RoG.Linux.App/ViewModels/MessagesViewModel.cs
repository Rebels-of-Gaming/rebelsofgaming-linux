using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Messages;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Nachrichten: Unterhaltungen (Suche, ungelesen) und Verlauf daneben. Im Spike nur lesen; Senden, Echtzeit und
/// Pop-out-Fenster folgen in Stufe 4 (die Logik dafür steht schon im Kern: <see cref="MessageService.SendAsync"/>).
/// </summary>
public sealed partial class MessagesViewModel(AppServices services, Action requestSignIn) : LoadableViewModel(services), IDetailPage, IRoutablePage
{
    private string? _pendingId;

    private readonly MessageService _messages = new(services.Api.Client);
    private int _searchVersion;

    public ObservableCollection<ConversationItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConversations), nameof(ShowSelectHint))]
    public partial bool IsGuest { get; set; }

    public bool ShowConversations => !IsGuest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial ConversationItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial ChatViewModel? Chat { get; set; }

    /// <summary>Unterhaltung per Link geöffnet, die nicht in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen && !IsGuest;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            return;
        }

        var page = await _messages.ListAsync(SearchText, null, cancellationToken);
        var selected = Selected?.Id;
        Items.Clear();
        foreach (var c in page.Items)
        {
            Items.Add(new ConversationItemViewModel(c, Services));
        }

        if (selected is not null)
        {
            _keepChat = true;
            Selected = Items.FirstOrDefault(i => i.Id == selected);
            _keepChat = false;
        }

        if (_pendingId is { } pending)
        {
            _pendingId = null;
            Open(pending);
        }
    }

    /// <summary>Unterhaltung per ID öffnen (Benachrichtigung, Squad-Intern, <c>rog://messages/id</c>).</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Items.FirstOrDefault(i => i.Id == key) is { } item)
        {
            DetailForced = false;
            Selected = item;
        }
        else if (Items.Count == 0 && (IsLoading || !ShowContent) && !IsGuest)
        {
            _pendingId = key;
        }
        else if (!IsGuest)
        {
            Selected = null;
            DetailForced = true;
            Chat = new ChatViewModel(Services, _messages, key, Services.T("MessagesPage_Title.Text"));
            _ = Chat.LoadAsync();
            _ = MarkReadAsync(key);
        }
    }

    private bool _keepChat;

    partial void OnSelectedChanged(ConversationItemViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
        }

        if (value is null || _keepChat || Chat?.ConversationId == value.Id)
        {
            return;
        }

        Chat = new ChatViewModel(Services, _messages, value.Id, value.Name);
        _ = Chat.LoadAsync();
        if (value.Unread)
        {
            value.Unread = false;
            _ = MarkReadAsync(value.Id);
        }
    }

    private async Task MarkReadAsync(string id)
    {
        try
        {
            await _messages.MarkReadAsync(id);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or HttpRequestException)
        {
            // gelesen markieren ist Beiwerk; beim nächsten Öffnen erneut
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        Selected = null;
        DetailForced = false;
        Chat = null;
    }

    partial void OnSearchTextChanged(string? value) => _ = ReloadDelayedAsync();

    private async Task ReloadDelayedAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(350);
        if (version == _searchVersion)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void SignIn() => requestSignIn();

    /// <summary>Hinweis im Chat: Antworten folgt in Stufe 4.</summary>
    public string ReadOnlyHint => Services.T("Linux_ChatReadOnly");

    [RelayCommand]
    private Task OpenInBrowserAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "messages"));
}

public sealed partial class ConversationItemViewModel : ObservableObject
{
    public ConversationItemViewModel(ConversationSummary conversation, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(services);
        Id = conversation.Id;
        Kind = conversation.Kind;
        Name = conversation.Name;
        Initial = string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpper(services.Culture);
        Preview = conversation.LastMessagePreview is null
            ? string.Empty
            : conversation.Kind == ConversationKind.Direct || conversation.LastMessageAuthor is null
                ? conversation.LastMessagePreview
                : $"{conversation.LastMessageAuthor}: {conversation.LastMessagePreview}";
        Time = conversation.LastMessageAt?.ToLocalTime() is { } t
            ? t.ToString(t.Date == DateTime.Today ? "t" : "d", services.Culture)
            : string.Empty;
        Unread = conversation.Unread;
        _ = LoadAvatarAsync(services, conversation.Avatar);
    }

    public string Id { get; }

    public ConversationKind Kind { get; }

    public string Name { get; }

    /// <summary>Anfangsbuchstabe als Platzhalter, solange (oder falls) kein Avatar da ist.</summary>
    public string Initial { get; }

    public string Preview { get; }

    public string Time { get; }

    public bool IsGroup => Kind != ConversationKind.Direct;

    [ObservableProperty]
    public partial bool Unread { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAvatar))]
    public partial Bitmap? Avatar { get; set; }

    public bool HasNoAvatar => Avatar is null;

    private async Task LoadAvatarAsync(AppServices services, Uri? avatar) =>
        Avatar = await services.Images.LoadAsync(avatar, 96);
}

/// <summary>Verlauf einer Unterhaltung (älteste oben), „Ältere laden“ über den Cursor.</summary>
public sealed partial class ChatViewModel(AppServices services, MessageService messages, string conversationId, string title) : LoadableViewModel(services)
{
    private string? _cursor;

    public string ConversationId { get; } = conversationId;

    public string Title { get; } = title;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    public partial bool HasOlder { get; set; }

    protected override bool HasData => Messages.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await messages.GetMessagesAsync(ConversationId, null, cancellationToken);
        var me = await MyIdAsync(cancellationToken);
        Messages.Clear();
        foreach (var m in page.Items)
        {
            Messages.Add(new ChatMessageViewModel(m, m.AuthorId is not null && m.AuthorId == me, Services));
        }

        _cursor = page.NextCursor;
        HasOlder = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadOlderAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await messages.GetMessagesAsync(ConversationId, _cursor);
            var me = await MyIdAsync(CancellationToken.None);
            for (var i = page.Items.Count - 1; i >= 0; i--)
            {
                var m = page.Items[i];
                Messages.Insert(0, new ChatMessageViewModel(m, m.AuthorId is not null && m.AuthorId == me, Services));
            }

            _cursor = page.NextCursor;
            HasOlder = page.HasMore;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or HttpRequestException)
        {
            // Knopf bleibt – erneut versuchen genügt
        }
    }

    private async Task<string?> MyIdAsync(CancellationToken cancellationToken) =>
        Services.CurrentUser?.Id ?? (await Services.Tokens.GetAsync(cancellationToken))?.UserId;
}

public sealed class ChatMessageViewModel(ChatMessage message, bool mine, AppServices services)
{
    public string Author { get; } = message.AuthorName;

    public bool IsMine { get; } = mine;

    public bool IsOther => !IsMine;

    public string Body { get; } = message.Deleted ? services.T("Messages_Deleted") : message.Body ?? string.Empty;

    public bool IsDeleted { get; } = message.Deleted;

    public string Time { get; } = message.CreatedAt?.ToLocalTime().ToString("g", services.Culture) ?? string.Empty;

    public bool HasQuote { get; } = message.QuoteBody is not null;

    public string Quote { get; } = message.QuoteBody is null ? string.Empty : $"{message.QuoteAuthor}: {message.QuoteBody}";

    public bool HasAttachments { get; } = message.Attachments.Count > 0;

    /// <summary>Anhänge im Spike nur als Namen (Vorschau/Download folgen in Stufe 4).</summary>
    public string Attachments { get; } = string.Join(", ", message.Attachments.Select(a => a.Name));

    public bool FromMinecraft { get; } = message.FromMinecraft;

    public string MinecraftHint { get; } = services.T("Messages_FromMinecraft");
}
