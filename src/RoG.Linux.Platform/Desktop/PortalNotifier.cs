using System.Globalization;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>
/// Desktop-Benachrichtigungen in Flatpak über <c>org.freedesktop.portal.Notification</c> (statt direkt an
/// <c>org.freedesktop.Notifications</c> – so braucht das Paket kein <c>--talk-name</c> dafür). Das Portal ordnet die
/// Benachrichtigung über die App-ID der Sandbox zu (Symbol, Name, Einstellungen des Desktops). Klick auf die Blase bzw.
/// „Öffnen“ kommt als <c>ActionInvoked</c> zurück. IDs des Portals sind Texte – nach außen bleiben es Zahlen wie beim
/// D-Bus-Dienst.
/// </summary>
public sealed class PortalNotifier(string? address = null) : IDesktopNotifier, IDisposable
{
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string Interface = "org.freedesktop.portal.Notification";
    private const string OpenAction = "open";

    private readonly string? _address = address ?? DBusAddress.Session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DBusConnection? _connection;
    private IDisposable? _actions;
    private uint _next;

    public event EventHandler<uint>? Activated;

    public async Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default) =>
        await NotifyAsync(new DesktopNotification(title, body), cancellationToken).ConfigureAwait(false) is not null;

    public async Task<uint?> NotifyAsync(DesktopNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
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
                _actions = await connection.WatchSignalAsync(
                    PortalName,
                    PortalPath,
                    Interface,
                    "ActionInvoked",
                    static (m, _) =>
                    {
                        var r = m.GetBodyReader();
                        r.ReadString(); // App-ID
                        return (r.ReadString(), r.ReadString());
                    },
                    (Notification<(string Id, string Action)> n) =>
                    {
                        if (n.HasValue && n.Value.Action is OpenAction or "default" && TryParseId(n.Value.Id) is { } id)
                        {
                            Activated?.Invoke(this, id);
                        }
                    },
                    ObserverFlags.None,
                    emitOnCapturedContext: false).ConfigureAwait(false);
            }

            var number = ++_next;
            var writer = _connection.GetMessageWriter();
            MessageBuffer message;
            try
            {
                writer.WriteMethodCallHeader(PortalName, PortalPath, Interface, "AddNotification", "sa{sv}", MessageFlags.None);
                writer.WriteString(IdFor(number));
                var dict = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("title");
                writer.WriteVariantString(notification.Title);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("body");
                writer.WriteVariantString(notification.Body);
                if (notification.ActionLabel is { Length: > 0 } label)
                {
                    // Klick auf die Blase …
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("default-action");
                    writer.WriteVariantString(OpenAction);
                    // … und Knopf „Öffnen“
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("buttons");
                    writer.WriteSignature("aa{sv}");
                    var buttons = writer.WriteArrayStart(DBusType.Array);
                    var button = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("label");
                    writer.WriteVariantString(label);
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("action");
                    writer.WriteVariantString(OpenAction);
                    writer.WriteDictionaryEnd(button);
                    writer.WriteArrayEnd(buttons);
                }

                writer.WriteDictionaryEnd(dict);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            await _connection.CallMethodAsync(message).ConfigureAwait(false);
            return number;
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

    internal static string IdFor(uint number) => "rog-" + number.ToString(CultureInfo.InvariantCulture);

    internal static uint? TryParseId(string id) =>
        id.StartsWith("rog-", StringComparison.Ordinal) && uint.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    public void Dispose()
    {
        _actions?.Dispose();
        _connection?.Dispose();
        _gate.Dispose();
    }
}
