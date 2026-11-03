using RoG.Core.Auth;

namespace RoG.Mac.Platform;

/// <summary>
/// Kennung der macOS-App – einzige Stelle mit Bundle-ID, Produktname und Plattform. Die Bundle-ID ist dieselbe wie die
/// der iOS-App (<c>rog-app/app.config.ts</c>, <c>BASE_ID</c>), damit beide unter einem App-Store-Connect-Eintrag
/// laufen („Universal Purchase“, Owner-Entscheidung 03.11.2026).
/// </summary>
public static class MacIdentity
{
    /// <summary>Bundle-ID der iOS- und macOS-App (Team K4KM7V6YK5).</summary>
    public const string BundleId = "com.rebelsofgaming.app";

    /// <summary>Plattform für Geräte-Meldung und Reverb (<c>client=rog-macos</c>), die Website kennt <c>macos</c>.</summary>
    public const string ClientPlatform = "macos";

    /// <summary>Produktname im User-Agent (<c>RoG-macOS/&lt;Version&gt;</c>).</summary>
    public const string UserAgentProduct = "RoG-macOS";

    /// <summary>Präfix des Gerätenamens beim App-Login (Konto → Apps &amp; Geräte).</summary>
    public const string DeviceNamePrefix = "macOS-App";

    /// <summary>„macOS-App – Mac von &lt;Name&gt;“, ohne Namen nur „macOS-App“ (Bildung im Kern).</summary>
    public static string DeviceName(string? owner) => RoG.Core.Auth.DeviceName.For(DeviceNamePrefix, owner, "Mac");

    /// <summary>Läuft die App in der App Sandbox (Mac App Store)? macOS setzt dann <c>APP_SANDBOX_CONTAINER_ID</c>.</summary>
    public static bool IsSandboxed(Func<string, string?>? environment = null) =>
        (environment ?? Environment.GetEnvironmentVariable)("APP_SANDBOX_CONTAINER_ID") is { Length: > 0 };
}
