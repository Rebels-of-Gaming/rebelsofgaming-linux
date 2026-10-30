using RoG.Core.Auth;

namespace RoG.Linux.Platform;

/// <summary>
/// Gerätename für den App-Login (<c>device_name</c>, max. 80 Zeichen) – Gegenstück zu <c>DeviceName.ForWindows</c>
/// im geteilten Kern. Steam Deck bekommt einen eigenen Namen, damit man es unter Konto → Apps &amp; Geräte erkennt.
/// </summary>
public static class LinuxDeviceName
{
    public const string Prefix = "Linux-App";

    /// <summary>„Linux-App – PC von &lt;Name&gt;“ bzw. „Linux-App – Steam Deck von &lt;Name&gt;“, ohne Namen nur „Linux-App“.</summary>
    public static string For(string? owner, bool steamDeck = false)
    {
        var clean = new string((owner ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return Prefix;
        }

        var device = steamDeck ? "Steam Deck" : "PC";
        var name = $"{Prefix} – {device} von {clean}";
        return name.Length <= DeviceName.MaxLength ? name : name[..DeviceName.MaxLength].TrimEnd();
    }

    /// <summary>
    /// Steam Deck erkennen: SteamOS setzt <c>SteamDeck=1</c> im Spielmodus, sonst steht die Board-Kennung (Jupiter/Galileo)
    /// in <c>/sys/devices/virtual/dmi/id/board_name</c>.
    /// </summary>
    public static bool IsSteamDeck(Func<string, string?>? environment = null, Func<string, string?>? readFile = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        readFile ??= TryRead;
        if (environment("SteamDeck") == "1")
        {
            return true;
        }

        var board = readFile("/sys/devices/virtual/dmi/id/board_name")?.Trim();
        return board is "Jupiter" or "Galileo";
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
