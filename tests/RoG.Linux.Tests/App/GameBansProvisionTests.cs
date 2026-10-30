using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using RoG.Core.Features.Servers;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 7 „G“: Reiter Spiel-Bans (Bans + Plugin-Schlüssel), öffentliche Ban-Liste, Gameserver einrichten.</summary>
public class GameBansProvisionTests
{
    internal static readonly ServerAbilities BanAbilities = new(Power: true, Console: true, ConsoleCommand: true, CanCommand: true, Bans: true);

    private static async Task<TestServices> SignedInAsync(string me)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = me;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static (string Method, string Path, JsonNode Body) LastWrite(TestServices t, string path)
    {
        var w = t.Api.Writes.Last(w => w.Path == path);
        return (w.Method, w.Path, w.Body.Length == 0 ? new JsonObject() : JsonNode.Parse(w.Body)!);
    }

    [AvaloniaFact]
    public async Task Manager_sees_bans_and_plugin_key_and_can_create_edit_lift_and_toggle()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeManager);
        var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities);
        Assert.True(vm.IsAvailable);
        Assert.True(vm.IsManager);
        Assert.Contains(vm.ScopeOptions, o => o.Value == "global");
        Assert.Equal("game", vm.SelectedScope.Value); // Manager: Standard Spiel
        Assert.Equal(3, vm.MaxScopeOptions.Count);

        await vm.EnsureLoadedAsync();
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.HasMore);
        Assert.False(vm.IsEmpty);
        Assert.Null(vm.ListError);
        var first = vm.Items[0];
        Assert.Equal("^1Sn1per^7King", first.Title);
        Assert.Equal("guid: 4A1F9C2B7D3E8F6A0B5C4D3E2F1A9B8C", first.Identity);
        Assert.Contains("Wolfenstein: Enemy Territory", first.Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.T(RoG.Core.Localization.StringKeys.BanPermanent), first.Meta, StringComparison.Ordinal);
        Assert.Contains("K7P2-QX9M", first.PublicInfo, StringComparison.Ordinal);
        Assert.True(first.HasPublicUrl);
        var second = vm.Items[1];
        Assert.Contains("Nyx", second.Meta, StringComparison.Ordinal); // ingame gebannt von Nyx
        Assert.Contains(t.Services.T("GameBans_HiddenBadge"), second.PublicInfo, StringComparison.Ordinal);
        Assert.False(second.HasPublicUrl);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query.Contains("filter%5Bserver%5D=01SV1", StringComparison.Ordinal));

        // Plugin-Schlüssel
        Assert.True(vm.HasKeySection);
        Assert.True(vm.KeyExists);
        Assert.Equal(t.Services.T("GameBans_PluginActive"), vm.KeyStatus);
        Assert.Contains("rog-etl-bans 1.4.2", vm.KeyDetails, StringComparison.Ordinal);
        Assert.Contains("ET: Legacy 2.83", vm.KeyDetails, StringComparison.Ordinal);
        Assert.True(vm.MayLift);
        Assert.Equal(t.Services.T("GameBans_KeyRotate"), vm.KeyActionText);

        // Weitere laden
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Items.Count);
        Assert.False(vm.HasMore);

        // Neuer Ban: Prüfung vor dem Senden
        var writes = t.Api.Writes.Count;
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.Equal(writes, t.Api.Writes.Count);
        Assert.True(vm.Feedback.IsError);
        Assert.Contains(t.Services.T("GameBans_Rule_Identifier"), vm.Feedback.Message, StringComparison.Ordinal);

        vm.Identifier = "STEAM_0:0:11223344";
        vm.IdentifierType = "steamid";
        vm.PlayerName = "Rusher";
        vm.Reason = "Cheat-Verdacht";
        vm.Minutes = 60;
        vm.Ip = "198.51.100.7";
        await vm.CreateCommand.ExecuteAsync(null);
        var created = LastWrite(t, "/v1/game-bans");
        Assert.Equal("POST", created.Method);
        Assert.Equal("game", (string?)created.Body["scope"]);
        Assert.Equal("01SV1", (string?)created.Body["server_id"]);
        Assert.Equal(60, (int?)created.Body["minutes"]);
        Assert.Equal("198.51.100.7", (string?)created.Body["ip"]);
        Assert.True(vm.Feedback.IsSuccess);
        Assert.Equal(string.Empty, vm.Identifier);

        // Dauer ändern: Rückfrage im Inhalt, 0 = dauerhaft, Grund unverändert → nicht gesendet
        await ViewModelTests.WaitUntil(() => vm.Items.Count == 2 && !vm.IsBusy);
        var temp = vm.Items[1];
        var edit = temp.EditCommand.ExecuteAsync(null);
        Assert.True(vm.Prompt.IsOpen);
        Assert.True(vm.Prompt.AsksDuration);
        Assert.Equal("Spawnkill trotz Verwarnung", vm.Prompt.Reason);
        vm.Prompt.Minutes = 0;
        vm.Prompt.AcceptCommand.Execute(null);
        await edit;
        var patched = LastWrite(t, "/v1/game-bans/" + FakeGameBansRoutes.TempBan);
        Assert.Equal(0, (int?)patched.Body["minutes"]);
        Assert.Null(patched.Body["reason"]);
        Assert.Equal(t.Services.T("GameBans_Updated"), vm.Feedback.Message);

        // Öffentlich zeigen/ausblenden
        await vm.Items[1].TogglePublicCommand.ExecuteAsync(null);
        Assert.False((bool?)LastWrite(t, "/v1/game-bans/" + FakeGameBansRoutes.TempBan).Body["is_public"]);
        Assert.Equal(t.Services.T("GameBans_Hidden"), vm.Feedback.Message);

        // RCON-Zustellungen
        await vm.Items[0].DetailsCommand.ExecuteAsync(null);
        Assert.True(vm.Items[0].HasDeliveries);
        Assert.Contains("RCON-Timeout", vm.Items[0].Deliveries, StringComparison.Ordinal);

        // Aufheben: abbrechen sendet nichts, bestätigen hebt mit Grund auf und nimmt den Ban aus „aktiv“
        var lift = vm.Items[0].LiftCommand.ExecuteAsync(null);
        Assert.True(vm.Prompt.IsOpen);
        Assert.False(vm.Prompt.AsksDuration);
        Assert.True(vm.CloseOverlay());
        await lift;
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/lift", StringComparison.Ordinal) && w.Path.StartsWith("/v1/game-bans/", StringComparison.Ordinal));
        lift = vm.Items[0].LiftCommand.ExecuteAsync(null);
        vm.Prompt.Reason = "Demo geprüft, Fehlalarm";
        vm.Prompt.AcceptCommand.Execute(null);
        await lift;
        Assert.Equal("Demo geprüft, Fehlalarm", (string?)LastWrite(t, "/v1/game-bans/" + FakeGameBansRoutes.ActiveBan + "/lift").Body["reason"]);
        Assert.Single(vm.Items);
        Assert.Equal(t.Services.T("GameBans_Lifted"), vm.Feedback.Message);

        // Öffentliche Seite eines sichtbaren Bans: ohne Shell (kein Navigator) im Browser
        await vm.Items[0].TogglePublicCommand.ExecuteAsync(null);
        Assert.True(vm.Items[0].HasPublicUrl);
        vm.Items[0].OpenPublicCommand.Execute(null);
        Assert.EndsWith("/de/bans/" + FakeGameBansRoutes.TempBan, t.Browser.Opened[^1].ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Plugin_key_rotate_shows_the_token_once_and_revoke_and_incident_ask_first()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeManager);
        var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities);
        await vm.EnsureLoadedAsync();

        // Rotieren fragt nach, zeigt den Schlüssel einmal, Kopieren in die Zwischenablage, „Fertig“ vergisst ihn
        var rotate = vm.CreateKeyCommand.ExecuteAsync(null);
        Assert.True(vm.Confirmation.IsOpen);
        Assert.Equal(t.Services.T("GameBans_KeyRotate"), vm.Confirmation.Title);
        vm.Confirmation.AcceptCommand.Execute(null);
        await ViewModelTests.WaitUntil(() => vm.Token.IsOpen);
        Assert.Equal(FakeGameBansRoutes.PluginToken, vm.Token.Value);
        var sent = LastWrite(t, "/v1/servers/et-sniper/plugin-key");
        Assert.Equal("POST", sent.Method);
        vm.Token.CopyTokenCommand.Execute(null);
        Assert.Equal(FakeGameBansRoutes.PluginToken, t.Clipboard.Text);
        Assert.Equal(t.Services.T("GameBans_TokenCopied"), vm.Feedback.Message);
        Assert.True(vm.CloseOverlay());
        await rotate;
        Assert.False(vm.Token.IsOpen);
        Assert.Equal(string.Empty, vm.Token.Value);

        // Widerrufen: abbrechen → kein DELETE
        var revoke = vm.RevokeKeyCommand.ExecuteAsync(null);
        Assert.True(vm.Confirmation.IsOpen);
        vm.Confirmation.Cancel();
        await revoke;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");
        revoke = vm.RevokeKeyCommand.ExecuteAsync(null);
        vm.Confirmation.AcceptCommand.Execute(null);
        await revoke;
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/servers/et-sniper/plugin-key");
        Assert.Equal(t.Services.T("GameBans_KeyRevokedDone"), vm.Feedback.Message);

        // Rechte speichern
        vm.MayIp = true;
        await vm.SaveRightsCommand.ExecuteAsync(null);
        Assert.True((bool?)LastWrite(t, "/v1/servers/et-sniper/plugin-key").Body["may_ip"]);

        // Vorfall: alle Plugin-Bans aufheben
        vm.IncidentReason = "Schlüssel geleakt";
        var incident = vm.LiftPluginBansCommand.ExecuteAsync(null);
        vm.Confirmation.AcceptCommand.Execute(null);
        await incident;
        Assert.Equal("Schlüssel geleakt", (string?)LastWrite(t, "/v1/servers/et-sniper/game-bans/lift").Body["reason"]);
        Assert.Equal(t.Services.Format("GameBans_IncidentDone", 3), vm.Feedback.Message);
        Assert.Equal(string.Empty, vm.IncidentReason);
    }

    [AvaloniaFact]
    public async Task Server_admin_without_gamebans_manage_is_limited_and_window_feedback_is_used()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeServerAdmin);
        var shown = new List<(string Text, MessageTone Tone)>();
        var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities, (m, tone) => shown.Add((m, tone)));
        Assert.False(vm.HasOwnFeedback);
        Assert.False(vm.IsManager);
        Assert.True(vm.CanBans);
        Assert.DoesNotContain(vm.ScopeOptions, o => o.Value == "global");
        Assert.Equal("server", vm.SelectedScope.Value);
        Assert.Single(vm.MaxScopeOptions);

        await vm.EnsureLoadedAsync();
        vm.MayLift = true;
        await vm.SaveRightsCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("GameBans_RightsNeedManage"), shown[^1].Text);
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PATCH");

        // IP nur für Manager
        vm.Identifier = "abcd1234";
        vm.Reason = "Test";
        vm.Ip = "198.51.100.7";
        await vm.CreateCommand.ExecuteAsync(null);
        var body = JsonNode.Parse(t.Api.Writes.Last(w => w.Path == "/v1/game-bans").Body)!;
        Assert.Null(body["ip"]);
        Assert.Equal("server", (string?)body["scope"]);
    }

    [AvaloniaFact]
    public async Task Tab_is_hidden_without_scopes_and_key_section_without_servers_manage()
    {
        using (var t = await SignedInAsync(FakeData.Me)) // nur servers:read
        {
            var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities);
            Assert.False(vm.IsAvailable);
        }

        using (var t = await SignedInAsync(FakeGameBansRoutes.MeServerAdmin))
        {
            t.Api.Failures["/v1/servers/et-sniper/plugin-key"] = (HttpStatusCode.Forbidden, FakeAccountRoutes.Problem(403, "forbidden"));
            t.Api.Failures["/v1/game-bans"] = (HttpStatusCode.InternalServerError, FakeAccountRoutes.Problem(500, "server_error"));
            var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities with { Bans = true });
            await vm.EnsureLoadedAsync();
            Assert.False(vm.HasKeySection);
            Assert.NotNull(vm.ListError);
            Assert.False(vm.Feedback.IsOpen); // Listenfehler steht in der Liste, nicht als Meldung

            // leere Suche
            t.Api.Failures.Remove("/v1/game-bans");
            vm.Search = "leer";
            await vm.SearchCommand.ExecuteAsync(null);
            Assert.Null(vm.ListError);
            Assert.True(vm.IsEmpty);
        }

        // ohne Ban-Recht am Server, aber mit Scope: nur der Schlüssel-Bereich (Recht prüft der Server)
        using (var t = await SignedInAsync(FakeGameBansRoutes.MeServerAdmin))
        {
            var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, BanAbilities with { Bans = false });
            Assert.False(vm.CanBans);
            Assert.True(vm.IsAvailable);
        }
    }

    [AvaloniaFact]
    public async Task Servers_page_offers_provisioning_only_with_right_and_scope_and_shows_ports()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeManager);
        t.Api.Overrides["/v1/servers"] = FakeGameBansRoutes.ServersWithProvisioning;
        var servers = new ServersViewModel(t.Services, () => { });
        await servers.LoadAsync();
        servers.Pause();
        Assert.True(servers.CanProvision);
        var card = servers.Servers[1];
        Assert.True(card.HasProvisioning);
        Assert.StartsWith(t.Services.T("Provision_Status_installing"), card.ProvisioningText, StringComparison.Ordinal);
        Assert.Contains("2026", card.ProvisioningText, StringComparison.Ordinal);
        Assert.Equal(3, card.Ports.Count);
        Assert.Equal("cs.rogclan.de:27025 · " + t.Services.T("Servers_PrimaryPort"), card.Ports[0]);
        Assert.Equal(t.Services.Format("Servers_PortsHeader", 3), card.PortsHeader);
        Assert.True(card.CanManageProvisioning);
        Assert.False(servers.Servers[0].CanManageProvisioning);
        Assert.False(servers.Servers[0].HasPorts);

        var requests = new List<ServerProvisionRequest>();
        servers.ProvisionRequested += (_, r) => requests.Add(r);
        card.ManageCommand.Execute(null);
        Assert.Equal(FakeGameBansRoutes.ProvisionedSlug, requests[^1].Key);
        var manage = requests[^1].Create();
        Assert.True(manage.IsManaging);
        Assert.Equal("RoG CS2: Scrim", manage.ServerName);
        Assert.Equal(t.Services.T("Provision_ManageTitle"), manage.Title);
        servers.ProvisionCommand.Execute(null);
        Assert.Equal(string.Empty, requests[^1].Key);
        Assert.False(requests[^1].Create().IsManaging);

        // Ohne servers.create: kein Einstieg
        using var t2 = await SignedInAsync(FakeData.Me);
        t2.Api.Overrides["/v1/servers"] = FakeGameBansRoutes.ServersWithProvisioning;
        var plain = new ServersViewModel(t2.Services, () => { });
        await plain.LoadAsync();
        plain.Pause();
        Assert.False(plain.CanProvision);
        Assert.False(plain.Servers[1].CanManageProvisioning);
        Assert.True(plain.Servers[1].HasProvisioning); // Hinweis bleibt (liefert die API nur Berechtigten)
    }

    [AvaloniaFact]
    public async Task Provisioning_creates_a_server_and_manages_expiry_and_deletion()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeManager);
        var vm = new ServerProvisionViewModel(t.Services, null);
        var changed = 0;
        vm.Changed += (_, _) => changed++;
        Assert.Equal(t.Services.T("Provision_Title"), vm.Title);
        await vm.LoadAsync();
        Assert.True(vm.ShowCreate);
        Assert.Null(vm.LoadError);
        Assert.Single(vm.Nodes);
        Assert.Contains("Falkenstein (fsn1)", vm.Nodes[0].Label, StringComparison.Ordinal);
        Assert.Equal(["Source Engine · Counter-Strike 2", "Wolfenstein · ET: Legacy"], vm.Eggs.Select(e => e.Label));
        Assert.True(vm.HasNodeInfo);
        Assert.Contains("node1.rognode.de", vm.NodeInfo, StringComparison.Ordinal);
        Assert.Equal(5, vm.ExpiryOptions.Count);

        // ohne Egg: Hinweis
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Provision_ChooseNodeEgg"), vm.Feedback.Message);

        vm.SelectedEgg = vm.Eggs[0];
        await ViewModelTests.WaitUntil(() => vm.Fields.Any(f => f.Field.Key == "environment.SRCDS_MAP"));
        Assert.Equal("CS2 Dedicated Server über SteamCMD.", vm.EggInfo);
        var map = vm.Fields.First(f => f.Field.Key == "environment.SRCDS_MAP");
        Assert.True(map.IsText);
        Assert.Equal("de_mirage", map.Text);
        var mode = vm.Fields.First(f => f.Field.Key == "environment.GAME_MODE");
        Assert.True(mode.IsChoice);
        Assert.Equal("competitive", mode.SelectedChoice?.Value);
        Assert.True(vm.Fields.First(f => f.Field.Key == "start_on_completion").IsToggle);
        await ViewModelTests.WaitUntil(() => vm.Fields.First(f => f.Field.Key == "game_id").Choices.Count > 1);

        // Pflichtfeld Name fehlt
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("AdminForm_CheckFields"), vm.Feedback.Message);
        Assert.True(vm.Fields.First(f => f.Field.Key == "name").HasError);

        vm.Fields.First(f => f.Field.Key == "name").Text = "RoG CS2: Scrim";
        vm.Fields.First(f => f.Field.Key == "allocation_id").SelectedChoice = vm.Fields.First(f => f.Field.Key == "allocation_id").Choices.First(c => c.Value == "101");
        var create = vm.CreateCommand.ExecuteAsync(null);
        Assert.True(vm.Confirmation.IsOpen);
        Assert.Contains("rog-node-1", vm.Confirmation.Text, StringComparison.Ordinal);
        vm.Confirmation.AcceptCommand.Execute(null);
        await create;
        var body = JsonNode.Parse(t.Api.Writes.Last(w => w.Path == "/v1/servers/provision").Body)!;
        Assert.Equal(3, (int?)body["node_id"]);
        Assert.Equal(7, (int?)body["egg_id"]);
        Assert.Equal(2, (int?)body["nest_id"]);
        Assert.Equal(101, (int?)body["allocation_id"]);
        Assert.Equal("de_mirage", (string?)body["environment"]?["SRCDS_MAP"]);
        Assert.Equal(1, changed);
        Assert.True(vm.IsManaging);
        Assert.False(vm.ShowCreate);
        Assert.Equal(t.Services.T("Provision_Status_installing"), vm.StatusText);
        Assert.True(vm.HasExpiry);
        Assert.Equal(t.Services.T("Provision_Created"), vm.Feedback.Message);

        // Ablauf und Status
        vm.SelectedExpiry = vm.ExpiryOptions.First(o => o.Value == "14");
        await vm.ExtendExpiryCommand.ExecuteAsync(null);
        Assert.Equal(14, (int?)JsonNode.Parse(t.Api.Writes.Last(w => w.Path == "/v1/servers/rog-cs2-scrim/expiry").Body)!["extend_days"]);
        Assert.Equal(t.Services.T("Provision_ExpirySaved"), vm.Feedback.Message);
        await vm.RefreshStatusCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Provision_Status_ready"), vm.StatusText);

        // Löschen nur mit exakt eingegebenem Namen und nach Rückfrage
        Assert.False(vm.CanDelete);
        vm.ConfirmName = "rog cs2: scrim";
        Assert.False(vm.CanDelete);
        vm.ConfirmName = "RoG CS2: Scrim";
        Assert.True(vm.CanDelete);
        var delete = vm.DeleteCommand.ExecuteAsync(null);
        vm.Confirmation.AcceptCommand.Execute(null);
        await delete;
        var deleted = t.Api.Writes.Last(w => w.Method == "DELETE");
        Assert.Equal("/v1/servers/rog-cs2-scrim/provision", deleted.Path);
        Assert.Equal("RoG CS2: Scrim", (string?)JsonNode.Parse(deleted.Body)!["confirm"]);
        Assert.False(vm.Deletable);
        Assert.Equal(2, changed);
    }

    [AvaloniaFact]
    public async Task Provisioning_shows_panel_rejection_and_catalog_errors()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeManager);
        t.Api.Failures["/v1/servers/provisioning/catalog"] = (HttpStatusCode.ServiceUnavailable, FakeAccountRoutes.Problem(503, "provisioning_not_configured"));
        var vm = new ServerProvisionViewModel(t.Services, null);
        await vm.LoadAsync();
        Assert.False(vm.ShowCreate);
        Assert.NotNull(vm.LoadError);
        Assert.False(vm.IsLoadingCatalog);

        t.Api.Failures.Remove("/v1/servers/provisioning/catalog");
        await vm.RetryCommand.ExecuteAsync(null);
        Assert.True(vm.ShowCreate);
        vm.SelectedEgg = vm.Eggs[1]; // ET: Legacy ohne Variablen
        await ViewModelTests.WaitUntil(() => vm.Fields.Any(f => f.Field.Key == "docker_image"));
        vm.Fields.First(f => f.Field.Key == "name").Text = "RoG ET: Event";
        t.Api.Failures["/v1/servers/provision"] = (HttpStatusCode.UnprocessableEntity,
            FakeAccountRoutes.Problem(422, "provisioning_rejected", "memory", "Node hat nicht genug freien Speicher."));
        vm.Confirm = (_, _) => Task.FromResult(true);
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.True(vm.Feedback.IsError);
        Assert.Equal("Node hat nicht genug freien Speicher.", vm.Feedback.Message);
        Assert.Equal("Node hat nicht genug freien Speicher.", vm.Fields.First(f => f.Field.Key == "memory").Error);
        Assert.False(vm.IsManaging);
    }

    [AvaloniaFact]
    public async Task Public_ban_list_with_detail_and_appeal()
    {
        using var t = await SignedInAsync(FakeGameBansRoutes.MeMember);
        var bans = new BansViewModel(t.Services);
        await bans.LoadAsync();
        Assert.Equal(2, bans.Items.Count);
        Assert.Equal("guid · 4A1F…9B8C", bans.Items[0].Identity);
        Assert.Contains("Wolfenstein: Enemy Territory", bans.Items[0].Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("Bans_Appeal_open"), bans.Items[1].Meta, StringComparison.Ordinal);
        Assert.True(bans.ShowSelectHint);

        bans.Selected = bans.Items[0];
        Assert.True(bans.IsDetailOpen);
        await ViewModelTests.WaitUntil(() => bans.Detail.ShowContent);
        var d = bans.Detail;
        Assert.Equal("^1Sn1per^7King", d.Title);
        Assert.Equal(t.Services.T("Bans_Status_active"), d.StatusText);
        Assert.True(d.CanAppeal);
        Assert.True(d.NeedsCode); // keine Nachweise → Code aus der Kick-Nachricht
        Assert.Equal(t.Services.T("Bans_CodeHint"), d.AppealHint);

        await d.SubmitCommand.ExecuteAsync(null);
        Assert.True(d.Feedback.IsError);
        Assert.Contains(t.Services.T("Bans_Rule_Statement"), d.Feedback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/appeals", StringComparison.Ordinal));

        d.Statement = "Ich habe nicht gecheatet – die Demo zeigt nur gutes Pre-Aiming auf Supply.";
        d.Code = "k7p2 qx9m";
        await d.SubmitCommand.ExecuteAsync(null);
        var sent = JsonNode.Parse(t.Api.Writes.Last(w => w.Path.EndsWith("/appeals", StringComparison.Ordinal)).Body)!;
        Assert.Equal("K7P2QX9M", (string?)sent["code"]);
        Assert.Equal(t.Services.T("Bans_AppealSent"), d.Feedback.Message);
        Assert.Equal(string.Empty, d.Statement);
        Assert.EndsWith("/forum/threads/ban-einspruch-sn1per", t.Browser.Opened[^1].ToString(), StringComparison.Ordinal); // ohne Shell → Browser

        bans.CloseDetail();
        Assert.False(bans.IsDetailOpen);

        // Filter „beendet“: Einspruch nicht möglich
        bans.SelectedStatus = bans.StatusOptions[1];
        await ViewModelTests.WaitUntil(() => !bans.IsLoading && bans.Items.Count == 1);
        bans.Selected = bans.Items[0];
        await ViewModelTests.WaitUntil(() => bans.Detail.ShowContent && bans.Detail.Title == "CampingCarl");
        Assert.False(bans.Detail.CanAppeal);
        Assert.Equal(t.Services.T("Bans_Blocked_not_active"), bans.Detail.AppealHint);
    }

    [AvaloniaFact]
    public async Task Ban_links_and_the_servers_button_open_the_native_ban_list()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://bans/" + FakeGameBansRoutes.PublicActive)));
        var bans = Assert.IsType<BansViewModel>(shell.CurrentPage);
        Assert.True(bans.DetailForced);
        await ViewModelTests.WaitUntil(() => bans.Detail.ShowContent && bans.ShowContent);
        Assert.False(bans.Detail.CanAppeal); // Gast
        Assert.Equal(t.Services.T("Bans_SignInToAppeal"), bans.Detail.AppealHint);
        Assert.Empty(t.Browser.Opened);
        ((IDetailPage)bans).CloseDetail();
        Assert.False(bans.IsDetailOpen);

        // ungültige ID im Link: „nicht gefunden“ statt Absturz
        await bans.OpenAsync("kein-ulid");
        Assert.True(bans.Detail.HasError);

        Assert.True(shell.NavigateTo("Nav_Servers"));
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        Assert.False(servers.CanProvision);
        servers.OpenBanListCommand.Execute(null);
        Assert.Same(bans, shell.CurrentPage);
    }
}
