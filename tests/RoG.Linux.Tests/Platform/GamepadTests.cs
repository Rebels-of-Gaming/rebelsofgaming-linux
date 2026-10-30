using RoG.Linux.App;
using RoG.Linux.Platform.Input;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>Stufe 10: Controller-Logik ohne SDL (Abbildung, Wiederholung, Totzone, Hot-Plug) und Spielmodus-Erkennung.</summary>
public class GamepadTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Buttons_map_to_actions_once_per_press()
    {
        var mapper = new GamepadMapper();
        Assert.Equal([GamepadAction.Activate], mapper.Update(GamepadState.Of(GamepadButton.A), Ms(0)));
        Assert.Empty(mapper.Update(GamepadState.Of(GamepadButton.A), Ms(2000))); // kein Wiederholen
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(2010)));
        Assert.Equal([GamepadAction.Activate], mapper.Update(GamepadState.Of(GamepadButton.A), Ms(2020)));

        var all = mapper.Update(GamepadState.Of(GamepadButton.B, GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.Start, GamepadButton.Back, GamepadButton.Y), Ms(3000));
        Assert.Equal(
            [GamepadAction.Back, GamepadAction.PreviousSection, GamepadAction.NextSection, GamepadAction.Menu, GamepadAction.Search, GamepadAction.Refresh],
            all);
    }

    [Fact]
    public void Held_direction_repeats_after_a_delay()
    {
        var mapper = new GamepadMapper();
        var down = GamepadState.Of(GamepadButton.DPadDown);
        Assert.Equal([GamepadAction.Down], mapper.Update(down, Ms(0)));
        Assert.Empty(mapper.Update(down, Ms(200)));
        Assert.Empty(mapper.Update(down, Ms(399)));
        Assert.Equal([GamepadAction.Down], mapper.Update(down, Ms(400)));
        Assert.Empty(mapper.Update(down, Ms(450)));
        Assert.Equal([GamepadAction.Down], mapper.Update(down, Ms(510)));
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(520)));
        Assert.Equal([GamepadAction.Left], mapper.Update(GamepadState.Of(GamepadButton.DPadLeft), Ms(530)));
    }

    [Fact]
    public void Stick_has_dead_zone_hysteresis_and_dominant_axis()
    {
        var mapper = new GamepadMapper();
        Assert.Empty(mapper.Update(new GamepadState(0, LeftY: 10000), Ms(0)));            // Totzone
        Assert.Equal([GamepadAction.Down], mapper.Update(new GamepadState(0, LeftY: 20000), Ms(10)));
        Assert.Empty(mapper.Update(new GamepadState(0, LeftY: 13000), Ms(20)));           // Hysterese: noch gedrückt
        Assert.Empty(mapper.Update(new GamepadState(0, LeftY: 11000), Ms(30)));           // losgelassen
        Assert.Equal([GamepadAction.Down], mapper.Update(new GamepadState(0, LeftY: 17000), Ms(40)));
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(50)));

        // Diagonale: stärkere Achse gewinnt
        Assert.Equal([GamepadAction.Left], mapper.Update(new GamepadState(0, LeftX: -30000, LeftY: 20000), Ms(60)));
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(70)));
        Assert.Equal([GamepadAction.Up], mapper.Update(new GamepadState(0, LeftX: 18000, LeftY: -25000), Ms(80)));
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(90)));

        // Rechter Stick scrollt (schneller wiederholt)
        Assert.Equal([GamepadAction.ScrollDown], mapper.Update(new GamepadState(0, RightY: 30000), Ms(100)));
        Assert.Equal([GamepadAction.ScrollDown], mapper.Update(new GamepadState(0, RightY: 30000), Ms(100 + 150)));
        Assert.Equal([GamepadAction.ScrollDown], mapper.Update(new GamepadState(0, RightY: 30000), Ms(100 + 210)));
    }

    [Fact]
    public void After_reset_held_buttons_wait_for_release()
    {
        var mapper = new GamepadMapper();
        Assert.Equal([GamepadAction.Activate], mapper.Update(GamepadState.Of(GamepadButton.A), Ms(0)));
        mapper.Reset();
        Assert.Empty(mapper.Update(GamepadState.Of(GamepadButton.A), Ms(10)));
        Assert.Empty(mapper.Update(GamepadState.Of(GamepadButton.A), Ms(1000)));
        Assert.Equal([GamepadAction.Down], mapper.Update(GamepadState.Of(GamepadButton.A, GamepadButton.DPadDown), Ms(1001))); // neu gedrückt zählt
        Assert.Empty(mapper.Update(GamepadState.Of(), Ms(1010)));
        Assert.Equal([GamepadAction.Activate], mapper.Update(GamepadState.Of(GamepadButton.A), Ms(1020)));
        Assert.Empty(mapper.Update(null, Ms(1030))); // Controller weg
    }

    [Fact]
    public void Service_polls_only_with_focus_and_reports_hot_plug()
    {
        var now = TimeSpan.Zero;
        var fake = new FakeGamepadBackend();
        using var service = new GamepadService(() => fake, () => now);
        var actions = new List<GamepadAction>();
        var changes = 0;
        service.ActionTriggered += (_, a) => actions.Add(a);
        service.ControllerChanged += (_, _) => changes++;

        fake.State = GamepadState.Of(GamepadButton.A);
        service.Tick();
        Assert.Equal(GamepadAvailability.Ready, service.Availability);
        Assert.Equal("Fake", service.Library);
        Assert.Equal("Steam Deck Controller", service.ControllerName);
        Assert.Equal(1, changes);
        Assert.Empty(actions); // ohne Fokus keine Aktionen

        service.WindowFocused = true;
        now += GamepadService.FocusedInterval;
        service.Tick();
        Assert.Empty(actions); // A war schon gedrückt → erst nach dem Loslassen
        fake.State = default;
        service.Tick();
        fake.State = GamepadState.Of(GamepadButton.RightShoulder);
        service.Tick();
        Assert.Equal([GamepadAction.NextSection], actions);

        // Abziehen und wieder anstecken (Suche höchstens jede Sekunde)
        fake.Connected = null;
        now += TimeSpan.FromMilliseconds(100);
        service.Tick();
        Assert.Null(service.ControllerName);
        Assert.Equal(2, changes);
        fake.Connected = "Xbox Wireless Controller";
        var scans = fake.Rescans;
        now += TimeSpan.FromMilliseconds(100);
        service.Tick();
        Assert.Equal(scans, fake.Rescans);
        now += GamepadService.RescanInterval;
        service.Tick();
        Assert.Equal("Xbox Wireless Controller", service.ControllerName);
        Assert.Equal(3, changes);

        // Ausgeschaltet: keine Abfrage, keine Aktionen
        service.Enabled = false;
        fake.State = GamepadState.Of(GamepadButton.B);
        service.Tick();
        service.Tick();
        Assert.Equal([GamepadAction.NextSection], actions);
        service.Enabled = true;
        fake.State = default;
        service.Tick();
        fake.State = GamepadState.Of(GamepadButton.B);
        service.Tick();
        Assert.Equal(GamepadAction.Back, actions[^1]);

        service.Dispose();
        Assert.True(fake.Disposed);
    }

    [Fact]
    public void Missing_sdl_is_a_silent_no_op()
    {
        using var none = new GamepadService(() => null);
        none.WindowFocused = true;
        none.Tick();
        none.Tick();
        Assert.Equal(GamepadAvailability.Unavailable, none.Availability);
        Assert.Null(none.ControllerName);

        var failing = new FakeGamepadBackend(initializes: false);
        using var broken = new GamepadService(() => failing);
        broken.Tick();
        Assert.Equal(GamepadAvailability.Unavailable, broken.Availability);
        Assert.True(failing.Disposed);

        // Der Thread endet ohne SDL von selbst
        none.Start();
        Assert.Null(SdlGamepadBackend.TryCreate(["libDoesNotExist-0.so.0"]));
    }

    [Fact]
    public void Sdl_loads_when_installed()
    {
        // Testrechner/Runner: SDL2 (Debian) bzw. SDL3 – fehlt beides, gibt es nichts zu prüfen
        using var sdl = SdlGamepadBackend.TryCreate();
        if (sdl is null)
        {
            return;
        }

        Assert.Contains(sdl.Library, new[] { "SDL2", "SDL3" });
        Assert.True(sdl.Initialize());
        _ = sdl.Poll(rescan: true); // ohne Controller null – darf nicht abstürzen
        _ = sdl.Poll(rescan: false);
    }

    [Theory]
    [InlineData(new[] { "--gamemode" }, null, null, null, true)]
    [InlineData(new string[0], "1", null, null, true)]
    [InlineData(new string[0], "0", "gamescope-0", null, false)]
    [InlineData(new string[0], null, "gamescope-0", null, true)]
    [InlineData(new string[0], null, null, "gamescope", true)]
    [InlineData(new string[0], null, null, "KDE", false)]
    public void Game_mode_detection(string[] args, string? force, string? gamescopeDisplay, string? desktop, bool expected)
    {
        var env = new Dictionary<string, string?>
        {
            ["ROG_GAMEMODE"] = force,
            ["GAMESCOPE_WAYLAND_DISPLAY"] = gamescopeDisplay,
            ["XDG_CURRENT_DESKTOP"] = desktop,
            ["SteamDeck"] = "1",
        };
        Assert.Equal(expected, LaunchOptions.Parse(args, n => env.GetValueOrDefault(n)).GameMode);
    }
}
