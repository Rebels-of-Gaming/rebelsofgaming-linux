using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 9 „M“: Verwaltungsreiter Minecraft (Gruppen + Accounts), Rebel-Coins und Minecraft-Netzwerk, Weltkarte und im
/// Server-Reiter Minecraft das Bearbeiten der Beitrittsdaten sowie das Ressourcenpaket.
/// </summary>
public class MinecraftAdminTests
{
    internal static async Task<TestServices> SignedInAsync(string me = FakeMinecraftAdminRoutes.MeAdmin)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = me;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static JsonNode LastBody(TestServices t, string method, string pathPrefix)
    {
        var w = t.Api.Writes.Last(w => w.Method == method && w.Path.StartsWith(pathPrefix, StringComparison.Ordinal));
        return w.Body.Length == 0 ? new JsonObject() : JsonNode.Parse(w.Body)!;
    }

    [AvaloniaFact]
    public async Task Tabs_follow_rights_and_scopes()
    {
        using (var t = await SignedInAsync())
        {
            Assert.True(new MinecraftAdminViewModel(t.Services, new(), new()).CanView);
            Assert.True(new CoinsAdminViewModel(t.Services, new(), new()).CanView);
            var ops = new MinecraftOpsViewModel(t.Services, new(), new());
            Assert.True(ops.CanView && ops.CanPool && ops.CanDeploy);
        }

        // Recht ohne Scope: nichts (ein Token darf nie mehr als ausdrücklich erlaubt)
        using (var t = await SignedInAsync(FakeMinecraftAdminRoutes.MeRightsWithoutScopes))
        {
            Assert.False(new MinecraftAdminViewModel(t.Services, new(), new()).CanView);
            Assert.False(new CoinsAdminViewModel(t.Services, new(), new()).CanView);
            Assert.False(new MinecraftOpsViewModel(t.Services, new(), new()).CanView);
            Assert.False(new MinecraftJoinViewModel(t.Services, new()).CanEdit);
        }

        // Nur Pool: Reiter sichtbar, ohne Rollen und Deploy
        using (var t = await SignedInAsync(FakeMinecraftAdminRoutes.MePoolOnly))
        {
            var ops = new MinecraftOpsViewModel(t.Services, new(), new());
            Assert.True(ops.CanView);
            Assert.False(ops.CanDeploy);
            await ops.EnsureLoadedAsync();
            Assert.All(ops.Servers, s => Assert.False(s.CanEditRole || s.CanSelect));
            Assert.Empty(ops.Deploys);
            Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/minecraft/plugin-deploys");
            Assert.False(ops.CheckReleaseCommand.CanExecute(null));
        }

        // Gast: nichts laden
        using (var guest = await TestServices.CreateAsync(guest: true))
        {
            var tab = new MinecraftAdminViewModel(guest.Services, new(), new());
            await tab.EnsureLoadedAsync();
            Assert.Empty(tab.Items);
            Assert.DoesNotContain(guest.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/minecraft/accounts", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public async Task Minecraft_tab_lists_accounts_and_groups_searches_filters_pages_and_unlinks()
    {
        using var t = await SignedInAsync();
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var tab = new MinecraftAdminViewModel(t.Services, feedback, confirm);
        await tab.EnsureLoadedAsync();
        await WaitUntil(() => !tab.IsBusy && !tab.Groups.IsBusy && tab.Groups.RoleOptions.Count > 0);

        Assert.Equal(3, tab.Items.Count);
        Assert.True(tab.HasMore);
        var wahke = tab.Items[0];
        Assert.Equal("Wahke", wahke.Name);
        Assert.Contains("Konto: Wahke", wahke.Meta, StringComparison.Ordinal);
        Assert.Contains("über RoG Lobby", wahke.Meta, StringComparison.Ordinal);
        Assert.True(wahke.HasProfile);
        Assert.Contains("Bedrock", tab.Items[1].Meta, StringComparison.Ordinal);

        // Gruppen: zwei Zuordnungen (höchste Priorität zuerst), verwaltete und entfernte Gruppen, Squad-Rollen nicht wählbar
        var groups = tab.Groups;
        Assert.Equal(["Moderator", "Member"], groups.Mappings.Select(m => m.RoleLabel));
        Assert.Equal("Gruppe rog-mod · Priorität 80", groups.Mappings[0].Meta);
        Assert.Equal("rog-member, rog-mod, rog-builder-alt", groups.ManagedText);
        Assert.True(groups.HasRetired);
        Assert.True(groups.Retired[0].CanRelease);
        Assert.False(groups.Retired[1].CanRelease); // erst ab releasable_at
        Assert.DoesNotContain(groups.RoleOptions, o => o.Label == "MC Squad-Leitung");

        // Weitere laden, Suche und Filter Edition
        await tab.MoreCommand.ExecuteAsync(null);
        Assert.Equal(4, tab.Items.Count);
        Assert.False(tab.HasMore);
        tab.Search = "Block";
        tab.SelectedEdition = tab.EditionOptions.Single(o => o.Value == "bedrock");
        await WaitUntil(() => !tab.IsBusy);
        await tab.SearchNowCommand.ExecuteAsync(null);
        var query = t.Api.Requests.Last(r => r.RequestUri!.AbsolutePath == "/v1/minecraft/accounts").RequestUri!.Query;
        Assert.Contains("filter%5Bsearch%5D=Block", query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bedition%5D=bedrock", query, StringComparison.Ordinal);

        // Trennen: Rückfrage, abbrechen → nichts; bestätigen → weg
        var block = tab.Items.First(i => i.Name == ".BlockMeister");
        var unlink = block.UnlinkCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Contains(".BlockMeister (Konto: Kaito)", confirm.Text, StringComparison.Ordinal);
        Assert.True(tab.CloseOverlay()); // Escape
        await unlink;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");
        unlink = block.UnlinkCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        confirm.AcceptCommand.Execute(null);
        await unlink;
        Assert.Equal("/v1/minecraft/accounts/" + FakeMinecraftAdminRoutes.AccountBlock, t.Api.Writes.Last().Path);
        Assert.DoesNotContain(block, tab.Items);
        Assert.True(feedback.IsSuccess);

        // Profil öffnen → Mitglieder
        AppRoute? opened = null;
        t.Services.Navigator = r => { opened = r; return true; };
        wahke.OpenProfileCommand.Execute(null);
        Assert.Equal(new AppRoute(AppSection.Members, "wahke"), opened);
    }

    [AvaloniaFact]
    public async Task Groups_are_validated_saved_edited_removed_and_released()
    {
        using var t = await SignedInAsync();
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var groups = new MinecraftGroupsViewModel(t.Services, feedback, confirm);
        await groups.EnsureLoadedAsync();
        await WaitUntil(() => !groups.IsBusy && groups.RoleOptions.Count == 3);

        // ohne Rolle bzw. mit ungültigem Namen: Hinweis, nichts gesendet
        await groups.SaveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("MinecraftGroups_PickRole"), feedback.Message);
        groups.SelectedRole = groups.RoleOptions.Single(o => o.Label == "Baumeister");
        groups.NewGroup = "Rog Builder!";
        await groups.SaveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("MinecraftGroups_Format"), feedback.Message);
        Assert.Empty(t.Api.Writes);

        groups.NewGroup = "  ROG-Builder ";
        groups.NewPriority = 55;
        await groups.SaveCommand.ExecuteAsync(null);
        var body = LastBody(t, "PUT", "/v1/minecraft/group-mappings/" + FakeMinecraftAdminRoutes.RoleBuilder);
        Assert.Equal("rog-builder", body["group"]!.GetValue<string>());
        Assert.Equal(55, body["priority"]!.GetValue<int>());
        Assert.True(feedback.IsSuccess);
        Assert.Null(groups.SelectedRole);
        Assert.Equal(string.Empty, groups.NewGroup);

        // Bearbeiten übernimmt die Zuordnung ins Formular
        groups.Mappings[0].EditCommand.Execute(null);
        Assert.Equal("Moderator", groups.SelectedRole!.Label);
        Assert.Equal("rog-mod", groups.NewGroup);
        Assert.Equal(80, groups.NewPriority);

        // Entfernen und Freigeben nur nach Rückfrage
        var remove = groups.Mappings[1].RemoveCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Contains("„Member“", confirm.Text, StringComparison.Ordinal);
        confirm.AcceptCommand.Execute(null);
        await remove;
        Assert.Equal("DELETE", t.Api.Writes.Last().Method);
        Assert.Equal("/v1/minecraft/group-mappings/" + FakeMinecraftAdminRoutes.RoleMember, t.Api.Writes.Last().Path);

        var release = groups.Retired[0].ReleaseCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        confirm.AcceptCommand.Execute(null);
        await release;
        Assert.Equal("/v1/minecraft/retired-groups/rog-builder-alt", t.Api.Writes.Last().Path);
        Assert.Equal(t.Services.T("MinecraftGroups_Released"), feedback.Message);

        // Server lehnt ab (Rangschutz): übersetzte Meldung
        t.Api.Failures["/v1/minecraft/retired-groups/rog-builder-alt"] = (HttpStatusCode.Forbidden, """{"type":"about:blank","title":"Verboten","status":403,"code":"forbidden"}""");
        release = groups.Retired[0].ReleaseCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        confirm.AcceptCommand.Execute(null);
        await release;
        Assert.True(feedback.IsError);
        Assert.Equal(t.Services.Localizer.GetError("forbidden"), feedback.Message);
    }

    [AvaloniaFact]
    public async Task Coins_tab_lists_wallets_books_with_reason_and_confirmation_and_saves_settings()
    {
        using var t = await SignedInAsync();
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var coins = new CoinsAdminViewModel(t.Services, feedback, confirm);
        await coins.EnsureLoadedAsync();

        Assert.Equal(3, coins.Items.Count);
        Assert.False(coins.IsDetailOpen);
        var wahke = coins.Items[0];
        Assert.Equal("Wahke", wahke.Title);
        Assert.Equal("12.850 Coins", wahke.BalanceText);
        Assert.Contains("Konto", wahke.Meta, StringComparison.Ordinal);
        var guest = coins.Items[2];
        Assert.Equal("Steve_2011", guest.Title);
        Assert.True(guest.HasUuid);
        Assert.False(guest.HasProfile);

        // Einstellungen
        Assert.True(coins.HasSettings);
        Assert.Equal(50, coins.DailyBase);
        Assert.Equal("7: 100, 30: 500, 100: 2000", coins.Milestones);
        coins.Milestones = "7 Tage = viel";
        await coins.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("CoinsAdmin_MilestonesFormat"), feedback.Message);
        Assert.Empty(t.Api.Writes);
        coins.Milestones = "7: 150; 30: 600";
        coins.AdminDailyLimit = 75000;
        await coins.SaveSettingsCommand.ExecuteAsync(null);
        var settings = LastBody(t, "PUT", "/v1/minecraft/economy");
        Assert.Equal(75000, settings["admin_daily_limit"]!.GetValue<int>());
        Assert.Equal(2, settings["daily_milestones"]!.AsArray().Count);
        Assert.Equal("7: 150, 30: 600", coins.Milestones);
        Assert.True(feedback.IsSuccess);

        // Wallet wählen: Buchungen
        coins.Selected = wahke;
        Assert.True(coins.IsDetailOpen);
        await WaitUntil(() => coins.Transactions.Count == 3);
        Assert.Equal("+500", coins.Transactions[0].Amount);
        Assert.Contains("Gewinn Bau-Wettbewerb Oktober", coins.Transactions[0].Meta, StringComparison.Ordinal);

        // Buchung: Betrag und Grund Pflicht
        await coins.AdjustCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("CoinsAdmin_AmountRange"), feedback.Message);
        coins.Amount = 250;
        coins.Reason = "ok";
        await coins.AdjustCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("CoinsAdmin_ReasonRequired"), feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/adjustments", StringComparison.Ordinal));

        coins.Reason = "Gewinn Kürbis-Event";
        var adjust = coins.AdjustCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Contains("Gutschrift über 250 Coins für Wahke", confirm.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("⛃", confirm.Text, StringComparison.Ordinal);
        confirm.AcceptCommand.Execute(null);
        await adjust;
        var booking = LastBody(t, "POST", "/v1/minecraft/wallets/" + FakeMinecraftAdminRoutes.WalletWahke);
        Assert.Equal("credit", booking["direction"]!.GetValue<string>());
        Assert.Equal(250, booking["amount"]!.GetValue<int>());
        Assert.Equal("Gewinn Kürbis-Event", booking["reason"]!.GetValue<string>());
        Assert.Equal(13100, wahke.Balance);
        Assert.Equal("13.100 Coins", wahke.BalanceText);
        Assert.Null(coins.Amount);
        Assert.True(feedback.IsSuccess);
        Assert.Contains("13.100 Coins", feedback.Message, StringComparison.Ordinal);

        // Escape schließt die Wallet; Filter Art lädt neu
        Assert.True(coins.CloseOverlay());
        Assert.False(coins.IsDetailOpen);
        coins.SelectedKind = coins.KindOptions.Single(o => o.Value == "guest");
        await WaitUntil(() => !coins.IsBusy);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query.Contains("filter%5Bkind%5D=guest", StringComparison.Ordinal));

        // Schmal: Einstellungen öffnen das Detail ohne Wallet
        coins.OpenSettingsCommand.Execute(null);
        Assert.True(coins.IsDetailOpen);
        coins.CloseDetailCommand.Execute(null);
        Assert.False(coins.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Network_tab_shows_heartbeat_saves_roles_checks_release_deploys_and_manages_pool()
    {
        using var t = await SignedInAsync();
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var ops = new MinecraftOpsViewModel(t.Services, feedback, confirm) { ReleasePoll = TimeSpan.Zero, DeployPoll = TimeSpan.Zero };
        await ops.EnsureLoadedAsync();

        Assert.Equal(5, ops.Servers.Count);
        var freebuild = ops.Servers.Single(s => s.Name == "RoG Freebuild");
        Assert.Contains("TPS 18,7", freebuild.StateText, StringComparison.Ordinal);
        Assert.Contains("rog 2.7.1", freebuild.StateText, StringComparison.Ordinal);
        Assert.True(freebuild.HasWarning); // Heartbeat meldet „plots“, festgelegt ist „freebuild“
        Assert.Equal(t.Services.T("Network_Mismatch"), freebuild.WarningText);
        var test = ops.Servers.Single(s => s.Name == "BedWars 3 (Test)");
        Assert.False(test.CanSelect);
        Assert.Equal(t.Services.T("Network_NoHeartbeat"), test.StateText);
        Assert.Equal(t.Services.T("Network_NotLinked"), test.WarningText);

        // Rolle Minigame braucht einen gültigen Ordner
        freebuild.SelectedRole = ops.RoleOptions.Single(o => o.Value == "minigame");
        Assert.True(freebuild.ShowFolder);
        freebuild.Folder = "Bed Wars";
        await freebuild.SaveRoleCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Network_FolderFormat"), feedback.Message);
        freebuild.SelectedRole = ops.RoleOptions.Single(o => o.Value == "plots");
        await freebuild.SaveRoleCommand.ExecuteAsync(null);
        var role = LastBody(t, "PUT", "/v1/minecraft/network/servers/" + FakeMinecraftAdminRoutes.ServerFreebuild);
        Assert.Equal("plots", role["role"]!.GetValue<string>());
        Assert.True(feedback.IsSuccess);
        Assert.Contains("plots", ops.Servers.Single(s => s.Name == "RoG Freebuild").DeployText, StringComparison.OrdinalIgnoreCase);

        // Deploy: erst prüfen (wartet auf „ready“), dann verteilen an die Auswahl
        Assert.False(ops.StartDeployCommand.CanExecute(null));
        await ops.CheckReleaseCommand.ExecuteAsync(null);
        Assert.True(ops.HasRelease);
        Assert.Contains("v2.8.0", ops.ReleaseText, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("Release_Signed"), ops.ReleaseText, StringComparison.Ordinal);
        Assert.Contains("bedwars: rog-core-2.8.0.jar, rog-bedwars-1.4.0.jar", ops.ReleaseFiles, StringComparison.Ordinal);
        Assert.True(ops.CanStartDeploy);
        ops.Servers.Single(s => s.Name == "RoG Lobby").IsSelected = true;
        ops.Restart = false;
        var deploy = ops.StartDeployCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Contains("v2.8.0", confirm.Text, StringComparison.Ordinal);
        Assert.Contains("RoG Lobby", confirm.Text, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("Deploy_WithoutRestart"), confirm.Text, StringComparison.Ordinal);
        confirm.AcceptCommand.Execute(null);
        await deploy;
        var started = LastBody(t, "POST", "/v1/minecraft/plugin-deploys");
        Assert.True(started["confirm"]!.GetValue<bool>());
        Assert.False(started["restart"]!.GetValue<bool>());
        Assert.Equal(FakeMinecraftAdminRoutes.ServerLobby, started["servers"]![0]!.GetValue<string>());
        Assert.Equal(2, ops.Deploys.Count);
        Assert.StartsWith("v2.8.0 · " + t.Services.T("Deploy_Status_done"), ops.Deploys[0].Title, StringComparison.Ordinal);
        Assert.Contains(ops.Deploys[0].Targets, l => l.Contains(t.Services.T("Deploy_Message_role_mismatch"), StringComparison.Ordinal));
        Assert.True(feedback.IsSuccess);

        // Pool: Gruppen, Instanzen, Vorschläge, Protokoll
        Assert.Equal(2, ops.Groups.Count);
        var bedwars = ops.Groups[0];
        Assert.Equal("2 laufen · 1 joinbar · 5 Spieler", bedwars.Summary);
        Assert.True(bedwars.HasLastAction);
        Assert.Contains(t.Services.T("Pool_Backoff"), bedwars.Instances[1].StatusText, StringComparison.Ordinal);
        Assert.True(bedwars.Instances[1].HasFailures);
        Assert.Equal(3, ops.PoolLog.Count);
        Assert.Contains(t.Services.T("Pool_ActionFailed") + ": Pterodactyl: 504", ops.PoolLog[1], StringComparison.Ordinal);
        Assert.Contains(t.Services.T("Pool_NotConfigured"), ops.Groups[1].Summary, StringComparison.Ordinal);

        await bedwars.Suggestions[0].AssignCommand.ExecuteAsync(null);
        var pool = LastBody(t, "PUT", "/v1/minecraft/pool/bedwars");
        Assert.Equal(3, pool["servers"]!.AsArray().Count);
        Assert.Equal(1, pool["min_running"]!.GetValue<int>());

        bedwars = ops.Groups[0];
        bedwars.Instances[0].Excluded = true; // sofort gespeichert
        await WaitUntil(() => t.Api.Writes.Any(w => w.Method == "PATCH"));
        Assert.True(LastBody(t, "PATCH", "/v1/minecraft/pool/bedwars/servers/" + FakeMinecraftAdminRoutes.ServerBedwars1)["excluded"]!.GetValue<bool>());

        await WaitUntil(() => !ReferenceEquals(ops.Groups[0], bedwars));
        var delete = ops.Groups[0].DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => confirm.IsOpen);
        Assert.Contains("„bedwars“", confirm.Text, StringComparison.Ordinal);
        Assert.True(ops.CloseOverlay());
        await delete;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");
    }

    [AvaloniaFact]
    public async Task Join_editor_loads_on_expand_validates_saves_and_manages_the_resource_pack()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/minecraft/join"] = FakeMinecraftAdminRoutes.JoinForManagers;
        var feedback = new FeedbackViewModel();
        var join = new MinecraftJoinViewModel(t.Services, feedback);
        await join.LoadAsync();
        Assert.True(join.CanEdit);
        Assert.True(join.ShowPackCard);
        Assert.Equal("3f2a9c0e5b7d1a4c8e6f0b2d4a6c8e0f1a3b5c7d", join.PackSha1);
        Assert.False(join.CanSave);

        join.IsEditExpanded = true;
        await WaitUntil(() => join.CanSave);
        Assert.Equal("mc.example.test", join.EditJavaAddress);
        Assert.Equal(25565, join.EditJavaPort);
        Assert.True(join.EditConsoleEnabled);
        Assert.Equal("RoG6328", join.EditGamertag);
        Assert.Equal("Console joining may briefly fail at weekends.", join.EditHintEn);
        t.Api.Overrides.Remove("/v1/minecraft/join"); // Overrides gelten je Pfad für alle Methoden – PUT beantwortet die Fake-Route

        // Prüfungen vor dem Senden
        join.EditJavaAddress = "mc example";
        await join.SaveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("MinecraftJoin_InvalidAddress"), feedback.Message);
        join.EditJavaAddress = "play.rogclan.de";
        join.EditBedrockPort = null;
        await join.SaveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("MinecraftJoin_InvalidPort"), feedback.Message);
        join.EditBedrockPort = 19133;
        join.EditGamertag = "#nope";
        await join.SaveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("MinecraftJoin_InvalidGamertag"), feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/minecraft/join");

        join.EditGamertag = "RoG6328";
        join.EditJavaPort = 25570;
        join.EditHintFr = "La connexion console peut échouer le week-end.";
        await join.SaveCommand.ExecuteAsync(null);
        var sent = LastBody(t, "PUT", "/v1/minecraft/join");
        Assert.Equal("play.rogclan.de", sent["java_address"]!.GetValue<string>());
        Assert.Equal(25570, sent["java_port"]!.GetValue<int>());
        Assert.Equal(19133, sent["bedrock_port"]!.GetValue<int>());
        Assert.Equal("La connexion console peut échouer le week-end.", sent["console_hint"]!["fr"]!.GetValue<string>());
        Assert.Equal(t.Services.T("MinecraftJoin_Saved"), feedback.Message);
        Assert.Equal("play.rogclan.de:25570", join.JavaAddress); // Anleitung zeigt sofort den neuen Stand
        Assert.False(join.ShowDefaultPortNote);

        // SHA-1 kopieren
        join.CopyPackSha1Command.Execute(null);
        Assert.Equal("3f2a9c0e5b7d1a4c8e6f0b2d4a6c8e0f1a3b5c7d", t.Clipboard.Text);

        // Hochladen: nur .zip bis 50 MB, dann multipart an die Plattform
        await join.UploadPackAsync(new UploadSource("pack.rar", 1024, () => Task.FromResult<Stream>(new MemoryStream([1]))));
        Assert.Equal(t.Services.T("ResourcePack_Invalid"), feedback.Message);
        await join.UploadPackAsync(new UploadSource("riesig.zip", (50L * 1024 * 1024) + 1, () => Task.FromResult<Stream>(new MemoryStream([1]))));
        Assert.Equal(t.Services.T("ResourcePack_Invalid"), feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/minecraft/resource-pack");
        await join.UploadPackAsync(new UploadSource("RoG-Pack-2026.ZIP", 9437184, () => Task.FromResult<Stream>(new MemoryStream("PK\u0003\u0004"u8.ToArray()))));
        var upload = t.Api.Requests.Last(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/v1/minecraft/resource-pack");
        Assert.IsType<MultipartFormDataContent>(upload.Content);
        Assert.Equal("7c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d", join.PackSha1);
        Assert.Equal(t.Services.T("ResourcePack_Uploaded"), feedback.Message);

        // Löschen nach Rückfrage (Escape bricht ab)
        var delete = join.DeletePackCommand.ExecuteAsync(null);
        await WaitUntil(() => join.Confirm.IsOpen);
        Assert.True(join.CloseOverlay());
        await delete;
        Assert.True(join.HasPack);
        delete = join.DeletePackCommand.ExecuteAsync(null);
        await WaitUntil(() => join.Confirm.IsOpen);
        join.Confirm.AcceptCommand.Execute(null);
        await delete;
        Assert.Equal("DELETE", t.Api.Writes.Last().Method);
        Assert.False(join.HasPack);
        Assert.True(join.ShowPackCard); // Verwaltende sehen die Karte weiter (zum Hochladen)
        Assert.Equal(t.Services.T("ResourcePack_None"), join.PackInfo);
    }

    [AvaloniaFact]
    public async Task Map_loads_worlds_warps_and_live_players_follows_links_and_manages_worlds()
    {
        using var t = await SignedInAsync();
        var map = new MinecraftMapViewModel(t.Services);
        await map.LoadAsync();

        Assert.True(map.ShowContent);
        Assert.Equal(["RoG Freebuild · Freebuild", "RoG Freebuild · Nether", "RoG Plots · Plotwelt"], map.Worlds.Select(w => w.Label));
        Assert.Equal("RoG Freebuild · Freebuild", map.SelectedWorld!.Label);
        Assert.Equal(["Arena", "Farmen", "Hafen", "Rathaus", "Shop"], map.Warps.Select(w => w.Label));
        Assert.Equal("x 120 · z -40", map.Warps.Single(w => w.Label == "Shop").Detail);
        Assert.StartsWith("1342 Kacheln · Oberwelt · Stand", map.WorldInfo, StringComparison.Ordinal);
        Assert.True(map.LiveAvailable);
        Assert.Equal(["Wahke", "BlockMeister · Bedrock"], map.Players.Select(p => p.Label)); // Nether-Spieler nicht hier
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query == "?server=freebuild-1");
        Assert.Null(map.LiveInfo);

        // Kachel-Index je Region
        var tiles = await map.LoadTilesAsync(FakeMinecraftAdminRoutes.WorldFreebuild, 0, -1, 0);
        Assert.Equal(9, tiles.Count);
        Assert.All(tiles, tile => Assert.True(tile.X < 0 && tile.Z >= 0));

        // Welt wechseln: Nether ohne Kacheln, Live-Schalter aus → keine Spieler, keine Abfrage
        MapWorldChange? changed = null;
        map.WorldChanged += (_, e) => changed = e;
        map.SelectedWorld = map.Worlds[1];
        Assert.True(map.HasNoTiles);
        Assert.Equal(FakeMinecraftAdminRoutes.WorldNether, changed!.World!.Id);
        await WaitUntil(() => map.Players.Count == 1);
        Assert.Equal("Redstoner", map.Players[0].Label);
        var liveRequests = t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/minecraft/map/live");
        map.ShowLive = false;
        await map.RefreshLiveAsync();
        Assert.Empty(map.Players);
        Assert.Null(map.LiveInfo);
        Assert.Equal(liveRequests, t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/minecraft/map/live"));
        map.ShowLive = true;

        // Link aus dem Spiel: Server/Welt wählen und zentrieren
        map.Open("server=plots-1&world=plotworld&x=200&z=-75");
        await WaitUntil(() => map.SelectedWorld?.World.World == "plotworld" && !map.IsLoading);
        Assert.Equal(200, changed!.X);
        Assert.Equal(-75, changed.Z);

        // Liste → Karte zentrieren
        MapPointItem? centered = null;
        map.CenterRequested += (_, p) => centered = p;
        map.CenterOnCommand.Execute(map.Warps[0]);
        Assert.Equal("Plot-Spawn", centered!.Label);

        // Verwaltung: Live-Modus, Speicher, Welt ausblenden/benennen, Kacheln löschen nach Rückfrage
        Assert.True(map.CanManage);
        Assert.Equal("public", map.SelectedLiveMode!.Value);
        Assert.Contains("1,2 GB von 50 GB", map.QuotaText, StringComparison.Ordinal);
        map.SelectedLiveMode = map.LiveModeOptions.Single(o => o.Value == "auth");
        await WaitUntil(() => t.Api.Writes.Any(w => w.Path == "/v1/minecraft/map/settings"));
        Assert.Equal("auth", LastBody(t, "PATCH", "/v1/minecraft/map/settings")["live_players"]!.GetValue<string>());
        await WaitUntil(() => map.Feedback.IsSuccess && !map.IsLoading);

        map.EditWorldHidden = true;
        map.EditNameEn = "Plot world";
        await map.SaveWorldCommand.ExecuteAsync(null);
        var world = LastBody(t, "PATCH", "/v1/minecraft/map/worlds/" + FakeMinecraftAdminRoutes.WorldPlots);
        Assert.True(world["hidden"]!.GetValue<bool>());
        Assert.Equal("Plot world", world["names"]!["en"]!.GetValue<string>());
        Assert.Equal(t.Services.T("MinecraftMap_Saved"), map.Feedback.Message);

        var reset = false;
        map.TilesReset += (_, _) => reset = true;
        var clear = map.ClearTilesCommand.ExecuteAsync(null);
        await WaitUntil(() => map.Confirm.IsOpen);
        Assert.Contains("RoG Plots · Plotwelt", map.Confirm.Text, StringComparison.Ordinal);
        map.Confirm.AcceptCommand.Execute(null);
        await clear;
        Assert.Equal("/v1/minecraft/map/worlds/" + FakeMinecraftAdminRoutes.WorldPlots + "/tiles", t.Api.Writes.Last().Path);
        Assert.True(reset);
    }

    [AvaloniaFact]
    public async Task Map_for_guests_hides_admin_and_explains_live_players()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.Overrides["/v1/minecraft/map"] = FakeMinecraftAdminRoutes.Map(admin: false, liveMode: "auth", live: false);
        var map = new MinecraftMapViewModel(t.Services);
        await map.LoadAsync();
        Assert.False(map.CanManage);
        Assert.False(map.LiveAvailable);
        Assert.Empty(map.Players);
        Assert.Equal(t.Services.T("MinecraftMap_LiveSignIn"), map.LiveInfo);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/minecraft/map/live");
        Assert.Null(map.QuotaText);

        // Leer und Fehler
        t.Api.Overrides["/v1/minecraft/map"] = """{"data":{"live_players":false,"live_players_mode":"off","servers":[]}}""";
        await map.RefreshAsync();
        Assert.True(map.IsEmpty);
        t.Api.FailWith = HttpStatusCode.InternalServerError;
        await map.LoadAsync();
        Assert.True(map.HasError);
    }

    [AvaloniaFact]
    public async Task Map_is_native_for_rog_links_and_the_join_button()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://map?server=freebuild-1&x=310&z=250")));
        var map = Assert.IsType<MinecraftMapViewModel>(shell.CurrentPage);
        await WaitUntil(() => map.ShowContent);
        Assert.Equal("Freebuild", map.SelectedWorld!.World.Name);
        Assert.Empty(t.Browser.Opened);
        Assert.True(t.Services.IsNative!(new AppRoute(AppSection.MinecraftMap)));

        shell.NavigateTo("Nav_Servers");
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        servers.Join.OpenMapCommand.Execute(null);
        Assert.Same(map, shell.CurrentPage);
        Assert.Empty(t.Browser.Opened);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Map_control_loads_only_visible_regions_and_platform_tiles()
    {
        using var t = await SignedInAsync();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme
        var tilesHandler = new FakeMinecraftTiles();
        var map = new MinecraftMapViewModel(t.Services) { Tiles = new MapTileCache(new ImageLoader(tilesHandler), MinecraftMapViewModel.TileCacheCapacity) };
        var view = new RoG.Linux.App.Views.MinecraftMapView { DataContext = map };
        var window = new Avalonia.Controls.Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        try
        {
            await map.LoadAsync();
            await WaitUntil(() => view.MapControl.DrawnTiles >= 4, 8000);
            var control = view.MapControl;
            Assert.InRange(control.LoadedRegions, 1, 4);
            Assert.All(tilesHandler.Requests, u => Assert.Equal("cdn.example.test", u.Host));
            Assert.Contains(tilesHandler.Requests, u => u.AbsolutePath.Contains("/heads/", StringComparison.Ordinal));

            // Zoom und Spawn per Tastatur
            var before = control.Viewport.PixelsPerBlock;
            control.ZoomAt(1.5, null);
            Assert.True(control.Viewport.PixelsPerBlock > before);
            control.CenterOn(500, 500);
            map.CenterOnCommand.Execute(map.Warps.Single(w => w.Label == "Shop"));
            Assert.Equal(120.5, control.Viewport.CenterX);
            Assert.Equal(-39.5, control.Viewport.CenterZ);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }
}
