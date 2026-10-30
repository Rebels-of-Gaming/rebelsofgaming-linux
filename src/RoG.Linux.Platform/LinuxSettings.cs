using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoG.Linux.Platform;

/// <summary>
/// Linux-eigene Einstellungen neben <c>settings.json</c> des Kerns (dessen <c>AppSettings</c> kennt kein Design):
/// <c>linux.json</c> im selben XDG-Ordner. Keine personenbezogenen Daten.
/// </summary>
public sealed record LinuxSettings
{
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";
    public const string ThemeSystem = "system";

    public static readonly IReadOnlyList<string> Themes = [ThemeDark, ThemeSystem, ThemeLight];

    /// <summary><c>dark</c> (RoG-Dunkel, Standard), <c>system</c> (hell/dunkel wie das System) oder <c>light</c>.</summary>
    public string Theme { get; init; } = ThemeDark;

    /// <summary>
    /// Letzter bekannter Stand von „Beim Anmelden starten“ – in Flatpak lässt sich der Autostart-Eintrag (vom Portal
    /// außerhalb der Sandbox angelegt) nicht lesen.
    /// </summary>
    public bool Autostart { get; init; }

    /// <summary>App-Sperre: auch per Fingerabdruck (fprintd) entsperren, wenn ein Finger eingelernt ist.</summary>
    public bool AppLockFingerprint { get; init; } = true;

    /// <summary>App-Sperre: Fehlversuche in Folge und Ende der Wartezeit – ein Neustart setzt sie nicht zurück.</summary>
    public int AppLockFailures { get; init; }

    public DateTimeOffset? AppLockBlockedUntil { get; init; }

    /// <summary>Mit Controller bedienen (SDL) – Standard an.</summary>
    public bool Controller { get; init; } = true;

    /// <summary>Unbekannte Werte (Hand-Edit, ältere Version) → Standard.</summary>
    public string EffectiveTheme => Themes.Contains(Theme) ? Theme : ThemeDark;
}

/// <summary>JSON-Datei, atomar geschrieben; defekt oder fehlend → Standardwerte.</summary>
public sealed class LinuxSettingsStore(string path)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Path { get; } = path;

    public LinuxSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<LinuxSettings>(File.ReadAllText(Path), s_json) ?? new LinuxSettings() : new LinuxSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LinuxSettings();
        }
    }

    public async Task SaveAsync(LinuxSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = Path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(settings, s_json), cancellationToken).ConfigureAwait(false);
        File.Move(tmp, Path, overwrite: true);
    }
}
