using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Messages;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Offline;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Ein Chat (Seite „Nachrichten“ und eigenes Fenster, wie <c>ChatViewModel</c> der Windows-App): Verlauf mit
/// „Ältere laden“, neue Nachrichten live, Senden (Enter; Umschalt+Enter = neue Zeile), Zitat-Antwort, Text kopieren,
/// Anhänge (Bilder mit Vorschau, sonst Öffnen im Browser), gelesen markieren. Ohne Netz geht die Nachricht in die
/// Offline-Warteschlange (DMs nur im Speicher).
/// </summary>
public sealed partial class ChatViewModel : LoadableViewModel, IDisposable
{
    private readonly MessageService _messages;
    private IDisposable? _watch;
    private string? _olderCursor;
    private string? _myId;

    public ChatViewModel(AppServices services, string conversationId, string title)
        : base(services)
    {
        _messages = new MessageService(services.Api.Client);
        ConversationId = conversationId;
        Title = title;
        Report = new ReportFormViewModel(services);
    }

    public string ConversationId { get; }

    public string Title { get; }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Nachricht melden – Formular über der Eingabe.</summary>
    public ReportFormViewModel Report { get; }

    internal bool CanReport => ReportFormViewModel.IsAllowed(Services);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(RemainingText), nameof(IsTooLong))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial bool HasOlder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    public partial ChatMessageViewModel? QuoteTarget { get; set; }

    public bool HasQuote => QuoteTarget is not null;

    /// <summary>Zeichen übrig (erst ab 90 % der Grenze sichtbar).</summary>
    public string RemainingText => Draft.Length >= MessageService.MaxLength * 9 / 10
        ? (MessageService.MaxLength - Draft.Length).ToString(Services.Culture)
        : string.Empty;

    public bool IsTooLong => Draft.Trim().Length > MessageService.MaxLength;

    /// <summary>Neue Nachricht am Ende – die Ansicht scrollt dann nach unten.</summary>
    public event EventHandler? MessageAppended;

    /// <summary>Eigene Nachricht gesendet (Liste der Unterhaltungen aktualisiert Vorschau/Reihenfolge).</summary>
    public event EventHandler? Sent;

    protected override bool HasData => Messages.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        _myId = Services.CurrentUser?.Id ?? (await Services.Tokens.GetAsync(cancellationToken))?.UserId;
        var page = await _messages.GetMessagesAsync(ConversationId, null, cancellationToken);
        Messages.Clear();
        foreach (var m in page.Items)
        {
            Messages.Add(Wrap(m));
        }

        _olderCursor = page.NextCursor;
        HasOlder = page.HasMore;
        _watch ??= Services.Realtime.WatchConversation(ConversationId, OnLiveMessage, OnLiveDeleted);
        MessageAppended?.Invoke(this, EventArgs.Empty);
        await MarkReadAsync();
    }

    private ChatMessageViewModel Wrap(ChatMessage m) => new(m, m.AuthorId is not null && m.AuthorId == _myId, this, Services);

    [RelayCommand]
    private async Task LoadOlderAsync()
    {
        if (_olderCursor is null)
        {
            return;
        }

        try
        {
            var page = await _messages.GetMessagesAsync(ConversationId, _olderCursor);
            for (var i = page.Items.Count - 1; i >= 0; i--)
            {
                Messages.Insert(0, Wrap(page.Items[i]));
            }

            _olderCursor = page.NextCursor;
            HasOlder = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // Knopf bleibt – erneut versuchen genügt
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft) && !IsTooLong;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        var quote = QuoteTarget?.Message.Id;
        IsSending = true;
        Feedback.Close();
        try
        {
            var sent = await _messages.SendAsync(ConversationId, text, quote);
            Draft = string.Empty;
            QuoteTarget = null;
            Append(sent);
            Sent?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError)
        {
            // Ohne Netz: mit festem Idempotency-Key in die Warteschlange (DMs nur im Speicher)
            await Services.Outbox.EnqueueAsync(OutboxKind.DirectMessage, ConversationId, new { body = text, quote_id = quote }, Services.T(StringKeys.OutboxLabelMessage));
            Draft = string.Empty;
            QuoteTarget = null;
            Feedback.Show(Services.T(StringKeys.OutboxQueued), MessageTone.Info);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void ClearQuote() => QuoteTarget = null;

    internal void Quote(ChatMessageViewModel message) => QuoteTarget = message;

    internal void ShowCopied(bool ok) =>
        Feedback.Show(Services.T(ok ? "Linux_TextCopied" : "Linux_CopyFailed"), ok ? MessageTone.Success : MessageTone.Error);

    private void Append(ChatMessage message)
    {
        if (Messages.Any(m => m.Message.Id == message.Id))
        {
            return; // eigene Nachricht kommt zusätzlich live zurück
        }

        Messages.Add(Wrap(message));
        DataChanged();
        MessageAppended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Neue Nachricht aus der Echtzeit (für Tests direkt aufrufbar).</summary>
    internal void OnLiveMessage(ChatMessage message)
    {
        Append(message);
        if (message.AuthorId != _myId)
        {
            _ = MarkReadAsync();
        }
    }

    /// <summary>Gelöschte Nachricht aus der Echtzeit: als „gelöscht“ zeigen.</summary>
    internal void OnLiveDeleted(string id)
    {
        var index = Messages.ToList().FindIndex(m => m.Message.Id == id);
        if (index >= 0)
        {
            var old = Messages[index];
            Messages[index] = Wrap(old.Message with { Body = null, Deleted = true, Attachments = [] });
        }
    }

    private async Task MarkReadAsync()
    {
        try
        {
            Services.Realtime.SetUnreadMessages(await _messages.MarkReadAsync(ConversationId));
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // Zähler korrigiert sich beim nächsten Abgleich
        }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
    }
}

/// <summary>Eine Nachricht: eigene rechts, fremde links; gelöschte als Hinweis. Antworten (Zitat) und Text kopieren.</summary>
public sealed partial class ChatMessageViewModel
{
    private readonly ChatViewModel _owner;
    private readonly AppServices _services;

    public ChatMessageViewModel(ChatMessage message, bool mine, ChatViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
        IsMine = mine;
        _owner = owner;
        _services = services;
        Body = message.Deleted ? services.T(StringKeys.MessagesDeleted) : message.Body ?? string.Empty;
        var local = message.CreatedAt?.ToLocalTime();
        Time = local?.ToString(local.Value.Date == DateTime.Today ? "t" : "g", services.Culture) ?? string.Empty;
        Quote = message.QuoteBody is null ? string.Empty : $"{message.QuoteAuthor}: {message.QuoteBody}";
        Attachments = message.Attachments.Select(a => new ChatAttachmentViewModel(a, services)).ToList();
        MinecraftHint = services.T(StringKeys.MessagesFromMinecraft);
        AutomationName = $"{Author}, {Time}: {Body}";
    }

    public ChatMessage Message { get; }

    public string Author => Message.AuthorName;

    public bool IsMine { get; }

    public bool IsOther => !IsMine;

    public string Body { get; }

    public bool HasBody => Body.Length > 0;

    public bool IsDeleted => Message.Deleted;

    public string Time { get; }

    public bool HasQuote => Message.QuoteBody is not null;

    public string Quote { get; }

    public IReadOnlyList<ChatAttachmentViewModel> Attachments { get; }

    public bool HasAttachments => Attachments.Count > 0;

    public bool FromMinecraft => Message.FromMinecraft;

    public string MinecraftHint { get; }

    public string AutomationName { get; }

    /// <summary>„Antwort an …“-Leiste über der Eingabe.</summary>
    public string ReplyPreview => $"↩ {Author}: {Body}";

    public bool CanReply => !IsDeleted;

    /// <summary>„Melden“ wie Windows: fremde, nicht gelöschte Nachricht (Scope <c>reports:write</c>).</summary>
    public bool CanReport => IsOther && !IsDeleted && _owner.CanReport;

    [RelayCommand]
    private void Report() =>
        _owner.Report.Open("message", Message.Id, $"{_services.T(StringKeys.ModTypeMessage)} · {Author}");

    [RelayCommand]
    private void Reply() => _owner.Quote(this);

    [RelayCommand]
    private void Copy()
    {
        if (Message.Body is { Length: > 0 } text)
        {
            _owner.ShowCopied(_services.ServerConnector.Copy(text));
        }
    }
}

/// <summary>Anhang einer Nachricht; die Bildvorschau lädt beim ersten Anzeigen (kurzlebiger Link, nur im Speicher).</summary>
public sealed partial class ChatAttachmentViewModel(ChatAttachment attachment, AppServices services) : ObservableObject
{
    private bool _loading;

    public string Name => attachment.Name;

    public string SizeText { get; } = attachment.Size >= 1024 * 1024
        ? (attachment.Size / 1048576.0).ToString("0.0 'MB'", services.Culture)
        : Math.Max(1, attachment.Size / 1024).ToString("0 'KB'", services.Culture);

    public bool CanPreview { get; } = AttachmentService.CanPreview(attachment);

    public bool IsFile => !CanPreview;

    public string AutomationName => $"{attachment.Name}, {SizeText}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial Bitmap? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    /// <summary>Von der Ansicht beim Anzeigen aufgerufen – lädt die Vorschau nur einmal.</summary>
    public async Task LoadPreviewAsync()
    {
        if (!CanPreview || _loading || Preview is not null)
        {
            return;
        }

        _loading = true;
        try
        {
            var content = await new AttachmentService(services.Api.Http).LoadAsync(attachment.Id);
            using var stream = new MemoryStream(content.Bytes);
            Preview = Bitmap.DecodeToWidth(stream, 480, BitmapInterpolationMode.HighQuality);
        }
#pragma warning disable CA1031 // Vorschau ist Beiwerk – der Knopf zum Öffnen bleibt
        catch (Exception)
#pragma warning restore CA1031
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        try
        {
            if (await new AttachmentService(services.Api.Http).LinkAsync(attachment.Id) is { } url)
            {
                await services.Browser.OpenAsync(url);
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // gelöscht, kein Recht oder offline – nichts öffnen
        }
    }
}
