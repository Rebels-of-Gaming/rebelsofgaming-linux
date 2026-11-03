using RoG.Linux.Platform.Desktop;
using RoG.Linux.Platform.Security;

namespace RoG.Mac.Platform.Desktop;

/// <summary>
/// Stufe 1: „Beim Anmelden starten“ gibt es unter macOS noch nicht (später <c>SMAppService.mainApp</c>, in der Sandbox
/// erlaubt). Die Einstellung bleibt sichtbar, Einschalten meldet „nicht möglich“.
/// </summary>
public sealed class MacAutostart : IAutostart
{
    public bool UsesPortal => false;

    public Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

    public Task<AutostartResult> SetAsync(bool enable, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(enable ? AutostartResult.Failed : AutostartResult.Disabled);
}

/// <summary>Stufe 1: Bildschirmfoto über das System gibt es unter macOS noch nicht (Bild wählen geht immer).</summary>
public sealed class MacScreenshots : IScreenshotService
{
    public Task<ScreenshotResult> TakeAsync(bool interactive = true, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScreenshotResult(ScreenshotStatus.Unavailable));
}

/// <summary>Stufe 1: Touch ID für die App-Sperre folgt (LocalAuthentication); bis dahin nur die PIN.</summary>
public sealed class MacFingerprint : IFingerprintVerifier
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<FingerprintResult> VerifyAsync(IProgress<FingerprintPrompt>? progress, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        Task.FromResult(FingerprintResult.Unavailable);
}
