using RoG.Core.Auth;
using RoG.Core.Features.Servers;

namespace RoG.Linux.Platform.Desktop;

/// <summary>
/// Öffnet Adressen über <c>xdg-open</c> im Standardbrowser bzw. im registrierten Programm (<c>steam://</c>, <c>et://</c>).
/// In Flatpak leitet <c>xdg-open</c> an das OpenURI-Portal weiter – der Login läuft also immer im System-Browser
/// (RFC 8252, nie in einer WebView).
/// </summary>
public sealed class XdgLauncher(IProcessLauncher? processes = null) : IBrowserLauncher, IExternalLauncher
{
    /// <summary>Erlaubte Schemata – nie <c>file:</c> oder Skript-Schemata aus Inhalten der API öffnen.</summary>
    private static readonly HashSet<string> s_webSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private static readonly HashSet<string> s_gameSchemes = new(StringComparer.OrdinalIgnoreCase) { "steam", "et", "wolf", "ts3server", "minecraft", "discord" };

    private readonly IProcessLauncher _processes = processes ?? new ProcessLauncher();

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && s_webSchemes.Contains(uri.Scheme) ? RunAsync(uri, cancellationToken) : Task.FromResult(false);
    }

    /// <summary>
    /// Ist ein Programm für das Schema registriert (<c>xdg-mime query default x-scheme-handler/steam</c>)? Ohne
    /// <c>xdg-mime</c> (z. B. in Flatpak – dort entscheidet das OpenURI-Portal) wird „ja“ angenommen; scheitert der
    /// Start danach, kopiert der <see cref="ServerConnector"/> die Adresse.
    /// </summary>
    public async Task<bool> CanLaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !s_gameSchemes.Contains(uri.Scheme))
        {
            return false;
        }

        var handler = await _processes.ReadAsync("xdg-mime", ["query", "default", "x-scheme-handler/" + uri.Scheme.ToLowerInvariant()], TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        return handler is null || handler.Trim().Length > 0;
    }

    public Task<bool> LaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && (s_gameSchemes.Contains(uri.Scheme) || s_webSchemes.Contains(uri.Scheme))
            ? RunAsync(uri, CancellationToken.None)
            : Task.FromResult(false);
    }

    private Task<bool> RunAsync(Uri uri, CancellationToken cancellationToken) =>
        _processes.RunAsync("xdg-open", [uri.AbsoluteUri], TimeSpan.FromSeconds(5), cancellationToken);
}
