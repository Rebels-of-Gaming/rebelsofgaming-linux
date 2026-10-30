using Avalonia.Headless.XUnit;
using RoG.Linux.App;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 3: Tray (Menü, Tooltip, Schließen in den Infobereich), Autostart und Start im Hintergrund.</summary>
public class DesktopIntegrationTests
{
    /// <summary>Tray ohne D-Bus: merkt sich Menü und Tooltip, Klicks lassen sich auslösen.</summary>
    private sealed class FakeTray : ITrayIcon
    {
        public bool IsVisible { get; set; } = true;

        public IReadOnlyList<TrayMenuEntry> Menu { get; private set; } = [];

        public (string Title, string Body) ToolTip { get; private set; }

        public event EventHandler? VisibilityChanged;

        public event EventHandler? Activated;

        public event EventHandler<int>? MenuItemInvoked;

        public Task<bool> StartAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsVisible);

        public void SetToolTip(string title, string body) => ToolTip = (title, body);

        public void SetMenu(IReadOnlyList<TrayMenuEntry> entries) => Menu = entries;

        public void Click() => Activated?.Invoke(this, EventArgs.Empty);

        public void Choose(int id) => MenuItemInvoked?.Invoke(this, id);

        public void Hide()
        {
            IsVisible = false;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Tray_menu_tooltip_and_commands()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var fake = new FakeTray();
        await using var tray = new TrayService(t.Services, fake);
        Assert.True(await tray.StartAsync());

        Assert.Equal(["Öffnen", "Benachrichtigungen", "Nachrichten", "Server", null, "Beenden"], fake.Menu.Select(e => e.Label));
        Assert.Equal(("Rebels of Gaming", string.Empty), fake.ToolTip);

        t.Services.Realtime.UnreadNotifications = 3;
        t.Services.Realtime.UnreadMessages = 2;
        Assert.Equal("5 ungelesen", fake.ToolTip.Body);

        var commands = new List<TrayCommand>();
        tray.Invoked += (_, c) => commands.Add(c);
        fake.Click();
        fake.Choose(3);
        fake.Choose(9);
        await ViewModelTests.WaitUntil(() => commands.Count == 3);
        Assert.Equal([TrayCommand.Open, TrayCommand.Messages, TrayCommand.Exit], commands);
    }

    [AvaloniaFact]
    public void Tray_icons_are_rendered_from_the_badge()
    {
        var icons = TrayService.RenderIcons();
        Assert.Equal([16, 22, 24, 32, 48, 64], icons.Select(i => i.Width));
        var big = icons[^1];
        Assert.Equal(64 * 64 * 4, big.Argb.Length);
        // Ecke durchsichtig, Mitte deckend (Abzeichen)
        Assert.Equal(0, big.Argb[0]);
        var center = ((32 * 64) + 32) * 4;
        Assert.Equal(255, big.Argb[center]);
    }

    [AvaloniaFact]
    public async Task Closing_hides_to_the_tray_only_when_wanted_and_exit_really_closes()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Show();
        var trayVisible = true;
        window.CloseToTray = () => trayVisible && t.Services.Settings.CloseToTray;
        var hidden = 0;
        window.HiddenToTray += (_, _) => hidden++;

        window.Close();
        Assert.False(window.IsVisible);
        Assert.Equal(1, hidden);
        Assert.NotNull(t.Services.Settings.Window); // Lage trotzdem gemerkt

        RoG.Linux.App.App.BringToFront(window);
        Assert.True(window.IsVisible);

        // Ohne sichtbares Tray schließt das Fenster
        trayVisible = false;
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        Assert.True(closed);

        // „Beenden“ im Tray schließt trotz Einstellung
        var second = RoG.Linux.App.App.CreateMainWindow(t.Services);
        second.Show();
        second.CloseToTray = () => true;
        second.ExitRequested = true;
        var closed2 = false;
        second.Closed += (_, _) => closed2 = true;
        second.Close();
        Assert.True(closed2);
    }

    [AvaloniaFact]
    public async Task Settings_close_to_tray_and_autostart()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var fake = new FakeTray();
        t.Services.Tray = new TrayService(t.Services, fake);
        var autostart = (FakeAutostart)t.Services.Autostart;
        var settings = new SettingsViewModel(t.Services);
        await ViewModelTests.WaitUntil(() => settings.CanChangeAutostart);

        Assert.True(settings.CloseToTray);
        Assert.False(settings.TrayUnavailable);
        settings.CloseToTray = false;
        await ViewModelTests.WaitUntil(() => !t.Services.Settings.CloseToTray);
        fake.Hide();
        await ViewModelTests.WaitUntil(() => settings.TrayUnavailable);

        Assert.False(settings.Autostart);
        settings.Autostart = true;
        await ViewModelTests.WaitUntil(() => settings.CanChangeAutostart && t.Services.LinuxSettings.Autostart);
        Assert.Equal([true], autostart.Requests);
        Assert.True(autostart.Enabled);

        // Portal lehnt ab → Schalter zurück, Hinweis
        autostart.Answer = AutostartResult.Denied;
        settings.Autostart = false;
        await ViewModelTests.WaitUntil(() => settings.HasAutostartFeedback);
        Assert.True(settings.Autostart);
        Assert.Equal(t.Services.T("Linux_AutostartDenied"), settings.AutostartFeedback);
    }

    [AvaloniaFact]
    public async Task Portal_autostart_state_comes_from_linux_json()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var autostart = (FakeAutostart)t.Services.Autostart;
        autostart.UsesPortal = true;
        await t.Services.UpdateLinuxSettingsAsync(s => s with { Autostart = true });
        var settings = new SettingsViewModel(t.Services);
        await ViewModelTests.WaitUntil(() => settings.CanChangeAutostart);
        Assert.True(settings.Autostart);
        Assert.True(settings.AutostartUsesPortal);
        Assert.True(settings.TrayUnavailable); // kein Tray in Tests
    }

    [Fact]
    public void Background_argument_is_parsed()
    {
        Assert.True(LaunchOptions.Parse(["--background"], _ => null).Background);
        Assert.False(LaunchOptions.Parse(["rog://news"], _ => null).Background);
        Assert.Equal(XdgAutostart.BackgroundArgument, LaunchOptions.BackgroundArgument);
        Assert.Equal("/home/x/.config", new LinuxPaths(n => n == "HOME" ? "/home/x" : null).ConfigHome);
    }
}
