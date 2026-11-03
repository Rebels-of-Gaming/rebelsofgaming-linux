namespace RoG.Mac.Platform;

/// <summary>
/// Ordner unter macOS. In der App Sandbox (Mac App Store) ist <c>HOME</c> der Container
/// (<c>~/Library/Containers/com.rebelsofgaming.app/Data</c>) – alles darunter gehört der App und verschwindet mit ihr.
/// Außerhalb der Sandbox (Entwicklung) dieselben Unterordner im echten Benutzerordner.
/// </summary>
public sealed class MacPaths
{
    public MacPaths(string? home = null)
    {
        Home = home is { Length: > 0 } ? home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var library = Path.Combine(Home, "Library");
        ConfigDirectory = Path.Combine(library, "Application Support", MacIdentity.BundleId);
        DataDirectory = ConfigDirectory;
        CacheDirectory = Path.Combine(library, "Caches", MacIdentity.BundleId);

        // In der Sandbox ist ~/Downloads im Container ein Verweis auf den echten Ordner (Recht files.downloads.read-write)
        DownloadDirectory = Path.Combine(Home, "Downloads");
    }

    public string Home { get; }

    /// <summary><c>settings.json</c>, <c>linux.json</c> (Design) – keine personenbezogenen Daten.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Warteschlange, Wiki-Kopien (verschlüsselt, Schlüssel im Schlüsselbund).</summary>
    public string DataDirectory { get; }

    /// <summary>Antwort-Cache (verschlüsselt).</summary>
    public string CacheDirectory { get; }

    public string DownloadDirectory { get; }
}
