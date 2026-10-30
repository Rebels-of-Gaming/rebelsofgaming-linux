using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>Flatpak-Wege: Notification- und Background-Portal gegen einen privaten Bus, XDG-Autostart-Datei.</summary>
[Trait("Category", "DBus")]
public class PortalTests
{
    [Fact]
    public async Task Portal_notification_is_added_and_its_click_comes_back()
    {
        await using var bus = await PrivateBus.StartAsync();
        var portal = new FakeDesktopPortal();
        portal.Connection = await bus.ServeAsync("org.freedesktop.portal.Desktop", portal);
        using var notifier = new PortalNotifier(bus.Address);

        Assert.True(await notifier.ShowAsync("Test", "Hallo"));
        var id = await notifier.NotifyAsync(new DesktopNotification("Nyx", "Bin dabei!", "Öffnen", "im.received"));
        Assert.Equal(2u, id);

        var added = portal.Notifications[1];
        Assert.Equal("rog-2", added.Id);
        Assert.Equal("Nyx", added.Notification["title"].GetString());
        Assert.Equal("Bin dabei!", added.Notification["body"].GetString());
        Assert.Equal("open", added.Notification["default-action"].GetString());
        Assert.Equal(1, added.Notification["buttons"].Count);
        Assert.False(portal.Notifications[0].Notification.ContainsKey("default-action"));

        var clicked = new TaskCompletionSource<uint>();
        notifier.Activated += (_, n) => clicked.TrySetResult(n);
        portal.Invoke("rog-2", "open");
        Assert.Equal(2u, await clicked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Portal_notifier_without_portal_returns_null()
    {
        await using var bus = await PrivateBus.StartAsync();
        using var notifier = new PortalNotifier(bus.Address);
        Assert.Null(await notifier.NotifyAsync(new DesktopNotification("A", "B")));
    }

    [Fact]
    public async Task Background_portal_enables_disables_and_reports_denial()
    {
        await using var bus = await PrivateBus.StartAsync();
        var portal = new FakeDesktopPortal();
        await bus.ServeAsync("org.freedesktop.portal.Desktop", portal);
        var autostart = new PortalAutostart(bus.Address) { Timeout = TimeSpan.FromSeconds(5) };

        Assert.True(autostart.UsesPortal);
        Assert.Null(await autostart.IsEnabledAsync());
        Assert.Equal(AutostartResult.Enabled, await autostart.SetAsync(true, "Grund"));
        var request = Assert.Single(portal.BackgroundRequests);
        Assert.True(request.Autostart);
        Assert.Equal(["rebelsofgaming", "--background"], request.Commandline);
        Assert.Equal("Grund", request.Reason);

        Assert.Equal(AutostartResult.Disabled, await autostart.SetAsync(false, "Grund"));
        portal.BackgroundResponse = 1;
        Assert.Equal(AutostartResult.Denied, await autostart.SetAsync(true, "Grund"));
    }

    [Fact]
    public async Task Screenshot_portal_returns_the_local_file_or_cancel()
    {
        await using var bus = await PrivateBus.StartAsync();
        var portal = new FakeDesktopPortal { ScreenshotResponse = (0, "file:///home/rebel/Bilder/Bildschirmfoto%20vom%2030.png") };
        await bus.ServeAsync("org.freedesktop.portal.Desktop", portal);
        var screenshots = new ScreenshotPortal(bus.Address) { Timeout = TimeSpan.FromSeconds(5) };

        var taken = await screenshots.TakeAsync();
        Assert.Equal(ScreenshotStatus.Taken, taken.Status);
        Assert.Equal("/home/rebel/Bilder/Bildschirmfoto vom 30.png", taken.Path);
        Assert.True(Assert.Single(portal.ScreenshotRequests));

        portal.ScreenshotResponse = (1, null);
        Assert.Equal(ScreenshotStatus.Cancelled, (await screenshots.TakeAsync()).Status);

        // Nur lokale Dateien
        portal.ScreenshotResponse = (0, "https://example.test/a.png");
        Assert.Equal(ScreenshotStatus.Unavailable, (await screenshots.TakeAsync()).Status);
    }

    [Fact]
    public async Task Screenshot_portal_missing_is_unavailable()
    {
        await using var bus = await PrivateBus.StartAsync();
        var screenshots = new ScreenshotPortal(bus.Address) { Timeout = TimeSpan.FromSeconds(2) };
        Assert.Equal(ScreenshotStatus.Unavailable, (await screenshots.TakeAsync()).Status);
    }

    [Fact]
    public async Task Background_portal_missing_is_a_failure_not_an_exception()
    {
        await using var bus = await PrivateBus.StartAsync();
        var autostart = new PortalAutostart(bus.Address) { Timeout = TimeSpan.FromSeconds(2) };
        Assert.Equal(AutostartResult.Failed, await autostart.SetAsync(true, "Grund"));
    }

    [Fact]
    public async Task Xdg_autostart_writes_and_removes_the_desktop_entry()
    {
        var home = Path.Combine(Path.GetTempPath(), "rog-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var autostart = new XdgAutostart(home, "/opt/Rebels of Gaming/rebelsofgaming");
            Assert.False(await autostart.IsEnabledAsync());
            Assert.Equal(AutostartResult.Enabled, await autostart.SetAsync(true, "Neue Nachrichten melden"));
            var text = await File.ReadAllTextAsync(Path.Combine(home, "autostart", "com.rebelsofgaming.RebelsOfGaming.desktop"));
            Assert.Contains("Exec=\"/opt/Rebels of Gaming/rebelsofgaming\" --background", text, StringComparison.Ordinal);
            Assert.Contains("Icon=com.rebelsofgaming.RebelsOfGaming", text, StringComparison.Ordinal);
            Assert.True(await autostart.IsEnabledAsync());

            // Vom Desktop abgeschaltet → aus
            await File.WriteAllTextAsync(autostart.FilePath, text.Replace("X-GNOME-Autostart-enabled=true", "X-GNOME-Autostart-enabled=false", StringComparison.Ordinal));
            Assert.False(await autostart.IsEnabledAsync());

            Assert.Equal(AutostartResult.Disabled, await autostart.SetAsync(false, "x"));
            Assert.False(File.Exists(autostart.FilePath));
            Assert.Equal("\"a\\\"b\\$c\"", XdgAutostart.QuoteExec("a\"b$c"));
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }
}
