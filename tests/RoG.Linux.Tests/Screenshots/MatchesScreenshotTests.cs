using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Matches, Hall of Fame, Statistiken, Turniere und Minecraft-Bestenlisten → <c>artifacts/screenshots/</c>.</summary>
[Trait("Category", "Screenshots")]
public class MatchesScreenshotTests
{
    [AvaloniaFact]
    public async Task Matches_detail() =>
        await ScreenshotTests.Shoot("matches_detail_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => Open(shell, MatchesTab.Matches, detail: true));

    [AvaloniaFact]
    public async Task Matches_list_narrow() =>
        await ScreenshotTests.Shoot("matches_list_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => Open(shell, MatchesTab.Matches, detail: false));

    [AvaloniaFact]
    public async Task Matches_detail_narrow() =>
        await ScreenshotTests.Shoot("matches_detail_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => Open(shell, MatchesTab.Matches, detail: true));

    [AvaloniaFact]
    public async Task Matches_guest() =>
        await ScreenshotTests.Shoot("matches_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => Open(shell, MatchesTab.Matches, detail: false));

    [AvaloniaFact]
    public async Task Hall_of_fame() =>
        await ScreenshotTests.Shoot("matches_hall_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => Open(shell, MatchesTab.Hall, detail: false));

    [AvaloniaFact]
    public async Task Hall_of_fame_narrow() =>
        await ScreenshotTests.Shoot("matches_hall_narrow_480x900", 480, 900, await TestServices.CreateAsync(guest: true), shell => Open(shell, MatchesTab.Hall, detail: false));

    [AvaloniaFact]
    public async Task Stats() =>
        await ScreenshotTests.Shoot("matches_stats_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), shell => Open(shell, MatchesTab.Stats, detail: false));

    [AvaloniaFact]
    public async Task Stats_narrow() =>
        await ScreenshotTests.Shoot("matches_stats_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), shell => Open(shell, MatchesTab.Stats, detail: false));

    [AvaloniaFact]
    public async Task Stats_guest() =>
        await ScreenshotTests.Shoot("matches_stats_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => Open(shell, MatchesTab.Stats, detail: false));

    [AvaloniaFact]
    public async Task Tournaments() =>
        await ScreenshotTests.Shoot("matches_tournaments_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => Open(shell, MatchesTab.Tournaments, detail: false));

    [AvaloniaFact]
    public async Task Leaderboards() =>
        await ScreenshotTests.Shoot("leaderboards_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => OpenLeaderboards(shell, "playtime"));

    [AvaloniaFact]
    public async Task Leaderboards_minigame_narrow() =>
        await ScreenshotTests.Shoot("leaderboards_bedwars_narrow_480x900", 480, 900, await TestServices.CreateAsync(guest: true), shell => OpenLeaderboards(shell, "bedwars"));

    private static async Task Open(ShellViewModel shell, MatchesTab tab, bool detail)
    {
        shell.NavigateTo("Nav_Matches");
        var matches = Assert.IsType<MatchesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => matches.ShowContent);
        matches.Tab = tab;
        switch (tab)
        {
            case MatchesTab.Matches when detail:
                matches.Selected = matches.Items[0];
                await ViewModelTests.WaitUntil(() => matches.Detail.HasReport);
                break;
            case MatchesTab.Hall:
                await ViewModelTests.WaitUntil(() => matches.Hall.ShowContent || matches.Hall.HasError);
                break;
            case MatchesTab.Stats:
                await ViewModelTests.WaitUntil(() => !matches.Stats.IsLoading && (matches.Stats.ShowContent || matches.Stats.NeedsLogin || matches.Stats.HasError));
                break;
            case MatchesTab.Tournaments:
                await ViewModelTests.WaitUntil(() => matches.Tournaments.ShowContent || matches.Tournaments.HasError);
                break;
        }

        await ScreenshotTests.Settle(400);
    }

    private static async Task OpenLeaderboards(ShellViewModel shell, string board)
    {
        shell.NavigateTo("Nav_Leaderboards");
        var boards = Assert.IsType<LeaderboardsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => boards.ShowContent);
        if (board != "playtime")
        {
            boards.SelectedBoard = boards.BoardOptions.First(b => b.Value == board);
            await ViewModelTests.WaitUntil(() => boards.ShowContent && boards.HasSorts && boards.Items.Count == 2);
        }

        await ScreenshotTests.Settle(400);
    }
}
