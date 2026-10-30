using Avalonia.Headless.XUnit;
using RoG.Core.Features.Members;
using RoG.Core.Realtime;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 2: Squads, Mitglieder/Profile, Server mit „Verbinden“, Kalender, Info, Suche, Deep Links.</summary>
public class Stage2Tests
{
    private static async Task<TestServices> SignedInAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    [AvaloniaFact]
    public async Task Squads_list_and_detail_with_lineup_links_servers_and_internal_tab_for_members()
    {
        using var t = await SignedInAsync();
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();

        Assert.Equal(3, squads.Items.Count);
        Assert.True(squads.Items[0].IsOpen);
        Assert.Equal(t.Services.T("Squads_RecruitingWaitlist"), squads.Items[1].RecruitingText);
        Assert.Contains("RoG|", squads.Items[0].Subtitle, StringComparison.Ordinal);

        squads.Selected = squads.Items[0];
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        var detail = squads.Detail;
        Assert.Equal("Enemy Territory", detail.Name);
        Assert.Equal(4, detail.Members.Count);
        Assert.Contains("Ersatz", detail.Members[3].Role, StringComparison.Ordinal);
        Assert.Equal(2, detail.Links.Count);
        Assert.True(detail.HasServers);
        Assert.True(detail.HasTrainingTimes);
        Assert.True(detail.CanSeeInternal); // Wahke steht im Line-up

        detail.IsInternalTab = true;
        await ViewModelTests.WaitUntil(() => detail.Internal.ShowContent || detail.Internal.HasError);
        Assert.Null(detail.Internal.ErrorMessage);
        Assert.Equal(2, detail.Internal.Threads.Count);
        Assert.Single(detail.Internal.Events);
        Assert.Equal(2, detail.Internal.Files.Count);
        Assert.True(detail.Internal.Conversation?.IsUnread);
        Assert.Contains("downloads:read", detail.Internal.ScopeHint, StringComparison.Ordinal);

        // Link kopieren → Zwischenablage + Rückmeldung
        detail.Links[1].CopyCommand.Execute(null);
        Assert.Equal("ROG2026", t.Clipboard.Text);
        Assert.True(detail.Feedback.IsOpen);
        Assert.True(detail.Feedback.IsSuccess);
    }

