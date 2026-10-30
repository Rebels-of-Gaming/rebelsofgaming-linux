using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>
/// Echte D-Bus-Aufrufe gegen einen privaten Bus (Tmds.DBus.Protocol 0.94 seit Avalonia 12): Schlüsselbund mit
/// Entsperr-Prompt, Desktop-Benachrichtigungen und Single-Instance über <c>org.freedesktop.Application</c>.
/// </summary>
[Trait("Category", "DBus")]
public class DBusIntegrationTests
{
    [Fact]
    public async Task Token_is_stored_replaced_read_and_deleted_in_the_secret_service()
    {
        await using var bus = await PrivateBus.StartAsync();
        var service = new FakeSecretService();
        await bus.ServeAsync("org.freedesktop.secrets", service);
        using var backend = new SecretServiceBackend(bus.Address);
        var store = new SecretServiceTokenStore(backend, "production");

        await store.SetAsync(TestTokens.Wahke);
        await store.SetAsync(TestTokens.Wahke with { AccessToken = "neu" });
        var entry = Assert.Single(service.Entries);
        Assert.Equal("Rebels of Gaming (Wahke)", entry.Label);
        Assert.Equal(AppIdentity.AppId, entry.Attributes["application"]);

        Assert.Equal("neu", (await store.GetAsync())?.AccessToken);

        await store.ClearAsync();
        Assert.Empty(service.Entries);
        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public async Task Locked_keyring_is_unlocked_through_the_prompt()
    {
        await using var bus = await PrivateBus.StartAsync();
        var service = new FakeSecretService();
        await bus.ServeAsync("org.freedesktop.secrets", service);
        using var backend = new SecretServiceBackend(bus.Address);
        var store = new SecretServiceTokenStore(backend, "production");
        await store.SetAsync(TestTokens.Wahke);

        service.Locked = true;
        Assert.Equal("test-token", (await store.GetAsync())?.AccessToken);
        Assert.Equal(1, service.PromptsShown);

        service.Locked = true;
        service.DismissPrompt = true;
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => backend.LookupAsync(store.Attributes));
    }

    [Fact]
    public async Task Missing_secret_service_is_reported_as_unavailable()
    {
        await using var bus = await PrivateBus.StartAsync();
        using var backend = new SecretServiceBackend(bus.Address);
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => backend.LookupAsync(new Dictionary<string, string> { ["a"] = "b" }));

        // Rückfall der App: Token nur im Speicher
        var fallback = new FallbackTokenStore(new SecretServiceTokenStore(backend, "production"));
        await fallback.SetAsync(TestTokens.Wahke);
        Assert.False(fallback.IsPersistent);
        Assert.Equal("test-token", (await fallback.GetAsync())?.AccessToken);
    }

    [Fact]
    public async Task Desktop_notification_reaches_the_notification_service()
    {
        await using var bus = await PrivateBus.StartAsync();
        var service = new FakeNotificationService();
        using var notifier = new FreedesktopNotifier(bus.Address);

        Assert.False(await notifier.ShowAsync("Titel", "Text")); // noch kein Dienst
        await bus.ServeAsync("org.freedesktop.Notifications", service);
        Assert.True(await notifier.ShowAsync("Neue Nachricht", "Nyx: Scrim heute?"));

        var shown = Assert.Single(service.Notifications);
        Assert.Equal("Rebels of Gaming", shown.AppName);
        Assert.Equal(AppIdentity.AppId, shown.Icon);
        Assert.Equal(AppIdentity.AppId, shown.DesktopEntry);
        Assert.Equal("Nyx: Scrim heute?", shown.Body);
    }

    [Fact]
    public async Task Clickable_notification_reports_its_activation()
    {
        await using var bus = await PrivateBus.StartAsync();
        var service = new FakeNotificationService();
        service.Connection = await bus.ServeAsync("org.freedesktop.Notifications", service);
        using var notifier = new FreedesktopNotifier(bus.Address);
        var clicked = new TaskCompletionSource<uint>();
        notifier.Activated += (_, id) => clicked.TrySetResult(id);

        var id = await notifier.NotifyAsync(new DesktopNotification("Nyx", "Scrim heute?", "Öffnen", "im.received"));
        Assert.Equal(1u, id);
        var shown = Assert.Single(service.Notifications);
        Assert.Equal(["default", "Öffnen"], shown.Actions);
        Assert.Equal("im.received", shown.Category);

        service.Invoke(7, "other"); // andere Aktion: ignoriert
        service.Invoke(1, "default");
        Assert.Equal(1u, await clicked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Second_instance_forwards_its_deep_link_over_dbus_and_exits()
    {
        await using var bus = await PrivateBus.StartAsync();
        using var first = await SingleInstance.AcquireAsync(null, bus.Address, null);
        Assert.True(first.IsPrimary);
        Assert.Equal("dbus", first.Mechanism);

        // Anfrage vor dem ersten Abonnenten wird gesammelt
        using (var second = await SingleInstance.AcquireAsync(new Uri("rog://news/scrim-serie-herbst-2026"), bus.Address, null))
        {
            Assert.False(second.IsPrimary);
        }

        var received = new List<Uri?>();
        first.Activated += (_, r) => { lock (received) { received.Add(r.DeepLink); } };
        Assert.Equal("rog://news/scrim-serie-herbst-2026", Assert.Single(received)?.ToString());

        using (var third = await SingleInstance.AcquireAsync(null, bus.Address, null))
        {
            Assert.False(third.IsPrimary);
        }

        await WaitFor(() => received.Count == 2);
        Assert.Null(received[1]);
    }

    [Fact]
    public async Task Single_instance_falls_back_to_a_unix_socket_without_session_bus()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rog-instance-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var first = await SingleInstance.AcquireAsync(null, null, dir);
            Assert.True(first.IsPrimary);
            Assert.Equal("socket", first.Mechanism);
            var received = new List<Uri?>();
            first.Activated += (_, r) => { lock (received) { received.Add(r.DeepLink); } };

            using (var second = await SingleInstance.AcquireAsync(new Uri("rog://settings"), null, dir))
            {
                Assert.False(second.IsPrimary);
            }

            using (var evil = await SingleInstance.AcquireAsync(new Uri("https://example.com"), null, dir))
            {
                Assert.False(evil.IsPrimary);
            }

            await WaitFor(() => received.Count == 2);
            Assert.Equal("settings", received[0]?.Host);
            Assert.Null(received[1]); // nur rog:// wird weitergereicht
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stale_socket_after_a_crash_is_taken_over()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rog-instance-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, AppIdentity.DataFolderName));
        await File.WriteAllTextAsync(Path.Combine(dir, AppIdentity.DataFolderName, "instance.sock"), string.Empty);
        try
        {
            using var instance = await SingleInstance.AcquireAsync(null, null, dir);
            Assert.True(instance.IsPrimary);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 > end)
            {
                throw new TimeoutException();
            }

            await Task.Delay(20);
        }
    }
}
