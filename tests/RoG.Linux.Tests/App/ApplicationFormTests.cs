using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Bewerbung auf der Squad-Seite (Formular aus der API, Pflichtfelder, Absenden, Fehler im Formular).</summary>
public class ApplicationFormTests
{
    private static async Task<TestServices> RookieAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeClanProcesses.MeRookie;
        t.Api.Overrides[FakeClanProcesses.FormPath] = FakeClanProcesses.Form;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static async Task<ApplicationFormViewModel> FormAsync(TestServices t, Action<bool>? closed = null)
    {
        var form = new ApplicationFormViewModel(t.Services, "enemy-territory", "Enemy Territory", closed ?? (_ => { }));
        await form.LoadAsync();
        Assert.True(form.ShowContent);
        return form;
    }

    private static void FillRequired(ApplicationFormViewModel form)
    {
        form.Birthdate = new DateTime(2004, 5, 17);
        form.Questions[0].SelectedOption = form.Questions[0].Options[1];
        form.Questions[2].SelectedOption = form.Questions[2].Options[0];
        form.Questions[4].Text = "  Spiele ET seit 2005, meist Medic.  ";
        foreach (var rule in form.Rules)
        {
            rule.Accepted = true;
        }

        form.TrialConsent = true;
    }

    [AvaloniaFact]
    public async Task Form_builds_questions_by_type_with_required_marks()
    {
        using var t = await RookieAsync();
        var form = await FormAsync(t);

        Assert.Equal(t.Services.Format("Apply_TitleFor", "Enemy Territory"), form.Title);
        Assert.Equal(t.Services.Format("Apply_Birthdate", 16), form.BirthdateLabel);
        Assert.False(form.IsWaitlist);
        Assert.Equal(5, form.Questions.Count);
        Assert.Equal([QuestionKind.Select, QuestionKind.Number, QuestionKind.YesNo, QuestionKind.Text, QuestionKind.LongText], form.Questions.Select(q => q.Kind));
        Assert.Equal("Wie oft kannst du spielen? *", form.Questions[0].Header);
        Assert.Equal("Wie viele Stunden hast du in ET?", form.Questions[1].Header);
        Assert.Equal(3, form.Questions[0].Options.Count);
        Assert.Equal([t.Services.T("Apply_Yes"), t.Services.T("Apply_No")], form.Questions[2].Options.Select(o => o.Label));
        Assert.True(form.Questions[4].HasHelp);
        Assert.Equal(2, form.Rules.Count);
        Assert.Equal(t.Services.Format("Apply_AcceptRules", "Clanregeln – Rebels of Gaming"), form.Rules[0].Text);
    }

