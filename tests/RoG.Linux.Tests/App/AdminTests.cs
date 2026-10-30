using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 9 „J“: Seite Verwaltung (Reiter je Recht + Scope), Übersicht, Mod-Log, Website-Bereiche und allgemeiner Editor.</summary>
public class AdminTests
{
    internal static async Task<TestServices> SignedInAsync(string me)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = me;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static AdminArea Games => AdminAreas.Find("games")!;

    [AvaloniaFact]
    public async Task Admin_sees_tabs_like_windows_and_the_nav_entry()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        var shell = new ShellViewModel(t.Services);
        var entry = shell.FooterItems.Single(i => i.Key == "Nav_Admin");
        Assert.True(entry.IsVisible);
        Assert.True(shell.IsNavDense);
        Assert.Equal(["Nav_Notifications", "Nav_Admin", "Nav_Account", "Nav_Settings"], shell.FooterItems.Select(i => i.Key));

        var vm = new AdminViewModel(t.Services);
        Assert.True(vm.HasAccess);
        Assert.False(vm.NoAccess);
        // Übersicht (admin.access), Mitglieder (members.view), Meldungen/Sperren (moderation.reports), Mod-Log, Wortfilter, Website
        Assert.Equal(
            [AdminTab.Overview, AdminTab.Members, AdminTab.Reports, AdminTab.Bans, AdminTab.Log, AdminTab.Words, AdminTab.Website],
            vm.Tabs.Select(x => x.Tab));
        Assert.Equal(t.Services.T("AdminPage_TabOverview.Text").ToUpper(t.Services.Culture), vm.Tabs[0].Label);
        Assert.Equal(AdminTab.Overview, vm.SelectedTab);
        Assert.IsType<AdminOverviewViewModel>(vm.OverviewTab);
        Assert.IsType<ModerationLogViewModel>(vm.LogTab);
        Assert.IsType<AdminWebsiteViewModel>(vm.WebsiteTab);
        Assert.IsType<MembersAdminViewModel>(vm.MembersTab);
        Assert.IsType<ReportsAdminViewModel>(vm.ReportsTab);
        Assert.IsType<ModBansAdminViewModel>(vm.BansTab);
        Assert.IsType<WordFilterViewModel>(vm.WordsTab);
        Assert.Null(vm.MinecraftTab); // ohne minecraft.manage
        Assert.False(vm.SelectedPending);

        // Reiter ohne ViewModel: „kommt“
        vm.Tabs.Single(x => x.Tab == AdminTab.Members).IsSelected = true;
        Assert.Equal(AdminTab.Members, vm.SelectedTab);
        vm.MembersTab = null;
        Assert.True(vm.SelectedPending);
        Assert.Null(vm.SelectedContent);

        // Später eingehängter Reiter lädt beim Öffnen (ILazyTab)
        var lazy = new LazyProbe();
        vm.MembersTab = lazy;
        Assert.Same(lazy, vm.SelectedContent);
        Assert.False(vm.SelectedPending);
        Assert.Equal(1, lazy.Loads);
        vm.SelectedTab = AdminTab.Overview;
        vm.SelectedTab = AdminTab.Members;
        Assert.Equal(2, lazy.Loads); // EnsureLoadedAsync entscheidet selbst, ob es noch einmal lädt

