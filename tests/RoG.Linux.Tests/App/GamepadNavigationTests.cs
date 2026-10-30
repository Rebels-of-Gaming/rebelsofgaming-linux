using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Platform.Input;
using RoG.Linux.Tests.Screenshots;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 10: Controller → Fokus, Bereiche, Zurück, Suche; Spielmodus (große Ziele, Tastatur-Hinweis); Einstellungen.</summary>
public class GamepadNavigationTests
{
    private static async Task<(Window Window, ShellViewModel Shell, GamepadNavigator Pad)> OpenAsync(TestServices t, int width = 1280, int height = 800)
    {
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = width;
        window.Height = height;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        await ScreenshotTests.Settle(150);
        return (window, shell, new GamepadNavigator(window, t.Services));
    }

    private static Control? Focused(Window window) => window.FocusManager?.GetFocusedElement() as Control;

    [AvaloniaFact]
    public async Task Shoulder_buttons_switch_sections_and_focus_the_entry()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var (window, shell, pad) = await OpenAsync(t);
        Assert.Equal("Nav_Home", shell.CurrentItem?.Key);

        Assert.True(pad.Execute(GamepadAction.NextSection));
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        await ScreenshotTests.Settle(100);
        var entry = Assert.IsType<Button>(Focused(window));
        Assert.Contains("nav", entry.Classes);
        Assert.Same(shell.CurrentItem, entry.DataContext);

        pad.Execute(GamepadAction.PreviousSection);
        pad.Execute(GamepadAction.PreviousSection); // ringsum: Home → letzter Eintrag (Einstellungen)
        Assert.Equal("Nav_Settings", shell.CurrentItem?.Key);
        pad.Execute(GamepadAction.NextSection);
        Assert.Equal("Nav_Home", shell.CurrentItem?.Key);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Directions_move_focus_through_the_page_and_into_the_sidebar()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var (window, shell, pad) = await OpenAsync(t);
        shell.NavigateTo("Nav_Settings");
        await ScreenshotTests.Settle(300);

        // Ohne Fokus: erstes Element der Seite
        Assert.True(pad.Execute(GamepadAction.Down));
        var first = Focused(window);
        Assert.NotNull(first);
        Assert.NotNull(first.FindAncestorOfType<SettingsView>());

        // ↓ wandert weiter (Auswahllisten ändern dabei ihren Wert nicht)
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        var language = settings.SelectedLanguage;
        var visited = new HashSet<Control> { first };
        for (var i = 0; i < 6; i++)
        {
            Assert.True(pad.Execute(GamepadAction.Down));
            visited.Add(Focused(window)!);
        }

        Assert.True(visited.Count >= 5, $"Fokus bleibt hängen: {visited.Count}");
        Assert.Same(language, settings.SelectedLanguage);

        // ← aus dem Inhalt in die Seitenleiste
        for (var i = 0; i < 4 && Focused(window)?.FindAncestorOfType<NavPane>() is null; i++)
        {
            pad.Execute(GamepadAction.Left);
        }

        Assert.NotNull(Focused(window)?.FindAncestorOfType<NavPane>());

        // Menü: Seitenleiste ↔ Inhalt
        Assert.True(pad.Execute(GamepadAction.Menu));
        Assert.NotNull(Focused(window)?.FindAncestorOfType<SettingsView>());
        Assert.True(pad.Execute(GamepadAction.Menu));
        var entry = Assert.IsType<Button>(Focused(window));
        Assert.Same(shell.CurrentItem, entry.DataContext);

