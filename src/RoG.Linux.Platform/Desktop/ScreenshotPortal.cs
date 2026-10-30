using System.Security.Cryptography;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Ergebnis eines Bildschirmfotos über das Portal.</summary>
public enum ScreenshotStatus
{
    /// <summary>Aufgenommen – <see cref="ScreenshotResult.Path"/> zeigt auf die Datei.</summary>
    Taken,

    /// <summary>Im Dialog des Systems abgebrochen.</summary>
    Cancelled,

    /// <summary>Kein Portal bzw. Fehler.</summary>
    Unavailable,
}

/// <summary>Status und (bei Erfolg) lokaler Pfad des Bildschirmfotos.</summary>
public sealed record ScreenshotResult(ScreenshotStatus Status, string? Path = null);

/// <summary>„Bildschirmfoto hochladen“ (Owner-Entscheidung F8): Aufnahme über das System, keine eigene Bildschirmaufnahme.</summary>
public interface IScreenshotService
{
    /// <summary>
    /// Bildschirmfoto aufnehmen. <paramref name="interactive"/> = der Desktop zeigt seinen Dialog (Bereich, Fenster,
    /// ganzer Bildschirm). Die App sieht nur das fertige Bild, das der Benutzer freigibt.
    /// </summary>
    Task<ScreenshotResult> TakeAsync(bool interactive = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>org.freedesktop.portal.Screenshot.Screenshot</c> (xdg-desktop-portal, GNOME/KDE/wlroots, auch in Flatpak ohne
/// Zusatzrecht). Unter Wayland der einzige erlaubte Weg; der Desktop fragt beim ersten Mal nach. Antwort kommt als
/// <c>Request.Response</c> mit <c>uri</c> (<c>file://…</c>, in Flatpak über das Dokumenten-Portal freigegeben).
/// </summary>
public sealed class ScreenshotPortal(string? address = null) : IScreenshotService
{
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";

    private readonly string? _address = address ?? DBusAddress.Session;

    /// <summary>Wie lange auf die Antwort gewartet wird (der Dialog kann offen bleiben).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public async Task<ScreenshotResult> TakeAsync(bool interactive = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_address))
        {
            return new ScreenshotResult(ScreenshotStatus.Unavailable);
        }

        try
        {
            using var connection = new DBusConnection(_address);
            await connection.ConnectAsync().ConfigureAwait(false);
            var token = "rog" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
            var handle = $"{PortalPath}/request/{sender}/{token}";

            var response = new TaskCompletionSource<(uint Code, string? Uri)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = await connection.WatchSignalAsync(
                PortalName,
                handle,
                "org.freedesktop.portal.Request",
                "Response",
                static (m, _) =>
                {
                    var r = m.GetBodyReader();
                    var code = r.ReadUInt32();
                    var results = r.ReadDictionaryOfStringToVariantValue();
                    return (code, results.TryGetValue("uri", out var u) ? u.GetString() : null);
                },
                (Notification<(uint, string?)> n) =>
                {
                    if (n.HasValue)
                    {
                        response.TrySetResult(n.Value);
                    }
                },
                ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);

            var writer = connection.GetMessageWriter();
            MessageBuffer call;
            try
            {
                writer.WriteMethodCallHeader(PortalName, PortalPath, "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}", MessageFlags.None);
                writer.WriteString(string.Empty); // kein Elternfenster (Wayland-Handle bräuchte xdg-foreign)
                var options = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("handle_token");
                writer.WriteVariantString(token);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("modal");
                writer.WriteVariantBool(true);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("interactive");
                writer.WriteVariantBool(interactive);
                writer.WriteDictionaryEnd(options);
                call = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            await connection.CallMethodAsync(call).ConfigureAwait(false);
            var (code, uri) = await response.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            return code switch
            {
                0 when ToLocalPath(uri) is { } path => new ScreenshotResult(ScreenshotStatus.Taken, path),
                1 => new ScreenshotResult(ScreenshotStatus.Cancelled),
                _ => new ScreenshotResult(ScreenshotStatus.Unavailable),
            };
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException)
        {
            return new ScreenshotResult(ScreenshotStatus.Unavailable);
        }
    }

    /// <summary>Nur lokale <c>file://</c>-Adressen annehmen.</summary>
    internal static string? ToLocalPath(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.IsFile && string.IsNullOrEmpty(u.Host) ? u.LocalPath : null;
}
