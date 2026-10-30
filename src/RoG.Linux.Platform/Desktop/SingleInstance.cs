using System.Net.Sockets;
using System.Text;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>
/// Nur eine laufende App je Benutzer. Die erste Instanz besitzt den D-Bus-Namen der App-ID auf dem Session-Bus und
/// stellt <c>org.freedesktop.Application</c> unter <see cref="ObjectPath"/> bereit (so machen es GTK/Qt-Apps auch;
/// Flatpak erlaubt jeder App, ihre eigene App-ID als Namen zu besitzen). Jeder weitere Start ruft dort
/// <c>Activate</c> bzw. <c>Open([rog://…])</c> auf und endet sofort; die erste Instanz holt ihr Fenster nach vorn und
/// öffnet den Link. Ohne Session-Bus (z. B. minimale Sitzungen) übernimmt ein Unix-Socket unter
/// <c>$XDG_RUNTIME_DIR/rebelsofgaming/instance.sock</c> (Rechte 0700 auf dem Ordner) dieselbe Aufgabe.
/// Angenommen werden nur <c>rog://</c>-Links – alles andere wird verworfen.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string ObjectPath = "/com/rebelsofgaming/RebelsOfGaming";
    public const string ApplicationInterface = "org.freedesktop.Application";

    private const string SocketMagic = "ROG-INSTANCE-1";
    private static readonly TimeSpan s_callTimeout = TimeSpan.FromSeconds(3);

    private readonly Lock _lock = new();
    private readonly List<Uri?> _pending = [];
    private DBusConnection? _connection;
    private Socket? _listener;
    private string? _socketPath;
    private EventHandler<ActivationRequest>? _activated;

    private SingleInstance(bool isPrimary, string mechanism)
    {
        IsPrimary = isPrimary;
        Mechanism = mechanism;
    }

    /// <summary>Diese Instanz läuft weiter (erste Instanz bzw. keine Abstimmung möglich).</summary>
    public bool IsPrimary { get; }

    /// <summary><c>dbus</c>, <c>socket</c> oder <c>none</c> (für Diagnose/Tests).</summary>
    public string Mechanism { get; }

    /// <summary>
    /// Eine weitere Instanz wurde gestartet: Fenster nach vorn holen und ggf. den Link öffnen. Kommt auf einem
    /// Hintergrund-Thread. Anfragen vor dem ersten Abonnenten (App startet noch) werden gesammelt und dann zugestellt.
    /// </summary>
    public event EventHandler<ActivationRequest>? Activated
    {
        add
        {
            Uri?[] pending;
            lock (_lock)
            {
                _activated += value;
                pending = [.. _pending];
                _pending.Clear();
            }

            foreach (var link in pending)
            {
                value?.Invoke(this, new ActivationRequest(link));
            }
        }

        remove
        {
            lock (_lock)
            {
                _activated -= value;
            }
        }
    }

    /// <summary>Einstieg aus <c>Main</c> (vor Avalonia, ohne Synchronisationskontext).</summary>
    public static SingleInstance Acquire(IReadOnlyList<string> args, string? busAddress = null, string? runtimeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var link = args.Select(a => TryLink(a)).FirstOrDefault(l => l is not null);
        return AcquireAsync(link, busAddress ?? DBusAddress.Session, runtimeDirectory ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"))
            .GetAwaiter().GetResult();
    }

    public static async Task<SingleInstance> AcquireAsync(Uri? link, string? busAddress, string? runtimeDirectory)
    {
        if (!string.IsNullOrEmpty(busAddress))
        {
            var viaBus = await TryDBusAsync(link, busAddress).ConfigureAwait(false);
            if (viaBus is not null)
            {
                return viaBus;
            }
        }

        if (!string.IsNullOrEmpty(runtimeDirectory) && Path.IsPathRooted(runtimeDirectory))
        {
            var viaSocket = await TrySocketAsync(link, Path.Combine(runtimeDirectory, AppIdentity.DataFolderName)).ConfigureAwait(false);
            if (viaSocket is not null)
            {
                return viaSocket;
            }
        }

        return new SingleInstance(true, "none");
    }

    /// <summary>Nur <c>rog://</c>-Links mit Host (z. B. <c>rog://news/slug</c>).</summary>
    public static Uri? TryLink(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == AppIdentity.DeepLinkScheme && value!.Length <= 2048 ? uri : null;

    private void Raise(Uri? link)
    {
        EventHandler<ActivationRequest>? handler;
        lock (_lock)
        {
            handler = _activated;
            if (handler is null)
            {
                _pending.Add(link);
                return;
            }
        }

        handler(this, new ActivationRequest(link));
    }

    // ---------- D-Bus ----------

    private static async Task<SingleInstance?> TryDBusAsync(Uri? link, string address)
    {
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            var instance = new SingleInstance(true, "dbus");
            // Handler vor dem Namen registrieren: sobald der Name gehört, können Aufrufe kommen
            connection.AddMethodHandler(new ApplicationHandler(instance));
            if (await connection.TryRequestNameAsync(AppIdentity.AppId, RequestNameOptions.None).ConfigureAwait(false))
            {
                instance._connection = connection;
                return instance;
            }

            // Name gehört schon einer laufenden Instanz → weiterreichen
            await ForwardAsync(connection, link).ConfigureAwait(false);
            connection.Dispose();
            return new SingleInstance(false, "dbus");
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException or SocketException)
        {
            // Bus kaputt oder laufende Instanz hängt: lieber selbst starten als gar nicht
            connection.Dispose();
            return null;
        }
    }

    private static async Task ForwardAsync(DBusConnection connection, Uri? link)
    {
        var writer = connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(AppIdentity.AppId, ObjectPath, ApplicationInterface, link is null ? "Activate" : "Open", link is null ? "a{sv}" : "asa{sv}", MessageFlags.NoAutoStart);
            if (link is not null)
            {
                writer.WriteArray(new[] { link.OriginalString });
            }

            WritePlatformData(ref writer);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        await connection.CallMethodAsync(message).WaitAsync(s_callTimeout).ConfigureAwait(false);
    }

    /// <summary><c>platform_data</c> laut Desktop-Entry-Spezifikation; Aktivierungs-Token des Starters, falls vorhanden.</summary>
    private static void WritePlatformData(ref MessageWriter writer)
    {
        var start = writer.WriteDictionaryStart();
        var token = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("activation-token");
            writer.WriteVariantString(token);
        }

        writer.WriteDictionaryEnd(start);
    }

    private sealed class ApplicationHandler(SingleInstance owner) : IPathMethodHandler
    {
        private static readonly ReadOnlyMemory<byte> s_introspection = Encoding.UTF8.GetBytes(
            """
            <interface name="org.freedesktop.Application">
              <method name="Activate"><arg type="a{sv}" name="platform_data" direction="in"/></method>
              <method name="Open"><arg type="as" name="uris" direction="in"/><arg type="a{sv}" name="platform_data" direction="in"/></method>
              <method name="ActivateAction"><arg type="s" name="action_name" direction="in"/><arg type="av" name="parameter" direction="in"/><arg type="a{sv}" name="platform_data" direction="in"/></method>
            </interface>
            """);

        public string Path => ObjectPath;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([s_introspection], ReadOnlySpan<string>.Empty);
                return ValueTask.CompletedTask;
            }

            var request = context.Request;
            if (request.InterfaceAsString != ApplicationInterface)
            {
                context.ReplyUnknownMethodError();
                return ValueTask.CompletedTask;
            }

            switch (request.MemberAsString)
            {
                case "Activate" when request.SignatureAsString == "a{sv}":
                    owner.Raise(null);
                    ReplyEmpty(context);
                    break;
                case "Open" when request.SignatureAsString == "asa{sv}":
                    var links = request.GetBodyReader().ReadArrayOfString().Select(TryLink).Where(l => l is not null).ToList();
                    if (links.Count == 0)
                    {
                        owner.Raise(null);
                    }

                    foreach (var link in links)
                    {
                        owner.Raise(link);
                    }

                    ReplyEmpty(context);
                    break;
                case "ActivateAction":
                    owner.Raise(null);
                    ReplyEmpty(context);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }

            return ValueTask.CompletedTask;
        }

        private static void ReplyEmpty(MethodContext context)
        {
            if (context.NoReplyExpected)
            {
                return;
            }

            // MessageWriter ist ein veränderliches Struct – kein „using var“ (readonly-Kopie), sondern try/finally
            var writer = context.CreateReplyWriter(null);
            try
            {
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    // ---------- Unix-Socket (ohne Session-Bus) ----------

    private static async Task<SingleInstance?> TrySocketAsync(Uri? link, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var path = Path.Combine(directory, "instance.sock");
            var endpoint = new UnixDomainSocketEndPoint(path);

            // Läuft schon eine Instanz? Dann Link schicken und beenden.
            using (var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                try
                {
                    using var timeout = new CancellationTokenSource(s_callTimeout);
                    await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                    var payload = Encoding.UTF8.GetBytes($"{SocketMagic}\n{link?.OriginalString}\n");
                    await client.SendAsync(payload, SocketFlags.None, timeout.Token).ConfigureAwait(false);
                    client.Shutdown(SocketShutdown.Send);
                    return new SingleInstance(false, "socket");
                }
                catch (SocketException)
                {
                    // niemand hört zu → alte Socket-Datei (Absturz) entfernen und selbst übernehmen
                }
            }

            File.Delete(path);
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(endpoint);
            listener.Listen(4);
            var instance = new SingleInstance(true, "socket") { _listener = listener, _socketPath = path };
            _ = instance.AcceptLoopAsync(listener);
            return instance;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task AcceptLoopAsync(Socket listener)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return;
            }

            using (client)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(s_callTimeout);
                    var buffer = new byte[4096];
                    var length = 0;
                    int read;
                    while (length < buffer.Length && (read = await client.ReceiveAsync(buffer.AsMemory(length), SocketFlags.None, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        length += read;
                    }

                    var lines = Encoding.UTF8.GetString(buffer, 0, length).Split('\n');
                    if (lines.Length >= 2 && lines[0] == SocketMagic)
                    {
                        Raise(TryLink(lines[1]));
                    }
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    // kaputte Anfrage ignorieren
                }
            }
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        if (_listener is not null)
        {
            _listener.Dispose();
            _listener = null;
            try
            {
                File.Delete(_socketPath!);
            }
            catch (IOException)
            {
                // egal – wird beim nächsten Start ersetzt
            }
        }
    }
}

/// <summary>Anfrage einer zweiten Instanz: Fenster zeigen und ggf. <see cref="DeepLink"/> öffnen.</summary>
public sealed record ActivationRequest(Uri? DeepLink);
