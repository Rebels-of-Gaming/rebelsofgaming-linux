using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using RoG.Core.Localization;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 9 (Teil L): Verwaltungs-Reiter Meldungen, Sperren, Einsprüche, Wortfilter, Mod-Log, „Mitglied moderieren“ und
/// „Melden“ an Inhalten (Forum, Profil, Galerie, Wiki, Direktnachricht, News-Kommentar) – gegen den Fake-Server.
/// </summary>
public class ModerationTests
{
    internal static async Task<TestServices> AsAsync(string me, bool guest = false)
    {
        var t = guest ? await TestServices.CreateAsync(guest: true) : await TestServices.CreateAsync(TestTokens.Wahke);
        if (!guest)
        {
            t.Api.Overrides["/v1/me"] = me;
            t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        }

        return t;
    }

    private static (FeedbackViewModel Feedback, InlineConfirmViewModel Confirm) Page() => (new FeedbackViewModel(), new InlineConfirmViewModel());

    private static JsonElement Body(TestServices t, string method, string path) =>
        JsonDocument.Parse(t.Api.Writes.Last(w => w.Method == method && w.Path == path).Body).RootElement;

    [AvaloniaFact]
    public async Task Tabs_follow_permissions_and_token_scopes()
    {
        using (var t = await AsAsync(FakeModerationRoutes.MeModerator))
        {
            var (f, c) = Page();
            Assert.True(new ReportsAdminViewModel(t.Services, f, c).IsAvailable);
            Assert.True(new ModBansAdminViewModel(t.Services, f, c).IsAvailable);
            Assert.True(new WordFilterViewModel(t.Services, f, c).IsAvailable);
            var appeals = new BanAppealsViewModel(t.Services, f, c);
            Assert.True(appeals.IsAvailable);
            Assert.True(appeals.IsManager);
            Assert.True(ModerationAccess.For(t.Services).Any);
        }

        // Rechte da, Token nur lesen: Meldungen/Sperren sehen, aber nicht verwarnen/sperren/aufheben
        using (var t = await AsAsync(FakeModerationRoutes.MeReader))
        {
            var (f, c) = Page();
            var reports = new ReportsAdminViewModel(t.Services, f, c);
            Assert.True(reports.IsAvailable);
            Assert.False(reports.Access.CanWarn);
            Assert.False(reports.Access.CanBan);
            Assert.False(reports.Member.CanAct);
            Assert.False(new WordFilterViewModel(t.Services, f, c).IsAvailable);
            Assert.False(new BanAppealsViewModel(t.Services, f, c).IsAvailable);
            var bans = new ModBansAdminViewModel(t.Services, f, c);
            await bans.EnsureLoadedAsync();
            Assert.Equal(3, bans.Items.Count);
            Assert.All(bans.Items, b => Assert.False(b.CanLift));

            await reports.EnsureLoadedAsync();
            Assert.All(reports.Items, r => Assert.False(r.CanModerateTarget));
        }

        // Mitglied und Gast: kein Reiter, keine Anfrage
        foreach (var guest in new[] { false, true })
        {
            using var t = await AsAsync(FakeModerationRoutes.MeMember, guest);
            var (f, c) = Page();
            var tabs = new ModerationTabViewModel[]
            {
                new ReportsAdminViewModel(t.Services, f, c), new ModBansAdminViewModel(t.Services, f, c), new WordFilterViewModel(t.Services, f, c),
                new BanAppealsViewModel(t.Services, f, c),
            };
            Assert.All(tabs, tab => Assert.False(tab.IsAvailable));
            var before = t.Api.Requests.Count;
            foreach (var tab in tabs)
            {
                await tab.EnsureLoadedAsync();
            }

            Assert.Equal(before, t.Api.Requests.Count);
            Assert.False(ModerationAccess.For(t.Services).Any);
        }

        // Server-Admin mit servers.bans: Einsprüche ja, Forum-Bereich nein
        using (var t = await AsAsync(FakeModerationRoutes.MeServerAdmin))
        {
            var (f, c) = Page();
            var appeals = new BanAppealsViewModel(t.Services, f, c);
            Assert.True(appeals.IsAvailable);
            Assert.False(appeals.IsManager);
            await appeals.EnsureLoadedAsync();
            Assert.Single(appeals.Items);
            Assert.False(appeals.HasSettings);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/game-bans/settings");
        }
    }

