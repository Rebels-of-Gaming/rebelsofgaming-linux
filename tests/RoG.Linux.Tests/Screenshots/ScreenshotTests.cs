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
            shell.NavigateTo("Nav_Wiki");
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

    // ---- Stufe 2 ----

    [AvaloniaFact]
    public async Task Squads_steam_deck() => await Shoot("squads_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenSquad(shell, SquadTab.About));

    [AvaloniaFact]
    public async Task Squads_lineup() => await Shoot("squads_lineup_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenSquad(shell, SquadTab.Lineup));

    [AvaloniaFact]
    public async Task Squads_servers() => await Shoot("squads_servers_1440x900", 1440, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenSquad(shell, SquadTab.Servers));

    [AvaloniaFact]
    public async Task Squads_internal() => await Shoot("squads_internal_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenSquad(shell, SquadTab.Internal));

    [AvaloniaFact]
    public async Task Squads_narrow() => await Shoot("squads_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenSquad(shell, SquadTab.About));

    [AvaloniaFact]
    public async Task Games() =>
        await Shoot("games_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Squads");
            var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => squads.ShowContent);
            squads.GamesMode = true;
            await ViewModelTests.WaitUntil(() => squads.Games.ShowContent);
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Members_steam_deck() => await Shoot("members_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenProfile, AssertSidebarFits);

    [AvaloniaFact]
    public async Task Members_narrow() => await Shoot("members_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), OpenProfile);

    [AvaloniaFact]
    public async Task Members_list_narrow() =>
        await Shoot("members_list_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            shell.NavigateTo("Nav_Members");
            var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => members.ShowContent);
            await Settle(400);
        });

    [AvaloniaFact]
    public async Task Members_light_theme() =>
        await Shoot("members_light_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            await OpenProfile(shell);
        });

    [AvaloniaFact]
    public async Task Servers_steam_deck() => await Shoot("servers_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), OpenServers);

    [AvaloniaFact]
    public async Task Servers_narrow() => await Shoot("servers_narrow_480x900", 480, 900, await TestServices.CreateAsync(guest: true), OpenServers);

    [AvaloniaFact]
    public async Task Servers_minecraft() => await Shoot("servers_minecraft_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenServerTab(shell, "minecraft"));

    [AvaloniaFact]
    public async Task Servers_minecraft_narrow() => await Shoot("servers_minecraft_narrow_480x900", 480, 900, await TestServices.CreateAsync(guest: true), shell => OpenServerTab(shell, "minecraft"));

    [AvaloniaFact]
    public async Task Servers_teamspeak() => await Shoot("servers_teamspeak_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenServerTab(shell, "teamspeak"));

    [AvaloniaFact]
    public async Task Servers_teamspeak_narrow() => await Shoot("servers_teamspeak_narrow_480x800", 480, 800, await TestServices.CreateAsync(guest: true), shell => OpenServerTab(shell, "teamspeak"));

    [AvaloniaFact]
    public async Task Servers_discord() => await Shoot("servers_discord_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenServerTab(shell, "discord"));

    [AvaloniaFact]
    public async Task Servers_discord_guest() => await Shoot("servers_discord_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => OpenServerTab(shell, "discord"));

    [AvaloniaFact]
    public async Task Uploads() => await Shoot("uploads_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenUploads(shell, false));

    [AvaloniaFact]
    public async Task Uploads_narrow() => await Shoot("uploads_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenUploads(shell, false));

    [AvaloniaFact]
    public async Task Uploads_links() => await Shoot("uploads_links_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenUploads(shell, true));

    [AvaloniaFact]
    public async Task Uploads_guest() =>
        await Shoot("uploads_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Uploads");
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Media_albums() => await Shoot("media_albums_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => OpenMedia(shell, 0));

    [AvaloniaFact]
    public async Task Media_album() => await Shoot("media_album_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenMedia(shell, 1));

    [AvaloniaFact]
    public async Task Media_viewer() => await Shoot("media_viewer_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenMedia(shell, 2));

    [AvaloniaFact]
    public async Task Media_downloads() => await Shoot("media_downloads_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenMedia(shell, 3));

    [AvaloniaFact]
    public async Task Media_album_narrow() => await Shoot("media_album_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => OpenMedia(shell, 1));

    [AvaloniaFact]
    public async Task Account_edit_profile() =>
        await Shoot("account_edit_1280x1300", 1280, 1300, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            shell.NavigateTo("Nav_Account");
            var account = Assert.IsType<AccountViewModel>(shell.CurrentPage);
            account.Editor.IsExpanded = true;
            await ViewModelTests.WaitUntil(() => account.Editor.ShowContent);
            await Settle(400);
        });

    [AvaloniaFact]
    public async Task Settings_full() =>
        await Shoot("settings_full_1280x2000", 1280, 2000, await TestServices.CreateAsync(guest: true), shell =>
        {
            shell.NavigateTo("Nav_Settings");
            return Settle(300);
        });

    [AvaloniaFact]
    public async Task Forum_thread() => await Shoot("forum_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenForumThread);

    [AvaloniaFact]
    public async Task Forum_thread_narrow() => await Shoot("forum_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), OpenForumThread);

    [AvaloniaFact]
    public async Task Forum_categories_guest() =>
        await Shoot("forum_categories_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Forum");
            var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => forum.ShowContent);
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Forum_new_thread() =>
        await Shoot("forum_new_thread_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            await OpenForumThread(shell);
            var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
            forum.StartNewThreadCommand.Execute(null);
            forum.NewThread.Title = "Scrim am Freitag?";
            forum.NewThread.Body = "[b]Wer ist dabei?[/b] Treffpunkt wie immer im TeamSpeak. @Nyx";
            await forum.NewThread.TogglePreviewCommand.ExecuteAsync(null);
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Calendar_steam_deck() => await Shoot("calendar_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenEvent);

    [AvaloniaFact]
    public async Task Calendar_month() =>
        await Shoot("calendar_month_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            await OpenEvent(shell);
            var events = Assert.IsType<EventsViewModel>(shell.CurrentPage);
            events.MonthMode = true;
            await ViewModelTests.WaitUntil(() => events.MonthDays.Count == 42);
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Calendar_narrow() => await Shoot("calendar_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), OpenEvent);

    [AvaloniaFact]
    public async Task Info_steam_deck() =>
        await Shoot("info_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            Assert.True(shell.OpenDeepLink(new Uri("rog://info/rules")));
            var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => info.Detail.ShowContent);
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Search_steam_deck() =>
        await Shoot("search_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true, language: "en"), async shell =>
        {
            Assert.True(shell.OpenDeepLink(new Uri("rog://search?q=et")));
            var search = Assert.IsType<SearchViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => search.ShowContent);
            await Settle(300);
        });

    // ---- Stufe 3 ----

    [AvaloniaFact]
    public async Task Notifications_steam_deck() =>
        await Shoot("notifications_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenBell, AssertSidebarFits);

    [AvaloniaFact]
    public async Task Notifications_narrow() =>
        await Shoot("notifications_narrow_480x800", 480, 800, await TestServices.CreateAsync(TestTokens.Wahke), OpenBell);

    [AvaloniaFact]
    public async Task Notifications_guest() =>
        await Shoot("notifications_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Notifications");
            await Settle(400);
        });

    private static async Task OpenBell(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Notifications");
        var bell = Assert.IsType<NotificationsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => bell.ShowContent);
        await Settle(300);
    }

    private static async Task OpenSquad(ShellViewModel shell, SquadTab tab)
    {
        // Profil für „Intern“ (Line-up enthält Wahke)
        shell.NavigateTo("Nav_Squads");
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.ShowContent);
        squads.Selected = squads.Items[0];
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent && squads.Detail.HasHeader);
        squads.Detail.Tab = tab;
        if (tab == SquadTab.Internal)
        {
            await ViewModelTests.WaitUntil(() => squads.Detail.Internal.ShowContent);
        }

        await Settle(400);
    }

    private static async Task OpenProfile(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Members");
        var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => members.ShowContent);
        members.Selected = members.Items[1];
        await ViewModelTests.WaitUntil(() => members.Detail.ShowContent && members.Detail.HasBanner);
        await Settle(400);
    }

    private static async Task OpenServers(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Servers");
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => servers.ShowContent);
        await servers.Servers[1].ConnectCommand.ExecuteAsync(null);
        await Settle(300);
        servers.Pause();
    }

    /// <summary>0 Alben, 1 Album (mit Einreichen), 2 Großansicht, 3 Downloads.</summary>
    private static async Task OpenMedia(ShellViewModel shell, int mode)
    {
        shell.NavigateTo("Nav_Media");
        var media = Assert.IsType<MediaViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => media.ShowContent);
        await ViewModelTests.WaitUntil(() => media.Albums[0].Cover is not null);
        if (mode is 1 or 2)
        {
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await ViewModelTests.WaitUntil(() => media.Items.Count == 3 && media.Items.All(i => i.Thumb is not null || i.IsVideo));
            if (mode == 1)
            {
                await ViewModelTests.WaitUntil(() => media.CanUpload);
                media.ToggleSubmitCommand.Execute(null);
            }
            else
            {
                media.ViewCommand.Execute(media.Items[0]);
                await ViewModelTests.WaitUntil(() => media.Viewed?.Full is not null);
            }
        }
        else if (mode == 3)
        {
            media.IsDownloadsTab = true;
            await ViewModelTests.WaitUntil(() => media.Downloads.ShowContent);
        }

        await Settle(400);
    }

    private static async Task OpenUploads(ShellViewModel shell, bool links)
    {
        shell.NavigateTo("Nav_Uploads");
        var uploads = Assert.IsType<UploadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => uploads.ShowContent);
        if (links)
        {
            uploads.IsLinksTab = true;
            await ViewModelTests.WaitUntil(() => uploads.Links.ShowContent);
        }
        else
        {
            uploads.Items[0].CopyDirectCommand.Execute(null);
        }

        await Settle(400);
    }

    private static async Task OpenForumThread(ShellViewModel shell)
    {
        Assert.True(shell.OpenDeepLink(new Uri("rog://forum/allgemein")));
        var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => forum.Threads.Count > 0);
        forum.SelectedThread = forum.Threads[0];
        await ViewModelTests.WaitUntil(() => forum.Thread.ShowContent);
        forum.Thread.Draft = "[quote=Kaito]Passt mir[/quote]\n\nIch bin Freitag dabei!";
        await Settle(400);
    }

    private static async Task OpenServerTab(ShellViewModel shell, string tab)
    {
        Assert.True(shell.OpenDeepLink(new Uri("rog://servers/" + tab)));
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        if (tab == "minecraft")
        {
            await ViewModelTests.WaitUntil(() => servers.Join.ShowContent && servers.Join.HasPack);
        }
        else
        {
            await ViewModelTests.WaitUntil(() => servers.Voice.ShowContent);
            if (tab == "teamspeak")
            {
                await servers.Voice.ConnectCommand.ExecuteAsync(null);
            }
        }

        await Settle(400);
        servers.Pause();
    }

    private static async Task OpenEvent(ShellViewModel shell)
    {
        Assert.True(shell.OpenDeepLink(new Uri("rog://events/01EV1")));
        var events = Assert.IsType<EventsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => events.Detail.ShowContent && events.Detail.HasParticipants);
        await Settle(300);
    }

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

    internal static async Task Shoot(string name, int width, int height, TestServices t, Func<ShellViewModel, Task> arrange, Action<Avalonia.Controls.Window>? check = null)
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
    internal static async Task Settle(int ms)
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
