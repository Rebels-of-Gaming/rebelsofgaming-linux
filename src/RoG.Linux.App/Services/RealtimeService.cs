using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Core.Api;
using RoG.Core.Features.Messages;
using RoG.Core.Features.Notifications;
using RoG.Core.Navigation;
using RoG.Core.Realtime;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;

namespace RoG.Linux.App.Services;

/// <summary>
/// Echtzeit der Linux-App (wie <c>RealtimeService</c> der Windows-App): startet den <see cref="RealtimeHub"/> des Kerns
/// (Reverb, nach Login/Abmelden neu), hält die Zähler für Glocke und Nachrichten und reicht Ereignisse im UI-Thread
/// weiter. Solange die App läuft, zeigt sie neue Benachrichtigungen (Erwähnungen, Antworten …) und neue Direktnachrichten
/// als Desktop-Benachrichtigung (<c>org.freedesktop.Notifications</c>); ein Klick öffnet das Ziel in der App.
/// Fällt Reverb aus, läuft alles ohne Live-Aktualisierung weiter.
/// </summary>
public sealed partial class RealtimeService : ObservableObject, IAsyncDisposable
{
    /// <summary>Höchstens so viele Desktop-Benachrichtigungen für Direktnachrichten auf einmal (kein Spam nach Pause).</summary>
    private const int MaxMessageToasts = 3;

    private readonly AppServices _services;
    private readonly RealtimeHub _hub;
    private readonly NotificationService _notifications;
    private readonly MessageService _messages;
    private readonly Dictionary<uint, object> _targets = [];
    private readonly HashSet<string> _toastedMessages = new(StringComparer.Ordinal);
    private DateTimeOffset _messagesSeenUntil = DateTimeOffset.UtcNow;

    /// <summary>Nachrichten bis zu diesem Zeitpunkt gelten als bekannt (App-Start) – für Tests setzbar.</summary>
    internal DateTimeOffset MessagesSeenUntil
    {
        get => _messagesSeenUntil;
        set => _messagesSeenUntil = value;
    }
    private bool _started;

    public RealtimeService(AppServices services, RealtimeHub? hub = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _notifications = new NotificationService(services.Api.Client);
        _messages = new MessageService(services.Api.Client);
        var userAgent = $"{AppIdentity.UserAgentProduct}/{AppServices.AppVersion}";
        // Reverb der Produktion lehnt Verbindungen ohne erlaubten Origin ab (pusher:error 4009 „Origin not allowed“) –
        // die App meldet sich deshalb mit der Website ihrer Umgebung als Origin (aus environments.json, keine feste Domain)
        var origin = services.Environment.WebBaseUrl.GetLeftPart(UriPartial.Authority);
        _hub = hub ?? new RealtimeHub(
            ct => RealtimeConfig.FetchAsync(services.Api.Http, ct),
            config => new ReverbClient(config, new HttpChannelAuthorizer(services.Api.Http, config.AuthEndpoint), () => new ClientWebSocketConnection(userAgent, origin), AppServices.AppVersion));

        _hub.NotificationReceived += (_, n) => OnUi(() => OnNotification(n));
        _hub.UnreadChanged += (_, n) => OnUi(() => UnreadNotifications = n);
        _hub.MessagesUnreadChanged += (_, n) => OnUi(() => _ = OnMessagesUnreadAsync(n));
        _hub.LiveChanged += (_, _) => OnUi(() => LiveChanged?.Invoke(this, EventArgs.Empty));
        _hub.FriendsChanged += (_, _) => OnUi(() => FriendsChanged?.Invoke(this, EventArgs.Empty));
        _hub.ServerStatusChanged += (_, s) => OnUi(() => ServerStatusChanged?.Invoke(this, s));
        _hub.Reconnected += (_, _) => OnUi(() => _ = RefreshCountersAsync());
        _hub.StateChanged += (_, s) => OnUi(() => State = s);
        services.Notifier.Activated += (_, id) => OnUi(() => OnActivated(id));
    }

    [ObservableProperty]
    public partial int UnreadNotifications { get; set; }

    [ObservableProperty]
    public partial int UnreadMessages { get; set; }

    [ObservableProperty]
    public partial RealtimeState State { get; set; }

    /// <summary>Hat das Hauptfenster gerade den Fokus? Dann keine Desktop-Benachrichtigung (die App zeigt es selbst).</summary>
    public Func<bool> IsAppFocused { get; set; } = () => false;

    /// <summary>Neue Benachrichtigung (nach der Desktop-Benachrichtigung) – die offene Glocke-Seite stellt sie vorn an.</summary>
    public event EventHandler<NotificationItem>? NotificationReceived;

    public event EventHandler? LiveChanged;

    public event EventHandler? FriendsChanged;

