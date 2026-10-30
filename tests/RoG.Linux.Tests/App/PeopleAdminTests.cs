using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 9 „K“: Verwaltung – Reiter Mitglieder und Bewerbungen, Squad-Verwaltung (Fenster) mit Einstieg auf der Squad-Seite.</summary>
public class PeopleAdminTests
{
    internal static async Task<TestServices> SignedInAsync(string me)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        FakePeopleAdminRoutes.Install(t.Api, me);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static JsonNode LastBody(TestServices t, string method, string path)
    {
        var w = t.Api.Writes.Last(w => w.Method == method && w.Path == path);
        return w.Body.Length == 0 ? new JsonObject() : JsonNode.Parse(w.Body)!;
    }

    internal static MembersAdminViewModel Members(TestServices t) => new(t.Services, new FeedbackViewModel(), new InlineConfirmViewModel());

    internal static ApplicationsAdminViewModel Applications(TestServices t) => new(t.Services, new FeedbackViewModel(), new InlineConfirmViewModel());

    [AvaloniaFact]
    public async Task Members_tab_lists_filters_and_assigns_and_revokes_roles()
    {
        using var t = await SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var vm = Members(t);
        Assert.True(vm.CanView);
        Assert.True(vm.CanAssign);
        Assert.Equal(["active", "suspended", "former"], vm.StatusOptions.Select(o => o.Value));

        await vm.EnsureLoadedAsync();
        await vm.EnsureLoadedAsync(); // nur einmal laden
        Assert.Single(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/admin/members");
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("Member · Enemy Territory, Counter-Strike 2", vm.Items[0].Meta);
        Assert.True(vm.HasMore);
        var query = Uri.UnescapeDataString(t.Api.Requests.Last(r => r.RequestUri!.AbsolutePath == "/v1/admin/members").RequestUri!.Query);
        Assert.Contains("filter[status]=active", query, StringComparison.Ordinal);
        Assert.Contains("include=squads", query, StringComparison.Ordinal);

        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.HasMore);

        // Konto öffnen
        vm.Selected = vm.Items[0];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
        Assert.True(vm.IsDetailOpen);
        Assert.Equal("Nyx · Member", vm.Header);
        Assert.Contains("nyx@example.test", vm.AccountText, StringComparison.Ordinal);
        Assert.Contains(t.Services.T(StringKeys.AccountTwoFactorOn), vm.AccountText, StringComparison.Ordinal);
        Assert.Contains("Enemy Territory, Counter-Strike 2", vm.AccountText, StringComparison.Ordinal);
        Assert.Equal(2, vm.Roles.Count);
        Assert.StartsWith("Member · " + t.Services.T(StringKeys.MemberAdminScopeGlobal), vm.Roles[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Co-Leader · Enemy Territory · ", vm.Roles[1].Text, StringComparison.Ordinal);
        Assert.True(vm.Roles[1].IsTemporary);
        Assert.True(vm.Roles[0].CanRevoke);
        Assert.Equal(4, vm.RoleOptions.Count); // Rollen aus /v1/roles nach sort
        Assert.Equal(["", "01SQ1", "01SQ2"], vm.ScopeOptions.Select(o => o.Value));

        // Rolle im Squad vergeben
        Assert.False(vm.AssignCommand.CanExecute(null));
        vm.SelectedRole = vm.RoleOptions.First(o => o.Value == "rk-eventteam");
        vm.SelectedScope = vm.ScopeOptions[1];
        await vm.AssignCommand.ExecuteAsync(null);
        var body = LastBody(t, "POST", "/v1/members/nyx/roles");
        Assert.Equal("rk-eventteam", body["role_id"]!.GetValue<string>());
        Assert.Equal("squad", body["scope_type"]!.GetValue<string>());
        Assert.Equal("01SQ1", body["scope_id"]!.GetValue<string>());
        Assert.True(vm.Feedback.IsSuccess);
        Assert.Equal(t.Services.T(StringKeys.MemberAdminAssigned), vm.Feedback.Message);

        // Entziehen mit Rückfrage – erst abbrechen, dann bestätigen
        var revoke = vm.Roles[1].RevokeCommand.ExecuteAsync(null);
        Assert.True(vm.Confirm.IsOpen);
        Assert.Contains("Co-Leader", vm.Confirm.Text, StringComparison.Ordinal);
        Assert.True(vm.CloseOverlay()); // Escape bricht ab
        await revoke;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");
        revoke = vm.Roles[1].RevokeCommand.ExecuteAsync(null);
        vm.Confirm.AcceptCommand.Execute(null);
        await revoke;
        Assert.Contains(t.Api.Writes, w => w is { Method: "DELETE", Path: "/v1/members/nyx/roles/01RA2" });
        Assert.Single(vm.Roles);
        Assert.Equal(t.Services.T(StringKeys.MemberAdminRevoked), vm.Feedback.Message);

        // Escape schließt das Detail; Status-Filter lädt neu
        Assert.True(vm.CloseOverlay());
        Assert.False(vm.IsDetailOpen);
        Assert.False(vm.CloseOverlay());
        vm.SelectedStatus = vm.StatusOptions[2];
        await ViewModelTests.WaitUntil(() => vm.Items.Count == 1 && vm.Items[0].Slug == "oldschool");
        Assert.Contains(t.Services.T("MemberAdmin_Status_former"), vm.Items[0].Meta, StringComparison.Ordinal);

        // Suche ohne Treffer: Leerzustand
        vm.Search = "zzz";
        await vm.SearchNowCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Members_tab_shows_hierarchy_refusal_of_the_server()
    {
        using var t = await SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var vm = Members(t);
        await vm.EnsureLoadedAsync();
        vm.Selected = vm.Items.First(i => i.Slug == "blizzard");
        await ViewModelTests.WaitUntil(() => vm.ShowDetail && vm.Header.StartsWith("Blizzard", StringComparison.Ordinal));

        vm.SelectedRole = vm.RoleOptions.First(o => o.Value == "rk-admin");
        await vm.AssignCommand.ExecuteAsync(null);

        Assert.True(vm.Feedback.IsError);
        Assert.Contains(t.Services.Localizer.GetError("forbidden"), vm.Feedback.Message, StringComparison.Ordinal);
        Assert.Contains("über deiner eigenen Position", vm.Feedback.Message, StringComparison.Ordinal);
        Assert.Single(vm.Roles); // unverändert
    }

    [AvaloniaFact]
    public async Task Members_tab_follows_rights_and_scopes()
    {
        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeSquadLeader))
        {
            var vm = Members(t);
            Assert.True(vm.CanView); // members.view im Squad reicht zum Lesen
            Assert.False(vm.CanAssign);
            await vm.EnsureLoadedAsync();
            vm.Selected = vm.Items[0];
            await ViewModelTests.WaitUntil(() => vm.ShowDetail);
            Assert.False(vm.Roles[0].CanRevoke);
            Assert.Empty(vm.RoleOptions);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/roles");
        }

        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeMember))
        {
            var vm = Members(t);
            Assert.False(vm.CanView);
            Assert.False(vm.CanAssign);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.HasError);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/admin/members");
        }

        using (var t = await TestServices.CreateAsync(guest: true))
        {
            var vm = Members(t);
            Assert.False(vm.CanView);
        }
    }

    [AvaloniaFact]
    public async Task Applications_tab_shows_everything_for_the_decision_and_decides_with_inline_prompt()
    {
        using var t = await SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var vm = Applications(t);
        Assert.True(vm.CanView);
        Assert.True(vm.CanReview);
        Assert.Equal(7, vm.DecisionOptions.Count);

        await vm.EnsureLoadedAsync();
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Nyx → Enemy Territory", vm.Items[0].Title);
        Assert.Contains("67 %", vm.Items[1].Meta, StringComparison.Ordinal);
        Assert.Contains(t.Api.Requests, r => Uri.UnescapeDataString(r.RequestUri!.Query).Contains("filter[status]=open", StringComparison.Ordinal));

        vm.Selected = vm.Items[0];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
        Assert.Equal("Nyx → Enemy Territory", vm.Header);
        Assert.Contains(t.Services.T("AppAdmin_Status_trial"), vm.Subtitle, StringComparison.Ordinal);
        Assert.Contains("Kaito", vm.Subtitle, StringComparison.Ordinal); // geworben von
        Assert.False(vm.HasVote);
        Assert.Equal(3, vm.Answers.Count);
        Assert.Equal("yes", vm.Answers[2].Answer);
        Assert.Equal(2, vm.Checks.Count);
        Assert.True(vm.Checks[1].Warn);
        Assert.Contains(t.Services.T("AppAdmin_Source_discord_voice"), vm.ActivityText, StringComparison.Ordinal);
        Assert.Contains("2,6 h", vm.ActivityText, StringComparison.Ordinal); // 155 Minuten
        Assert.Equal(3, vm.Timeline.Count);
        Assert.True(vm.Timeline[2].Internal);
        Assert.Equal("Interne Notiz", vm.Timeline[2].Title);
        Assert.Equal(2, vm.Messages.Count);
        Assert.True(vm.Messages[1].Internal);
        Assert.Equal(2, vm.Tasks.Count);
        Assert.True(vm.Tasks[0].Done);

        // Verlängern: Rückfrage im Inhalt mit Nachricht und Tagen
        Assert.False(vm.DecideCommand.CanExecute(null));
        vm.SelectedDecision = vm.DecisionOptions.First(o => o.Value == "extend");
        vm.DecideCommand.Execute(null);
        Assert.True(vm.IsDecisionOpen);
        Assert.True(vm.DecisionNeedsDays);
        Assert.Equal(t.Services.T("AppAdmin_Decision_extend"), vm.DecisionTitle);
        Assert.True(vm.CloseOverlay()); // Escape schließt nur die Rückfrage
        Assert.False(vm.IsDecisionOpen);
        Assert.True(vm.IsDetailOpen);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.Contains("/decisions/", StringComparison.Ordinal));

        vm.DecideCommand.Execute(null);
        vm.DecisionMessage = "Noch ein Training, dann entscheiden wir.";
        vm.DecisionDays = 5;
        await vm.ConfirmDecisionCommand.ExecuteAsync(null);
        var body = LastBody(t, "POST", "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/decisions/extend");
        Assert.Equal(5, body["days"]!.GetValue<int>());
        Assert.Equal("Noch ein Training, dann entscheiden wir.", body["message"]!.GetValue<string>());
        Assert.False(vm.IsDecisionOpen);
        Assert.True(vm.Feedback.IsSuccess);
        Assert.Equal(t.Services.T(StringKeys.AppAdminDecided), vm.Feedback.Message);
        Assert.True(vm.IsDetailOpen); // Auswahl bleibt nach dem Neuladen

        // Ohne Tage bei „aufnehmen“
        vm.SelectedDecision = vm.DecisionOptions.First(o => o.Value == "accept");
        vm.DecideCommand.Execute(null);
        Assert.False(vm.DecisionNeedsDays);
        await vm.ConfirmDecisionCommand.ExecuteAsync(null);
        Assert.Null(LastBody(t, "POST", "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/decisions/accept")["days"]);

        // Aufgabe abhaken, hinzufügen, löschen (mit Rückfrage)
        vm.Tasks[1].Done = true;
        await ViewModelTests.WaitUntil(() => t.Api.Writes.Any(w => w is { Method: "PATCH", Path: "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/tasks/t2" }));
        Assert.True(LastBody(t, "PATCH", "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/tasks/t2")["done"]!.GetValue<bool>());
        Assert.False(vm.AddTaskCommand.CanExecute(null));
        vm.NewTask = "Demo vom Clanwar hochladen";
        await vm.AddTaskCommand.ExecuteAsync(null);
        Assert.Equal("Demo vom Clanwar hochladen", LastBody(t, "POST", "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/tasks")["title"]!.GetValue<string>());
        Assert.Empty(vm.NewTask);
        var delete = vm.Tasks[0].DeleteCommand.ExecuteAsync(null);
        Assert.True(vm.Confirm.IsOpen);
        vm.Confirm.AcceptCommand.Execute(null);
        await delete;
        Assert.Contains(t.Api.Writes, w => w is { Method: "DELETE", Path: "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/tasks/t1" });

        // Interne Notiz und Erinnern
        vm.NewMessage = "Passt gut ins Team.";
        await vm.SendMessageCommand.ExecuteAsync(null);
        var note = LastBody(t, "POST", "/v1/applications/" + FakePeopleAdminRoutes.AppNyx + "/messages");
        Assert.True(note["internal"]!.GetValue<bool>());
        Assert.Equal(t.Services.T("Linux_AppAdminNoteSaved"), vm.Feedback.Message);
        await vm.RemindCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.AppAdminReminded), vm.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Applications_tab_shows_vote_and_server_refusal()
    {
        using var t = await SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var vm = Applications(t);
        await vm.EnsureLoadedAsync();
        vm.Selected = vm.Items[1];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
        Assert.True(vm.HasVote);
        Assert.Contains(t.Services.T("AppAdmin_Result_accept"), vm.VoteText, StringComparison.Ordinal);
        Assert.Contains("Kaito, Blizzard", vm.VoteText, StringComparison.Ordinal);
        Assert.Equal(t.Services.T(StringKeys.AppAdminNoActivity), vm.ActivityText);
        Assert.False(vm.HasTasks);
        Assert.False(vm.HasMessages);

        vm.SelectedDecision = vm.DecisionOptions.First(o => o.Value == "reject");
        vm.DecideCommand.Execute(null);
        await vm.ConfirmDecisionCommand.ExecuteAsync(null);
        Assert.True(vm.Feedback.IsError);
        Assert.Contains("nicht mehr in diesem Schritt", vm.Feedback.Message, StringComparison.Ordinal);
        Assert.True(vm.IsDecisionOpen); // bleibt offen zum Korrigieren

        // Leerer Status
        vm.SelectedStatus = vm.StatusOptions.First(o => o.Value == "rejected");
        await ViewModelTests.WaitUntil(() => vm.IsEmpty);
        Assert.False(vm.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Applications_tab_follows_rights()
    {
        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeSquadLeader))
        {
            var vm = Applications(t);
            Assert.True(vm.CanView);
            Assert.True(vm.CanReview); // applications.review im Squad
        }

        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeMember))
        {
            var vm = Applications(t);
            Assert.False(vm.CanView);
            Assert.False(vm.CanReview);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.HasError);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/applications");
        }
    }

    internal static async Task<SquadAdminViewModel> OpenSquadAdminAsync(TestServices t)
    {
        var squads = new SquadsViewModel(t.Services);
        await squads.LoadAsync();
        SquadAdminViewModel? admin = null;
        squads.Detail.ManageRequested += (_, a) => admin = a;
        squads.Selected = squads.Items.First(i => i.Slug == FakePeopleAdminRoutes.SquadSlug);
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        Assert.True(squads.Detail.CanManage);
        squads.Detail.ManageCommand.Execute(null);
        return admin ?? throw new InvalidOperationException("kein Verwaltungsfenster angefordert");
    }

    [AvaloniaFact]
    public async Task Squad_page_offers_manage_only_with_rights()
    {
        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeMember))
        {
            var squads = new SquadsViewModel(t.Services);
            await squads.LoadAsync();
            squads.Selected = squads.Items[0];
            await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
            Assert.False(squads.Detail.CanManage); // squads:write ohne Recht
        }

        using (var t = await SignedInAsync(FakePeopleAdminRoutes.MeSquadLeader))
        {
            var admin = await OpenSquadAdminAsync(t);
            Assert.False(admin.CanEdit);
            Assert.True(admin.CanMembers);
            Assert.False(admin.CanArchive);
            Assert.False(admin.ShowTabs);
            Assert.Equal(SquadAdminTab.Members, admin.Tab);
        }

        using (var t = await TestServices.CreateAsync(guest: true))
        {
            var squads = new SquadsViewModel(t.Services);
            await squads.LoadAsync();
            squads.Selected = squads.Items[0];
            await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
            Assert.False(squads.Detail.CanManage);
        }
    }

    [AvaloniaFact]
    public async Task Squad_admin_saves_profile_lineup_and_archive()
    {
        using var t = await SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var admin = await OpenSquadAdminAsync(t);
        bool? closedChanged = null;
        var reload = admin.Closed;
        admin.Closed = changed =>
        {
            closedChanged = changed;
            reload?.Invoke(changed);
        };
        Assert.True(admin.CanEdit && admin.CanMembers && admin.CanArchive);
        Assert.True(admin.ShowTabs);
        Assert.Equal(SquadAdminTab.Profile, admin.Tab);
        Assert.Equal("Enemy Territory", admin.Title);
        Assert.Equal("Objective-Spezialisten seit Tag eins", admin.Tagline);
        Assert.Equal("open", admin.SelectedRecruiting?.Value);

        // Ohne Änderung: nichts senden
        await admin.SaveProfileCommand.ExecuteAsync(null);
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PATCH");
        Assert.Equal(t.Services.T(StringKeys.SquadAdminNothing), admin.Feedback.Message);

        admin.Tagline = "Objective first – seit 2020";
        admin.SelectedRecruiting = admin.RecruitingOptions.First(o => o.Value == "waitlist");
        admin.ClanLevel = 12;
        await admin.SaveProfileCommand.ExecuteAsync(null);
        var patch = LastBody(t, "PATCH", "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug);
        Assert.Equal("Objective first – seit 2020", patch["tagline"]!["de"]!.GetValue<string>());
        Assert.Equal("waitlist", patch["recruiting_status"]!.GetValue<string>());
        Assert.Equal(12, patch["clan_level"]!.GetValue<int>());
        Assert.Equal(t.Services.T(StringKeys.SquadAdminSaved), admin.Feedback.Message);

        // Line-up: Squad-Leader (nicht vergebbar) bleibt gesperrt
        admin.IsMembersTab = true;
        await ViewModelTests.WaitUntil(() => admin.Lineup.Count == 4);
        Assert.Equal(["r3", "r2"], admin.Roles.Select(r => r.Value));
        Assert.Equal("r2", admin.NewMemberRole?.Value); // niedrigste vergebbare
        var wahke = admin.Lineup.First(m => m.Slug == "wahke");
        Assert.True(wahke.Locked);
        var nyx = admin.Lineup.First(m => m.Slug == "nyx");
        Assert.Equal("r2", nyx.SelectedRole?.Value);
        Assert.False(nyx.IsDirty);
        nyx.SelectedRole = nyx.Roles.First(r => r.Value == "r3");
        Assert.True(nyx.IsDirty);
        await nyx.SaveCommand.ExecuteAsync(null);
        var put = LastBody(t, "PUT", "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug + "/members/nyx");
        Assert.Equal("r3", put["role_id"]!.GetValue<string>());
        Assert.False(put["is_substitute"]!.GetValue<bool>());
        Assert.False(nyx.IsDirty);

        // Hierarchie: Server lehnt ab → Rückmeldung
        var blizzard = admin.Lineup.First(m => m.Slug == "blizzard");
        Assert.True(blizzard.Substitute);
        blizzard.SelectedRole = blizzard.Roles.First(r => r.Value == "r3");
        await blizzard.SaveCommand.ExecuteAsync(null);
        Assert.True(admin.Feedback.IsError);
        Assert.Contains("über deiner eigenen Position", admin.Feedback.Message, StringComparison.Ordinal);
        Assert.True(blizzard.IsDirty);

        // Mitglied hinzufügen: Vorschläge ohne Line-up, auswählen, hinzufügen
        Assert.False(admin.AddMemberCommand.CanExecute(null));
        admin.MemberSearch = "a";
        Assert.False(admin.HasSuggestions);
        admin.MemberSearch = "sa";
        await ViewModelTests.WaitUntil(() => admin.HasSuggestions);
        Assert.DoesNotContain(admin.Suggestions, s => s.Slug is "wahke" or "nyx" or "kaito" or "blizzard");
        var sariela = admin.Suggestions.First(s => s.Slug == "sariela");
        sariela.ChooseCommand.Execute(null);
        Assert.Equal("Sariela", admin.MemberSearch);
        Assert.True(admin.HasNewMember);
        Assert.False(admin.HasSuggestions);
        admin.NewMemberSubstitute = true;
        await admin.AddMemberCommand.ExecuteAsync(null);
        var added = LastBody(t, "PUT", "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug + "/members/sariela");
        Assert.Equal("r2", added["role_id"]!.GetValue<string>());
        Assert.True(added["is_substitute"]!.GetValue<bool>());
        Assert.Equal(t.Services.Format(StringKeys.SquadAdminMemberAdded, "Sariela"), admin.Feedback.Message);
        Assert.Empty(admin.MemberSearch);

        // Entfernen mit Rückfrage
        var kaito = admin.Lineup.First(m => m.Slug == "kaito");
        var remove = kaito.RemoveCommand.ExecuteAsync(null);
        Assert.True(admin.Confirm.IsOpen);
        Assert.True(admin.HandleEscape()); // Escape bricht nur ab
        await remove;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");
        remove = kaito.RemoveCommand.ExecuteAsync(null);
        admin.Confirm.AcceptCommand.Execute(null);
        await remove;
        Assert.Contains(t.Api.Writes, w => w is { Method: "DELETE", Path: "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug + "/members/kaito" });
        Assert.DoesNotContain(admin.Lineup, m => m.Slug == "kaito");

        // Archivieren
        admin.IsArchiveTab = true;
        Assert.False(admin.IsArchived);
        Assert.Equal(t.Services.T(StringKeys.SquadAdminArchive), admin.ArchiveLabel);
        var archive = admin.ToggleArchiveCommand.ExecuteAsync(null);
        admin.Confirm.AcceptCommand.Execute(null);
        await archive;
        Assert.True(LastBody(t, "POST", "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug + "/archive")["archived"]!.GetValue<bool>());
        Assert.True(admin.IsArchived);
        Assert.Equal(t.Services.T(StringKeys.SquadAdminRestore), admin.ArchiveLabel);

        // Logo entfernen (mit Rückfrage), Bild hochladen nur für Bilddateien
        var removeLogo = admin.RemoveImageCommand.ExecuteAsync(SquadImage.Logo);
        admin.Confirm.AcceptCommand.Execute(null);
        await removeLogo;
        Assert.Contains(t.Api.Writes, w => w is { Method: "DELETE", Path: "/v1/squads/" + FakePeopleAdminRoutes.SquadSlug + "/logo" });
        await admin.SetImageAsync(SquadImage.Header, new UploadSource("notizen.txt", 12, () => Task.FromResult<Stream>(new MemoryStream(new byte[12]))));
        Assert.Equal(t.Services.T(StringKeys.WikiImageOnly), admin.Feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/header", StringComparison.Ordinal));

        Assert.False(admin.HandleEscape());
        admin.NotifyClosed();
        Assert.True(closedChanged);
    }
}
