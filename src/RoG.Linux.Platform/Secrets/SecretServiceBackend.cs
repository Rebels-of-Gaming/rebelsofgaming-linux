using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Secrets;

/// <summary>
/// Secret Service API (<c>org.freedesktop.secrets</c>) über D-Bus – GNOME Keyring, KWallet (ab Plasma 5.97 / KF6),
/// KeePassXC. Sitzung mit Algorithmus <c>plain</c>: Das Geheimnis geht nur über den lokalen Session-Bus an den Dienst,
/// der es verschlüsselt ablegt. Gesperrte Sammlungen werden über den Prompt des Dienstes entsperrt.
/// In Flatpak braucht das <c>--talk-name=org.freedesktop.secrets</c> (siehe Manifest).
/// </summary>
public sealed class SecretServiceBackend : ISecretBackend, IAsyncDisposable, IDisposable
{
    private const string Service = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string CollectionInterface = "org.freedesktop.Secret.Collection";
    private const string ItemInterface = "org.freedesktop.Secret.Item";
    private const string PromptInterface = "org.freedesktop.Secret.Prompt";
    private const string DefaultCollection = "/org/freedesktop/secrets/aliases/default";
    private const string NoPrompt = "/";

    /// <summary>Wie lange auf eine Eingabe im Entsperr-Dialog des Schlüsselbunds gewartet wird.</summary>
    public static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _address;
    private DBusConnection? _connection;
    private string? _session;

    /// <param name="address">D-Bus-Adresse; <c>null</c> = Session-Bus aus <c>DBUS_SESSION_BUS_ADDRESS</c>.</param>
    public SecretServiceBackend(string? address = null)
    {
        _address = address ?? DBusAddress.Session;
    }

