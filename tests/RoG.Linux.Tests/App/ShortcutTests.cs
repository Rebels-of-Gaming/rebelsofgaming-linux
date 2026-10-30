using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 4: Tastenkürzel – Verlauf (Alt+←/→), Strg+N, Strg+R, Strg+W/Q, F1 und Enter im Chat.</summary>
public class ShortcutTests
{
    private static void Press(Avalonia.Controls.Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    [AvaloniaFact]
    public async Task History_new_refresh_help_and_close()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();

        shell.NavigateTo("Nav_News");
        shell.NavigateTo("Nav_Calendar");
        Press(window, Key.Left, RawInputModifiers.Alt);
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        Press(window, Key.Left, RawInputModifiers.Alt);
        Assert.Equal("Nav_Home", shell.CurrentItem?.Key);
        Assert.False(shell.CanGoBack);
        Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        Assert.True(shell.CanGoForward);

        // Strg+R lädt neu
        var before = t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/news");
        Press(window, Key.R, RawInputModifiers.Control);
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/news") > before);

        // Strg+N → Nachrichten; im Forum mit Schreibrecht → neues Thema
        Press(window, Key.N, RawInputModifiers.Control);
        Assert.Equal("Nav_Messages", shell.CurrentItem?.Key);
        Assert.True(shell.OpenDeepLink(new Uri("rog://forum/allgemein")));
        var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => forum.CanCreateThread);
        Press(window, Key.N, RawInputModifiers.Control);
        Assert.True(forum.IsComposing);

        // F1 → Einstellungen (Tastenkürzel)
        Press(window, Key.F1);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Assert.True(settings.Shortcuts.Count >= 10);
        Assert.Contains(settings.Shortcuts, s => s.Keys.StartsWith("Strg+K", StringComparison.Ordinal));

        // Strg+W schließt (ohne Tray wirklich), Strg+Q beendet
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.CloseToTray = () => true;
        Press(window, Key.W, RawInputModifiers.Control);
        Assert.False(window.IsVisible);
        Assert.False(closed);
        window.Show();
        window.KeyPress(Key.Q, RawInputModifiers.Control, PhysicalKey.None, null); // Fenster ist danach geschlossen
        Assert.True(closed);
    }
}