    [AvaloniaFact]
    public async Task Missing_required_fields_are_listed_and_nothing_is_sent()
    {
        using var t = await RookieAsync();
        var form = await FormAsync(t);
        form.Birthdate = new DateTime(2004, 5, 17);

        await form.SubmitCommand.ExecuteAsync(null);
        Assert.True(form.Feedback.IsError);
        Assert.StartsWith(t.Services.T("Apply_Incomplete"), form.Feedback.Message, StringComparison.Ordinal);
        Assert.Contains("Wie oft kannst du spielen?", form.Feedback.Message, StringComparison.Ordinal);
        Assert.Contains("Erzähl uns kurz etwas über dich", form.Feedback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Lieblingsklasse", form.Feedback.Message, StringComparison.Ordinal);

        // Fragen ausgefüllt, aber Regeln/Trial fehlen → weiterhin nichts gesendet
        FillRequired(form);
        form.Rules[1].Accepted = false;
        await form.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Apply_Incomplete"), form.Feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/applications", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Submit_sends_birthdate_answers_rules_trial_and_referral()
    {
        using var t = await RookieAsync();
        bool? closed = null;
        var form = await FormAsync(t, submitted => closed = submitted);
        FillRequired(form);
        form.Questions[1].Number = 350;
        form.Referral = "Nyx";

        await form.SubmitCommand.ExecuteAsync(null);
        Assert.True(closed);
        var post = Assert.Single(t.Api.Writes, w => w.Path == "/v1/squads/enemy-territory/applications");
        using var body = JsonDocument.Parse(post.Body);
        var root = body.RootElement;
        Assert.Equal("2004-05-17", root.GetProperty("birthdate").GetString());
        var answers = root.GetProperty("answers");
        Assert.Equal("3-4", answers.GetProperty("01Q1").GetString());
        Assert.Equal("350", answers.GetProperty("01Q2").GetString());
        Assert.Equal("yes", answers.GetProperty("01Q3").GetString());
        Assert.False(answers.TryGetProperty("01Q4", out _)); // leere optionale Antwort fehlt
        Assert.Equal("Spiele ET seit 2005, meist Medic.", answers.GetProperty("01Q5").GetString());
        Assert.True(root.GetProperty("accept_rules").GetProperty("01R1").GetBoolean());
        Assert.True(root.GetProperty("accept_rules").GetProperty("01R2").GetBoolean());
        Assert.True(root.GetProperty("trial_consent").GetBoolean());
        Assert.Equal("Nyx", root.GetProperty("referral").GetString());
    }

    [AvaloniaTheory]
    [InlineData("application_blocked", 409, "Du hast bereits eine offene Bewerbung bei Counter-Strike 2.")]
    [InlineData("validation_failed", 422, "Du musst mindestens 16 Jahre alt sein.")]
    public async Task Api_errors_with_detail_stay_in_the_form(string code, int status, string detail)
    {
        using var t = await RookieAsync();
        bool? closed = null;
        var form = await FormAsync(t, submitted => closed = submitted);
        FillRequired(form);
        t.Api.Failures["/v1/squads/enemy-territory/applications"] = ((HttpStatusCode)status,
            $$"""{"type":"about:blank","title":"Fehler","status":{{status}},"code":"{{code}}","detail":"{{detail}}"}""");

        await form.SubmitCommand.ExecuteAsync(null);
        Assert.Null(closed);
        Assert.True(form.Feedback.IsError);
        Assert.Equal(detail, form.Feedback.Message);
        Assert.False(form.IsSubmitting);
    }

    [AvaloniaFact]
    public async Task Other_api_errors_use_the_translated_text()
    {
        using var t = await RookieAsync();
        var form = await FormAsync(t);
        FillRequired(form);
        t.Api.Failures["/v1/squads/enemy-territory/applications"] = (HttpStatusCode.Conflict,
            """{"type":"about:blank","title":"Conflict","status":409,"code":"discord_required","detail":"Discord fehlt"}""");
        await form.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.Localizer.GetString("Error_discord_required"), form.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Waitlist_form_shows_the_hint_and_min_age()
    {
        using var t = await RookieAsync();
        t.Api.Overrides[FakeClanProcesses.FormPath] = FakeClanProcesses.FormWaitlist;
        var form = await FormAsync(t);
        Assert.True(form.IsWaitlist);
        Assert.Equal(t.Services.Format("Apply_Birthdate", 18), form.BirthdateLabel);
        Assert.False(form.HasRules);
    }

    [AvaloniaFact]
    public async Task Squad_page_offers_apply_only_to_signed_in_non_members_and_closes_with_escape()
    {
        using var t = await RookieAsync();
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();
        squads.Open("enemy-territory");
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        Assert.True(squads.Detail.CanApply);

        squads.Detail.ApplyCommand.Execute(null);
        Assert.True(squads.Detail.IsApplying);
        await ViewModelTests.WaitUntil(() => squads.Detail.Application!.ShowContent);

        // Escape (CloseDetail) schließt zuerst das Formular, die Squad-Seite bleibt offen
        Assert.True(squads.IsDetailOpen);
        squads.CloseDetail();
        Assert.False(squads.Detail.IsApplying);
        Assert.True(squads.IsDetailOpen);

        // Absenden → Rückmeldung, Knopf weg
        squads.Detail.ApplyCommand.Execute(null);
        var form = squads.Detail.Application!;
        await ViewModelTests.WaitUntil(() => form.ShowContent);
        FillRequired(form);
        await form.SubmitCommand.ExecuteAsync(null);
        Assert.False(squads.Detail.IsApplying);
        Assert.False(squads.Detail.CanApply);
        Assert.True(squads.Detail.Feedback.IsSuccess);
        Assert.Equal(t.Services.T("Linux_ApplySent"), squads.Detail.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Members_and_guests_get_no_apply_button()
    {
        using (var t = await TestServices.CreateAsync(TestTokens.Wahke))
        {
            t.Services.CurrentUser = await t.Services.Account.GetMeAsync(); // Wahke ist im Line-up
            var squads = new SquadsViewModel(t.Services);
            await squads.LoadAsync();
            squads.Open("enemy-territory");
            await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
            Assert.False(squads.Detail.CanApply);
        }

        using (var t = await TestServices.CreateAsync(guest: true))
        {
            var squads = new SquadsViewModel(t.Services);
            await squads.LoadAsync();
            squads.Open("enemy-territory");
            await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
            Assert.False(squads.Detail.CanApply);
        }
    }

    [AvaloniaFact]
    public async Task Read_rules_opens_the_rule_set_on_the_info_page()
    {
        using var t = await RookieAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        shell.NavigateTo("Nav_Squads");
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.ShowContent);
        squads.Open("enemy-territory");
        await ViewModelTests.WaitUntil(() => squads.Detail.CanApply);
        squads.Detail.ApplyCommand.Execute(null);
        await ViewModelTests.WaitUntil(() => squads.Detail.Application!.ShowContent);

        squads.Detail.Application!.Rules[0].ReadCommand.Execute(null);
        var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => info.Detail.ShowContent);
        Assert.Equal("clan", info.Selected?.Entry?.Key);
        Assert.Empty(t.Browser.Opened);

        // zurück: das Formular ist noch offen
        Assert.True(shell.GoBack());
        Assert.Same(squads, shell.CurrentPage);
        Assert.True(squads.Detail.IsApplying);
    }
}
