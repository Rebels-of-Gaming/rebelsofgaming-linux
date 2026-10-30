using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Rendert die Hauptseiten headless (Skia, echte Schriften) nach <c>artifacts/screenshots/</c> – zum Ansehen ohne
/// Bildschirm und als Rauchtest der Oberfläche (Bindungen, Vorlagen, Layout). 1280×800 = Steam Deck.
/// </summary>
[Trait("Category", "Screenshots")]
public class ScreenshotTests
{
    private static readonly string s_folder = Path.Combine(LocalizationTests.RepoRoot(), "artifacts", "screenshots");

    [AvaloniaFact]
    public async Task Login() => await Shoot("login_1280x800", 1280, 800, await TestServices.CreateAsync(), _ => Task.CompletedTask);

    [AvaloniaFact]
    public async Task Login_without_keyring() =>
        await Shoot("login_no-keyring_1280x800", 1280, 800, await TestServices.CreateAsync(keyring: false), _ => Task.CompletedTask);

    [AvaloniaFact]
    public async Task News_wide() => await Shoot("news_1440x900", 1440, 900, await TestServices.CreateAsync(guest: true), OpenArticle);

    [AvaloniaFact]
    public async Task News_steam_deck() => await Shoot("news_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), OpenArticle);

    /// <summary>Ganzer Artikel (hohes Fenster): Überschriften, Liste, Zitat, Code, Inline-Link, Kommentare.</summary>
    [AvaloniaFact]
    public async Task News_article_full() => await Shoot("news_article_full_1280x1500", 1280, 1500, await TestServices.CreateAsync(guest: true), OpenArticle);

    [AvaloniaFact]
    public async Task News_narrow() => await Shoot("news_narrow_760x720", 760, 720, await TestServices.CreateAsync(guest: true), OpenArticle);

    [AvaloniaFact]
    public async Task News_list_english() =>
        await Shoot("news_list_en_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true, language: "en"), _ => Task.CompletedTask);

    [AvaloniaFact]
    public async Task Messages_wide() => await Shoot("messages_1440x900", 1440, 900, await TestServices.CreateAsync(TestTokens.Wahke), OpenChat);

    [AvaloniaFact]
    public async Task Messages_steam_deck() => await Shoot("messages_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenChat);

    [AvaloniaFact]
    public async Task Account() =>
        await Shoot("account_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell =>
        {
            shell.NavigateTo("Nav_Account");
            return Settle(400);
        });

    [AvaloniaFact]
    public async Task Placeholder_french() =>
        await Shoot("placeholder_fr_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true, language: "fr"), shell =>
        {
            shell.NavigateTo("Nav_Servers");
            return Settle(100);
        });

    [AvaloniaFact]
    public async Task Home_steam_deck() =>
        await Shoot("home_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), WaitForHome, AssertSidebarFits);

    [AvaloniaFact]
    public async Task Home_guest() =>
        await Shoot("home_guest_1440x900", 1440, 900, await TestServices.CreateAsync(guest: true), WaitForHome);

    [AvaloniaFact]
    public async Task Home_light_theme() =>
        await Shoot("home_light_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            await WaitForHome(shell);
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            await Settle(200);
        });

    [AvaloniaFact]
    public async Task Home_narrow() =>
        await Shoot("home_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), WaitForHome);

    [AvaloniaFact]
    public async Task Menu_narrow() =>
        await Shoot("menu_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            await WaitForHome(shell);
            shell.TogglePaneCommand.Execute(null);
            await Settle(150);
        });

    [AvaloniaFact]
    public async Task Compact_sidebar() =>
        await Shoot("home_compact_760x800", 760, 800, await TestServices.CreateAsync(TestTokens.Wahke), WaitForHome, AssertSidebarFits);

    [AvaloniaFact]
    public async Task Settings_steam_deck() =>
        await Shoot("settings_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            await Settle(400);
        }, AssertSidebarFits);

    [AvaloniaFact]
    public async Task Settings_english_narrow() =>
        await Shoot("settings_en_narrow_480x900", 480, 900, await TestServices.CreateAsync(guest: true, language: "en"), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            await Settle(400);
        });

    [AvaloniaFact]
    public async Task Messages_narrow() =>
        await Shoot("messages_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenChat);

    [AvaloniaFact]
    public async Task Account_narrow() =>
        await Shoot("account_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell =>
        {
            shell.NavigateTo("Nav_Account");
            return Settle(400);
        });

    private static async Task WaitForHome(ShellViewModel shell)
    {
        var home = Assert.IsType<HomeViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => home.ShowContent && home.HasTeasers);
        await Settle(300);
    }

    /// <summary>Steam Deck: alle Bereiche der Seitenleiste ohne Scrollen sichtbar.</summary>
    private static void AssertSidebarFits(Avalonia.Controls.Window window)
    {
        var pane = window.GetVisualDescendants().OfType<RoG.Linux.App.Views.NavPane>().First(p => p.IsEffectivelyVisible);
        var scroll = pane.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First();
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, $"Seitenleiste scrollt: {scroll.Extent.Height} > {scroll.Viewport.Height}");
    }

    private static async Task OpenArticle(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_News");
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.ShowContent);
        news.Selected = news.Items[0];
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent && news.Detail.HasCover);
        await Settle(300);
    }

    private static async Task OpenChat(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Messages");
        var messages = Assert.IsType<MessagesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => messages.ShowContent);
        messages.Selected = messages.Items[0];
        await ViewModelTests.WaitUntil(() => messages.Chat?.ShowContent == true);
        await Settle(300);
    }

    private static async Task Shoot(string name, int width, int height, TestServices t, Func<ShellViewModel, Task> arrange, Action<Avalonia.Controls.Window>? check = null)
    {
        using (t)
        {
            var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
            window.Width = width;
            window.Height = height;
            window.Show();
            var shell = (ShellViewModel)window.DataContext!;
            await shell.InitializeAsync();
            await Settle(200);
            await arrange(shell);

            Directory.CreateDirectory(s_folder);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width);
            frame.Save(Path.Combine(s_folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            check?.Invoke(window);
            window.Close();
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }
    }

    /// <summary>Bilder (asynchron) und Layout fertig werden lassen.</summary>
    private static async Task Settle(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }
}
