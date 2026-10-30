namespace RoG.Linux.Platform;

/// <summary>
/// Ordner nach der XDG Base Directory Specification. Unter Flatpak zeigen die Variablen in den Sandbox-Ordner
/// (<c>~/.var/app/&lt;App-ID&gt;/…</c>) – Deinstallation mit <c>--delete-data</c> entfernt dann alles.
/// </summary>
public sealed class LinuxPaths
{
    private readonly Func<string, string?> _env;

    public LinuxPaths(Func<string, string?>? environment = null)
    {
        _env = environment ?? Environment.GetEnvironmentVariable;
        var home = _env("HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ConfigDirectory = Path.Combine(Resolve("XDG_CONFIG_HOME", home, ".config"), AppIdentity.DataFolderName);
        DataDirectory = Path.Combine(Resolve("XDG_DATA_HOME", home, ".local/share"), AppIdentity.DataFolderName);
        CacheDirectory = Path.Combine(Resolve("XDG_CACHE_HOME", home, ".cache"), AppIdentity.DataFolderName);
    }

    /// <summary>Einstellungen (<c>settings.json</c>), keine personenbezogenen Daten.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Später: Offline-Daten (Wiki, Warteschlange) – verschlüsselt.</summary>
    public string DataDirectory { get; }

    /// <summary>Wegwerfbare Caches (Bilder).</summary>
    public string CacheDirectory { get; }

    /// <summary>Läuft die App in einer Flatpak-Sandbox?</summary>
    public bool IsFlatpak => _env("FLATPAK_ID") is { Length: > 0 } || File.Exists("/.flatpak-info");

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    private string Resolve(string variable, string home, string fallback)
    {
        // Spezifikation: nur absolute Pfade gelten, sonst Standard
        var value = _env(variable);
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value) ? value : Path.Combine(home, fallback);
    }
}