    public event EventHandler<ServerStatusUpdate>? ServerStatusChanged;

    /// <summary>Desktop-Benachrichtigung angeklickt: Fenster nach vorn, Ziel öffnen (Route in der App oder Adresse).</summary>
    public event EventHandler<object>? ActivationRequested;

    /// <summary>Verbinden (Gast: nur öffentliche Kanäle) und Zähler per API holen. Im UI-Thread aufrufen.</summary>
    public async Task StartAsync(string? userId)
    {
        _started = true;
        _messagesSeenUntil = DateTimeOffset.UtcNow;
        try
        {
            await _hub.StartAsync(userId);
        }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or HttpRequestException or ApiProblemException or OperationCanceledException or InvalidOperationException)
        {
            // Reverb nicht erreichbar – ohne Live-Aktualisierung weiter (Neuverbindung macht der Client selbst)
        }

        if (userId is null)
        {
            UnreadNotifications = 0;
            UnreadMessages = 0;
        }
        else
        {
            await RefreshCountersAsync();
        }
    }

    public async Task StopAsync()
    {
        _started = false;
        UnreadNotifications = 0;
        UnreadMessages = 0;
        await _hub.StopAsync();
    }

    /// <summary>Zähler per API (Start, nach Neuverbindung, nach „alle gelesen“).</summary>
    public async Task RefreshCountersAsync()
    {
        if (await _services.Tokens.GetAsync() is null)
        {
            return;
        }

        try
        {
            UnreadNotifications = await _notifications.UnreadCountAsync();
            UnreadMessages = await _messages.UnreadCountAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            // Zähler bleiben beim letzten Stand
        }
    }

    /// <summary>Neue Benachrichtigung aus <c>.notification</c> (für Tests direkt aufrufbar).</summary>
    internal void OnNotification(NotificationItem item)
    {
        UnreadNotifications++;
        NotificationReceived?.Invoke(this, item);
        if (!ShouldToast())
        {
            return;
        }

        object target = item.Url is { } url
            ? (object?)_services.RouteFor(url) ?? url
            : new AppRoute(AppSection.Notifications);
        _ = ToastAsync(new DesktopNotification(item.Title, item.Body ?? string.Empty, _services.T("Linux_NotificationOpen"), Category(item.Type)), target);
    }

    /// <summary>
    /// <c>.messages.unread</c>: Zahl der Unterhaltungen mit Neuem. Steigt sie, holt die App die Unterhaltungen und zeigt
    /// für neue Nachrichten (nach dem Start, noch nicht gezeigt) eine Desktop-Benachrichtigung „Name – Text“.
    /// </summary>
    internal async Task OnMessagesUnreadAsync(int unread)
    {
        UnreadMessages = unread;
        if (unread <= 0 || !ShouldToast())
        {
            return;
        }

        try
        {
            var page = await _messages.ListAsync();
            var fresh = page.Items
                .Where(c => c.Unread && c.LastMessageAt is { } at && at > _messagesSeenUntil)
                .Where(c => _toastedMessages.Add(c.Id + "@" + c.LastMessageAt!.Value.ToUnixTimeMilliseconds()))
                .Take(MaxMessageToasts)
                .ToList();
            foreach (var c in fresh)
            {
                var body = string.Join(": ", new[] { c.Kind == ConversationKind.Direct ? null : c.LastMessageAuthor, c.LastMessagePreview }.Where(s => !string.IsNullOrEmpty(s)));
                await ToastAsync(new DesktopNotification(c.Name, body, _services.T("Linux_NotificationOpen"), "im.received"), new AppRoute(AppSection.Messages, c.Id));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            // ohne Desktop-Benachrichtigung – der Zähler stimmt trotzdem
        }
    }

    /// <summary>Klick auf eine eigene Desktop-Benachrichtigung (für Tests direkt aufrufbar).</summary>
    internal void OnActivated(uint id)
    {
        if (_targets.Remove(id, out var target))
        {
            ActivationRequested?.Invoke(this, target);
        }
    }

    /// <summary>Unterhaltung gelesen (Nachrichten-Seite) – Zähler sofort anpassen.</summary>
    public void SetUnreadMessages(int unread) => UnreadMessages = Math.Max(0, unread);

    private bool ShouldToast() => _started && _services.Settings.ToastNotifications && !IsAppFocused();

    private async Task ToastAsync(DesktopNotification notification, object target)
    {
        if (await _services.Notifier.NotifyAsync(notification) is { } id)
        {
            _targets[id] = target;
        }
    }

    /// <summary>Kategorie nach freedesktop (<c>im.received</c> für Nachrichten, sonst allgemein).</summary>
    private static string? Category(string? type) =>
        type is not null && type.StartsWith("messages", StringComparison.Ordinal) ? "im.received" : null;

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();
}