    [AvaloniaFact]
    public async Task Reports_put_child_safety_first_filter_and_resolve()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var reports = new ReportsAdminViewModel(t.Services, feedback, confirm);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/moderation", StringComparison.Ordinal));
        await reports.EnsureLoadedAsync();
        await reports.EnsureLoadedAsync(); // nur einmal
        Assert.Single(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/moderation/reports");

        Assert.Equal(3, reports.Items.Count);
        var child = reports.Items[0];
        Assert.True(child.IsChildSafety);
        Assert.Equal($"{t.Services.T("Mod_Type_message")} · {t.Services.T("Report_Reason_child_safety")}", child.Title);
        Assert.Contains(t.Services.Format(StringKeys.ModReportedBy, "Nyx"), child.Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.Format(StringKeys.ModAgainst, "Kaito"), child.Meta, StringComparison.Ordinal);
        Assert.True(child.HasExcerpt);
        Assert.True(child.HasComment);
        Assert.False(child.HasUrl); // Direktnachricht: kein Link
        Assert.True(child.CanModerateTarget);

        var forum = reports.Items[1];
        Assert.True(forum.HasUrl);
        await forum.OpenContentCommand.ExecuteAsync(null);
        Assert.Contains("scrim-serie-herbst-2026", t.Browser.Opened[^1].ToString(), StringComparison.Ordinal);

        var game = reports.Items[2];
        Assert.True(game.IsGame);
        Assert.False(game.CanModerateTarget); // Gast ohne RoG-Konto
        Assert.Contains("Notch", game.GameText, StringComparison.Ordinal);
        Assert.Contains("RoG Network", game.GameText, StringComparison.Ordinal);
        Assert.Contains("lobby", game.GameText, StringComparison.Ordinal);

        // Filter
        reports.SelectedReason = reports.ReasonOptions[1];
        await WaitUntil(() => !reports.IsLoading && reports.Items.Count == 1);
        Assert.Contains(t.Api.Requests, r => Uri.UnescapeDataString(r.RequestUri!.Query).Contains("filter[reason]=child_safety", StringComparison.Ordinal));
        reports.SelectedReason = reports.ReasonOptions[0];
        reports.SelectedSource = reports.SourceOptions.Single(o => o.Value == "minecraft");
        await WaitUntil(() => !reports.IsLoading && reports.Items.Count == 1 && reports.Items[0].IsGame);

        reports.SelectedSource = reports.SourceOptions[0];
        await WaitUntil(() => !reports.IsLoading && reports.Items.Count == 3);

        // Erledigen mit Notiz
        reports.Items[1].Resolution = "Beitrag ausgeblendet, Verwarnung folgt";
        await reports.Items[1].ResolveCommand.ExecuteAsync(null);
        var patch = Body(t, "PATCH", "/v1/moderation/reports/" + FakeModerationRoutes.ForumReport);
        Assert.Equal("resolved", patch.GetProperty("status").GetString());
        Assert.Equal("Beitrag ausgeblendet, Verwarnung folgt", patch.GetProperty("resolution").GetString());
        Assert.Equal(2, reports.Items.Count);
        Assert.Equal(t.Services.T(StringKeys.ModReportUpdated), feedback.Message);
        Assert.True(feedback.IsSuccess);

        await reports.Items[0].DismissCommand.ExecuteAsync(null);
        Assert.Equal("dismissed", Body(t, "PATCH", "/v1/moderation/reports/" + FakeModerationRoutes.ChildSafetyReport).GetProperty("status").GetString());

        // Status „erledigt“: keine Aktionen
        reports.SelectedStatus = reports.StatusOptions[1];
        await WaitUntil(() => !reports.IsLoading && reports.Items.Count == 1 && !reports.Items[0].IsOpen);
    }

    [AvaloniaFact]
    public async Task Reports_show_errors_and_empty_state()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var reports = new ReportsAdminViewModel(t.Services, feedback, confirm);
        t.Api.Failures["/v1/moderation/reports"] = (HttpStatusCode.Forbidden, """{"type":"about:blank","title":"Verboten","status":403,"code":"forbidden"}""");
        await reports.EnsureLoadedAsync();
        Assert.True(reports.HasError);

