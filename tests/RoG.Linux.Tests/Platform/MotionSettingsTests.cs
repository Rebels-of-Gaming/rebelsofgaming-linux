using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>Stufe 10: „Bewegung reduzieren“ aus dem Settings-Portal (freedesktop, GNOME, KDE; ReadOne und altes Read; live).</summary>
[Trait("Category", "DBus")]
public class MotionSettingsTests
{
    private static async Task<(PrivateBus Bus, FakeSettingsPortal Portal)> StartAsync(FakeSettingsPortal portal)
    {
        var bus = await PrivateBus.StartAsync();
        portal.Connection = await bus.ServeAsync("org.freedesktop.portal.Desktop", portal);
        return (bus, portal);
    }

    [Fact]
    public async Task Gnome_enable_animations_off_means_reduce_and_changes_live()
    {
        var (bus, portal) = await StartAsync(new FakeSettingsPortal());
        await using var _ = bus;
        portal.Values[("org.gnome.desktop.interface", "enable-animations")] = false;
        await using var motion = new MotionSettings(bus.Address);

        Assert.True(await motion.StartAsync());

        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        motion.Changed += (_, reduce) => changed.TrySetResult(reduce);
        portal.Change("org.gnome.desktop.interface", "enable-animations", true);
        Assert.False(await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(motion.ReduceMotion);
    }

    [Fact]
    public async Task Freedesktop_reduced_motion_wins_over_gnome()
    {
        var (bus, portal) = await StartAsync(new FakeSettingsPortal());
        await using var _ = bus;
        portal.Values[("org.freedesktop.appearance", "reduced-motion")] = 0u;
        portal.Values[("org.gnome.desktop.interface", "enable-animations")] = false;
        await using var motion = new MotionSettings(bus.Address);
        Assert.False(await motion.StartAsync());
    }

    [Fact]
    public async Task Old_portal_read_and_kde_factor()
    {
        var (bus, portal) = await StartAsync(new FakeSettingsPortal { OnlyRead = true });
        await using var _ = bus;
        portal.Values[("org.kde.kdeglobals.KDE", "AnimationDurationFactor")] = "0";
        await using var motion = new MotionSettings(bus.Address);
        Assert.True(await motion.StartAsync());
    }

    [Fact]
    public async Task Without_portal_or_values_animations_stay_on()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using var none = new MotionSettings(bus.Address) { Timeout = TimeSpan.FromSeconds(1) };
        Assert.False(await none.StartAsync());

        var (bus2, _) = await StartAsync(new FakeSettingsPortal());
        await using var __ = bus2;
        await using var empty = new MotionSettings(bus2.Address);
        Assert.False(await empty.StartAsync());
        await using var noBus = new MotionSettings(string.Empty);
        Assert.False(await noBus.StartAsync());
    }
}
