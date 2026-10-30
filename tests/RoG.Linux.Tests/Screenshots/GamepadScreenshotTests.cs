using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 10: Spielmodus (Steam Deck, große Ziele, Tastatur-Hinweis) und Einstellungen → Controller.</summary>
[Trait("Category", "Screenshots")]
public class GamepadScreenshotTests
{
    private static async Task<TestServices> CreateAsync(bool gameMode, string language = "de")
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke, language: language);
        t.Services.GameMode = gameMode;
        t.Services.Gamepad.UseBackend(new FakeGamepadBackend());
        t.Services.Gamepad.Tick();
        return t;
    }

    [AvaloniaFact]
    public async Task Game_mode_home() =>
        await ScreenshotTests.Shoot("gamemode_home_1280x800", 1280, 800, await CreateAsync(gameMode: true), async shell =>
        {
            var home = Assert.IsType<HomeViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => home.ShowContent && home.HasTeasers);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Game_mode_settings_controller() =>
        await ScreenshotTests.Shoot("gamemode_settings_controller_1280x800", 1280, 800, await CreateAsync(gameMode: true), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            await ScreenshotTests.Settle(300);
            FocusFirstToggle(shell);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Game_mode_keyboard_hint() =>
        await ScreenshotTests.Shoot("gamemode_search_keyboard_hint_1280x800", 1280, 800, await CreateAsync(gameMode: true), async shell =>
        {
            shell.NavigateTo("Nav_Search");
            await ScreenshotTests.Settle(200);
            Window(shell).GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible).Focus(NavigationMethod.Tab);
            await ScreenshotTests.Settle(200);
            Assert.True(shell.IsKeyboardHintVisible);
        });

    [AvaloniaFact]
    public async Task Settings_controller_narrow() =>
        await ScreenshotTests.Shoot("settings_controller_narrow_480x900", 480, 900, await CreateAsync(gameMode: false), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            await ScreenshotTests.Settle(300);
            Window(shell).GetVisualDescendants().OfType<SettingsView>().First().ShowController();
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Settings_controller_english() =>
        await ScreenshotTests.Shoot("settings_controller_en_1280x800", 1280, 800, await CreateAsync(gameMode: false, language: "en"), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            await ScreenshotTests.Settle(300);
            Window(shell).GetVisualDescendants().OfType<SettingsView>().First().ShowController();
            await ScreenshotTests.Settle(300);
        });

    private static readonly List<WeakReference<MainWindow>> s_windows = [];

    static GamepadScreenshotTests()
    {
        // Shoot gibt das Fenster nicht heraus – geöffnete Hauptfenster merken und über die Shell wiederfinden
        // (andere Tests laufen auf demselben UI-Thread dazwischen)
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<MainWindow>((w, _) => s_windows.Add(new WeakReference<MainWindow>(w)));
    }

    private static MainWindow Window(ShellViewModel shell) =>
        s_windows.Select(r => r.TryGetTarget(out var w) ? w : null).LastOrDefault(w => ReferenceEquals(w?.DataContext, shell))
        ?? throw new InvalidOperationException("Kein Hauptfenster");

    /// <summary>Controller-Abschnitt in Sicht, Fokus (wie per Controller) auf seinem Schalter.</summary>
    private static void FocusFirstToggle(ShellViewModel shell)
    {
        var view = Window(shell).GetVisualDescendants().OfType<SettingsView>().First();
        view.ShowController();
        var toggle = view.FindControl<Border>("ControllerCard")!.GetVisualDescendants().OfType<ToggleSwitch>().First();
        toggle.Focus(NavigationMethod.Directional);
    }
}
