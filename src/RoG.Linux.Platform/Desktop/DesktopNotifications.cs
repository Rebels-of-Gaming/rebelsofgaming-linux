using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Desktop-Benachrichtigung zeigen (Gegenstück zu den Windows-Toasts).</summary>
public interface IDesktopNotifier
{
    /// <returns><c>false</c>, wenn kein Benachrichtigungsdienst erreichbar ist – nie eine Ausnahme für die Oberfläche.</returns>
    Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>org.freedesktop.Notifications.Notify</c> über den Session-Bus (GNOME, KDE, XFCE, Steam-Deck-Desktopmodus).
/// In Flatpak leitet xdg-dbus-proxy das an den Dienst weiter (<c>--talk-name=org.freedesktop.Notifications</c>);
/// später ggf. das Notification-Portal mit Aktionen. Inhalte: nur, was die Plattform ohnehin anzeigt, kein Token.
/// </summary>
public sealed class FreedesktopNotifier(string? address = null) : IDesktopNotifier, IDisposable
{
    private readonly string? _address = address ?? DBusAddress.Session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DBusConnection? _connection;

    public async Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrEmpty(_address))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var connection = new DBusConnection(_address);
                await connection.ConnectAsync().ConfigureAwait(false);
                _connection = connection;
            }

            var writer = _connection.GetMessageWriter();
            MessageBuffer message;
            try
            {
                writer.WriteMethodCallHeader(
                    "org.freedesktop.Notifications",
                    "/org/freedesktop/Notifications",
                    "org.freedesktop.Notifications",
                    "Notify",
                    "susssasa{sv}i",
                    MessageFlags.None);
                writer.WriteString("Rebels of Gaming");
                writer.WriteUInt32(0);
                writer.WriteString(AppIdentity.AppId); // Symbol aus dem Icon-Theme (Flatpak exportiert es unter der App-ID)
                writer.WriteString(title);
                writer.WriteString(body);
                writer.WriteArray(Array.Empty<string>());
                var hints = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("desktop-entry");
                writer.WriteVariantString(AppIdentity.AppId);
                writer.WriteDictionaryEnd(hints);
                writer.WriteInt32(-1);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            await _connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadUInt32(), null).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException)
        {
            _connection?.Dispose();
            _connection = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _gate.Dispose();
    }
}