        // A auf einem Eintrag der Seitenleiste öffnet ihn; ↓ geht zum nächsten
        pad.Execute(GamepadAction.Up);
        var target = Assert.IsType<NavItem>(Focused(window)!.DataContext);
        Assert.True(pad.Execute(GamepadAction.Activate));
        Assert.Same(target, shell.CurrentItem);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Back_closes_the_detail_then_goes_back_search_and_refresh()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var (window, shell, pad) = await OpenAsync(t);
        shell.NavigateTo("Nav_News");
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.ShowContent);
        news.Selected = news.Items[0];
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
        Assert.True(((IDetailPage)news).IsDetailOpen);

        Assert.True(pad.Execute(GamepadAction.Back)); // Escape: Detail zu
        Assert.False(((IDetailPage)news).IsDetailOpen);
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        Assert.True(pad.Execute(GamepadAction.Back)); // Alt+←: vorheriger Bereich
        Assert.Equal("Nav_Home", shell.CurrentItem?.Key);

        // Y = F5 lädt neu, Ansicht = Suche
        shell.NavigateTo("Nav_News");
        var before = t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/news");
        Assert.True(pad.Execute(GamepadAction.Refresh));
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/news") > before);
        Assert.True(pad.Execute(GamepadAction.Search));
        Assert.Equal("Nav_Search", shell.CurrentItem?.Key);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Narrow_menu_opens_the_overlay_and_keeps_focus_inside()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var (window, shell, pad) = await OpenAsync(t, 480, 800);
        Assert.True(shell.IsNarrow);
        Assert.True(pad.Execute(GamepadAction.Menu));
        Assert.True(shell.IsOverlayOpen);
        await ScreenshotTests.Settle(100);
        var overlay = window.GetVisualDescendants().OfType<NavPane>().Single(p => p.Name == "OverlayPane");
        Assert.True(overlay.IsVisualAncestorOf(Focused(window)!));
        for (var i = 0; i < 25; i++)
        {
            pad.Execute(GamepadAction.Down);
            Assert.True(overlay.IsVisualAncestorOf(Focused(window)!), "Fokus hat die Überlagerung verlassen");
        }

        Assert.True(pad.Execute(GamepadAction.Back)); // Escape schließt die Überlagerung
        Assert.False(shell.IsOverlayOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Controller_events_reach_the_window_only_with_focus()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var fake = new FakeGamepadBackend();
        t.Services.Gamepad.UseBackend(fake);
        var (window, shell, _) = await OpenAsync(t);

        t.Services.Gamepad.WindowFocused = false;
        fake.State = GamepadState.Of(GamepadButton.RightShoulder);
        t.Services.Gamepad.Tick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Nav_Home", shell.CurrentItem?.Key);

        t.Services.Gamepad.WindowFocused = true;
        fake.State = default;
        t.Services.Gamepad.Tick();
        fake.State = GamepadState.Of(GamepadButton.RightShoulder);
        t.Services.Gamepad.Tick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Game_mode_enlarges_targets_and_hints_the_keyboard_once()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.GameMode = true;
        var (window, shell, _) = await OpenAsync(t);
        Assert.Contains("gamemode", window.Classes);
        Assert.True(shell.IsGameMode);
        var nav = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("nav") && b.IsEffectivelyVisible);
        Assert.Equal(44, nav.Bounds.Height, 0.5);

        shell.NavigateTo("Nav_Search");
        await ScreenshotTests.Settle(200);
        var box = window.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible);
        box.Focus(NavigationMethod.Tab); // die Suche fokussiert ihr Feld ggf. schon selbst
        Assert.True(shell.IsKeyboardHintVisible);
        shell.CloseKeyboardHintCommand.Execute(null);
        Assert.False(shell.IsKeyboardHintVisible);
        window.Focus();
        box.Focus(NavigationMethod.Tab);
        Assert.False(shell.IsKeyboardHintVisible); // einmal je Sitzung
        window.Close();
    }

    [AvaloniaFact]
    public async Task Desktop_mode_keeps_compact_sidebar_and_no_hint()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.GameMode = false;
        var (window, shell, _) = await OpenAsync(t);
        Assert.DoesNotContain("gamemode", window.Classes);
        var nav = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("nav") && b.IsEffectivelyVisible);
        Assert.True(nav.Bounds.Height < 40);
        shell.NavigateTo("Nav_Search");
        await ScreenshotTests.Settle(200);
        window.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible).Focus(NavigationMethod.Tab);
        Assert.False(shell.IsKeyboardHintVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_show_the_controller_and_switch_it_off()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var fake = new FakeGamepadBackend();
        t.Services.Gamepad.UseBackend(fake);
        t.Services.Gamepad.Tick();
        var settings = new SettingsViewModel(t.Services);
        var controller = settings.Controller;
        Assert.True(controller.Enabled);
        Assert.Equal("Angeschlossen: Steam Deck Controller · über Fake", controller.StatusText);
        Assert.Equal(9, controller.Mapping.Count);
        Assert.Equal("A", controller.Mapping[1].Keys);
        Assert.Equal("Auswählen / aktivieren", controller.Mapping[1].Description);

        // Abziehen → Hot-Plug-Meldung
        fake.Connected = null;
        t.Services.Gamepad.Tick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Kein Controller angeschlossen", controller.StatusText);

        controller.Enabled = false;
        Assert.False(t.Services.Gamepad.Enabled);
        Assert.Equal("Controller ausgeschaltet", controller.StatusText);
        await ViewModelTests.WaitUntil(() => !t.Services.LinuxSettings.Controller);

        // Ohne SDL: Hinweis statt Schalter
        t.Services.Gamepad.UseBackend(new FakeGamepadBackend(initializes: false));
        controller.UpdateStatus();
        Assert.True(controller.IsUnavailable);
        Assert.Contains("SDL", controller.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Mapping_is_translated()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke, language: "fr");
        var controller = new ControllerSettingsViewModel(t.Services);
        Assert.Equal("Croix · stick gauche", controller.Mapping[0].Keys);
        Assert.Equal("Aucune manette connectée", controller.StatusText);
    }
}
