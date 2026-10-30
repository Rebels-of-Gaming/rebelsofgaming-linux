using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Security;

/// <summary>Ergebnis einer Fingerabdruck-Prüfung.</summary>
public enum FingerprintResult
{
    Match,
    NoMatch,

    /// <summary>Kein fprintd, kein Gerät, kein eingelernter Finger, Gerät belegt.</summary>
    Unavailable,
    Cancelled,
    TimedOut,
    Failed,
}

/// <summary>Zwischenstand während der Prüfung (für den Hinweis unter dem Knopf).</summary>
public enum FingerprintPrompt
{
    /// <summary>Gerät bereit – Finger auflegen.</summary>
    Scan,

    /// <summary>Nicht erkannt, nochmal (fprintd: <c>verify-retry-scan</c>, <c>verify-swipe-too-short</c> …).</summary>
    Retry,
}

/// <summary>Fingerabdruck des Systems – abstrahiert, damit die App-Sperre ohne D-Bus testbar ist.</summary>
public interface IFingerprintVerifier
{
    /// <summary>Gibt es ein Lesegerät mit mindestens einem eingelernten Finger der angemeldeten Person?</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Einen Finger prüfen; bricht nach <paramref name="timeout"/> bzw. über das Token ab.</summary>
    Task<FingerprintResult> VerifyAsync(IProgress<FingerprintPrompt>? progress, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fingerabdruck über <b>fprintd</b> (<c>net.reactivated.Fprint</c> auf dem System-Bus): <c>Manager.GetDefaultDevice</c>
/// → <c>Device.ListEnrolledFingers("")</c> (verfügbar?) bzw. <c>Claim("")</c> → <c>VerifyStart("any")</c> → Signal
/// <c>VerifyStatus(result, done)</c> → <c>VerifyStop</c> → <c>Release</c>. Das Gerät ist nur während der Prüfung belegt
/// (sonst blockiert die App z. B. <c>sudo</c> mit pam_fprintd). Die Berechtigung prüft fprintd selbst über Polkit
/// (<c>net.reactivated.fprint.device.verify</c>, für die aktive Sitzung ohne Rückfrage) – die App braucht keine eigene
/// Polkit-Regel. In Flatpak: <c>--system-talk-name=net.reactivated.Fprint</c>.
/// </summary>
public sealed class FprintdVerifier : IFingerprintVerifier
{
    private const string Service = "net.reactivated.Fprint";
    private const string ManagerPath = "/net/reactivated/Fprint/Manager";
    private const string ManagerInterface = "net.reactivated.Fprint.Manager";
    private const string DeviceInterface = "net.reactivated.Fprint.Device";

    /// <summary>„Angemeldete Person“ für Claim/ListEnrolledFingers.</summary>
    private const string CurrentUser = "";

    private readonly string? _address;

    /// <param name="address">D-Bus-Adresse; <c>null</c> = System-Bus.</param>
    public FprintdVerifier(string? address = null)
    {
        _address = address ?? DBusAddress.System;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            var device = await GetDeviceAsync(connection).ConfigureAwait(false);
            return (await ListFingersAsync(connection, device).ConfigureAwait(false)).Length > 0;
        }
        catch (Exception ex) when (IsDBusFailure(ex))
        {
            return false;
        }
    }

    public async Task<FingerprintResult> VerifyAsync(IProgress<FingerprintPrompt>? progress, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DBusConnection? connection = null;
        string? device = null;
        var claimed = false;
        try
        {
            connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            device = await GetDeviceAsync(connection).ConfigureAwait(false);
            if ((await ListFingersAsync(connection, device).ConfigureAwait(false)).Length == 0)
            {
                return FingerprintResult.Unavailable;
            }

            await CallAsync<object?>(connection, device, DeviceInterface, "Claim", "s", static (ref MessageWriter w) => w.WriteString(CurrentUser), static (_, _) => null).ConfigureAwait(false);
            claimed = true;
            return await RunVerificationAsync(connection, device, progress, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FingerprintResult.Cancelled;
        }
        catch (DBusErrorReplyException ex) when (claimed)
        {
            return ex.ErrorName == "net.reactivated.Fprint.Error.NoEnrolledPrints" ? FingerprintResult.Unavailable : FingerprintResult.Failed;
        }
        catch (Exception ex) when (IsDBusFailure(ex))
        {
            // u. a. AlreadyInUse (anderes Programm prüft gerade), NoSuchDevice, PermissionDenied, kein fprintd
            return FingerprintResult.Unavailable;
        }
        finally
        {
            if (connection is not null)
            {
                if (claimed && device is not null)
                {
                    await TryCallAsync(connection, device, "Release").ConfigureAwait(false);
                }

                connection.Dispose();
            }
        }
    }

    private static async Task<FingerprintResult> RunVerificationAsync(DBusConnection connection, string device, IProgress<FingerprintPrompt>? progress, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<FingerprintResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = DeviceInterface,
            Member = "VerifyStatus",
            Path = device,
        };
        using var subscription = await connection.AddMatchAsync(
            rule,
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                return (Result: reader.ReadString(), Done: reader.ReadBool());
            },
            (Notification<(string Result, bool Done)> n) =>
            {
                if (n.HasValue)
                {
                    var (result, done) = n.Value;
                    switch (result)
                    {
                        case "verify-match":
                            finished.TrySetResult(FingerprintResult.Match);
                            break;
                        case "verify-no-match":
                            finished.TrySetResult(FingerprintResult.NoMatch);
                            break;
                        case "verify-retry-scan" or "verify-swipe-too-short" or "verify-finger-not-centered" or "verify-remove-and-retry" when !done:
                            progress?.Report(FingerprintPrompt.Retry);
                            break;
                        default:
                            // verify-disconnected, verify-unknown-error oder Wiederholung mit done=true
                            finished.TrySetResult(FingerprintResult.Failed);
                            break;
                    }
                }
                else if (n.IsCompletion)
                {
                    finished.TrySetResult(FingerprintResult.Failed);
                }
            },
            emitOnCapturedContext: false,
            ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnConnectionFailed | ObserverFlags.EmitOnReaderFailed).ConfigureAwait(false);

        await CallAsync<object?>(connection, device, DeviceInterface, "VerifyStart", "s", static (ref MessageWriter w) => w.WriteString("any"), static (_, _) => null).ConfigureAwait(false);
        progress?.Report(FingerprintPrompt.Scan);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            return await finished.Task.WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested ? FingerprintResult.Cancelled : FingerprintResult.TimedOut;
        }
        finally
        {
            // Auch nach „fertig“ Pflicht, sonst bleibt die Prüfung im Gerät offen
            await TryCallAsync(connection, device, "VerifyStop").ConfigureAwait(false);
        }
    }

    private async Task<DBusConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(_address))
        {
            throw new DBusConnectFailedException("No system bus address.");
        }

        var connection = new DBusConnection(_address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static Task<string> GetDeviceAsync(DBusConnection connection) =>
        CallAsync(connection, ManagerPath, ManagerInterface, "GetDefaultDevice", null, null, static (m, _) => m.GetBodyReader().ReadObjectPathAsString());

    private static async Task<string[]> ListFingersAsync(DBusConnection connection, string device)
    {
        try
        {
            return await CallAsync(connection, device, DeviceInterface, "ListEnrolledFingers", "s", static (ref MessageWriter w) => w.WriteString(CurrentUser), static (m, _) => m.GetBodyReader().ReadArrayOfString()).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex) when (ex.ErrorName == "net.reactivated.Fprint.Error.NoEnrolledPrints")
        {
            return [];
        }
    }

    private static async Task TryCallAsync(DBusConnection connection, string device, string member)
    {
        try
        {
            await CallAsync<object?>(connection, device, DeviceInterface, member, null, null, static (_, _) => null).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDBusFailure(ex))
        {
            // Aufräumen ist bestmöglich – fprintd gibt das Gerät beim Trennen der Verbindung ohnehin frei
        }
    }

    private static bool IsDBusFailure(Exception ex) =>
        ex is DBusErrorReplyException or DBusConnectFailedException or DBusConnectionClosedException or DBusUnexpectedValueException or InvalidOperationException;

    private delegate void BodyWriter(ref MessageWriter writer);

    private static Task<T> CallAsync<T>(DBusConnection connection, string path, string @interface, string member, string? signature, BodyWriter? body, MessageValueReader<T> reader)
    {
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
}
