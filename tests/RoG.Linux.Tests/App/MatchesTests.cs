using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Matches (Liste-Detail, Deep Links), Hall of Fame, Turniere, Statistiken und Minecraft-Bestenlisten.</summary>
public class MatchesTests
{
    [AvaloniaFact]
    public async Task Matches_list_detail_and_load_more()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var matches = new MatchesViewModel(t.Services, () => { });
        await matches.LoadAsync();
        Assert.Equal(4, matches.Items.Count);
        var first = matches.Items[0];
        Assert.Equal("RoG ET vs. Wolfpack eSports", first.Title);
        Assert.Equal("2 : 0", first.Score);
        Assert.Equal(t.Services.T("Matches_Win"), first.ResultText);
        Assert.Contains("ET", first.Subtitle, StringComparison.Ordinal);
        Assert.Equal("–", matches.Items[3].Score);
        Assert.Equal(t.Services.T("Matches_Open"), matches.Items[3].ResultText);
        Assert.True(matches.HasMore);

        // Detail: sofort aus der Liste, dann Line-up, Links und Bericht nachgeladen
        matches.Selected = first;
        Assert.True(matches.IsDetailOpen);
        Assert.Equal(first.Title, matches.Detail.Title);
        Assert.Equal(2, matches.Detail.Maps.Count);
        await ViewModelTests.WaitUntil(() => matches.Detail.HasReport);
        Assert.Equal(4, matches.Detail.Lineup.Count);
        Assert.Equal("Kaito", matches.Detail.Lineup[^1].Name); // Ersatz zuletzt
        Assert.Contains(t.Services.T("Linux_MatchSubstitute"), matches.Detail.Lineup[^1].Detail, StringComparison.Ordinal);
        Assert.Equal("Medic", matches.Detail.Lineup[0].Detail);
        Assert.Equal(2, matches.Detail.Links.Count);
        Assert.Equal("Twitch-VOD", matches.Detail.Links[0].Label);
        Assert.Equal("demo", matches.Detail.Links[1].Label); // ohne Bezeichnung: Art des Links

        await matches.Detail.OpenInBrowserCommand.ExecuteAsync(null);
        Assert.EndsWith("/de/matches/" + FakeMatchesRoutes.FirstSlug, t.Browser.Opened[^1].ToString(), StringComparison.Ordinal);

        ((IDetailPage)matches).CloseDetail();
        Assert.False(matches.IsDetailOpen);

