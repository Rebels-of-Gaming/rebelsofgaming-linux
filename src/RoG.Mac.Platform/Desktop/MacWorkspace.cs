using RoG.Core.Auth;
using RoG.Core.Features.Servers;
using RoG.Linux.Platform.Desktop;
using RoG.Mac.Platform.Interop;
using CF = RoG.Mac.Platform.Interop.CoreFoundation;

namespace RoG.Mac.Platform.Desktop;

/// <summary>
/// Links im Standardbrowser bzw. im registrierten Programm öffnen und Dateien im Finder zeigen – über
/// <c>NSWorkspace</c> (in der App Sandbox erlaubt, kein Kindprozess). Gleiche Schema-Regeln wie unter Linux: aus
/// Inhalten nur <c>http</c>/<c>https</c>/<c>mailto</c>, Spiele-Links nur bekannte Schemata, nie <c>file:</c>.
/// Der Login läuft damit immer im System-Browser (RFC 8252).
/// </summary>
public sealed class MacWorkspace : IBrowserLauncher, IExternalLauncher, IFileManager
{
    private static readonly HashSet<string> s_webSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private static readonly HashSet<string> s_gameSchemes = new(StringComparer.OrdinalIgnoreCase) { "steam", "et", "wolf", "ts3server", "minecraft", "discord" };

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return RoG.Core.Api.ApiValues.IsBrowsable(uri) ? Task.Run(() => Open(uri), cancellationToken) : Task.FromResult(false);
    }

    /// <summary>Gibt es ein Programm für das Schema (<c>URLForApplicationToOpenURL:</c>)?</summary>
    public Task<bool> CanLaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !s_gameSchemes.Contains(uri.Scheme) || !OperatingSystem.IsMacOS())
        {
            return Task.FromResult(false);
        }

        return Task.Run(() => WithUrl(uri, (workspace, url) => ObjC.Send(workspace, "URLForApplicationToOpenURL:", url) != IntPtr.Zero));
    }

    public Task<bool> LaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && (s_gameSchemes.Contains(uri.Scheme) || s_webSchemes.Contains(uri.Scheme))
            ? Task.Run(() => Open(uri))
            : Task.FromResult(false);
    }

    /// <summary>Datei im Finder markieren („Im Ordner zeigen“ nach einem Download).</summary>
    public Task<bool> ShowAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(false);
        }

        return Task.Run(
            () =>
            {
                using var pool = ObjC.AutoreleasePool.Create();
                var workspace = Workspace();
                var file = CF.CreateString(path);
                var root = CF.CreateString(string.Empty);
                try
                {
                    return workspace != IntPtr.Zero && ObjC.SendBool(workspace, "selectFile:inFileViewerRootedAtPath:", file, root);
                }
                finally
                {
                    CF.CFRelease(root);
                    CF.CFRelease(file);
                }
            },
            cancellationToken);
    }

    private static bool Open(Uri uri) =>
        OperatingSystem.IsMacOS() && WithUrl(uri, (workspace, url) => ObjC.SendBool(workspace, "openURL:", url));

    private static bool WithUrl(Uri uri, Func<IntPtr, IntPtr, bool> action)
    {
        try
        {
            using var pool = ObjC.AutoreleasePool.Create();
            var workspace = Workspace();
            var text = CF.CreateString(uri.AbsoluteUri);
            try
            {
                var url = ObjC.Send(ObjC.GetClass("NSURL"), "URLWithString:", text);
                return workspace != IntPtr.Zero && url != IntPtr.Zero && action(workspace, url);
            }
            finally
            {
                CF.CFRelease(text);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static IntPtr Workspace() =>
        ObjC.LoadFramework("AppKit") ? ObjC.Send(ObjC.GetClass("NSWorkspace"), "sharedWorkspace") : IntPtr.Zero;
}
