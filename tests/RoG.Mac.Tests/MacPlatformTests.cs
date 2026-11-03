using System.Text;
using RoG.Core.Auth;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Platform.Secrets;
using RoG.Mac.Platform;
using RoG.Mac.Platform.Desktop;
using RoG.Mac.Platform.Secrets;

namespace RoG.Mac.Tests;

public class MacPlatformTests
{
    [Fact]
    public void Account_name_is_sorted_and_stable()
    {
        var a = new Dictionary<string, string> { ["kind"] = "device-token", ["environment"] = "staging", ["application"] = "x" };
        var b = new Dictionary<string, string> { ["environment"] = "staging", ["application"] = "x", ["kind"] = "device-token" };

        Assert.Equal("application=x;environment=staging;kind=device-token", KeychainSecretBackend.AccountFor(a));
        Assert.Equal(KeychainSecretBackend.AccountFor(a), KeychainSecretBackend.AccountFor(b));
    }

    [Fact]
    public void Paths_live_in_the_container_library()
    {
        var paths = new MacPaths("/Users/test/Library/Containers/com.rebelsofgaming.app/Data");

        Assert.Equal("/Users/test/Library/Containers/com.rebelsofgaming.app/Data/Library/Application Support/com.rebelsofgaming.app", paths.ConfigDirectory);
        Assert.Equal(paths.ConfigDirectory, paths.DataDirectory);
        Assert.Equal("/Users/test/Library/Containers/com.rebelsofgaming.app/Data/Library/Caches/com.rebelsofgaming.app", paths.CacheDirectory);
        Assert.Equal("/Users/test/Library/Containers/com.rebelsofgaming.app/Data/Downloads", paths.DownloadDirectory);
    }

    [Fact]
    public void Identity_matches_the_ios_app_and_the_website()
    {
        Assert.Equal("com.rebelsofgaming.app", MacIdentity.BundleId);
        Assert.Equal("macos", RoG.Core.Api.ClientPlatform.Normalize(MacIdentity.ClientPlatform));
        Assert.Equal("macOS-App – Mac von Wahke", MacIdentity.DeviceName("Wahke"));
        Assert.True(MacIdentity.IsSandboxed(n => n == "APP_SANDBOX_CONTAINER_ID" ? "com.rebelsofgaming.app" : null));
        Assert.False(MacIdentity.IsSandboxed(_ => null));
    }

    [Fact]
    public async Task Keychain_is_unavailable_outside_macos()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "nur außerhalb von macOS");
        var backend = new KeychainSecretBackend();

        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => backend.LookupAsync(new Dictionary<string, string> { ["k"] = "v" }));

        // Token-Speicher fällt auf „nur diese Sitzung“ zurück statt abzustürzen
        var tokens = new FallbackTokenStore(new SecretServiceTokenStore(backend, "test"));
        Assert.Null(await tokens.GetAsync());
        Assert.False(tokens.IsPersistent);
    }

    [Fact]
    public async Task Keychain_round_trip_on_macos()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "nur unter macOS");
        var backend = new KeychainSecretBackend("com.rebelsofgaming.app.tests." + Guid.NewGuid().ToString("N"));
        var attributes = new Dictionary<string, string> { ["kind"] = "test", ["environment"] = "ci" };
        try
        {
            Assert.Null(await backend.LookupAsync(attributes));
            await backend.StoreAsync("RoG Test", attributes, Encoding.UTF8.GetBytes("geheim-1"));
            await backend.StoreAsync("RoG Test", attributes, Encoding.UTF8.GetBytes("geheim-2")); // ersetzt
            Assert.Equal("geheim-2", Encoding.UTF8.GetString((await backend.LookupAsync(attributes))!));

            var store = new SecretServiceTokenStore(backend, "ci");
            await store.SetAsync(new StoredToken("tok", "01J", "Wahke", ["read"], "macOS-App – Mac von Wahke", DateTimeOffset.UnixEpoch));
            Assert.Equal("tok", (await store.GetAsync())?.AccessToken);
            await store.ClearAsync();
            Assert.Null(await store.GetAsync());
        }
        finally
        {
            await backend.ClearAsync(attributes);
        }

        Assert.Null(await backend.LookupAsync(attributes));
    }

    [Fact]
    public async Task Notifier_without_app_bundle_reports_no_service()
    {
        var notifier = new MacNotifier(() => null);

        Assert.Null(await notifier.NotifyAsync(new DesktopNotification("Titel", "Text")));
        Assert.False(await notifier.ShowAsync("Titel", "Text"));
    }

    [Fact]
    public async Task Workspace_only_opens_allowed_schemes()
    {
        var workspace = new MacWorkspace();

        Assert.False(await workspace.OpenAsync(new Uri("file:///etc/passwd")));
        Assert.False(await workspace.LaunchAsync(new Uri("file:///etc/passwd")));
        Assert.False(await workspace.CanLaunchAsync(new Uri("https://example.test/")));

        // unter macOS echter NSWorkspace-Aufruf – darf nicht abstürzen (Ergebnis hängt vom Runner ab)
        _ = await workspace.CanLaunchAsync(new Uri("steam://connect/127.0.0.1:27960"));
    }

    [Fact]
    public async Task Unsupported_features_degrade_gracefully()
    {
        Assert.Equal(AutostartResult.Failed, await new MacAutostart().SetAsync(true, "x"));
        Assert.Equal(ScreenshotStatus.Unavailable, (await new MacScreenshots().TakeAsync()).Status);
        Assert.False(await new MacFingerprint().IsAvailableAsync());
    }
}
