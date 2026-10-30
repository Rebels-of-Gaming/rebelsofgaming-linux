namespace RoG.Linux.Platform;

/// <summary>
/// Kennung der Linux-App – einzige Stelle mit der App-ID (Flatpak, .desktop-Datei, AppStream, D-Bus-Hinweise).
/// Vorschlag aus <c>docs/LINUX-BAUPLAN.md</c> (Annahme L3); vor der ersten Flathub-Einreichung vom Owner zu bestätigen.
/// </summary>
public static class AppIdentity
{
    /// <summary>Reverse-DNS von rebelsofgaming.com plus App-Name (Flathub-Regel: mindestens drei Teile).</summary>
    public const string AppId = "com.rebelsofgaming.RebelsOfGaming";

    /// <summary>Ordnername unter den XDG-Verzeichnissen (außerhalb von Flatpak).</summary>
    public const string DataFolderName = "rebelsofgaming";

    /// <summary>Produktname im User-Agent (<c>RoG-Linux/&lt;Version&gt;</c>).</summary>
    public const string UserAgentProduct = "RoG-Linux";

    /// <summary>Schema der Deep Links (über die .desktop-Datei als <c>x-scheme-handler/rog</c> registriert).</summary>
    public const string DeepLinkScheme = "rog";
}
