using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Auth;
using RoG.Core.Features.Account;

namespace RoG.Linux.Platform;

/// <summary>
/// Meldet das Gerät des Tokens mit Plattform <c>linux</c> (Guide 35 Abschnitt 2). <c>AccountService.RegisterDeviceAsync</c>
/// im geteilten Kern meldet fest <c>windows</c> – deshalb hier die Linux-Fassung mit derselben Logik.
/// Ohne Scope <c>devices:manage</c> passiert nichts; 403/409/422 sind kein Fehler für die Oberfläche.
/// </summary>
public sealed class LinuxDeviceRegistration(IRoGApiClient client, ITokenStore tokens)
{
    public async Task<bool> RegisterAsync(string appVersion, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (token is null || !ScopeMatcher.Allows(token.Scopes, "devices:manage"))
        {
            return false;
        }

        try
        {
            await client.V1_devices_storeAsync(new ManageDevices { Platform = ManageDevicesPlatform.Linux, App_version = appVersion }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiProblemException ex) when (ex.Problem.Status is 403 or 409 or 422)
        {
            return false;
        }
    }
}
