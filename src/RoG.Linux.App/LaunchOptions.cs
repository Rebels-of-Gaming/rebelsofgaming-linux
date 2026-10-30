using RoG.Core.Config;
using RoG.Linux.Platform;

namespace RoG.Linux.App;

/// <summary>Fenstersystem der App: natives Wayland (Avalonia 12.1, Rückfall X11) oder X11/XWayland.</summary>
public enum DisplayBackend
{
    /// <summary>Wayland, wenn ein Compositor da ist (<c>WAYLAND_DISPLAY</c>), sonst X11.</summary>
    Wayland,

    /// <summary>X11 bzw. XWayland.</summary>
    X11,
}

/// <summary>Start-Argumente: Umgebung für die Entwicklung, Fenstersystem und ein Deep Link (<c>rog://…</c>).</summary>
public sealed record LaunchOptions(string? Environment, Uri? DeepLink, DisplayBackend Backend = DisplayBackend.X11)
{
    public static LaunchOptions Current { get; set; } = new(null, null);

    /// <summary>
    /// <c>--environment=staging</c> bzw. Umgebungsvariable <c>ROG_ENVIRONMENT</c> gehen den Einstellungen vor; unbekannte
    /// Namen werden ignoriert. Domains stehen nur in <c>environments.json</c> des Kerns.
    /// Fenstersystem: <c>--x11</c>/<c>--wayland</c> bzw. <c>ROG_DISPLAY=x11|wayland</c>, sonst automatisch
    /// (<see cref="DetectBackend"/>).
    /// </summary>
    public static LaunchOptions Parse(IReadOnlyList<string> args, Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        string? environment = null;
        Uri? link = null;
        DisplayBackend? backend = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--environment=", StringComparison.Ordinal))
            {
                environment = arg["--environment=".Length..];
            }
            else if (arg == "--x11")
            {
                backend = DisplayBackend.X11;
            }
            else if (arg == "--wayland")
            {
                backend = DisplayBackend.Wayland;
            }
            else if (TryParseDeepLink(arg, out var uri))
            {
                link = uri;
            }
        }

        environment ??= env("ROG_ENVIRONMENT");
        backend ??= env("ROG_DISPLAY") switch
        {
            "x11" => DisplayBackend.X11,
            "wayland" => DisplayBackend.Wayland,
            _ => DetectBackend(env),
        };
        return new LaunchOptions(environment is not null && RoGEnvironment.All.ContainsKey(environment) ? environment : null, link, backend.Value);
    }

    /// <summary>Nur <c>rog://…</c>-Links werden angenommen (von der .desktop-Datei bzw. einer zweiten Instanz).</summary>
    public static bool TryParseDeepLink(string value, out Uri? link)
    {
        link = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == AppIdentity.DeepLinkScheme ? uri : null;
        return link is not null;
    }

    /// <summary>
    /// Wayland, sobald eine Wayland-Sitzung läuft – außer im Spielmodus des Steam Deck (Gamescope stellt Apps über
    /// XWayland dar) und ohne <c>WAYLAND_DISPLAY</c>. Der Wayland-Backend fällt selbst auf X11 zurück, wenn der
    /// Compositor nicht erreichbar ist.
    /// </summary>
    public static DisplayBackend DetectBackend(Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (string.IsNullOrEmpty(env("WAYLAND_DISPLAY")))
        {
            return DisplayBackend.X11;
        }

        var gamescope = !string.IsNullOrEmpty(env("GAMESCOPE_WAYLAND_DISPLAY"))
            || (env("XDG_CURRENT_DESKTOP") ?? string.Empty).Contains("gamescope", StringComparison.OrdinalIgnoreCase);
        return gamescope ? DisplayBackend.X11 : DisplayBackend.Wayland;
    }
}