        await matches.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(5, matches.Items.Count);
        Assert.False(matches.HasMore);
    }

    [AvaloniaFact]
    public async Task Deep_links_open_matches_in_the_app()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        // in der Liste (per Slug)
        Assert.True(shell.OpenDeepLink(new Uri("rog://matches/" + FakeMatchesRoutes.FirstSlug)));
        var matches = Assert.IsType<MatchesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => matches.Selected is not null);
        Assert.Equal(FakeMatchesRoutes.FirstSlug, matches.Selected!.Slug);

        // nicht in der Liste → direkt laden
        matches.IsHallTab = true;
        Assert.True(shell.OpenDeepLink(new Uri("rog://matches/" + FakeMatchesRoutes.OldSlug)));
        Assert.True(matches.IsMatchesTab);
        Assert.True(matches.IsLinkedDetailOpen);
        await ViewModelTests.WaitUntil(() => matches.Detail.ShowContent);
        Assert.Equal("RoG CS2 vs. Night Owls", matches.Detail.Title);
        Assert.Equal("16 : 9", matches.Detail.Score);
        Assert.False(matches.Detail.HasReport);

        // Website-Adresse (Suche, Benachrichtigung) → nativ, kein Browser
        await t.Services.OpenLinkAsync(new Uri("https://rebelsofgaming.com/de/matches/" + FakeMatchesRoutes.FirstSlug));
        Assert.Same(matches.Items[0], matches.Selected);
        Assert.Empty(t.Browser.Opened);

        // unbekanntes Match → Fehlerzustand im Detail
        Assert.True(shell.OpenDeepLink(new Uri("rog://matches/gibt-es-nicht")));
        await ViewModelTests.WaitUntil(() => matches.Detail.HasError);
    }

    [AvaloniaFact]
    public async Task Hall_of_fame_and_tournaments()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var matches = new MatchesViewModel(t.Services, () => { });
        await matches.LoadAsync();
        Assert.True(matches.ShowContent); // Matches auch im Gastmodus

        matches.IsHallTab = true;
        await ViewModelTests.WaitUntil(() => matches.Hall.ShowContent);
        Assert.Equal(3, matches.Hall.Trophies.Count);
        Assert.Contains("#1", matches.Hall.Trophies[0].Meta, StringComparison.Ordinal);
        Assert.Contains("RoG ET", matches.Hall.Trophies[0].Meta, StringComparison.Ordinal);
        Assert.NotSame(matches.Hall.Trophies[0].MedalBrush, matches.Hall.Trophies[2].MedalBrush);
        Assert.Equal(2, matches.Hall.Milestones.Count);
        Assert.False(matches.Hall.Milestones[1].HasDescription);
        Assert.Equal("Snoop", Assert.Single(matches.Hall.Honorary).Name);

        matches.IsTournamentsTab = true;
        await ViewModelTests.WaitUntil(() => matches.Tournaments.ShowContent);
        Assert.Equal(2, matches.Tournaments.Items.Count);
        var winter = matches.Tournaments.Items[0]; // neueste zuerst
        Assert.Equal("RoG Winter-Scrims · CS2", winter.Title);
        Assert.False(winter.HasPlacement);
        Assert.False(winter.HasUrl);
        var cup = matches.Tournaments.Items[1];
        Assert.Equal("2.", cup.Placement);
        Assert.Contains(t.Services.Format("Tournament_Participants", 16), cup.Meta, StringComparison.Ordinal);
        await cup.OpenCommand.ExecuteAsync(null);
        Assert.Equal("https://etnations.example.org/cup-2026", t.Browser.Opened[^1].ToString());

        // Stats: Mitgliederbereich → Hinweis + Anmelden statt Fehler
        matches.IsStatsTab = true;
        await ViewModelTests.WaitUntil(() => matches.Stats.NeedsLogin && !matches.Stats.IsLoading);
        Assert.False(matches.Stats.HasError);
        Assert.False(matches.Stats.ShowFilters);
    }

    [AvaloniaFact]
    public async Task Stats_leaderboard_my_rank_and_awards()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var signIn = 0;
        var matches = new MatchesViewModel(t.Services, () => signIn++);
        matches.IsStatsTab = true;
        var stats = matches.Stats;
        await ViewModelTests.WaitUntil(() => stats.ShowContent);
        Assert.False(stats.NeedsLogin);
        Assert.True(stats.ShowFilters);
        Assert.Equal(2, stats.Games.Count);
        Assert.Equal("et", stats.SelectedGame?.Value);
        Assert.Equal("30d", stats.SelectedPeriod?.Value);
        Assert.Equal(4, stats.Rows.Count);
        Assert.Equal("1.", stats.Rows[0].Rank);
        Assert.Equal("402", stats.Rows[0].Value);
        Assert.True(stats.Rows[2].IsMe);
        Assert.NotNull(stats.Rows[2].RowBackground);
        Assert.Equal(t.Services.Format("Stats_MyRank", 3), stats.MyRankText);
        Assert.Equal(3, stats.Awards.Count);
        Assert.Equal(t.Services.T("Stats_Award_most_kills"), stats.Awards[0].Label);
        Assert.Equal("Wahke, Kaito", stats.Awards[2].Members);
        Assert.Equal(4.0.ToString("0.0", t.Services.Culture) + " h", stats.Awards[2].Value);
        Assert.Equal(t.Services.Format("Stats_AwardsTitle", "2026-W43"), stats.AwardsTitle);

        // Kennzahl K/D
        stats.SelectedMetric = stats.Metrics.First(m => m.Value == "kd");
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Any(r => r.RequestUri!.Query.Contains("metric=kd", StringComparison.Ordinal)) && !stats.IsLoading && stats.Rows.Count == 4);
        Assert.Equal(2.12.ToString("0.00", t.Services.Culture), stats.Rows[0].Value);

        // Spiel ohne Werte → Leerzustand
        stats.SelectedGame = stats.Games[1];
        await ViewModelTests.WaitUntil(() => stats.IsEmpty);
        Assert.Equal(0, signIn);
    }

    [AvaloniaFact]
    public async Task Minecraft_leaderboards_boards_sorts_courses_and_profile()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://leaderboards")));
        Assert.Equal("Nav_Leaderboards", shell.CurrentItem?.Key);
        var boards = Assert.IsType<LeaderboardsViewModel>(shell.CurrentPage);
        Assert.Empty(t.Browser.Opened);
        await ViewModelTests.WaitUntil(() => boards.ShowContent);
        Assert.Equal(4, boards.Items.Count);
        Assert.Equal("#1", boards.Items[0].RankText);
        Assert.Equal(t.Services.Format("Stats_PlaytimeHours", "112", 5), boards.Items[0].ValueText);
        Assert.Equal(t.Services.Format("Stats_PlaytimeMinutes", 30), boards.Items[3].ValueText);
        Assert.False(boards.HasSorts);
        Assert.True(boards.HasHint);

        // Minispiel: Wertungen Siege/Siegquote, Hinweis „ab 10 Spielen“
        boards.SelectedBoard = boards.BoardOptions.First(b => b.Value == "bedwars");
        await ViewModelTests.WaitUntil(() => boards.ShowContent && boards.Items.Count == 2 && boards.Items[0].HasMeta);
        Assert.True(boards.HasSorts);
        Assert.Equal("25", boards.Items[0].ValueText);
        boards.SelectedSort = boards.SortOptions.First(s => s.Value == "win_rate");
        await ViewModelTests.WaitUntil(() => boards.Items.Count == 2 && boards.Items[0].ValueText.EndsWith('%'));
        Assert.Contains(t.Services.Format("Leaderboard_MinMatches", 10), boards.Hint, StringComparison.Ordinal);

        // Parkour: Kurse aus der Antwort
        boards.SelectedBoard = boards.BoardOptions.First(b => b.Value == "parkour");
        await ViewModelTests.WaitUntil(() => boards.HasCourses && !boards.IsLoading);
        Assert.Equal(2, boards.CourseOptions.Count);
        Assert.False(boards.HasSorts);
        boards.SelectedCourse = boards.CourseOptions[1];
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Any(r => r.RequestUri!.Query.Contains("course=nether", StringComparison.Ordinal)));

        // Eintrag → Profil in der App
        await ViewModelTests.WaitUntil(() => !boards.IsLoading && boards.Items.Count > 0);
        boards.Items[0].OpenCommand.Execute(null);
        Assert.Equal("Nav_Members", shell.CurrentItem?.Key);
    }

    [AvaloniaFact]
    public async Task Server_tab_minecraft_opens_the_native_leaderboards()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.NavigateTo("Nav_Servers"));
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        servers.IsMinecraftTab = true;
        servers.Join.OpenLeaderboardsCommand.Execute(null);
        Assert.IsType<LeaderboardsViewModel>(shell.CurrentPage);
        Assert.Empty(t.Browser.Opened);
        servers.Pause();
    }
}
