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
    public sealed record Shown(string AppName, string Icon, string Summary, string Body, string? DesktopEntry);

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
        reader.ReadArrayOfString();
        var hints = reader.ReadDictionaryOfStringToVariantValue();
        reader.ReadInt32();
        Notifications.Add(new Shown(app, icon, summary, body, hints.TryGetValue("desktop-entry", out var entry) ? entry.GetString() : null));
        var id = (uint)Notifications.Count;
        Replies.Send(context, "u", (ref MessageWriter w) => w.WriteUInt32(id));
        return ValueTask.CompletedTask;
    }
}
