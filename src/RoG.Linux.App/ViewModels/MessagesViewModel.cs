using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Messages;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Nachrichten: Unterhaltungen (Suche, ungelesen) und daneben der Chat (<see cref="ChatViewModel"/>: schreiben,
/// zitieren, live, Anhänge) – auch als eigenes Fenster (<see cref="PopOutRequested"/>).
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
            ReplaceChat(new ChatViewModel(Services, key, Services.T("MessagesPage_Title.Text")));
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

        ReplaceChat(new ChatViewModel(Services, value.Id, value.Name));
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
            Services.Realtime.SetUnreadMessages(await _messages.MarkReadAsync(id));
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
        ReplaceChat(null);
    }

    private void ReplaceChat(ChatViewModel? chat)
    {
        Chat?.Dispose();
        Chat = chat;
        if (chat is not null)
        {
            chat.Sent += (_, _) => _ = LoadAsync(silent: true); // Vorschau und Reihenfolge der Liste
            _ = chat.LoadAsync();
        }
    }

    /// <summary>Chat in eigenem Fenster öffnen (vom Hauptfenster umgesetzt).</summary>
    public event EventHandler<ChatViewModel>? PopOutRequested;

    [RelayCommand]
    private void PopOut()
    {
        if (Chat is { } chat)
        {
            PopOutRequested?.Invoke(this, new ChatViewModel(Services, chat.ConversationId, chat.Title));
        }
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
