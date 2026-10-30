using Avalonia.Headless.XUnit;
using RoG.Core.Features.Info;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Regeln und Rechtstexte bestätigen (Hinweisbalken nach dem Login, Knopf auf der Info-Seite).</summary>
public class AcceptanceTests
{
    private static async Task<(TestServices T, ShellViewModel Shell)> PendingAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me/rules/pending"] = FakeClanProcesses.PendingRules;
        t.Api.Overrides["/v1/me/legal"] = FakeClanProcesses.PendingLegal;
        t.Api.Overrides["/v1/legal/privacy"] = FakeClanProcesses.LegalPrivacy;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        await ViewModelTests.WaitUntil(() => shell.Acceptance.HasPending);
        return (t, shell);
    }

    [AvaloniaFact]
    public async Task Banner_lists_pending_rules_and_legal_texts_and_says_writing_is_blocked()
    {
        var (t, shell) = await PendingAsync();
        using (t)
        {
            var acceptance = shell.Acceptance;
            Assert.Equal(2, acceptance.Items.Count);
            Assert.Equal(new PendingAcceptance(InfoKind.Rules, "clan", "Clanregeln – Rebels of Gaming", true), acceptance.Items[0]);
            Assert.Equal(new PendingAcceptance(InfoKind.Legal, "privacy", "Datenschutzerklärung", false), acceptance.Items[1]);
            Assert.True(acceptance.WriteBlocked);
            Assert.Equal(t.Services.Format("Linux_AcceptBannerBlocked", "Clanregeln – Rebels of Gaming, Datenschutzerklärung"), acceptance.Text);

            acceptance.OpenCommand.Execute(null);
            var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => info.Detail.ShowContent);
            Assert.Equal("clan", info.Selected?.Entry?.Key);
            Assert.True(info.Detail.CanAccept);
        }
    }

    [AvaloniaFact]
    public async Task Accepting_rules_and_legal_texts_on_the_info_page_clears_the_banner()
    {
        var (t, shell) = await PendingAsync();
        using (t)
        {
            shell.NavigateTo("Nav_Info");
            var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => info.ShowContent);

            // Rechtstext: „bitte bestätigen“ nur, wenn für diese Person offen (Impressum nie)
            var privacy = info.Rows.Single(r => r.Entry?.Key == "privacy");
            Assert.True(privacy.NeedsAcceptance);
            Assert.False(info.Rows.Single(r => r.Entry?.Key == "imprint").NeedsAcceptance);

            info.Open("clan");
            await ViewModelTests.WaitUntil(() => info.Detail.ShowContent && info.Detail.CanAccept);
            await info.Detail.AcceptCommand.ExecuteAsync(null);
            Assert.Contains(t.Api.Writes, w => w.Method == "POST" && w.Path == "/v1/rules/clan/accept");
            Assert.False(info.Detail.CanAccept);
            Assert.True(info.Detail.Feedback.IsSuccess);
            Assert.Equal(t.Services.T("Info_Accepted"), info.Detail.Feedback.Message);
            Assert.False(info.Rows.Single(r => r.Entry?.Key == "clan").NeedsAcceptance);
            Assert.Single(shell.Acceptance.Items);
            Assert.False(shell.Acceptance.WriteBlocked);
            Assert.Equal(t.Services.Format("Linux_AcceptBanner", "Datenschutzerklärung"), shell.Acceptance.Text);

            info.Open("privacy");
            await ViewModelTests.WaitUntil(() => info.Detail.ShowContent && info.Detail.Title == "Datenschutzerklärung");
            Assert.True(info.Detail.CanAccept);
            await info.Detail.AcceptCommand.ExecuteAsync(null);
            Assert.Contains(t.Api.Writes, w => w.Method == "POST" && w.Path == "/v1/me/legal/privacy/accept");
            Assert.Equal(t.Services.T("Linux_LegalAccepted"), info.Detail.Feedback.Message);
            Assert.False(privacy.NeedsAcceptance);
            Assert.False(shell.Acceptance.HasPending);
            Assert.Null(shell.Acceptance.Text);
        }
    }

    [AvaloniaFact]
    public async Task Accept_errors_are_shown_and_the_button_stays()
    {
        var (t, shell) = await PendingAsync();
        using (t)
        {
            t.Api.Failures["/v1/rules/clan/accept"] = (System.Net.HttpStatusCode.Forbidden,
                """{"type":"about:blank","title":"Forbidden","status":403,"code":"insufficient_scope"}""");
            shell.Acceptance.OpenCommand.Execute(null);
            var info = Assert.IsType<InfoViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => info.Detail.CanAccept);
            await info.Detail.AcceptCommand.ExecuteAsync(null);
            Assert.True(info.Detail.Feedback.IsError);
            Assert.Equal(t.Services.Localizer.GetString("Error_insufficient_scope"), info.Detail.Feedback.Message);
            Assert.True(info.Detail.CanAccept);
            Assert.Equal(2, shell.Acceptance.Items.Count);
        }
    }

    [AvaloniaFact]
    public async Task Nothing_pending_or_guest_mode_shows_no_banner()
    {
        using (var t = await TestServices.CreateAsync(TestTokens.Wahke))
        {
            var shell = new ShellViewModel(t.Services);
            await shell.InitializeAsync();
            await ViewModelTests.WaitUntil(() => t.Api.Requests.Any(r => r.RequestUri!.AbsolutePath == "/v1/me/legal"));
            await ViewModelTests.WaitUntil(() => shell.Acceptance.IsLoaded);
            Assert.False(shell.Acceptance.HasPending);
        }

        using (var t = await TestServices.CreateAsync(guest: true))
        {
            var shell = new ShellViewModel(t.Services);
            await shell.InitializeAsync();
            await shell.Acceptance.RefreshAsync();
            Assert.False(shell.Acceptance.HasPending);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/me/", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public async Task Missing_scope_for_pending_checks_is_quiet()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Failures["/v1/me/rules/pending"] = (System.Net.HttpStatusCode.Forbidden,
            """{"type":"about:blank","title":"Forbidden","status":403,"code":"insufficient_scope"}""");
        t.Api.Overrides["/v1/me/legal"] = FakeClanProcesses.PendingLegal;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        await ViewModelTests.WaitUntil(() => shell.Acceptance.HasPending);
        Assert.Equal("privacy", Assert.Single(shell.Acceptance.Items).Key);
        Assert.False(shell.Acceptance.WriteBlocked);
    }

    [AvaloniaFact]
    public async Task Write_blocked_error_points_to_the_rules()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        Assert.Contains(t.Services.T("Info_GroupRules"), t.Services.Localizer.GetString("Error_rules_acceptance_required"), StringComparison.Ordinal);
    }
}
