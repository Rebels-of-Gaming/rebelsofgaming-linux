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

    private static readonly HashSet<string> s_gameSchemes = new(StringComparer.OrdinalIgnoreCase) { "steam", "et", "ts3server", "minecraft", "discord" };

    private readonly IProcessLauncher _processes = processes ?? new ProcessLauncher();

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && s_webSchemes.Contains(uri.Scheme) ? RunAsync(uri, cancellationToken) : Task.FromResult(false);
    }

    public Task<bool> CanLaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // Ob ein Programm registriert ist, sagt erst xdg-open; die App bietet zusätzlich „Adresse kopieren“ an
        return Task.FromResult(uri.IsAbsoluteUri && s_gameSchemes.Contains(uri.Scheme));
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
