using System.Security.Cryptography;
using System.Text;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Platform.Security;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>Flatpak ohne Zugriff auf den ganzen Schlüsselbund (Audit M1): Secret-Portal + verschlüsselte Einträge.</summary>
public sealed class SecretPortalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rog-secret-portal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Token_cache_key_and_pin_round_trip_through_the_portal_store()
    {
        var portal = new FakeSecretPortal();
        using var backend = new PortalSecretBackend(portal, _directory);

        var tokens = new SecretServiceTokenStore(backend, "production");
        await tokens.SetAsync(TestTokens.Wahke);
        var protector = await KeyringPayloadProtector.TryCreateAsync(backend, "production");
        var pins = new AppLockPinStore(backend, 1_000);
        await pins.SetAsync("4711");

        Assert.Equal("test-token", (await tokens.GetAsync())?.AccessToken);
        Assert.NotNull(protector);
        Assert.Equal(PinCheck.Correct, await pins.VerifyAsync("4711"));
        Assert.Null(await new SecretServiceTokenStore(backend, "staging").GetAsync());

        // drei Einträge, nichts im Klartext, Dateiname verrät nichts, nur der Besitzer darf lesen
        var files = Directory.GetFiles(_directory);
        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.Equal("RGC1"u8.ToArray(), bytes[..4]);
            Assert.DoesNotContain("test-token", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            Assert.DoesNotContain("production", Path.GetFileName(file), StringComparison.Ordinal);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }

        Assert.Equal(1, portal.Calls); // Geheimnis einmal je App-Lauf
    }

    [Fact]
    public async Task Entries_survive_a_restart_and_are_useless_with_another_app_secret()
    {
        var portal = new FakeSecretPortal();
        using (var first = new PortalSecretBackend(portal, _directory))
        {
            await new SecretServiceTokenStore(first, "production").SetAsync(TestTokens.Wahke);
        }

        using (var restarted = new PortalSecretBackend(portal, _directory))
        {
            Assert.Equal("test-token", (await new SecretServiceTokenStore(restarted, "production").GetAsync())?.AccessToken);
        }

        using var other = new PortalSecretBackend(new FakeSecretPortal(), _directory);
        Assert.Null(await new SecretServiceTokenStore(other, "production").GetAsync());
    }

    [Fact]
    public async Task Tampered_entry_counts_as_missing_and_clear_removes_the_file()
    {
        using var backend = new PortalSecretBackend(new FakeSecretPortal(), _directory);
        var tokens = new SecretServiceTokenStore(backend, "production");
        await tokens.SetAsync(TestTokens.Wahke);
        var file = Assert.Single(Directory.GetFiles(_directory));
        var bytes = await File.ReadAllBytesAsync(file);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(file, bytes);

        Assert.Null(await tokens.GetAsync());
        await tokens.ClearAsync();
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Keys_are_derived_per_entry()
    {
        var master = RandomNumberGenerator.GetBytes(64);
        var token = PortalSecretBackend.DeriveKey(master, "entry", new SecretServiceTokenStore(new FakeSecretBackend(), "production").Attributes);
        var cache = PortalSecretBackend.DeriveKey(master, "entry", KeyringPayloadProtector.AttributesFor("production"));
        var pin = PortalSecretBackend.DeriveKey(master, "entry", AppLockPinStore.Attributes);
        Assert.Equal(32, token.Length);
        Assert.NotEqual(token, cache);
        Assert.NotEqual(cache, pin);
        Assert.NotEqual(token, pin);
    }

    [Fact]
    public async Task Unavailable_portal_falls_back_to_memory_only()
    {
        using var backend = new PortalSecretBackend(new FakeSecretPortal { Unavailable = true }, _directory);
        var store = new FallbackTokenStore(new SecretServiceTokenStore(backend, "production"));

        await store.SetAsync(TestTokens.Wahke);

        Assert.False(store.IsPersistent);
        Assert.Equal("test-token", (await store.GetAsync())?.AccessToken);
        Assert.False(Directory.Exists(_directory));
        Assert.Null(await KeyringPayloadProtector.TryCreateAsync(backend, "production"));
    }

    [Fact]
    public void Flatpak_uses_the_portal_and_native_packages_the_secret_service()
    {
        var flatpak = new LinuxPaths(v => v switch { "FLATPAK_ID" => "com.rebelsofgaming.RebelsOfGaming", "HOME" => "/home/rebel", _ => null });
        var portal = Assert.IsType<PortalSecretBackend>(RoG.Linux.App.Services.AppServices.CreateSecretBackend(flatpak));
        Assert.Equal(Path.Combine(flatpak.DataDirectory, "secrets"), portal.Directory);

        if (!File.Exists("/.flatpak-info"))
        {
            var native = new LinuxPaths(v => v == "HOME" ? "/home/rebel" : null);
            Assert.IsType<SecretServiceBackend>(RoG.Linux.App.Services.AppServices.CreateSecretBackend(native));
        }
    }

    [Fact]
    [Trait("Category", "DBus")]
    public async Task Secret_portal_hands_over_the_app_secret_through_a_pipe()
    {
        await using var bus = await PrivateBus.StartAsync();
        var fake = new FakeDesktopPortal();
        await bus.ServeAsync("org.freedesktop.portal.Desktop", fake);
        var portal = new SecretPortal(bus.Address) { Timeout = TimeSpan.FromSeconds(5) };

        Assert.Equal(fake.AppSecret, await portal.RetrieveSecretAsync());

        fake.SecretResponse = 1;
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => portal.RetrieveSecretAsync());

        fake.SecretResponse = 0;
        fake.AppSecret = [1, 2, 3];
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => portal.RetrieveSecretAsync());
        Assert.Equal(3, fake.SecretRequests);
    }

    [Fact]
    [Trait("Category", "DBus")]
    public async Task Missing_secret_portal_is_reported_as_unavailable()
    {
        await using var bus = await PrivateBus.StartAsync();
        var portal = new SecretPortal(bus.Address) { Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => portal.RetrieveSecretAsync());
    }

    private sealed class FakeSecretPortal : ISecretPortal
    {
        private readonly byte[] _secret = RandomNumberGenerator.GetBytes(64);

        public bool Unavailable { get; init; }

        public int Calls { get; private set; }

        public Task<byte[]> RetrieveSecretAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Unavailable
                ? Task.FromException<byte[]>(new SecretStoreUnavailableException("test"))
                : Task.FromResult((byte[])_secret.Clone());
        }
    }
}
