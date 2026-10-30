using Avalonia;
using RoG.Core.Settings;
using RoG.Linux.App;
using RoG.Linux.App.Controls;
using RoG.Linux.App.Services;
using RoG.Linux.Platform;

namespace RoG.Linux.Tests.Platform;

public class Stage1PlatformTests
{
    [Fact]
    public void Window_placement_is_restored_inside_the_screen()
    {
        var area = new PixelRect(0, 0, 1280, 800);

        Assert.Null(WindowPlacementService.Plan(null, area, 1, true));
        Assert.Null(WindowPlacementService.Plan(new WindowPlacement(0, 0, 100, 100, false), area, 1, true)); // unbrauchbar klein

        var plan = WindowPlacementService.Plan(new WindowPlacement(100, 50, 1000, 700, true), area, 1, true)!;
        Assert.Equal(new PixelPoint(100, 50), plan.Position);
        Assert.Equal(1000, plan.Width);
        Assert.True(plan.Maximized);

        // Monitor abgesteckt / kleinerer Bildschirm: zurück in den sichtbaren Bereich, nicht größer als der Bildschirm
        var moved = WindowPlacementService.Plan(new WindowPlacement(2500, 900, 1600, 1000, false), area, 1, true)!;
        Assert.Equal(new PixelPoint(0, 0), moved.Position);
        Assert.Equal(1280, moved.Width);
        Assert.Equal(800, moved.Height);

        // HiDPI: Größe in DIP gespeichert, Bildschirm in Pixeln
        var hidpi = WindowPlacementService.Plan(new WindowPlacement(0, 0, 1200, 760, false), new PixelRect(0, 0, 2560, 1600), 2, true)!;
        Assert.Equal(1200, hidpi.Width);
        Assert.Equal(760, hidpi.Height);

        // Wayland: keine Position
        Assert.Null(WindowPlacementService.Plan(new WindowPlacement(10, 10, 900, 600, false), area, 1, false)!.Position);
    }

    [Fact]
    public void Window_placement_capture_rounds_to_pixels()
    {
        var p = WindowPlacementService.Capture(new PixelPoint(12, 34), new Size(1279.6, 799.4), maximized: true);
        Assert.Equal(new WindowPlacement(12, 34, 1280, 799, true), p);
    }

    [Theory]
    [InlineData(1000, 200, 16, 6, 4)]
    [InlineData(1000, 200, 16, 2, 2)]
    [InlineData(300, 200, 16, 6, 1)]
    [InlineData(416, 200, 16, 6, 2)]
    [InlineData(415, 200, 16, 6, 1)]
    public void Adaptive_grid_columns(double width, double min, double spacing, int count, int expected) =>
        Assert.Equal(expected, AdaptiveGrid.ColumnsFor(width, min, spacing, int.MaxValue, count));

    [Fact]
    public void Adaptive_grid_respects_max_columns() => Assert.Equal(2, AdaptiveGrid.ColumnsFor(2000, 300, 16, 2, 2));

    [Theory]
    [InlineData(null, null, null, DisplayBackend.X11)]
    [InlineData("wayland-0", null, null, DisplayBackend.Wayland)]
    [InlineData("wayland-0", "gamescope", null, DisplayBackend.X11)]
    [InlineData("wayland-0", "KDE", "gamescope-0", DisplayBackend.X11)]
    public void Display_backend_prefers_wayland_but_not_in_game_mode(string? wayland, string? desktop, string? gamescope, DisplayBackend expected)
    {
        var env = new Dictionary<string, string?> { ["WAYLAND_DISPLAY"] = wayland, ["XDG_CURRENT_DESKTOP"] = desktop, ["GAMESCOPE_WAYLAND_DISPLAY"] = gamescope };
        Assert.Equal(expected, LaunchOptions.DetectBackend(n => env.GetValueOrDefault(n)));
    }

    [Fact]
    public void Display_backend_can_be_forced()
    {
        Func<string, string?> wayland = n => n == "WAYLAND_DISPLAY" ? "wayland-0" : null;
        Assert.Equal(DisplayBackend.X11, LaunchOptions.Parse(["--x11"], wayland).Backend);
        Assert.Equal(DisplayBackend.X11, LaunchOptions.Parse([], n => n == "ROG_DISPLAY" ? "x11" : wayland(n)).Backend);
        Assert.Equal(DisplayBackend.Wayland, LaunchOptions.Parse(["--wayland"], _ => null).Backend);
    }

    [Fact]
    public async Task Linux_settings_round_trip_and_survive_broken_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rog-linux-settings-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new LinuxSettingsStore(Path.Combine(dir, "linux.json"));
            Assert.Equal(LinuxSettings.ThemeDark, store.Load().EffectiveTheme);

            await store.SaveAsync(new LinuxSettings { Theme = LinuxSettings.ThemeSystem });
            Assert.Equal(LinuxSettings.ThemeSystem, store.Load().Theme);

            await File.WriteAllTextAsync(store.Path, "{kaputt");
            Assert.Equal(LinuxSettings.ThemeDark, store.Load().Theme);

            await File.WriteAllTextAsync(store.Path, """{"theme":"neon"}""");
            Assert.Equal(LinuxSettings.ThemeDark, store.Load().EffectiveTheme);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
