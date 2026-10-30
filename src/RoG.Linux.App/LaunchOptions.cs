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
    /// <summary>Argument des Autostarts: ohne Fenster im Infobereich starten (nur wenn ein Tray da ist).</summary>
    public const string BackgroundArgument = "--background";

    /// <summary>Im Hintergrund starten (<see cref="BackgroundArgument"/>, z. B. beim Anmelden am Desktop).</summary>
    public bool Background { get; init; }

    /// <summary>Argument zum Erzwingen des Spielmodus (Tests am Desktop); Umgebungsvariable <c>ROG_GAMEMODE=1</c> bzw. <c>0</c>.</summary>
    public const string GameModeArgument = "--gamemode";

    /// <summary>
    /// Spielmodus (Steam Deck/Gamescope): Vollbild, keine Fensterdekoration, große Bedienziele, Hinweis auf die
    /// Bildschirmtastatur. Erkannt über Gamescope (<see cref="IsGamescope"/>), erzwingbar mit <see cref="GameModeArgument"/>.
    /// </summary>
    public bool GameMode { get; init; }

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
        var background = false;
        var gameMode = false;
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
            else if (arg == BackgroundArgument)
            {
                background = true;
            }
            else if (arg == GameModeArgument)
            {
                gameMode = true;
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
        return new LaunchOptions(environment is not null && RoGEnvironment.All.ContainsKey(environment) ? environment : null, link, backend.Value)
        {
            Background = background,
            GameMode = gameMode || DetectGameMode(env),
        };
    }

    /// <summary>
    /// Spielmodus: <c>ROG_GAMEMODE=1</c> erzwingt, <c>ROG_GAMEMODE=0</c> schaltet ab; sonst unter Gamescope (Spielmodus des
    /// Steam Deck – dort setzt Steam zusätzlich <c>SteamDeck=1</c>; der Desktop-Modus des Deck läuft unter KDE und gilt
    /// nicht als Spielmodus).
    /// </summary>
    public static bool DetectGameMode(Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return env("ROG_GAMEMODE") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => IsGamescope(env),
        };
    }

    /// <summary>Läuft die App unter Gamescope (eigener Wayland-Socket bzw. Desktop-Name „gamescope“)?</summary>
    public static bool IsGamescope(Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return !string.IsNullOrEmpty(env("GAMESCOPE_WAYLAND_DISPLAY"))
            || (env("XDG_CURRENT_DESKTOP") ?? string.Empty).Contains("gamescope", StringComparison.OrdinalIgnoreCase);
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

        return IsGamescope(env) ? DisplayBackend.X11 : DisplayBackend.Wayland;
    }
}
