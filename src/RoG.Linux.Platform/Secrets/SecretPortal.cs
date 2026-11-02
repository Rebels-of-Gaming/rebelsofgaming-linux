using System.IO.Pipes;
using System.Security.Cryptography;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Secrets;

/// <summary>Liefert das Geheimnis der App aus dem Secret-Portal (ein Geheimnis je App-ID, vom Schlüsselbund verwaltet).</summary>
public interface ISecretPortal
{
    /// <summary>Geheimnis der App holen (beim ersten Aufruf legt der Schlüsselbund es an).</summary>
    /// <exception cref="SecretStoreUnavailableException">Kein Portal, kein Schlüsselbund-Backend, abgelehnt, Zeitüberschreitung.</exception>
    Task<byte[]> RetrieveSecretAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>org.freedesktop.portal.Secret.RetrieveSecret</c> (xdg-desktop-portal ≥ 1.5, Backend z. B. gnome-keyring oder
/// KWallet über ksecretd). Die App übergibt das Schreib-Ende einer Pipe; der Schlüsselbund schreibt das Geheimnis
/// dieser App-ID hinein und meldet sich über <c>Request.Response</c>. Die Sandbox braucht dafür <b>kein</b>
/// <c>--talk-name=org.freedesktop.secrets</c> – die App sieht nur ihr eigenes Geheimnis, nie andere Einträge.
/// </summary>
public sealed class SecretPortal(string? address = null) : ISecretPortal
{
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";

    /// <summary>Weniger ist kein brauchbarer Schlüssel (gnome-keyring liefert 64 Byte).</summary>
    public const int MinimumSecretLength = 16;

    /// <summary>Obergrenze beim Lesen der Pipe – das Portal liefert nur einen kurzen Schlüssel.</summary>
    public const int MaximumSecretLength = 4096;

    private readonly string? _address = address ?? DBusAddress.Session;

    /// <summary>Wie lange auf die Antwort gewartet wird (der Schlüsselbund kann zum Entsperren nachfragen).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public async Task<byte[]> RetrieveSecretAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_address))
        {
            throw new SecretStoreUnavailableException("Kein D-Bus-Session-Bus (DBUS_SESSION_BUS_ADDRESS fehlt).");
        }

        try
        {
            using var connection = new DBusConnection(_address);
            await connection.ConnectAsync().ConfigureAwait(false);
            var token = "rog" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
            var handle = $"{PortalPath}/request/{sender}/{token}";

            var response = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = await connection.WatchSignalAsync(
                PortalName,
                handle,
                "org.freedesktop.portal.Request",
                "Response",
                static (m, _) => m.GetBodyReader().ReadUInt32(),
                (Notification<uint> n) =>
                {
                    if (n.HasValue)
                    {
                        response.TrySetResult(n.Value);
                    }
                },
                ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);

            using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            var writeEnd = pipe.ClientSafePipeHandle;
            var writer = connection.GetMessageWriter();
            MessageBuffer call;
            try
            {
                writer.WriteMethodCallHeader(PortalName, PortalPath, "org.freedesktop.portal.Secret", "RetrieveSecret", "ha{sv}", MessageFlags.None);
                writer.WriteHandle(writeEnd);
                var options = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("handle_token");
                writer.WriteVariantString(token);
                writer.WriteDictionaryEnd(options);
                call = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            try
            {
                await connection.CallMethodAsync(call).ConfigureAwait(false);
            }
            finally
            {
                // Eigenes Schreib-Ende schließen, sonst kommt beim Lesen nie das Dateiende
                pipe.DisposeLocalCopyOfClientHandle();
            }

            var code = await response.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            if (code != 0)
            {
                throw new SecretStoreUnavailableException(code == 1 ? "Secret-Portal: abgelehnt." : "Secret-Portal: Fehler.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var secret = await ReadCappedAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (secret.Length < MinimumSecretLength)
            {
                CryptographicOperations.ZeroMemory(secret);
                throw new SecretStoreUnavailableException("Secret-Portal: kein brauchbares Geheimnis.");
            }

            return secret;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreUnavailableException("Secret-Portal nicht erreichbar.", ex);
        }
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumSecretLength + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            // WaitAsync: Lesen aus einer Pipe lässt sich unter Linux nicht zuverlässig abbrechen
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total > MaximumSecretLength)
        {
            CryptographicOperations.ZeroMemory(buffer);
            throw new SecretStoreUnavailableException("Secret-Portal: Antwort zu groß.");
        }

        var result = buffer.AsSpan(0, total).ToArray();
        CryptographicOperations.ZeroMemory(buffer);
        return result;
    }
}
