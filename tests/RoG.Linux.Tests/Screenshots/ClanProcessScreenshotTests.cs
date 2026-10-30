using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.Screenshots.ScreenshotTests;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 6 (Teil A): Abstimmungen, Bewerbungsformular, Bestätigen auf der Info-Seite und Hinweisbalken.</summary>
[Trait("Category", "Screenshots")]
public class ClanProcessScreenshotTests
{
    private static async Task<TestServices> WithVotesAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/votes"] = FakeClanProcesses.Votes;
        return t;
    }

    private static async Task<TestServices> RookieAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeClanProcesses.MeRookie;
        t.Api.Overrides[FakeClanProcesses.FormPath] = FakeClanProcesses.Form;
        return t;
    }

    private static async Task<TestServices> PendingAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me/rules/pending"] = FakeClanProcesses.PendingRules;
        t.Api.Overrides["/v1/me/legal"] = FakeClanProcesses.PendingLegal;
        t.Api.Overrides["/v1/legal/privacy"] = FakeClanProcesses.LegalPrivacy;
        return t;
    }

    [AvaloniaFact]
    public async Task Votes() => await Shoot("votes_1280x800", 1280, 800, await WithVotesAsync(), OpenVotes);

    [AvaloniaFact]
    public async Task Votes_narrow() => await Shoot("votes_narrow_480x900", 480, 900, await WithVotesAsync(), OpenVotes);

    [AvaloniaFact]
    public async Task Votes_guest() =>
        await Shoot("votes_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Votes");
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Votes_empty() =>
        await Shoot("votes_empty_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            shell.NavigateTo("Nav_Votes");
            await Settle(300);
        });

    [AvaloniaFact]
    public async Task Apply_button() => await Shoot("squad_apply_1280x800", 1280, 800, await RookieAsync(), shell => OpenApplication(shell, false));

    [AvaloniaFact]
    public async Task Application_form() => await Shoot("application_form_1280x800", 1280, 800, await RookieAsync(), shell => OpenApplication(shell, true));

    [AvaloniaFact]
    public async Task Application_form_full() => await Shoot("application_form_full_1280x1400", 1280, 1400, await RookieAsync(), shell => OpenApplication(shell, true, error: true));

    [AvaloniaFact]
    public async Task Application_form_narrow() => await Shoot("application_form_narrow_480x900", 480, 900, await RookieAsync(), shell => OpenApplication(shell, true));

    [AvaloniaFact]
    public async Task Info_accept_rules() => await Shoot("info_accept_rules_1280x1100", 1280, 1100, await PendingAsync(), shell => OpenInfo(shell, "clan"));

    [AvaloniaFact]
    public async Task Info_accept_legal() => await Shoot("info_accept_legal_1280x800", 1280, 800, await PendingAsync(), shell => OpenInfo(shell, "privacy"));

    [AvaloniaFact]
    public async Task Acceptance_banner_home() =>
        await Shoot("acceptance_banner_1280x800", 1280, 800, await PendingAsync(), async shell =>
        {
            await ViewModelTests.WaitUntil(() => shell.Acceptance.HasPending);
            await Settle(400);
        });

    [AvaloniaFact]
    public async Task Acceptance_banner_narrow() =>
        await Shoot("acceptance_banner_narrow_480x900", 480, 900, await PendingAsync(), async shell =>
        {
            await ViewModelTests.WaitUntil(() => shell.Acceptance.HasPending);
            await Settle(400);
        });

    private static async Task OpenVotes(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Votes");
        var votes = Assert.IsType<VotesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => votes.ShowContent);
        await Settle(400);
    }

    private static async Task OpenApplication(ShellViewModel shell, bool form, bool error = false)
    {
        shell.NavigateTo("Nav_Squads");
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.ShowContent);
        squads.Open("enemy-territory");
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent && squads.Detail.CanApply);
        if (form)
        {
            squads.Detail.ApplyCommand.Execute(null);
            var application = squads.Detail.Application!;
            await ViewModelTests.WaitUntil(() => application.ShowContent);
            application.Questions[0].SelectedOption = application.Questions[0].Options[1];
            application.Questions[4].Text = "Spiele ET seit 2005, meist Engineer oder Medic.";
            application.Rules[0].Accepted = true;
            if (error)
            {
                await application.SubmitCommand.ExecuteAsync(null);
            }
        }

        await Settle(400);
    }

    private static async Task OpenInfo(ShellViewModel shell, string key)
    {
        await ViewModelTests.WaitUntil(() => shell.Acceptance.HasPending);
        shell.NavigateTo("Nav_Info");
        var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => info.ShowContent);
        info.Open(key);
        await ViewModelTests.WaitUntil(() => info.Detail.ShowContent && info.Detail.CanAccept);
        await Settle(400);
    }
}
