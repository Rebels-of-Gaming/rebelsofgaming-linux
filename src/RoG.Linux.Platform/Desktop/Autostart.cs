using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Ergebnis einer Änderung am Autostart.</summary>
public enum AutostartResult
{
    Enabled,
    Disabled,

    /// <summary>Im Dialog des Systems abgelehnt (Portal) – nichts geändert.</summary>
    Denied,

    /// <summary>Nicht möglich (kein Portal, Ordner nicht schreibbar …).</summary>
    Failed,
}

/// <summary>„Beim Anmelden starten“ (Gegenstück zur StartupTask der Windows-App); Start mit <c>--background</c>.</summary>
public interface IAutostart
{
    /// <summary>Läuft der Autostart über das Background-Portal (Flatpak)? Dann fragt das System beim Einschalten nach.</summary>
    bool UsesPortal { get; }

    /// <summary>Aktueller Stand; <c>null</c> = nicht lesbar (Portal – die App merkt sich den letzten Stand selbst).</summary>
    Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Ein- oder ausschalten; <paramref name="reason"/> zeigt das Portal im Dialog an.</summary>
    Task<AutostartResult> SetAsync(bool enable, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Außerhalb von Flatpak: Eintrag nach der XDG-Autostart-Spezifikation
/// (<c>$XDG_CONFIG_HOME/autostart/&lt;App-ID&gt;.desktop</c>), der dieses Programm mit <c>--background</c> startet.
/// GNOME, KDE, XFCE und der Desktopmodus des Steam Deck werten ihn beim Anmelden aus.
/// </summary>
public sealed class XdgAutostart(string configHome, string executable) : IAutostart
{
    public const string BackgroundArgument = "--background";

    public string FilePath { get; } = Path.Combine(configHome, "autostart", AppIdentity.AppId + ".desktop");

    public bool UsesPortal => false;

    public Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Task.FromResult<bool?>(false);
            }

            // „Hidden=true“ bzw. „X-GNOME-Autostart-enabled=false“ (vom Desktop abgeschaltet) zählen als aus
            var text = File.ReadAllText(FilePath);
            var off = text.Contains("\nHidden=true", StringComparison.Ordinal) || text.Contains("\nX-GNOME-Autostart-enabled=false", StringComparison.Ordinal);
            return Task.FromResult<bool?>(!off);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<bool?>(null);
        }
    }

    public async Task<AutostartResult> SetAsync(bool enable, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!enable)
            {
                File.Delete(FilePath);
                return AutostartResult.Disabled;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, Entry(executable, reason), cancellationToken).ConfigureAwait(false);
            File.Move(tmp, FilePath, overwrite: true);
            return AutostartResult.Enabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AutostartResult.Failed;
        }
    }

    /// <summary>Inhalt der .desktop-Datei; <c>Exec</c> nach Desktop-Entry-Spezifikation in Anführungszeichen.</summary>
    internal static string Entry(string executable, string comment) =>
        string.Create(CultureInfo.InvariantCulture, $"""
            [Desktop Entry]
            Type=Application
            Name=Rebels of Gaming
            Comment={comment.Replace('\n', ' ')}
            Exec={QuoteExec(executable)} {BackgroundArgument}
            Icon={AppIdentity.AppId}
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);

    internal static string QuoteExec(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c is '"' or '`' or '$' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }
}

/// <summary>
/// In Flatpak: <c>org.freedesktop.portal.Background.RequestBackground</c> mit <c>autostart</c> und Befehlszeile
/// <c>rebelsofgaming --background</c>. Das System fragt beim ersten Mal nach (GNOME) und legt den Autostart-Eintrag
/// außerhalb der Sandbox an; lesen lässt er sich von innen nicht – deshalb liefert <see cref="IsEnabledAsync"/>
/// <c>null</c>.
/// </summary>
public sealed class PortalAutostart(string? address = null, string command = "rebelsofgaming") : IAutostart
{
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";

    private readonly string? _address = address ?? DBusAddress.Session;

    /// <summary>Wie lange auf die Antwort (Dialog des Systems) gewartet wird.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public bool UsesPortal => true;

    public Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(null);

    public async Task<AutostartResult> SetAsync(bool enable, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_address))
        {
            return AutostartResult.Failed;
        }

        try
        {
            using var connection = new DBusConnection(_address);
            await connection.ConnectAsync().ConfigureAwait(false);
            var token = "rog" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
            var handle = $"{PortalPath}/request/{sender}/{token}";

            // Antwort abonnieren, bevor die Anfrage rausgeht (sonst kann sie verloren gehen)
            var response = new TaskCompletionSource<(uint Code, bool Autostart)>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    return (code, results.TryGetValue("autostart", out var a) && a.GetBool());
                },
                (Notification<(uint, bool)> n) =>
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
                writer.WriteMethodCallHeader(PortalName, PortalPath, "org.freedesktop.portal.Background", "RequestBackground", "sa{sv}", MessageFlags.None);
                writer.WriteString(string.Empty); // kein Elternfenster (Wayland-Handle bräuchte xdg-foreign)
                var options = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("handle_token");
                writer.WriteVariantString(token);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("reason");
                writer.WriteVariantString(reason);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("autostart");
                writer.WriteVariantBool(enable);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("commandline");
                writer.WriteSignature("as");
                writer.WriteArray(new[] { command, XdgAutostart.BackgroundArgument });
                writer.WriteDictionaryEntryStart();
                writer.WriteString("dbus-activatable");
                writer.WriteVariantBool(false);
                writer.WriteDictionaryEnd(options);
                call = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            await connection.CallMethodAsync(call).ConfigureAwait(false);
            var (code, autostart) = await response.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            return code switch
            {
                0 => autostart ? AutostartResult.Enabled : AutostartResult.Disabled,
                1 => AutostartResult.Denied,
                _ => AutostartResult.Failed,
            };
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException)
        {
            return AutostartResult.Failed;
        }
    }
}
