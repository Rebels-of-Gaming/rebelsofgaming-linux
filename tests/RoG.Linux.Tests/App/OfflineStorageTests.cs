using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using RoG.Core.Offline;
using RoG.Linux.App.Services;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 4: verschlüsselter Plattencache und Warteschlange (Schlüssel im Schlüsselbund), Speicherdauer.</summary>
public class OfflineStorageTests
{
    [Fact]
    public async Task Protector_encrypts_detects_tampering_and_keeps_its_key_in_the_keyring()
    {
        var secrets = new FakeSecretBackend();
        using var protector = await KeyringPayloadProtector.TryCreateAsync(secrets, "production");
        Assert.NotNull(protector);
        var (label, key) = Assert.Single(secrets.Items).Value;
        Assert.Equal(32, key.Length);
        Assert.Contains("Cache", label, StringComparison.Ordinal);

        var plain = "Geheime Antwort der API"u8.ToArray();
        var sealedData = await protector.ProtectAsync(plain);
        Assert.False(sealedData.AsSpan().IndexOf("Geheime"u8) >= 0);
        Assert.Equal(plain, await protector.UnprotectAsync(sealedData));
        Assert.NotEqual(sealedData, await protector.ProtectAsync(plain)); // neue Nonce je Aufruf

        sealedData[^1] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => protector.UnprotectAsync(sealedData));

        // Zweiter Start: derselbe Schlüssel → alte Daten lesbar
        using var again = await KeyringPayloadProtector.TryCreateAsync(secrets, "production");
        Assert.Equal(plain, await again!.UnprotectAsync(await protector.ProtectAsync(plain)));
        Assert.Single(secrets.Items);

        // Ohne Schlüsselbund: kein Schutz → nichts auf die Platte
        Assert.Null(await KeyringPayloadProtector.TryCreateAsync(new FakeSecretBackend { Unavailable = true }, "production"));
    }

    [AvaloniaFact]
    public async Task Responses_and_outbox_are_stored_encrypted_dms_never_and_sign_out_removes_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "rog-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var protector = KeyringPayloadProtector.FromKey(RandomNumberGenerator.GetBytes(32));
            var api = new FakeApi();
            using (var services = await Create(root, api, protector))
            {
                Assert.True(services.PersistentCache);
                await new RoG.Core.Features.News.NewsService(services.Api.Client, services.Environment.WebBaseUrl).ListAsync();
                await new RoG.Core.Features.Messages.MessageService(services.Api.Client).ListAsync();
                await services.Outbox.EnqueueAsync(OutboxKind.ForumReply, "01T1", new { body = "Antwort" }, "Forum");
                await services.Outbox.EnqueueAsync(OutboxKind.DirectMessage, "01CONV1", new { body = "DM" }, "DM");
                await ViewModelTests.WaitUntil(() => Directory.Exists(Path.Combine(root, "cache", "http", "production"))
                    && Directory.GetFiles(Path.Combine(root, "cache", "http", "production"), "*.dat").Length > 0);
            }

            var cacheFiles = Directory.GetFiles(Path.Combine(root, "cache", "http", "production"), "*.dat");
            Assert.Single(cacheFiles); // News ja, Unterhaltungen nie
            Assert.False((await File.ReadAllBytesAsync(cacheFiles[0])).AsSpan().IndexOf("Herbst-Scrims"u8) >= 0);
            var outboxFile = Path.Combine(root, "data", "outbox-production.dat");
            Assert.False((await File.ReadAllBytesAsync(outboxFile)).AsSpan().IndexOf("Antwort"u8) >= 0);

            // Neustart: Forenantwort wartet weiter, die DM war nur im Speicher
            using (var services = await Create(root, api, protector))
            {
                var item = Assert.Single(services.Outbox.Items);
                Assert.Equal(OutboxKind.ForumReply, item.Kind);

                services.ClearCaches();
                Assert.Empty(services.Outbox.Items);
            }

            Assert.False(Directory.Exists(Path.Combine(root, "cache", "http", "production")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Old_cache_files_are_pruned()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rog-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var old = Path.Combine(folder, "old.dat");
            var fresh = Path.Combine(folder, "fresh.dat");
            File.WriteAllText(old, "x");
            File.WriteAllText(fresh, "y");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow - AppServices.CacheRetention - TimeSpan.FromDays(1));
            AppServices.PruneOld(folder, AppServices.CacheRetention);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<AppServices> Create(string root, FakeApi api, KeyringPayloadProtector protector)
    {
        var tokens = new FallbackTokenStore(new SecretServiceTokenStore(new FakeSecretBackend(), "production"));
        await tokens.SetAsync(TestTokens.Wahke);
        return await AppServices.CreateAsync(new AppServicesOptions
    {
        SettingsDirectory = Path.Combine(root, "config"),
        CacheDirectory = Path.Combine(root, "cache"),
        DataDirectory = Path.Combine(root, "data"),
        Tokens = tokens,
        Protector = protector,
        ApiHandler = api,
        ImageHandler = new FakeImages(),
        Browser = new RecordingBrowser(),
        Notifier = new SilentNotifier(),
        Autostart = new FakeAutostart(),
        Clipboard = new FakeClipboard(),
        Launcher = new FakeLauncher(),
        Environment = "production",
        Language = "de",
    });
    }
}