        // Escape: Rückfrage, dann Overlay des Reiters
        Assert.True(vm.IsDetailOpen);
        vm.CloseDetail();
        Assert.Equal(1, lazy.Closed);
        _ = vm.Confirm.AskAsync("T", "X", "Ja", "Nein");
        Assert.True(vm.CloseOverlay());
        Assert.False(vm.Confirm.IsOpen);
    }

    private sealed class LazyProbe : ILazyTab, IOverlayTab
    {
        public int Loads { get; private set; }

        public int Closed { get; private set; }

        public Task EnsureLoadedAsync()
        {
            Loads++;
            return Task.CompletedTask;
        }

        public bool CloseOverlay()
        {
            Closed++;
            return true;
        }
    }

    [AvaloniaFact]
    public async Task Without_admin_rights_there_is_no_entry_and_the_page_says_so()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeMember);
        var shell = new ShellViewModel(t.Services);
        Assert.False(shell.FooterItems.Single(i => i.Key == "Nav_Admin").IsVisible);
        Assert.False(shell.IsNavDense);
        Assert.False(AdminAccess.ShowsNavEntry(t.Services.CurrentUser));

        var vm = new AdminViewModel(t.Services);
        Assert.True(vm.NoAccess);
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.OverviewTab);
        Assert.Null(vm.LogTab);
        Assert.Null(vm.WebsiteTab);
        Assert.False(vm.SelectedPending);
    }

    [AvaloniaFact]
    public async Task Guest_has_no_entry_and_no_tabs()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        Assert.False(shell.FooterItems.Single(i => i.Key == "Nav_Admin").IsVisible);
        var vm = new AdminViewModel(t.Services);
        Assert.True(vm.NoAccess);
    }

    [AvaloniaFact]
    public async Task Rights_without_scopes_show_the_entry_but_no_tabs()
    {
        // Recht vorhanden (Seitenleiste wie Windows), aber das Gerät hat die Verwaltungs-Scopes nicht → Hinweis
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdminWithoutScopes);
        Assert.True(AdminAccess.ShowsNavEntry(t.Services.CurrentUser));
        var vm = new AdminViewModel(t.Services);
        Assert.True(vm.NoAccess);
        Assert.Empty(vm.Tabs);
    }

    [AvaloniaFact]
    public async Task Moderator_only_sees_the_mod_log()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeModerator);
        Assert.True(AdminAccess.ShowsNavEntry(t.Services.CurrentUser));
        var vm = new AdminViewModel(t.Services);
        Assert.Equal([AdminTab.Log], vm.Tabs.Select(x => x.Tab));
        Assert.Equal(AdminTab.Log, vm.SelectedTab);
        var log = Assert.IsType<ModerationLogViewModel>(vm.SelectedContent);
        await ViewModelTests.WaitUntil(() => log.ShowContent);
        Assert.Equal(3, log.Items.Count);
        Assert.Contains("Nyx · warn · Sn1perKing", log.Items[0], StringComparison.Ordinal);
        Assert.EndsWith("Nyx · report_resolved", log.Items[2], StringComparison.Ordinal);
        Assert.True(log.HasMore);
        await log.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(4, log.Items.Count);
        Assert.False(log.HasMore);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/moderation/log" && r.RequestUri.Query.Contains("cursor=m2", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Rights_arrive_later_and_rebuild_the_tabs()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeAdminRoutes.MeAdmin;
        var shell = new ShellViewModel(t.Services);
        var vm = new AdminViewModel(t.Services);
        Assert.True(vm.NoAccess);
        Assert.False(shell.FooterItems.Single(i => i.Key == "Nav_Admin").IsVisible);

        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        await ViewModelTests.WaitUntil(() => vm.HasAccess && shell.FooterItems.Single(i => i.Key == "Nav_Admin").IsVisible);
        Assert.IsType<AdminOverviewViewModel>(vm.OverviewTab);
    }

    [AvaloniaFact]
    public async Task Admin_deep_link_opens_the_native_page()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeAdminRoutes.MeAdmin;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var opened = t.Browser.Opened.Count;
        Assert.True(shell.OpenDeepLink(new Uri("rog://admin")));
        Assert.Equal("Nav_Admin", shell.CurrentItem?.Key);
        Assert.IsType<AdminViewModel>(shell.CurrentPage);
        Assert.Equal(opened, t.Browser.Opened.Count); // nicht mehr im Browser
    }

    [AvaloniaFact]
    public async Task Overview_loads_kpis_system_checks_and_audit_with_search_and_paging()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        var vm = new AdminOverviewViewModel(t.Services, new FeedbackViewModel());
        Assert.True(vm.CanKpis && vm.CanSystem && vm.CanAudit);
        await vm.EnsureLoadedAsync();
        Assert.True(vm.ShowContent);

        Assert.Equal(6, vm.Kpis.Count);
        Assert.Equal("Mitglieder", vm.Kpis[0].Label);
        Assert.Equal("42", vm.Kpis[0].Value);
        Assert.Equal("5/6", vm.Kpis[4].Value);
        Assert.Equal("–", vm.Kpis[5].Value); // Quelle antwortet gerade nicht

        Assert.StartsWith(t.Services.T("Admin_System_degraded"), vm.SystemText, StringComparison.Ordinal);
        Assert.Contains("v0.53.2", vm.SystemText, StringComparison.Ordinal);
        Assert.Equal(5, vm.Checks.Count);
        Assert.Equal("✗", vm.Checks[0].Icon); // rote Prüfungen zuerst
        Assert.Equal("Mailversand", vm.Checks[0].Label);
        Assert.True(vm.Checks[0].HasHelp);
        Assert.Equal("Datenbank *", vm.Checks[1].Label); // kritisch
        Assert.Equal("–", vm.Checks[^1].Icon); // nicht eingerichtet zuletzt

        Assert.Equal(3, vm.Audit.Count);
        Assert.Contains("Wahke · role.assigned · Kaito", vm.Audit[0], StringComparison.Ordinal);
        Assert.True(vm.HasMoreAudit);
        await vm.MoreAuditCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Audit.Count);
        Assert.False(vm.HasMoreAudit);

        vm.AuditSearch = "Sniper";
        await vm.SearchAuditCommand.ExecuteAsync(null);
        Assert.Single(vm.Audit);
        Assert.Contains("server.restarted", vm.Audit[0], StringComparison.Ordinal);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query.Contains("filter%5Bsearch%5D=Sniper", StringComparison.Ordinal));

        vm.AuditSearch = "nichts";
        await vm.SearchAuditCommand.ExecuteAsync(null);
        Assert.True(vm.IsAuditEmpty);

        // Zweites Öffnen lädt nicht erneut
        var requests = t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/admin/dashboard");
        await vm.EnsureLoadedAsync();
        Assert.Equal(requests, t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/admin/dashboard"));
    }

    [AvaloniaFact]
    public async Task Overview_keeps_other_sections_when_one_source_fails()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        t.Api.Failures["/v1/system/status"] = (System.Net.HttpStatusCode.Forbidden,
            """{"type":"about:blank","title":"Fehler","status":403,"code":"insufficient_scope"}""");
        var vm = new AdminOverviewViewModel(t.Services, new FeedbackViewModel());
        await vm.EnsureLoadedAsync();
        Assert.True(vm.ShowContent);
        Assert.Equal(6, vm.Kpis.Count);
        Assert.True(vm.HasSystemError);
        Assert.Equal(t.Services.Localizer.GetError("insufficient_scope"), vm.SystemError);
        Assert.Equal(3, vm.Audit.Count);
    }

    [AvaloniaFact]
    public async Task Overview_shows_the_error_when_everything_fails()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        t.Api.Offline = true;
        var vm = new AdminOverviewViewModel(t.Services, new FeedbackViewModel());
        await vm.EnsureLoadedAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.ShowContent);
        t.Api.Offline = false;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.ShowContent);
        Assert.False(vm.HasSystemError);
    }

    [AvaloniaFact]
    public async Task Website_lists_native_areas_and_remaining_browser_links()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        var vm = new AdminWebsiteViewModel(t.Services, new FeedbackViewModel());
        var keys = vm.NativeAreas.Select(a => a.Area.Key).ToList();
        Assert.Equal(["games", "badges", "team-groups", "partners", "settings-clan", "roles"], keys);
        Assert.Equal(t.Services.T("AdminArea_games"), vm.NativeAreas[0].Label);
        Assert.Equal(t.Services.T("AdminGroup_clan"), vm.NativeAreas[0].Group);
        Assert.DoesNotContain(vm.NativeAreas, a => a.Area.Key == "servers"); // ohne servers.manage

        // Browser-Links: nur ohne Gegenstück in der App und mit bekanntem Recht (roles.view ja, settings → Editor)
        Assert.Contains(vm.WebLinks, l => l.Url.ToString() == "https://admin.rebelsofgaming.com/roles");
        Assert.DoesNotContain(vm.WebLinks, l => l.Url.AbsolutePath == "/games");
        Assert.DoesNotContain(vm.WebLinks, l => l.Url.AbsolutePath == "/settings");
        Assert.DoesNotContain(vm.WebLinks, l => l.Url.AbsolutePath == "/integrations");
        await vm.WebLinks.First(l => l.Url.AbsolutePath == "/roles").OpenCommand.ExecuteAsync(null);
        Assert.Equal("https://admin.rebelsofgaming.com/roles", t.Browser.Opened[^1].ToString());

        AdminArea? requested = null;
        vm.EditorRequested += (_, a) => requested = a;
        vm.NativeAreas[0].OpenCommand.Execute(null);
        Assert.Equal("games", requested?.Key);
    }

    [AvaloniaFact]
    public async Task Editor_lists_filters_opens_saves_creates_and_deletes()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        var vm = new AdminEditorViewModel(t.Services, Games);
        Assert.True(vm.CanWrite);
        Assert.True(vm.CanCreate);
        Assert.True(vm.ShowList);
        Assert.True(vm.HasWebPath);
        Assert.True(vm.HasHelp);
        await vm.LoadAsync();
        Assert.Equal(3, vm.Records.Count);
        Assert.True(vm.ShowRecords);
        Assert.Equal("Wolfenstein: Enemy Territory", vm.Records[0].Title);
        Assert.True(vm.HasNoForm);

        vm.Search = "counter";
        Assert.Single(vm.Records);
        vm.Search = "zzz";
        Assert.True(vm.IsEmpty);
        vm.Search = string.Empty;

        // Öffnen: Detail frisch laden, Formular füllen
        vm.Selected = vm.Records[0];
        await ViewModelTests.WaitUntil(() => vm.HasForm);
        Assert.Equal("Wolfenstein: Enemy Territory", vm.FormTitle);
        Assert.True(vm.CanSave);
        Assert.True(vm.CanDelete);
        var shortField = vm.Fields.Single(f => f.Field.Key == "short");
        Assert.Equal("ET", shortField.Text);
        Assert.Equal("pc", vm.Fields.Single(f => f.Field.Key == "platform").SelectedChoice?.Value);
        Assert.True(vm.Fields.Single(f => f.Field.Key == "active").IsOn);

        // Nichts geändert
        var writes = t.Api.Writes.Count;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(writes, t.Api.Writes.Count);
        Assert.Equal(t.Services.T(StringKeys.AdminFormNoChanges), vm.Feedback.Message);

        // Prüfung vor dem Senden
        var name = vm.Fields.Single(f => f.Field.Key == "name");
        name.Text = string.Empty;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(writes, t.Api.Writes.Count);
        Assert.True(name.HasError);
        Assert.True(vm.Feedback.IsError);
        name.Text = "Wolfenstein: Enemy Territory";

        // Speichern: nur Geändertes per PATCH
        shortField.Text = "W:ET";
        await vm.SaveCommand.ExecuteAsync(null);
        var patch = t.Api.Writes.Last();
        Assert.Equal(("PATCH", "/v1/games/et"), (patch.Method, patch.Path));
        var body = JsonNode.Parse(patch.Body)!.AsObject();
        Assert.Equal("W:ET", body["short"]!.GetValue<string>());
        Assert.False(body.ContainsKey("platform"));
        Assert.True(vm.Feedback.IsSuccess);
        Assert.True(vm.Changed);
        Assert.True(vm.HasForm);
        Assert.Equal("W:ET", vm.Fields.Single(f => f.Field.Key == "short").Text);

        // Neu anlegen
        await vm.NewCommand.ExecuteAsync(null);
        Assert.True(vm.IsNew);
        Assert.False(vm.CanDelete);
        Assert.Equal(t.Services.T(StringKeys.AdminFormNew), vm.FormTitle);
        vm.Fields.Single(f => f.Field.Key == "name").Text = "Return to Castle Wolfenstein";
        vm.Fields.Single(f => f.Field.Key == "short").Text = "RTCW";
        vm.Fields.Single(f => f.Field.Key == "platform").SelectedChoice = vm.Fields.Single(f => f.Field.Key == "platform").Choices.First(c => c.Value == "pc");
        await vm.SaveCommand.ExecuteAsync(null);
        var post = t.Api.Writes.Last();
        Assert.Equal(("POST", "/v1/games"), (post.Method, post.Path));
        Assert.Equal("RTCW", JsonNode.Parse(post.Body)!["short"]!.GetValue<string>());
        Assert.False(vm.IsNew);
        Assert.Equal("Return to Castle Wolfenstein", vm.FormTitle);

        // Löschen mit Rückfrage – erst abbrechen, dann bestätigen
        vm.Selected = vm.Records.Single(r => r.Title == "Minecraft");
        await ViewModelTests.WaitUntil(() => vm.FormTitle == "Minecraft" && vm.CanDelete);
        writes = t.Api.Writes.Count;
        var cancelled = vm.DeleteCommand.ExecuteAsync(null);
        Assert.True(vm.Confirm.IsOpen);
        Assert.Contains("Minecraft", vm.Confirm.Text, StringComparison.Ordinal);
        Assert.True(vm.CloseOverlay());
        await cancelled;
        Assert.Equal(writes, t.Api.Writes.Count);

        var deleting = vm.DeleteCommand.ExecuteAsync(null);
        vm.Confirm.AcceptCommand.Execute(null);
        await deleting;
        Assert.Equal(("DELETE", "/v1/games/mc"), (t.Api.Writes.Last().Method, t.Api.Writes.Last().Path));
        Assert.False(vm.HasForm);
        Assert.Equal(t.Services.T(StringKeys.AdminFormDeleted), vm.Feedback.Message);

        // Website-Pfad im Browser
        await vm.OpenWebCommand.ExecuteAsync(null);
        Assert.Equal("https://admin.rebelsofgaming.com/games", t.Browser.Opened[^1].ToString());
    }

    [AvaloniaFact]
    public async Task Editor_shows_list_errors_and_server_validation()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        t.Api.Failures["/v1/games"] = (System.Net.HttpStatusCode.InternalServerError,
            """{"type":"about:blank","title":"Fehler","status":500,"code":"server_error"}""");
        var vm = new AdminEditorViewModel(t.Services, Games);
        await vm.LoadAsync();
        Assert.True(vm.HasListError);
        Assert.False(vm.IsEmpty);
        t.Api.Failures.Remove("/v1/games");
        await vm.RetryCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Records.Count);

        vm.Selected = vm.Records[0];
        await ViewModelTests.WaitUntil(() => vm.HasForm);
        t.Api.Failures["/v1/games/et"] = (System.Net.HttpStatusCode.UnprocessableEntity,
            """{"type":"about:blank","title":"Fehler","status":422,"code":"validation_failed","detail":"Das Kürzel ist schon vergeben."}""");
        vm.Fields.Single(f => f.Field.Key == "short").Text = "CS2";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.Feedback.IsError);
        Assert.Equal("Das Kürzel ist schon vergeben.", vm.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Editor_for_settings_shows_only_the_form_and_read_only_without_write_scope()
    {
        using var t = await SignedInAsync(FakeAdminRoutes.MeAdmin);
        var area = AdminAreas.Find("settings-clan")!;
        var vm = new AdminEditorViewModel(t.Services, area);
        Assert.False(vm.ShowList);
        Assert.True(vm.IsSingleton);
        await vm.LoadAsync();
        Assert.True(vm.HasForm);
        Assert.False(vm.HasNoForm);
        Assert.Equal("Rebels of Gaming", vm.Fields.Single(f => f.Field.Key == "name").Text);
        Assert.Equal("Wir sind gleich zurück.", vm.Fields.Single(f => f.Field.Key == "maintenance.message").De);
        Assert.True(vm.CanSave);

        // Nur Lese-Scope: Felder gesperrt, kein Speichern
        t.Api.Overrides["/v1/me"] = FakeAdminRoutes.MeAdmin.Replace("\"settings:write\",", string.Empty, StringComparison.Ordinal);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var readOnly = new AdminEditorViewModel(t.Services, area);
        await readOnly.LoadAsync();
        Assert.False(readOnly.CanWrite);
        Assert.False(readOnly.CanSave);
        Assert.All(readOnly.Fields, f => Assert.False(f.IsEnabled));
    }
}
