using Avalonia.Headless.XUnit;
using RoG.Core.Navigation;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Abstimmungen über Bewerbungen nativ (Karten, eigene Stimme, Ja/Nein/Enthaltung, Gast, leer).</summary>
public class VotesTests
{
    [AvaloniaFact]
    public async Task Votes_show_applicant_answers_tasks_and_my_vote()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/votes"] = FakeClanProcesses.Votes;
        var votes = new VotesViewModel(t.Services, () => { });
        await votes.LoadAsync();

        Assert.False(votes.IsGuest);
        Assert.Equal(2, votes.Items.Count);
        var first = votes.Items[0];
        Assert.Equal("Frostbyte → Enemy Territory", first.Title);
        Assert.Contains(t.Services.Format("Votes_Age", 24), first.Meta, StringComparison.Ordinal);
        Assert.Equal(t.Services.Format("Votes_Participation", 67, 50), first.Participation);
        Assert.True(first.QuorumMet);
        Assert.Equal(3, first.Answers.Count);
        Assert.Equal(t.Services.T("Apply_Yes"), first.Answers[2].Answer); // Ja/Nein-Antwort übersetzt
        Assert.Equal(3, first.Tasks.Count);
        Assert.False(first.Tasks[2].Done);
        Assert.True(first.IsYes);
        Assert.Equal(t.Services.T("Votes_MineYes"), first.MyVote);

        var second = votes.Items[1];
        Assert.False(second.HasMyVote);
        Assert.False(second.HasTasks);
        Assert.False(second.QuorumMet);
        Assert.Equal("120", second.Answers[1].Answer);
    }

    [AvaloniaFact]
    public async Task Casting_a_vote_sends_it_and_gives_feedback()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/votes"] = FakeClanProcesses.Votes;
        var votes = new VotesViewModel(t.Services, () => { });
        await votes.LoadAsync();

        await votes.Items[1].AbstainCommand.ExecuteAsync(null);
        var put = Assert.Single(t.Api.Writes, w => w.Path == "/v1/applications/01APP2/vote");
        Assert.Equal("PUT", put.Method);
        Assert.Contains("\"abstain\"", put.Body, StringComparison.Ordinal);
        Assert.True(votes.Feedback.IsSuccess);
        Assert.Equal(t.Services.T("Votes_Cast"), votes.Feedback.Message);

        // Fehler der API erscheinen als Rückmeldung, die Liste bleibt
        t.Api.Failures["/v1/applications/01APP1/vote"] = (System.Net.HttpStatusCode.Forbidden,
            """{"type":"about:blank","title":"Forbidden","status":403,"code":"forbidden"}""");
        await votes.Items[0].NoCommand.ExecuteAsync(null);
        Assert.True(votes.Feedback.IsError);
        Assert.Equal(t.Services.Localizer.GetString("Error_forbidden"), votes.Feedback.Message);
        Assert.Equal(2, votes.Items.Count);
        Assert.False(votes.Items[0].IsBusy);
    }

    [AvaloniaFact]
    public async Task Without_open_votes_the_empty_state_shows()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var votes = new VotesViewModel(t.Services, () => { });
        await votes.LoadAsync();
        Assert.True(votes.IsEmptyForMember);
        Assert.False(votes.ShowContent);
    }

    [AvaloniaFact]
    public async Task Guests_see_the_sign_in_hint_without_asking_the_api()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var signIn = 0;
        var votes = new VotesViewModel(t.Services, () => signIn++);
        await votes.LoadAsync();
        Assert.True(votes.IsGuest);
        Assert.False(votes.IsEmptyForMember);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/votes");
        votes.SignInCommand.Execute(null);
        Assert.Equal(1, signIn);
    }

    [AvaloniaFact]
    public async Task Deep_link_and_home_open_the_votes_page_in_the_app()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/votes"] = FakeClanProcesses.Votes;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        var home = Assert.IsType<HomeViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => home.HasVotes);
        home.OpenVotesCommand.Execute(null);
        var votes = Assert.IsType<VotesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => votes.ShowContent);
        Assert.Equal("Nav_Votes", shell.CurrentItem?.Key);

        shell.NavigateTo("Nav_Home");
        Assert.True(shell.OpenDeepLink(new Uri("rog://votes")));
        Assert.IsType<VotesViewModel>(shell.CurrentPage);
        Assert.True(shell.Navigate(new AppRoute(AppSection.Votes)));
        Assert.Empty(t.Browser.Opened);
    }
}
