using RoG.Core.Api.Generated;
using RoG.Core.Auth;
using RoG.Core.Features.Account;

namespace RoG.Linux.Platform;

/// <summary>
/// Meldet das Gerät des Tokens mit Plattform <c>linux</c> bzw. <c>macos</c> (Guide 35 Abschnitt 2) über
/// <see cref="AccountService.RegisterDeviceAsync(string, ManageDevicesPlatform, CancellationToken)"/> des Kerns.
/// Ohne Scope <c>devices:manage</c> passiert nichts; 403/409/422 sind kein Fehler für die Oberfläche.
/// </summary>
public sealed class LinuxDeviceRegistration(IRoGApiClient client, ITokenStore tokens, ManageDevicesPlatform platform = ManageDevicesPlatform.Linux)
{
    private readonly AccountService _account = new(client, tokens);

    public Task<bool> RegisterAsync(string appVersion, CancellationToken cancellationToken = default) =>
        _account.RegisterDeviceAsync(appVersion, platform, cancellationToken);
}