    [AvaloniaFact]
    public async Task Internal_tab_is_hidden_for_guests()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();
        squads.Open("enemy-territory");
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        Assert.False(squads.Detail.CanSeeInternal);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/intern", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Games_mode_lists_clan_games()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();
        squads.GamesMode = true;
        await ViewModelTests.WaitUntil(() => squads.Games.ShowContent);
        Assert.Equal(3, squads.Games.Items.Count);
        Assert.False(squads.IsDetailOpen);
        Assert.True(squads.Games.Items[0].HasLinks);
    }

    [AvaloniaFact]
    public async Task Members_show_presence_rank_search_team_and_profile()
    {
        using var t = await SignedInAsync();
        var members = new MembersViewModel(t.Services);
        await members.LoadAsync();

        Assert.Equal(6, members.Items.Count);
        Assert.Equal(Presence.Online, members.Items[0].Presence);
        Assert.Equal(Presence.Voice, members.Items[1].Presence);
        Assert.Equal(t.Services.T("Presence_Voice"), members.Items[1].PresenceText);
        Assert.Equal(Presence.Server, members.Items[2].Presence);
        Assert.Equal(Presence.Ingame, members.Items[3].Presence);
        Assert.Equal(Presence.Offline, members.Items[4].Presence);
        Assert.Equal("Discord: Talk 1", members.Items[1].Subtitle);
        Assert.Equal("OWNER", members.Items[0].Rank);
        Assert.True(members.HasMore);

        members.SearchText = "ny";
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Any(r => r.RequestUri!.Query.Contains("filter%5Bsearch%5D=ny", StringComparison.Ordinal)
            || r.RequestUri!.Query.Contains("filter[search]=ny", StringComparison.Ordinal)));

        members.Selected = members.Items[1];
        await ViewModelTests.WaitUntil(() => members.Detail.ShowContent);
        var p = members.Detail;
        Assert.Equal("Nyx", p.Name);
        Assert.Equal(Presence.Voice, p.Presence);
        Assert.Contains("Discord: Talk 1", p.StatusLine, StringComparison.Ordinal);
        Assert.Equal(3, p.Badges.Count);
        Assert.Equal(2, p.Platforms.Count);
        Assert.True(p.HasCoins);
        Assert.Contains("Trier", p.Facts, StringComparison.Ordinal);
        Assert.Equal("Counter-Strike 2 · Enemy Territory", p.Squads);

        members.TeamMode = true;
        await ViewModelTests.WaitUntil(() => members.Items.Count == 3);
        Assert.Null(members.Items[0].Presence);
        Assert.Contains("Finanzen", members.Items[0].Subtitle, StringComparison.Ordinal);
        Assert.False(members.ShowFilters);
    }

    [AvaloniaFact]
    public async Task Servers_connect_launches_known_handlers_and_copies_otherwise()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var servers = new ServersViewModel(t.Services);
        await servers.LoadAsync();
        Assert.Equal(4, servers.Servers.Count);

        var et = servers.Servers[0];
        Assert.True(et.IsOnline);
        Assert.Equal(3, et.Players.Count);
        Assert.Contains("Nyx", et.Players[0], StringComparison.Ordinal);
        Assert.Equal("9/20 Spieler", et.PlayersText);

        // ET: kein Programm für et:// → Adresse kopiert, Hinweis mit Konsolenbefehl
        await et.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(t.Launcher.Launched);
        Assert.Equal("et.rogclan.de:27960", t.Clipboard.Text);
        Assert.True(servers.Feedback.IsInfo);

        // CS2: Steam ist da → steam://connect/…, mit Passwort-Hinweis
        var cs = servers.Servers[1];
        await cs.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("steam://connect/cs.rogclan.de:27015", Assert.Single(t.Launcher.Launched).ToString());
        Assert.True(servers.Feedback.IsSuccess);
        Assert.Contains(t.Services.T("Servers_Passworded"), servers.Feedback.Message, StringComparison.Ordinal);

        // Minecraft: Adresse kopieren und Anleitung
        var mc = servers.Servers[2];
        await mc.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("mc.rogclan.de:25565", t.Clipboard.Text);

        // Offline-Server: Status als Text und Form
        Assert.True(servers.Servers[3].IsOffline);
        Assert.False(servers.Servers[3].ShowPlayers);

        // Kopieren bei gesperrter Zwischenablage → Fehler
        t.Clipboard.Locked = true;
        cs.CopyCommand.Execute(null);
        Assert.True(servers.Feedback.IsError);

        // Echtzeit: Statusänderung ersetzt die Karte
        servers.Apply(new ServerStatusUpdate("01SV1", "et-sniper", true, "goldrush", 12, 20));
        Assert.Equal("12/20 Spieler", servers.Servers[0].PlayersText);
        Assert.Contains("goldrush", servers.Servers[0].Meta, StringComparison.Ordinal);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Servers_error_state_and_retry()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.FailWith = System.Net.HttpStatusCode.ServiceUnavailable;
        var servers = new ServersViewModel(t.Services);
        await servers.LoadAsync();
        Assert.True(servers.HasError);
        t.Api.FailWith = null;
        await servers.RefreshCommand.ExecuteAsync(null);
        Assert.True(servers.ShowContent);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Calendar_list_detail_rsvp_display_participants_and_month()
    {
        using var t = await SignedInAsync();
        var events = new EventsViewModel(t.Services);
        await events.LoadAsync();

        Assert.Equal(3, events.Items.Count);
        Assert.All(events.Items, e => Assert.True(e.ShowDayHeader));

        events.Open("01EV1");
        Assert.Same(events.Items[0], events.Selected);
        await ViewModelTests.WaitUntil(() => events.Detail.HasParticipants && events.Detail.HasMyRsvp);
        var d = events.Detail;
        Assert.Equal("Scrim-Abend CS2", d.Title);
        Assert.Equal(t.Services.T("Events_RsvpYes"), d.RsvpText);
        Assert.True(d.HasServer);
        Assert.Equal("cs.rogclan.de:27015", d.Server!.Address);
        Assert.Contains("Nyx, Kaito", d.ParticipantsText, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("Events_Absent"), d.ParticipantsText, StringComparison.Ordinal);

        await d.AddToCalendarCommand.ExecuteAsync(null);
        Assert.Equal("https://rebelsofgaming.com/ics/01EV1", t.Browser.Opened[^1].ToString());

        events.MonthMode = true;
        await ViewModelTests.WaitUntil(() => events.MonthDays.Count == 42);
        Assert.Contains(events.MonthDays, day => day.IsSelected);
        Assert.False(string.IsNullOrEmpty(events.MonthTitle));
        await events.NextMonthCommand.ExecuteAsync(null);
        Assert.Equal(42, events.MonthDays.Count);

        events.CloseDetail();
        Assert.False(events.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Calendar_for_guests_shows_sign_in_hint_instead_of_answer()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var events = new EventsViewModel(t.Services);
        await events.LoadAsync();
        events.Selected = events.Items[1]; // Termin ohne eigene Antwort (Liste)
        await ViewModelTests.WaitUntil(() => events.Detail.RsvpText is not null);
        Assert.False(events.Detail.IsSignedIn);
        Assert.Equal(t.Services.T("Linux_EventsRsvpGuest"), events.Detail.RsvpText);
        Assert.False(events.Detail.HasParticipants);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/participants", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Info_groups_rules_pages_and_legal_and_renders_rule_sections()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var info = new InfoViewModel(t.Services);
        await info.LoadAsync();

        Assert.Equal(3, info.Rows.Count(r => r.IsGroup));
        Assert.Equal(6, info.Rows.Count(r => r.IsItem));
        Assert.True(info.Rows.First(r => r.IsItem).NeedsAcceptance);

        info.Open("rules");
        Assert.Equal("clan", info.Selected?.Entry?.Key);
        await ViewModelTests.WaitUntil(() => info.Detail.ShowContent);
        Assert.Equal(3, info.Detail.Sections.Count);
        Assert.True(info.Detail.Sections[1].Important);
        Assert.StartsWith("§ 2", info.Detail.Sections[1].Title, StringComparison.Ordinal);
        Assert.True(info.Detail.NeedsAcceptance);

        // Überschriften sind nicht wählbar
        info.Selected = info.Rows[0];
        Assert.Null(info.Selected);
    }

    [AvaloniaFact]
    public async Task Search_groups_hits_and_opens_squads_in_the_app()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://search?q=et")));
        var search = Assert.IsType<SearchViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => search.ShowContent);
        Assert.Equal(3, search.Rows.Count(r => r.IsGroup)); // Wiki fehlgeschlagen → ausgeblendet
        Assert.Equal("et", search.Query);

        search.Rows[1].OpenCommand.Execute(null);
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        Assert.Equal("enemy-territory", squads.Detail.Slug);
    }

    [AvaloniaFact]
    public async Task Search_needs_two_characters()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var search = new SearchViewModel(t.Services);
        search.Query = "e";
        await search.SearchNowCommand.ExecuteAsync(null);
        Assert.True(search.ShowHint);
        Assert.False(search.ShowEmpty);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/search");
    }

    [AvaloniaTheory]
    [InlineData("rog://squads/enemy-territory", "Nav_Squads")]
    [InlineData("rog://u/nyx", "Nav_Members")]
    [InlineData("rog://events/01EV1", "Nav_Calendar")]
    [InlineData("rog://info/rules", "Nav_Info")]
    [InlineData("rog://servers/et-sniper", "Nav_Servers")]
    [InlineData("rog://open?url=https://rebelsofgaming.com/de/u/nyx", "Nav_Members")]
    [InlineData("rog://messages/01CONV2", "Nav_Messages")]
    public async Task Deep_links_open_the_entry(string link, string section)
    {
        using var t = await SignedInAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri(link)));
        Assert.Equal(section, shell.CurrentItem?.Key);
        switch (shell.CurrentPage)
        {
            case SquadsViewModel s:
                await ViewModelTests.WaitUntil(() => s.Detail.ShowContent);
                break;
            case MembersViewModel m:
                await ViewModelTests.WaitUntil(() => m.Detail.ShowContent);
                Assert.True(m.IsDetailOpen);
                break;
            case EventsViewModel e:
                await ViewModelTests.WaitUntil(() => e.Detail.ShowContent);
                Assert.Equal("01EV1", e.Detail.Id);
                break;
            case InfoViewModel i:
                await ViewModelTests.WaitUntil(() => i.Detail.ShowContent);
                break;
            case ServersViewModel v:
                await ViewModelTests.WaitUntil(() => v.ShowContent);
                Assert.True(v.Servers[0].IsHighlighted);
                v.Pause();
                break;
            case MessagesViewModel msg:
                await ViewModelTests.WaitUntil(() => msg.Chat?.ShowContent == true);
                Assert.Equal("01CONV2", msg.Selected?.Id);
                break;
            default:
                Assert.Fail("unerwartete Seite " + shell.CurrentPage);
                break;
        }
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_detail_of_every_list_page()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();
        squads.Selected = squads.Items[0];
        IDetailPage page = squads;
        Assert.True(page.IsDetailOpen);
        page.CloseDetail();
        Assert.False(page.IsDetailOpen);
    }

    [Fact]
    public async Task Xdg_launcher_checks_the_scheme_handler()
    {
        var processes = new RecordingProcessLauncher { Output = "steam.desktop\n" };
        var launcher = new XdgLauncher(processes);
        Assert.True(await launcher.CanLaunchAsync(new Uri("steam://connect/1.2.3.4:27015")));
        Assert.Equal(["query", "default", "x-scheme-handler/steam"], processes.Calls[0].Args);

        processes.Output = string.Empty; // kein Programm registriert
        Assert.False(await launcher.CanLaunchAsync(new Uri("et://1.2.3.4:27960")));

        processes.Output = null; // xdg-mime fehlt (Flatpak) → Portal entscheidet
        Assert.True(await launcher.CanLaunchAsync(new Uri("wolf://1.2.3.4:27960")));

        Assert.False(await launcher.CanLaunchAsync(new Uri("file:///etc/passwd")));
    }
}
