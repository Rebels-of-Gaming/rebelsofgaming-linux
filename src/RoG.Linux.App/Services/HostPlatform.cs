using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Linux.Platform;
using RoG.Mac.Platform;

namespace RoG.Linux.App.Services;

/// <summary>
/// Eine Oberfläche, zwei Systeme: Linux (Flatpak/.deb) und macOS (Mac App Store). Hier – und nur hier – steht, was
/// sich je System an Kennung unterscheidet; die Verdrahtung wählt in <see cref="AppServices.CreateAsync"/>.
/// </summary>
public static class HostPlatform
{
    /// <summary>Läuft die App unter macOS? (Tests können es nicht umstellen – macOS-Pfade werden dort direkt geprüft.)</summary>
    public static bool IsMac { get; } = OperatingSystem.IsMacOS();

    /// <summary><c>linux</c> bzw. <c>macos</c> für Kern (User-Agent/Reverb) und Website.</summary>
    public static string Client => IsMac ? MacIdentity.ClientPlatform : ClientPlatform.Linux;

    /// <summary><c>RoG-Linux</c> bzw. <c>RoG-macOS</c> (User-Agent für API, Reverb, Server-Konsole).</summary>
    public static string UserAgentProduct => IsMac ? MacIdentity.UserAgentProduct : AppIdentity.UserAgentProduct;

    public static string UserAgent => $"{UserAgentProduct}/{AppServices.AppVersion}";

    /// <summary>Plattform der Geräte-Meldung (<c>POST /v1/devices</c>).</summary>
    public static ManageDevicesPlatform DevicePlatform => IsMac ? ManageDevicesPlatform.Macos : ManageDevicesPlatform.Linux;

    /// <summary>Gerätename beim Login: „macOS-App – Mac von …“ bzw. „Linux-App – PC/Steam Deck von …“.</summary>
    public static string DeviceName(string? owner) =>
        IsMac ? MacIdentity.DeviceName(owner) : LinuxDeviceName.For(owner, LinuxDeviceName.IsSteamDeck());
}