        t.Api.Failures.Clear();
        t.Api.Overrides["/v1/moderation/reports"] = """{"data":[],"meta":{"next_cursor":null,"prev_cursor":null}}""";
        await reports.RefreshCommand.ExecuteAsync(null);
        Assert.True(reports.IsEmpty);
        Assert.False(reports.HasError);
    }

    [AvaloniaFact]
    public async Task Moderate_member_from_a_report_warns_bans_revokes_and_lifts()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var reports = new ReportsAdminViewModel(t.Services, feedback, confirm);
        await reports.EnsureLoadedAsync();

        await reports.Items[0].ModerateCommand.ExecuteAsync(null);
        var member = reports.Member;
        Assert.True(member.IsOpen);
        Assert.True(member.ShowContent);
        Assert.Equal("Kaito", member.Name);
        Assert.Equal(t.Services.Format(StringKeys.ModActivePoints, 3), member.PointsText);
        Assert.Equal(3, member.Warnings.Count);
        Assert.Contains("Beleidigung im TeamSpeak", member.Warnings[0].Text, StringComparison.Ordinal); // neueste zuerst
        Assert.True(member.Warnings[0].CanAct);
        Assert.False(member.Warnings[2].CanAct); // abgelaufen
        Assert.Contains(t.Services.T(StringKeys.ModInactive), member.Warnings[2].Text, StringComparison.Ordinal);
        Assert.Single(member.Bans);
        Assert.Contains(t.Services.T("Mod_Scope_forum"), member.Bans[0].Text, StringComparison.Ordinal);

        await member.Warnings[0].ActCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/moderation/warnings/01JWN0000000000000000000N2/revoke");
        Assert.False(member.Warnings[0].CanAct);
        Assert.Equal(t.Services.T(StringKeys.ModRevoked), feedback.Message);

        await member.Bans[0].ActCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/moderation/bans/" + FakeModerationRoutes.ForumBan + "/lift");

        // Ohne Grund: Hinweis, nichts gesendet
        await member.WarnCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.ModReasonMissing), member.Error);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/warnings", StringComparison.Ordinal));

        member.Reason = "Unangemessene DMs an Minderjährige";
        member.Points = 2;
        member.Days = 30;
        await member.WarnCommand.ExecuteAsync(null);
        var warn = Body(t, "POST", "/v1/moderation/members/" + FakeModerationRoutes.Kaito + "/warnings");
        Assert.Equal("Unangemessene DMs an Minderjährige", warn.GetProperty("reason").GetString());
        Assert.Equal(2, warn.GetProperty("points").GetInt32());
        Assert.Equal(30, warn.GetProperty("days").GetInt32());
        Assert.Equal(FakeModerationRoutes.ChildSafetyReport, warn.GetProperty("report_id").GetString());
        Assert.False(member.IsOpen);
        Assert.Equal(t.Services.T(StringKeys.ModWarned), feedback.Message);

        // Sperre (Website), öffentlich, unbefristet
        await member.OpenAsync(FakeModerationRoutes.Kaito, null);
        member.Reason = "Wiederholte Belästigung";
        member.SelectedScope = member.ScopeOptions.Single(s => s.Value == "site");
        member.IsPublic = true;
        await member.BanCommand.ExecuteAsync(null);
        var ban = Body(t, "POST", "/v1/moderation/members/" + FakeModerationRoutes.Kaito + "/bans");
        Assert.Equal("site", ban.GetProperty("scope").GetString());
        Assert.True(ban.GetProperty("public").GetBoolean());
        Assert.Equal(t.Services.T(StringKeys.ModBanned), feedback.Message);

        // Server lehnt ab (Owner): Fehler im Bereich, bleibt offen
        await member.OpenAsync(FakeModerationRoutes.Kaito, null);
        t.Api.Failures["/v1/moderation/members/" + FakeModerationRoutes.Kaito + "/bans"] =
            (HttpStatusCode.Conflict, """{"type":"about:blank","title":"Owner","status":409,"code":"moderation_owner"}""");
        member.Reason = "Test";
        await member.BanCommand.ExecuteAsync(null);
        Assert.True(member.IsOpen);
        Assert.True(member.HasError);

        // Escape schließt den Bereich
        Assert.True(reports.CloseOverlay());
        Assert.False(member.IsOpen);
        Assert.False(reports.CloseOverlay());
    }

    [AvaloniaFact]
    public async Task Bans_lift_only_after_confirmation()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var bans = new ModBansAdminViewModel(t.Services, feedback, confirm);
        await bans.EnsureLoadedAsync();
        Assert.Equal(3, bans.Items.Count);
        Assert.Equal($"Gast_Rambo · {t.Services.T("Mod_Scope_site")}", bans.Items[0].Title);
        Assert.Contains(t.Services.T(StringKeys.BanPermanent), bans.Items[0].Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.Format(StringKeys.BanUntil, string.Empty).Trim(), bans.Items[1].Meta, StringComparison.Ordinal);
        Assert.True(bans.Items[0].CanLift);

        var lift = bans.Items[0].LiftCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Equal(t.Services.T(StringKeys.ModLiftTitle), confirm.Title);
        Assert.True(bans.CloseOverlay()); // Escape = Nein
        await lift;
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/lift", StringComparison.Ordinal));

        lift = bans.Items[0].LiftCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        confirm.AcceptCommand.Execute(null);
        await lift;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/moderation/bans/" + FakeModerationRoutes.SiteBan + "/lift");
        Assert.Equal(2, bans.Items.Count);
        Assert.Equal(t.Services.T(StringKeys.ModLifted), feedback.Message);

        // Mitglied öffnen
        await bans.Items[0].OpenCommand.ExecuteAsync(null);
        Assert.True(bans.Member.IsOpen);
        Assert.Equal("Kaito", bans.Member.Name);
    }

    [AvaloniaFact]
    public async Task Word_filter_adds_and_deletes_with_confirmation_and_log_lists_entries()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var words = new WordFilterViewModel(t.Services, feedback, confirm);
        await words.EnsureLoadedAsync();
        Assert.Equal(["cheat-shop.example", "discord.gg/*", "noob*"], words.Items.Select(w => w.Pattern)); // alphabetisch
        Assert.Equal($"{t.Services.T("Mod_Word_replace")} → Neuling", words.Items[2].Meta);
        Assert.True(words.IsReplace);
        Assert.False(words.AddCommand.CanExecute(null));

        words.NewPattern = "  boost-service  ";
        words.NewAction = words.Actions.Single(a => a.Value == "block");
        Assert.False(words.IsReplace);
        words.NewReplacement = "egal";
        await words.AddCommand.ExecuteAsync(null);
        var added = Body(t, "POST", "/v1/moderation/words");
        Assert.Equal("boost-service", added.GetProperty("pattern").GetString());
        Assert.Equal("block", added.GetProperty("action").GetString());
        Assert.True(!added.TryGetProperty("replacement", out var replacement) || replacement.ValueKind == JsonValueKind.Null); // Ersatz nur bei „ersetzen“
        Assert.Equal(string.Empty, words.NewPattern);

        var delete = words.Items[0].DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Equal("cheat-shop.example", confirm.Text);
        confirm.AcceptCommand.Execute(null);
        await delete;
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/moderation/words/01JWF0000000000000000000W2");
        Assert.Equal(2, words.Items.Count);
    }

    [AvaloniaFact]
    public async Task Appeals_decide_with_public_note_and_manage_board()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeModerator);
        var (feedback, confirm) = Page();
        var appeals = new BanAppealsViewModel(t.Services, feedback, confirm);
        await appeals.EnsureLoadedAsync();
        Assert.Single(appeals.Items);
        var item = appeals.Items[0];
        Assert.Equal("^1Sn1per^7King", item.Title);
        Assert.True(item.CanDecide);
        Assert.Contains(t.Services.Format(StringKeys.BanAppealsBy, "Kaito"), item.Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("BanAppeals_Proof_steam"), item.Meta, StringComparison.Ordinal);

        Assert.True(appeals.HasSettings);
        Assert.Equal("01FB9", appeals.SelectedBoard?.Value);
        Assert.Equal(t.Services.Format(StringKeys.BanAppealsBoardActive, "Ban-Einsprüche"), appeals.BoardText);
        Assert.Equal("rog-paper 0.6.0", appeals.PluginVersions);
        Assert.Contains("rog-proxy 0.5.0", appeals.PluginVersionsInfo, StringComparison.Ordinal);

        // Ablehnen abbrechen (Escape) → nichts gesendet
        var reject = item.RejectCommand.ExecuteAsync(null);
        await WaitUntil(() => appeals.Decision.IsOpen);
        Assert.True(appeals.Decision.IsReject);
        Assert.Equal(t.Services.Format(StringKeys.BanAppealsRejectTitle, item.Title), appeals.Decision.Title);
        Assert.True(appeals.CloseOverlay());
        await reject;
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/decision", StringComparison.Ordinal));

        // Ablehnen mit Begründung und sofortigem Neu-Einspruch
        reject = item.RejectCommand.ExecuteAsync(null);
        await WaitUntil(() => appeals.Decision.IsOpen);
        appeals.Decision.Note = "Demo zeigt eindeutig Wallhack.";
        appeals.Decision.Reappeal = true;
        appeals.Decision.AcceptCommand.Execute(null);
        await reject;
        var body = Body(t, "POST", "/v1/game-ban-appeals/" + FakeModerationRoutes.OpenAppeal + "/decision");
        Assert.Equal("rejected", body.GetProperty("decision").GetString());
        Assert.Equal("Demo zeigt eindeutig Wallhack.", body.GetProperty("note").GetString());
        Assert.True(body.GetProperty("reappeal_allowed").GetBoolean());
        Assert.Empty(appeals.Items); // Filter „offen“
        Assert.True(appeals.IsEmpty);
        Assert.Equal(t.Services.T(StringKeys.BanAppealsRejected), feedback.Message);

        // Alle: Annehmen ersetzt den Eintrag, ohne Neu-Einspruch
        appeals.SelectedStatus = appeals.StatusOptions.Single(o => o.Value == "all");
        await WaitUntil(() => !appeals.IsLoading && appeals.Items.Count == 2);
        Assert.False(appeals.Items[1].CanDecide);
        Assert.True(appeals.Items[1].HasNote);
        var accept = appeals.Items[0].AcceptCommand.ExecuteAsync(null);
        await WaitUntil(() => appeals.Decision.IsOpen);
        Assert.False(appeals.Decision.IsReject);
        appeals.Decision.Reappeal = true; // beim Annehmen ohne Wirkung
        appeals.Decision.AcceptCommand.Execute(null);
        await accept;
        body = Body(t, "POST", "/v1/game-ban-appeals/" + FakeModerationRoutes.OpenAppeal + "/decision");
        Assert.Equal("accepted", body.GetProperty("decision").GetString());
        Assert.False(body.TryGetProperty("reappeal_allowed", out _));
        Assert.Equal(2, appeals.Items.Count);
        Assert.False(appeals.Items[0].CanDecide);

        // Thema öffnen (Forum noch im Browser bzw. in der App, falls nativ)
        appeals.Items[0].OpenThreadCommand.Execute(null);
        await WaitUntil(() => t.Browser.Opened.Count > 0);

        // Forum-Bereich ausschalten und Plugin-Versionen
        appeals.SelectedBoard = appeals.BoardOptions[0];
        await appeals.SaveBoardCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.BanAppealsBoardMissing), appeals.BoardText);
        appeals.PluginVersions = "rog-paper 0.6.0\nrog-paper 0.7.0";
        await appeals.SavePluginVersionsCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.BanAppealsPluginVersionsFormat), feedback.Message);
        appeals.PluginVersions = "rog-paper 0.7.0\nrog-proxy=0.6";
        await appeals.SavePluginVersionsCommand.ExecuteAsync(null);
        var versions = Body(t, "PATCH", "/v1/game-bans/settings").GetProperty("min_plugin_versions");
        Assert.Equal(2, versions.GetArrayLength());
        Assert.Equal(t.Services.T(StringKeys.BanAppealsPluginVersionsSaved), feedback.Message);
    }

    // --- Melden ----------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Report_form_offers_api_reasons_child_safety_first_and_sends()
    {
        using var t = await AsAsync(FakeModerationRoutes.MeMember);
        var form = new ReportFormViewModel(t.Services);
        Assert.Equal(["child_safety", "spam", "insult", "harassment", "illegal", "cheating", "other"], form.Reasons.Select(r => r.Value));
        Assert.Equal(t.Services.T("Report_Reason_child_safety"), form.Reasons[0].Label);
        Assert.True(ReportFormViewModel.IsAllowed(t.Services));
        Assert.False(form.IsOpen);

        form.Open("wiki_page", "01JWIKIREGELN0000000000001", "Wiki-Seite · Clan-Regeln", "en");
        Assert.True(form.IsOpen);
        Assert.True(form.HasSubject);
        await form.SendCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.ReportReasonMissing), form.Error);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/reports");

        (string Type, string Id)? reported = null;
        form.Reported += (_, r) => reported = r;
        form.SelectedReason = form.Reasons.Single(r => r.Value == "insult");
        form.Comment = "  beleidigt andere im Abschnitt Voice  ";
        await form.SendCommand.ExecuteAsync(null);
        var body = Body(t, "POST", "/v1/reports");
        Assert.Equal("wiki_page", body.GetProperty("type").GetString());
        Assert.Equal("01JWIKIREGELN0000000000001", body.GetProperty("id").GetString());
        Assert.Equal("insult", body.GetProperty("reason").GetString());
        Assert.Equal("beleidigt andere im Abschnitt Voice", body.GetProperty("comment").GetString());
        Assert.Equal("en", body.GetProperty("locale").GetString());
        Assert.True(form.IsSent);
        Assert.False(form.ShowForm);
        Assert.Equal(("wiki_page", "01JWIKIREGELN0000000000001"), reported);

        // Neu öffnen setzt zurück; Fehler des Servers (zu viele Meldungen) im Formular
        form.Open("message", "01M6");
        Assert.False(form.IsSent);
        Assert.Null(form.SelectedReason);
        Assert.Equal(string.Empty, form.Comment);
        t.Api.Failures["/v1/reports"] = (HttpStatusCode.TooManyRequests, """{"type":"about:blank","title":"Zu viele","status":429,"code":"too_many_reports"}""");
        form.SelectedReason = form.Reasons[0];
        await form.SendCommand.ExecuteAsync(null);
        Assert.True(form.HasError);
        Assert.False(form.IsSent);
        var sent = JsonDocument.Parse(t.Api.Writes[^1].Body).RootElement;
        Assert.True(!sent.TryGetProperty("locale", out var locale) || locale.ValueKind == JsonValueKind.Null); // Sprache nur bei wiki_page

        Assert.True(form.Close());
        Assert.False(form.Close());
    }

    [AvaloniaFact]
    public async Task Report_is_offered_where_windows_offers_it()
    {
        // Forum: sichtbare Beiträge
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            using var forum = new ForumViewModel(t.Services);
            await forum.LoadAsync();
            forum.OpenCategoryCommand.Execute(forum.Categories.Single(c => c.Slug == "allgemein"));
            await WaitUntil(() => forum.Threads.Count == 3);
            forum.SelectedThread = forum.Threads[0];
            await WaitUntil(() => forum.Thread.ShowContent);
            var post = forum.Thread.Posts[0];
            Assert.True(post.CanReport);
            post.ReportCommand.Execute(null);
            Assert.True(forum.Thread.Report.IsOpen);
            Assert.Equal($"{t.Services.T("Mod_Type_forum_post")} · Nyx", forum.Thread.Report.Subject);
            forum.CloseDetail(); // Escape: zuerst das Formular
            Assert.False(forum.Thread.Report.IsOpen);
            Assert.True(forum.IsDetailOpen);
            forum.Thread.Report.SelectedReason = forum.Thread.Report.Reasons[1];
            post.ReportCommand.Execute(null);
            forum.Thread.Report.SelectedReason = forum.Thread.Report.Reasons[1];
            await forum.Thread.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("forum_post", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
            Assert.Equal(post.Id, Body(t, "POST", "/v1/reports").GetProperty("id").GetString());
        }

        // Ohne Scope reports:write (Standard-Token der Tests) bzw. als Gast: kein „Melden“
        using (var t = await TestServices.CreateAsync(TestTokens.Wahke))
        {
            t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
            using var forum = new ForumViewModel(t.Services);
            await forum.LoadAsync();
            forum.OpenCategoryCommand.Execute(forum.Categories.Single(c => c.Slug == "allgemein"));
            await WaitUntil(() => forum.Threads.Count == 3);
            forum.SelectedThread = forum.Threads[0];
            await WaitUntil(() => forum.Thread.ShowContent);
            Assert.All(forum.Thread.Posts, p => Assert.False(p.CanReport));
        }

        // Profil: fremdes Profil
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            var members = new MembersViewModel(t.Services);
            await members.LoadAsync();
            members.Selected = members.Items[1];
            await WaitUntil(() => members.Detail.ShowContent);
            Assert.True(members.Detail.CanReport);
            members.Detail.ReportProfileCommand.Execute(null);
            Assert.True(members.Detail.Report.IsOpen);
            Assert.Contains("Nyx", members.Detail.Report.Subject, StringComparison.Ordinal);
            members.Detail.Report.SelectedReason = members.Detail.Report.Reasons[2];
            await members.Detail.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("profile", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
            Assert.Equal("01U2", Body(t, "POST", "/v1/reports").GetProperty("id").GetString());
        }

        // Galerie: Großansicht
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            var media = new MediaViewModel(t.Services);
            await media.LoadAsync();
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await WaitUntil(() => !media.IsLoadingItems && media.Items.Count > 0);
            Assert.True(media.CanReport);
            media.ViewCommand.Execute(media.Items[0]);
            media.ReportViewedCommand.Execute(null);
            Assert.True(media.Report.IsOpen);
            media.NextCommand.Execute(null); // anderes Bild → Formular zu
            Assert.False(media.Report.IsOpen);
            media.ReportViewedCommand.Execute(null);
            media.CloseDetail(); // Escape: erst das Formular, dann die Großansicht
            Assert.False(media.Report.IsOpen);
            Assert.True(media.IsViewerOpen);
            media.ReportViewedCommand.Execute(null);
            media.Report.SelectedReason = media.Report.Reasons[1];
            await media.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("gallery_item", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
        }

        // Direktnachricht: nur fremde, nicht gelöschte
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            var messages = new MessagesViewModel(t.Services, () => { });
            await messages.LoadAsync();
            messages.Selected = messages.Items[0];
            await WaitUntil(() => messages.Chat?.ShowContent == true);
            var chat = messages.Chat!;
            Assert.False(chat.Messages.Single(m => m.IsDeleted).CanReport);
            Assert.All(chat.Messages.Where(m => m.IsMine), m => Assert.False(m.CanReport));
            var other = chat.Messages.Last(m => m.IsOther && !m.IsDeleted);
            Assert.True(other.CanReport);
            other.ReportCommand.Execute(null);
            Assert.True(chat.Report.IsOpen);
            chat.Report.SelectedReason = chat.Report.Reasons[3];
            await chat.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("message", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
            Assert.Equal(other.Message.Id, Body(t, "POST", "/v1/reports").GetProperty("id").GetString());
        }

        // News-Kommentar
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            var news = new NewsViewModel(t.Services);
            await news.LoadAsync();
            news.Selected = news.Items[0];
            await WaitUntil(() => news.Detail.ShowContent);
            var comment = news.Detail.Comments[0];
            Assert.True(comment.CanReport);
            comment.ReportCommand.Execute(null);
            Assert.True(news.Detail.Report.IsOpen);
            news.Detail.Report.SelectedReason = news.Detail.Report.Reasons[1];
            await news.Detail.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("news_comment", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
            Assert.Equal("01C1", Body(t, "POST", "/v1/reports").GetProperty("id").GetString());
        }

        // Wiki: Seite (fremde Fassung) und Versionen (nicht die eigenen)
        using (var t = await AsAsync(FakeModerationRoutes.MeMember))
        {
            var wiki = new WikiViewModel(t.Services, () => { });
            await wiki.LoadAsync();
            wiki.Selected = wiki.Items.First(i => i.Id == FakeWikiRoutes.Rules);
            await WaitUntil(() => wiki.ShowPage && wiki.Title == "Clan-Regeln");
            Assert.True(wiki.CanReportPage);
            wiki.ReportPageCommand.Execute(null);
            wiki.Report.SelectedReason = wiki.Report.Reasons[6];
            await wiki.Report.SendCommand.ExecuteAsync(null);
            var page = Body(t, "POST", "/v1/reports");
            Assert.Equal("wiki_page", page.GetProperty("type").GetString());
            Assert.Equal(FakeWikiRoutes.Rules, page.GetProperty("id").GetString());
            Assert.Equal("de", page.GetProperty("locale").GetString());

            wiki.IsRevisionsExpanded = true;
            await WaitUntil(() => wiki.Revisions.Count == 3);
            Assert.True(wiki.Revisions[0].CanReport); // Kaito
            Assert.False(wiki.Revisions[1].CanReport); // eigene
            wiki.Revisions[0].ReportCommand.Execute(null);
            Assert.True(wiki.Report.IsOpen);
            Assert.False(wiki.Report.IsSent);
            wiki.Report.SelectedReason = wiki.Report.Reasons[1];
            await wiki.Report.SendCommand.ExecuteAsync(null);
            Assert.Equal("wiki_revision", Body(t, "POST", "/v1/reports").GetProperty("type").GetString());
        }
    }
}
