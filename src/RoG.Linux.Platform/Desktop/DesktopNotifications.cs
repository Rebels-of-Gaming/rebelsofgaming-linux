using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>
/// Inhalt einer Desktop-Benachrichtigung. <see cref="ActionLabel"/> gesetzt = anklickbar (Aktion <c>default</c>, die
/// meisten Dienste lösen sie beim Klick auf die Blase aus); <see cref="Category"/> nach der freedesktop-Spezifikation,
/// z. B. <c>im.received</c> für Direktnachrichten.
/// </summary>
public sealed record DesktopNotification(string Title, string Body, string? ActionLabel = null, string? Category = null);

/// <summary>Desktop-Benachrichtigung zeigen (Gegenstück zu den Windows-Toasts).</summary>
public interface IDesktopNotifier
{
    /// <returns><c>false</c>, wenn kein Benachrichtigungsdienst erreichbar ist – nie eine Ausnahme für die Oberfläche.</returns>
    Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <returns>ID der Benachrichtigung (für <see cref="Activated"/>) oder <c>null</c> ohne Benachrichtigungsdienst.</returns>
    Task<uint?> NotifyAsync(DesktopNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Benachrichtigung angeklickt (ID aus <see cref="NotifyAsync"/>); kommt auf einem Hintergrund-Thread.</summary>
    event EventHandler<uint>? Activated;
}

/// <summary>
/// <c>org.freedesktop.Notifications.Notify</c> über den Session-Bus (GNOME, KDE, XFCE, Steam-Deck-Desktopmodus).
/// In Flatpak leitet xdg-dbus-proxy das an den Dienst weiter (<c>--talk-name=org.freedesktop.Notifications</c>);
/// später ggf. das Notification-Portal mit Aktionen. Inhalte: nur, was die Plattform ohnehin anzeigt, kein Token.
/// </summary>
public sealed class FreedesktopNotifier(string? address = null) : IDesktopNotifier, IDisposable
{
    private const string Interface = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";

    private readonly string? _address = address ?? DBusAddress.Session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DBusConnection? _connection;
    private IDisposable? _actions;

    public event EventHandler<uint>? Activated;

    public async Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default) =>
        await NotifyAsync(new DesktopNotification(title, body), cancellationToken).ConfigureAwait(false) is not null;

    public async Task<uint?> NotifyAsync(DesktopNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var title = notification.Title;
        var body = notification.Body;
        if (string.IsNullOrEmpty(_address))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var connection = new DBusConnection(_address);
                await connection.ConnectAsync().ConfigureAwait(false);
                _connection = connection;
                // Klick auf die Benachrichtigung: Signal ActionInvoked(id, "default")
                _actions = await connection.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Interface = Interface, Member = "ActionInvoked", Path = ObjectPath },
                    static (m, _) =>
                    {
                        var reader = m.GetBodyReader();
                        return (reader.ReadUInt32(), reader.ReadString());
                    },
                    (Notification<(uint Id, string Key)> n) =>
                    {
                        if (n.HasValue && n.Value.Key == "default")
                        {
                            Activated?.Invoke(this, n.Value.Id);
                        }
                    },
                    emitOnCapturedContext: false).ConfigureAwait(false);
            }

            var writer = _connection.GetMessageWriter();
            MessageBuffer message;
            try
            {
                writer.WriteMethodCallHeader(
                    Interface,
                    ObjectPath,
                    Interface,
                    "Notify",
                    "susssasa{sv}i",
                    MessageFlags.None);
                writer.WriteString("Rebels of Gaming");
                writer.WriteUInt32(0);
                writer.WriteString(AppIdentity.AppId); // Symbol aus dem Icon-Theme (Flatpak exportiert es unter der App-ID)
                writer.WriteString(title);
                writer.WriteString(body);
                writer.WriteArray(notification.ActionLabel is { Length: > 0 } label ? ["default", label] : Array.Empty<string>());
                var hints = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("desktop-entry");
                writer.WriteVariantString(AppIdentity.AppId);
                if (notification.Category is { Length: > 0 } category)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("category");
                    writer.WriteVariantString(category);
                }

                writer.WriteDictionaryEnd(hints);
                writer.WriteInt32(-1);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            return await _connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadUInt32(), null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException)
        {
            _actions?.Dispose();
            _actions = null;
            _connection?.Dispose();
            _connection = null;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _actions?.Dispose();
        _connection?.Dispose();
        _gate.Dispose();
    }
}
