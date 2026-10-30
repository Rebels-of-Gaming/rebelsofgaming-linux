using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Eigener D-Bus-Session-Bus je Test (<c>dbus-daemon --session</c>) – so laufen Secret Service, Benachrichtigungen und
/// Single-Instance gegen echtes D-Bus-Protokoll, ohne den Desktop des Entwicklers anzufassen. Ohne <c>dbus-daemon</c>
/// werden die Tests übersprungen.
/// </summary>
public sealed class PrivateBus : IAsyncDisposable
{
    private readonly Process _daemon;
    private readonly List<DBusConnection> _connections = [];

    private PrivateBus(Process daemon, string address)
    {
        _daemon = daemon;
        Address = address;
    }

    public string Address { get; }

    public static async Task<PrivateBus> StartAsync()
    {
        var daemonPath = new[] { "/usr/bin/dbus-daemon", "/bin/dbus-daemon" }.FirstOrDefault(File.Exists);
        if (daemonPath is null)
        {
            Assert.Skip("dbus-daemon nicht installiert");
        }

        // Eigene Konfiguration ohne Dienst-Verzeichnisse: sonst startet der Bus echte Dienste des Desktops (z. B.
        // xfce4-notifyd), sobald ein Test einen noch nicht vorhandenen Namen anspricht
        var config = Path.Combine(Path.GetTempPath(), $"rog-test-bus-{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(config, """
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig>
              <type>session</type>
              <listen>unix:tmpdir=/tmp</listen>
              <auth>EXTERNAL</auth>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true" />
                <allow eavesdrop="true" />
                <allow own="*" />
              </policy>
            </busconfig>
            """);
        var start = new ProcessStartInfo(daemonPath, ["--config-file=" + config, "--nofork", "--nopidfile", "--print-address=1"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var daemon = Process.Start(start) ?? throw new InvalidOperationException("dbus-daemon startet nicht");
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => File.Delete(config), TaskScheduler.Default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var address = (await daemon.StandardOutput.ReadLineAsync(timeout.Token))?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            daemon.Kill();
            throw new InvalidOperationException("dbus-daemon nennt keine Adresse");
        }

        return new PrivateBus(daemon, address);
    }

    /// <summary>Dienst auf dem Bus anbieten: Namen besitzen, Handler registrieren.</summary>
    public async Task<DBusConnection> ServeAsync(string name, IPathMethodHandler handler)
    {
        var connection = new DBusConnection(Address);
        await connection.ConnectAsync();
        connection.AddMethodHandler(handler);
        await connection.RequestNameAsync(name, RequestNameOptions.None);
        _connections.Add(connection);
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _connections)
        {
            c.Dispose();
        }

        if (!_daemon.HasExited)
        {
            _daemon.Kill();
            await _daemon.WaitForExitAsync();
        }

        _daemon.Dispose();
    }
}

/// <summary>Antworten schreiben (MessageWriter ist ein veränderliches Struct → per ref, nie kopieren).</summary>
internal static class Replies
{
    public delegate void Body(ref MessageWriter writer);

    public static void Send(MethodContext context, string? signature, Body? body = null)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        var writer = context.CreateReplyWriter(signature);
        try
        {
            body?.Invoke(ref writer);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }
}

/// <summary>
/// Minimaler Secret Service (wie GNOME Keyring): Sitzung „plain“, Suchen, Anlegen/Ersetzen, Geheimnis lesen, Löschen,
/// Entsperren über einen Prompt mit <c>Completed</c>-Signal. <see cref="Locked"/> simuliert einen gesperrten Schlüsselbund.
/// </summary>
public sealed class FakeSecretService : IPathMethodHandler
{
    private const string Root = "/org/freedesktop/secrets";
    private int _next;

    public sealed record Entry(string Path, string Label, Dictionary<string, string> Attributes, byte[] Secret);

    public List<Entry> Entries { get; } = [];

    /// <summary>Gesperrt, bis der Prompt bestätigt wurde.</summary>
    public bool Locked { get; set; }

    /// <summary>Prompt ablehnen (Benutzer klickt „Abbrechen“).</summary>
    public bool DismissPrompt { get; set; }

    public int PromptsShown { get; private set; }

    public string Path => Root;

    public bool HandlesChildPaths => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case ("org.freedesktop.Secret.Service", "OpenSession"):
                Replies.Send(context, "vo", static (ref MessageWriter w) =>
                {
                    w.WriteVariantString(string.Empty);
                    w.WriteObjectPath(Root + "/session/1");
                });
                break;

            case ("org.freedesktop.Secret.Service", "SearchItems"):
            {
                var query = ReadStringDictionary(ref reader);
                var found = Entries.Where(e => query.All(q => e.Attributes.GetValueOrDefault(q.Key) == q.Value)).Select(e => new ObjectPath(e.Path)).ToArray();
                var locked = Locked;
                Replies.Send(context, "aoao", (ref MessageWriter w) =>
                {
                    w.WriteArray(locked ? [] : found);
                    w.WriteArray(locked ? found : []);
                });
                break;
            }

            case ("org.freedesktop.Secret.Service", "Unlock"):
                Replies.Send(context, "aoo", static (ref MessageWriter w) =>
                {
                    w.WriteArray(Array.Empty<ObjectPath>());
                    w.WriteObjectPath(Root + "/prompt/1");
                });
                break;

            case ("org.freedesktop.Secret.Prompt", "Prompt"):
            {
                PromptsShown++;
                Replies.Send(context, null);
                var dismissed = DismissPrompt;
                if (!dismissed)
                {
                    Locked = false;
                }

                var writer = context.Connection.GetMessageWriter();
                try
                {
                    writer.WriteSignalHeader(null, request.PathAsString!, "org.freedesktop.Secret.Prompt", "Completed", "bv");
                    writer.WriteBool(dismissed);
                    writer.WriteVariantString(string.Empty);
                    context.Connection.TrySendMessage(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }

                break;
            }

            case ("org.freedesktop.Secret.Service", "GetSecrets"):
            {
                var items = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToHashSet(StringComparer.Ordinal);
                var session = reader.ReadObjectPathAsString();
                var hits = Entries.Where(e => items.Contains(e.Path)).ToList();
                Replies.Send(context, "a{o(oayays)}", (ref MessageWriter w) =>
                {
                    var start = w.WriteDictionaryStart();
                    foreach (var e in hits)
                    {
                        w.WriteDictionaryEntryStart();
                        w.WriteObjectPath(e.Path);
                        w.WriteStructureStart();
                        w.WriteObjectPath(session);
                        w.WriteArray(Array.Empty<byte>());
                        w.WriteArray(e.Secret);
                        w.WriteString("text/plain");
                    }

                    w.WriteDictionaryEnd(start);
                });
                break;
            }

            case ("org.freedesktop.Secret.Collection", "CreateItem"):
            {
                string label = string.Empty;
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                var props = reader.ReadDictionaryStart();
                while (reader.HasNext(props))
                {
                    reader.AlignStruct();
                    var key = reader.ReadString();
                    var value = reader.ReadVariantValue();
                    if (key == "org.freedesktop.Secret.Item.Label")
                    {
                        label = value.GetString();
                    }
                    else if (key == "org.freedesktop.Secret.Item.Attributes")
                    {
                        attributes = new Dictionary<string, string>(value.GetDictionary<string, string>(), StringComparer.Ordinal);
                    }
                }

                reader.AlignStruct();
                reader.ReadObjectPathAsString();
                reader.ReadArrayOfByte();
                var secret = reader.ReadArrayOfByte();
                reader.ReadString();
                var replace = reader.ReadBool();
                var existing = Entries.FirstOrDefault(e => replace && e.Attributes.Count == attributes.Count && e.Attributes.All(a => attributes.GetValueOrDefault(a.Key) == a.Value));
                if (existing is not null)
                {
                    Entries.Remove(existing);
                }

                var path = existing?.Path ?? $"{Root}/collection/login/{++_next}";
                Entries.Add(new Entry(path, label, attributes, secret));
                Replies.Send(context, "oo", (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(path);
                    w.WriteObjectPath("/");
                });
                break;
            }

            case ("org.freedesktop.Secret.Item", "Delete"):
                Entries.RemoveAll(e => e.Path == request.PathAsString);
                Replies.Send(context, "o", static (ref MessageWriter w) => w.WriteObjectPath("/"));
                break;

            default:
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string> ReadStringDictionary(ref Reader reader)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            result[reader.ReadString()] = reader.ReadString();
        }

        return result;
    }
}

/// <summary><c>org.freedesktop.Notifications</c>: merkt sich jede Benachrichtigung.</summary>
public sealed class FakeNotificationService : IPathMethodHandler
{
    public sealed record Shown(string AppName, string Icon, string Summary, string Body, string? DesktopEntry, string[] Actions, string? Category);

    /// <summary>Verbindung des Dienstes (von <see cref="PrivateBus.ServeAsync"/>) – zum Senden von Signalen.</summary>
    public DBusConnection? Connection { get; set; }

    /// <summary>Klick simulieren: Signal <c>ActionInvoked(id, key)</c>.</summary>
    public void Invoke(uint id, string key)
    {
        var writer = Connection!.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, Path, "org.freedesktop.Notifications", "ActionInvoked", "us");
            writer.WriteUInt32(id);
            writer.WriteString(key);
            Connection.TrySendMessage(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    public List<Shown> Notifications { get; } = [];

    public string Path => "/org/freedesktop/Notifications";

    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (request.InterfaceAsString != "org.freedesktop.Notifications" || request.MemberAsString != "Notify" || request.SignatureAsString != "susssasa{sv}i")
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        var reader = request.GetBodyReader();
        var app = reader.ReadString();
        reader.ReadUInt32();
        var icon = reader.ReadString();
        var summary = reader.ReadString();
        var body = reader.ReadString();
        var actions = reader.ReadArrayOfString();
        var hints = reader.ReadDictionaryOfStringToVariantValue();
        reader.ReadInt32();
        Notifications.Add(new Shown(app, icon, summary, body, hints.TryGetValue("desktop-entry", out var entry) ? entry.GetString() : null,
            actions, hints.TryGetValue("category", out var category) ? category.GetString() : null));
        var id = (uint)Notifications.Count;
        Replies.Send(context, "u", (ref MessageWriter w) => w.WriteUInt32(id));
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// <c>org.kde.StatusNotifierWatcher</c> wie in KDE/der GNOME-Erweiterung: merkt sich angemeldete Symbole (Busname des
/// Absenders), meldet <c>IsStatusNotifierHostRegistered</c>.
/// </summary>
public sealed class FakeStatusNotifierWatcher : IPathMethodHandler
{
    public List<string> Registered { get; } = [];

    /// <summary>Zeigt ein Panel Symbole an?</summary>
    public bool HostRegistered { get; set; } = true;

    public string Path => "/StatusNotifierWatcher";

    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case ("org.kde.StatusNotifierWatcher", "RegisterStatusNotifierItem"):
                var service = reader.ReadString();
                Registered.Add(service.StartsWith('/') ? request.SenderAsString! : service);
                Replies.Send(context, null);
                break;
            case ("org.freedesktop.DBus.Properties", "Get"):
                reader.ReadString();
                var name = reader.ReadString();
                var host = HostRegistered;
                if (name == "IsStatusNotifierHostRegistered")
                {
                    Replies.Send(context, "v", (ref MessageWriter w) => w.WriteVariantBool(host));
                }
                else
                {
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                }

                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// <c>org.freedesktop.portal.Desktop</c> mit Notification- und Background-Portal (wie xdg-desktop-portal):
/// Benachrichtigungen merken, <c>ActionInvoked</c> simulieren, <c>RequestBackground</c> mit Antwort über das
/// Request-Objekt.
/// </summary>
public sealed class FakeDesktopPortal : IPathMethodHandler
{
    public sealed record Added(string Id, Dictionary<string, VariantValue> Notification);

    public List<Added> Notifications { get; } = [];

    public List<(bool Autostart, string[] Commandline, string Reason)> BackgroundRequests { get; } = [];

    /// <summary>Antwortcode für RequestBackground: 0 = ok, 1 = abgelehnt, 2 = Fehler.</summary>
    public uint BackgroundResponse { get; set; }

    public DBusConnection? Connection { get; set; }

    /// <summary>Antwort auf Screenshot: Code (0 ok, 1 abgebrochen) und <c>uri</c>.</summary>
    public (uint Code, string? Uri) ScreenshotResponse { get; set; } = (0, "file:///tmp/shot.png");

    public List<bool> ScreenshotRequests { get; } = [];

    public string Path => "/org/freedesktop/portal/desktop";

    public bool HandlesChildPaths => false;

    /// <summary>Klick simulieren: <c>ActionInvoked(app_id, id, action, parameter)</c>.</summary>
    public void Invoke(string id, string action)
    {
        var writer = Connection!.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, Path, "org.freedesktop.portal.Notification", "ActionInvoked", "sssav");
            writer.WriteString("com.rebelsofgaming.RebelsOfGaming");
            writer.WriteString(id);
            writer.WriteString(action);
            writer.WriteArray(Array.Empty<VariantValue>());
            Connection.TrySendMessage(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case ("org.freedesktop.portal.Notification", "AddNotification"):
            {
                var id = reader.ReadString();
                Notifications.Add(new Added(id, reader.ReadDictionaryOfStringToVariantValue()));
                Replies.Send(context, null);
                break;
            }

            case ("org.freedesktop.portal.Background", "RequestBackground"):
            {
                reader.ReadString();
                var options = reader.ReadDictionaryOfStringToVariantValue();
                var token = options["handle_token"].GetString();
                var autostart = options["autostart"].GetBool();
                var commandline = options["commandline"].GetArray<string>();
                BackgroundRequests.Add((autostart, commandline, options["reason"].GetString()));
                var sender = request.SenderAsString!.TrimStart(':').Replace('.', '_');
                var handle = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
                Replies.Send(context, "o", (ref MessageWriter w) => w.WriteObjectPath(handle));
                var code = BackgroundResponse;
                var writer = context.Connection.GetMessageWriter();
                try
                {
                    writer.WriteSignalHeader(null, handle, "org.freedesktop.portal.Request", "Response", "ua{sv}");
                    writer.WriteUInt32(code);
                    var dict = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("background");
                    writer.WriteVariantBool(code == 0);
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("autostart");
                    writer.WriteVariantBool(code == 0 && autostart);
                    writer.WriteDictionaryEnd(dict);
                    context.Connection.TrySendMessage(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }

                break;
            }

            case ("org.freedesktop.portal.Screenshot", "Screenshot"):
            {
                reader.ReadString();
                var options = reader.ReadDictionaryOfStringToVariantValue();
                var token = options["handle_token"].GetString();
                ScreenshotRequests.Add(options["interactive"].GetBool());
                var sender = request.SenderAsString!.TrimStart(':').Replace('.', '_');
                var handle = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
                Replies.Send(context, "o", (ref MessageWriter w) => w.WriteObjectPath(handle));
                var (code, uri) = ScreenshotResponse;
                var writer = context.Connection.GetMessageWriter();
                try
                {
                    writer.WriteSignalHeader(null, handle, "org.freedesktop.portal.Request", "Response", "ua{sv}");
                    writer.WriteUInt32(code);
                    var dict = writer.WriteDictionaryStart();
                    if (uri is not null)
                    {
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("uri");
                        writer.WriteVariantString(uri);
                    }

                    writer.WriteDictionaryEnd(dict);
                    context.Connection.TrySendMessage(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }

                break;
            }

            default:
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }
}