    public async Task<byte[]?> LookupAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return await GuardAsync(async connection =>
        {
            var (unlocked, locked) = await SearchAsync(connection, attributes).ConfigureAwait(false);
            if (unlocked.Length == 0 && locked.Length > 0)
            {
                unlocked = await UnlockAsync(connection, locked, cancellationToken).ConfigureAwait(false);
            }

            if (unlocked.Length == 0)
            {
                return null;
            }

            var session = await EnsureSessionAsync(connection).ConfigureAwait(false);
            return await GetSecretAsync(connection, unlocked[0], session).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task StoreAsync(string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(secret);
        await GuardAsync<object?>(async connection =>
        {
            var session = await EnsureSessionAsync(connection).ConfigureAwait(false);
            var (_, prompt) = await CreateItemAsync(connection, label, attributes, secret, session).ConfigureAwait(false);
            if (prompt != NoPrompt)
            {
                // Sammlung gesperrt: Dienst fragt nach dem Passwort, danach ist der Eintrag angelegt
                await RunPromptAsync(connection, prompt, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }).ConfigureAwait(false);
    }

    public async Task ClearAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        await GuardAsync<object?>(async connection =>
        {
            var (unlocked, locked) = await SearchAsync(connection, attributes).ConfigureAwait(false);
            foreach (var item in unlocked.Concat(locked))
            {
                var prompt = await CallAsync(connection, item, ItemInterface, "Delete", null, null, static (m, _) => m.GetBodyReader().ReadObjectPathAsString()).ConfigureAwait(false);
                if (prompt != NoPrompt)
                {
                    await RunPromptAsync(connection, prompt, cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
        }).ConfigureAwait(false);
    }

    private async Task<T> GuardAsync<T>(Func<DBusConnection, Task<T>> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            return await action(connection).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex)
        {
            ResetConnection();
            throw new SecretStoreUnavailableException($"Secret Service: {ex.ErrorName}", ex);
        }
        catch (DBusConnectFailedException ex)
        {
            ResetConnection();
            throw new SecretStoreUnavailableException("Kein D-Bus-Session-Bus erreichbar.", ex);
        }
        catch (DBusConnectionClosedException ex)
        {
            ResetConnection();
            throw new SecretStoreUnavailableException("D-Bus-Verbindung getrennt.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DBusConnection> ConnectAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        if (string.IsNullOrEmpty(_address))
        {
            throw new SecretStoreUnavailableException("Kein D-Bus-Session-Bus (DBUS_SESSION_BUS_ADDRESS fehlt).");
        }

        var connection = new DBusConnection(_address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        _connection = connection;
        return connection;
    }

    private void ResetConnection()
    {
        _connection?.Dispose();
        _connection = null;
        _session = null;
    }

    private async Task<string> EnsureSessionAsync(DBusConnection connection)
    {
        if (_session is not null)
        {
            return _session;
        }

        _session = await CallAsync(
            connection,
            ServicePath,
            ServiceInterface,
            "OpenSession",
            "sv",
            static (ref MessageWriter w) =>
            {
                w.WriteString("plain");
                w.WriteVariantString(string.Empty);
            },
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                reader.ReadVariantValue();
                return reader.ReadObjectPathAsString();
            }).ConfigureAwait(false);
        return _session;
    }

    private static Task<(string[] Unlocked, string[] Locked)> SearchAsync(DBusConnection connection, IReadOnlyDictionary<string, string> attributes) =>
        CallAsync(
            connection,
            ServicePath,
            ServiceInterface,
            "SearchItems",
            "a{ss}",
            (ref MessageWriter w) => WriteStringDictionary(ref w, attributes),
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var unlocked = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
                var locked = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
                return (unlocked, locked);
            });

    private static async Task<string[]> UnlockAsync(DBusConnection connection, string[] items, CancellationToken cancellationToken)
    {
        var (unlocked, prompt) = await CallAsync(
            connection,
            ServicePath,
            ServiceInterface,
            "Unlock",
            "ao",
            (ref MessageWriter w) => w.WriteArray(items.Select(i => new ObjectPath(i)).ToArray()),
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var done = reader.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
                return (done, reader.ReadObjectPathAsString());
            }).ConfigureAwait(false);
        if (prompt == NoPrompt)
        {
            return unlocked;
        }

        await RunPromptAsync(connection, prompt, cancellationToken).ConfigureAwait(false);
        return items;
    }

    private static Task<byte[]?> GetSecretAsync(DBusConnection connection, string item, string session) =>
        CallAsync(
            connection,
            ServicePath,
            ServiceInterface,
            "GetSecrets",
            "aoo",
            (ref MessageWriter w) =>
            {
                w.WriteArray(new[] { new ObjectPath(item) });
                w.WriteObjectPath(session);
            },
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var end = reader.ReadDictionaryStart();
                byte[]? value = null;
                while (reader.HasNext(end))
                {
                    reader.AlignStruct();
                    reader.ReadObjectPathAsString();
                    reader.AlignStruct();
                    reader.ReadObjectPathAsString(); // Sitzung
                    reader.ReadArrayOfByte(); // Parameter (bei plain leer)
                    var bytes = reader.ReadArrayOfByte();
                    reader.ReadString(); // Inhaltstyp
                    value ??= bytes;
                }

                return value;
            });

    private static Task<(string Item, string Prompt)> CreateItemAsync(DBusConnection connection, string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, string session) =>
        CallAsync(
            connection,
            DefaultCollection,
            CollectionInterface,
            "CreateItem",
            "a{sv}(oayays)b",
            (ref MessageWriter w) =>
            {
                var properties = w.WriteDictionaryStart();
                w.WriteDictionaryEntryStart();
                w.WriteString("org.freedesktop.Secret.Item.Label");
                w.WriteVariantString(label);
                w.WriteDictionaryEntryStart();
                w.WriteString("org.freedesktop.Secret.Item.Attributes");
                w.WriteSignature("a{ss}"); // Variant von Hand: Signatur, dann Wert
                WriteStringDictionary(ref w, attributes);
                w.WriteDictionaryEnd(properties);

                w.WriteStructureStart();
                w.WriteObjectPath(session);
                w.WriteArray(Array.Empty<byte>());
                w.WriteArray(secret);
                w.WriteString("text/plain; charset=utf8");

                w.WriteBool(true); // gleichen Eintrag ersetzen
            },
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
            });

    /// <summary>Prompt des Dienstes zeigen und auf <c>Completed</c> warten; abgelehnt → <see cref="SecretStoreUnavailableException"/>.</summary>
    private static async Task RunPromptAsync(DBusConnection connection, string prompt, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = PromptInterface,
            Member = "Completed",
            Path = prompt,
        };
        using var subscription = await connection.AddMatchAsync(
            rule,
            static (m, _) => m.GetBodyReader().ReadBool(),
            (Notification<bool> n) =>
            {
                if (n.HasValue)
                {
                    completed.TrySetResult(n.Value);
                }
                else if (n.IsCompletion)
                {
                    completed.TrySetException(n.Exception);
                }
            },
            emitOnCapturedContext: false,
            ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnConnectionFailed | ObserverFlags.EmitOnReaderFailed).ConfigureAwait(false);

        await CallAsync<object?>(connection, prompt, PromptInterface, "Prompt", "s", static (ref MessageWriter w) => w.WriteString(string.Empty), static (_, _) => null).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PromptTimeout);
        bool dismissed;
        try
        {
            dismissed = await completed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreUnavailableException("Schlüsselbund wurde nicht rechtzeitig entsperrt.");
        }

        if (dismissed)
        {
            throw new SecretStoreUnavailableException("Entsperren des Schlüsselbunds abgelehnt.");
        }
    }

    private static void WriteStringDictionary(ref MessageWriter writer, IReadOnlyDictionary<string, string> values)
    {
        var start = writer.WriteDictionaryStart();
        foreach (var (key, value) in values)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(key);
            writer.WriteString(value);
        }

        writer.WriteDictionaryEnd(start);
    }

    private delegate void BodyWriter(ref MessageWriter writer);

    private static Task<T> CallAsync<T>(
        DBusConnection connection,
        string path,
        string @interface,
        string member,
        string? signature,
        BodyWriter? body,
        MessageValueReader<T> reader)
    {
        // MessageWriter ist ein Struct mit Schreibposition – nur per ref weiterreichen, nie kopieren
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Service, path, @interface, member, signature, MessageFlags.None);
            body?.Invoke(ref writer);
            return connection.CallMethodAsync(writer.CreateMessage(), reader, null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    public void Dispose()
    {
        ResetConnection();
        _gate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
