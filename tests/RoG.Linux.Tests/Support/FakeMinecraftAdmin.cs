using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Minecraft-Betrieb der Verwaltung (Guide 44, 46, 47), Weltkarte (Guide 48) und Beitrittsdaten/Ressourcenpaket
/// (Guide 44 „So kommst du auf den Server“): Antworten in der Form der Website-API, Beispieldaten aus dem Clan-Netzwerk
/// (Proxy, Lobby, Freebuild, Plots, BedWars-Instanzen).
/// </summary>
[FakeRoutes]
internal static class FakeMinecraftAdminRoutes
{
    public const string Cdn = "https://cdn.example.test";

    public const string AccountWahke = "01JCMA00000000000000000AC1";
    public const string AccountBlock = "01JCMA00000000000000000AC2";
    public const string AccountGuest = "01JCMA00000000000000000AC3";

    public const string RoleMember = "01JCR0000000000000000R0001";
    public const string RoleModerator = "01JCR0000000000000000R0002";
    public const string RoleBuilder = "01JCR0000000000000000R0003";
    public const string RoleSquad = "01JCR0000000000000000R0004";

    public const string WalletWahke = "01JCW0000000000000000W0001";
    public const string WalletNyx = "01JCW0000000000000000W0002";
    public const string WalletGuest = "01JCW0000000000000000W0003";

    public const string ServerProxy = "01JCS0000000000000000S0001";
    public const string ServerLobby = "01JCS0000000000000000S0002";
    public const string ServerFreebuild = "01JCS0000000000000000S0003";
    public const string ServerBedwars1 = "01JCS0000000000000000S0004";
    public const string ServerBedwars2 = "01JCS0000000000000000S0005";
    public const string ServerBedwars3 = "01JCS0000000000000000S0006";

    public const string ReleaseId = "01JCE0000000000000000E0001";
    public const string DeployOld = "01JCD0000000000000000D0001";
    public const string DeployNew = "01JCD0000000000000000D0002";

    public const string WorldFreebuild = "01JCK0000000000000000K0001";
    public const string WorldNether = "01JCK0000000000000000K0002";
    public const string WorldPlots = "01JCK0000000000000000K0003";

    /// <summary>Wie <c>FakeData.Me</c>, mit allen Minecraft-Rechten und -Scopes (Owner).</summary>
    public const string MeAdmin = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01","squads":[],
          "permissions":{"global":["minecraft.manage","minecraft.economy","minecraft.pool","minecraft.deploy","roles.view"]},
          "token":{"scopes":["profile:read","servers:read","roles:read","minecraft:read","minecraft:manage","minecraft:economy","minecraft:pool","minecraft:deploy"]} }}
        """;

    /// <summary>Recht ohne Token-Scope: ein Token darf nie mehr als ausdrücklich erlaubt.</summary>
    public const string MeRightsWithoutScopes = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01","squads":[],
          "permissions":{"global":["minecraft.manage","minecraft.economy","minecraft.pool","minecraft.deploy"]},
          "token":{"scopes":["profile:read","servers:read","minecraft:read"]} }}
        """;

    /// <summary>Nur Pool (kein Deploy): Netzwerk-Reiter ohne Rollen und Verteilen.</summary>
    public const string MePoolOnly = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"admin","label":"Admin","color":"#E0324A"},"two_factor_enabled":true,"joined_at":"2020-03-01","squads":[],
          "permissions":{"global":["minecraft.pool"]},
          "token":{"scopes":["profile:read","servers:read","minecraft:pool"]} }}
        """;

    // --- Verknüpfte Accounts (Guide 44) --------------------------------------------------------------------------------

    public const string Accounts = $$$$"""
        {"data":[
          {"id":"{{{{AccountWahke}}}}","uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","xuid":null,"name":"Wahke","edition":"java","platform":null,
           "linked_at":"2024-05-12T18:30:00Z","last_seen_at":"2026-10-29T21:10:00Z","user":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},"linked_via":{"server":{"id":"01SV3","name":"RoG Lobby"}}},
          {"id":"{{{{AccountBlock}}}}","uuid":"00000000-0000-0000-0009-01f2c3d4e5f6","xuid":"2535412345678901","name":".BlockMeister","edition":"bedrock","platform":"xbox",
           "linked_at":"2026-09-03T16:05:00Z","last_seen_at":"2026-10-28T19:40:00Z","user":{"id":"01U3","name":"Kaito","slug":"kaito"},"linked_via":{"server":{"id":"01SV4","name":"RoG Freebuild"}}},
          {"id":"{{{{AccountGuest}}}}","uuid":"8667ba71-b85a-4004-af54-457a9734eed7","xuid":null,"name":"PixelPaula","edition":"java","platform":null,
           "linked_at":"2026-10-20T10:00:00Z","last_seen_at":null,"user":{"id":"01U9","name":"Paula","slug":"paula"},"linked_via":null}
        ],"meta":{"per_page":50,"next_cursor":"ACCOUNTS2"}}
        """;

    public const string AccountsMore = """
        {"data":[
          {"id":"01JCMA00000000000000000AC4","uuid":"1b2c3d4e-5f60-4718-9a0b-c1d2e3f4a5b6","xuid":null,"name":"Redstoner","edition":"java","platform":null,
           "linked_at":"2025-01-02T12:00:00Z","last_seen_at":null,"user":{"id":"01U2","name":"Nyx","slug":"nyx"},"linked_via":null}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    // --- Gruppen (LuckPerms) ---------------------------------------------------------------------------------------------

    public const string GroupMappings = $$$"""
        {"data":[
          {"role":{"id":"{{{RoleModerator}}}","name":"moderator","label":"Moderator","type":"rank"},"group":"rog-mod","priority":80,"updated_at":"2026-10-01T12:00:00Z"},
          {"role":{"id":"{{{RoleMember}}}","name":"member","label":"Member","type":"rank"},"group":"rog-member","priority":20,"updated_at":"2026-09-14T12:00:00Z"}
        ],"meta":{"managed_groups":["rog-member","rog-mod","rog-builder-alt"],
          "retired_groups":[
            {"group":"rog-builder-alt","retired_at":"2026-10-20T09:00:00Z","releasable_at":"2026-10-27T09:00:00Z"},
            {"group":"rog-vip","retired_at":"2026-10-29T09:00:00Z","releasable_at":"2099-11-05T09:00:00Z"}
          ]}}
        """;

    public const string Roles = $$$"""
        {"data":[
          {"id":"{{{RoleMember}}}","name":"member","label":"Member","labels":{"de":"Member"},"type":"rank","color":"#F1EFEA","attachment_limit_mb":null,"icon":null,"sort":10,"is_system":false,"parents":[],"permissions":[],"exclusions":[]},
          {"id":"{{{RoleModerator}}}","name":"moderator","label":"Moderator","labels":{"de":"Moderator"},"type":"rank","color":"#3DDC84","attachment_limit_mb":null,"icon":null,"sort":20,"is_system":false,"parents":[],"permissions":[],"exclusions":[]},
          {"id":"{{{RoleBuilder}}}","name":"builder","label":"Baumeister","labels":{"de":"Baumeister"},"type":"extra","color":"#D4AE3C","attachment_limit_mb":null,"icon":null,"sort":30,"is_system":false,"parents":[],"permissions":[],"exclusions":[]},
          {"id":"{{{RoleSquad}}}","name":"mc-squad-lead","label":"MC Squad-Leitung","labels":{"de":"MC Squad-Leitung"},"type":"squad","color":"#E0324A","attachment_limit_mb":null,"icon":null,"sort":40,"is_system":false,"parents":[],"permissions":[],"exclusions":[]}
        ],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"/v1/roles","per_page":100,"next_cursor":null,"prev_cursor":null}}
        """;

    // --- Rebel-Coins (Guide 46) --------------------------------------------------------------------------------------------

    public const string Wallets = $$$"""
        {"data":[
          {"id":"{{{WalletWahke}}}","kind":"account","uuid":null,"name":null,"user":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},
           "accounts":[{"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Wahke","edition":"java"}],"balance":12850,
           "daily":{"streak":12,"best_streak":31,"claimed_today":true,"next_at":"2026-10-30T23:00:00Z"},"last_activity_at":"2026-10-30T08:12:00Z"},
          {"id":"{{{WalletNyx}}}","kind":"account","uuid":null,"name":null,"user":{"id":"01U2","name":"Nyx","slug":"nyx"},
           "accounts":[{"uuid":"1b2c3d4e-5f60-4718-9a0b-c1d2e3f4a5b6","name":"Redstoner","edition":"java"},{"uuid":"00000000-0000-0000-0009-01f2c3d4e5f7","name":".NyxPE","edition":"bedrock"}],"balance":1250,
           "daily":{"streak":4,"best_streak":12,"claimed_today":false,"next_at":null},"last_activity_at":"2026-10-29T20:45:00Z"},
          {"id":"{{{WalletGuest}}}","kind":"guest","uuid":"5f1e2d3c-4b5a-4968-8778-695a4b3c2d1e","name":"Steve_2011","user":null,
           "accounts":[],"balance":340,"daily":{"streak":1,"best_streak":3,"claimed_today":true,"next_at":null},"last_activity_at":"2026-10-30T07:01:00Z"}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    public const string Transactions = """
        {"data":[
          {"id":"01JCT0000000000000000T0001","type":"admin","direction":"in","counterparty":"admin","amount":500,"reason":"Gewinn Bau-Wettbewerb Oktober","source":null,"server":null,"period_start":null,"created_at":"2026-10-28T19:00:00Z"},
          {"id":"01JCT0000000000000000T0002","type":"daily","direction":"in","counterparty":"system","amount":120,"reason":null,"source":null,"server":{"id":"01SV3","name":"RoG Lobby"},"period_start":null,"created_at":"2026-10-30T08:12:00Z"},
          {"id":"01JCT0000000000000000T0003","type":"shop_buy","direction":"out","counterparty":"shop","amount":64,"reason":"2x DIAMOND","source":null,"server":{"id":"01SV4","name":"RoG Freebuild"},"period_start":null,"created_at":"2026-10-29T21:02:00Z"}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    public const string Economy = """
        {"data":{"daily_base":50,"daily_milestones":[{"day":7,"bonus":100},{"day":30,"bonus":500},{"day":100,"bonus":2000}],
          "system_daily_limit":5000,"key_system_daily_limit":200000,"key_earnings_hourly_limit":50000,"pay_daily_limit":20000,"admin_daily_limit":100000}}
        """;

    // --- Netzwerk, Pool, Deploy (Guide 47) -------------------------------------------------------------------------------

    private static string Server(string id, string slug, string name, bool linked, string? role, string? folder, bool configured, bool mismatch, string? state) =>
        $$"""{"id":"{{id}}","slug":"{{slug}}","name":"{{name}}","linked":{{(linked ? "true" : "false")}},"deploy":{"role":{{Q(role)}},"folder":{{Q(folder)}},"configured":{{(configured ? "true" : "false")}},"mismatch":{{(mismatch ? "true" : "false")}}},"minecraft":{{state ?? "null"}}}""";

    private static string State(string networkId, string role, double tps, int online, int max, bool fresh, string? game = null, string? folder = null) =>
        $$"""{"network_id":"{{networkId}}","role":"{{role}}","tps":{{tps.ToString(CultureInfo.InvariantCulture)}},"mspt":12.4,"players_online":{{online}},"players_max":{{max}},"minigame":{{(game is null ? "null" : "{\"game\":\"" + game + "\",\"state\":\"waiting\",\"arena\":\"Inselwelt\"}")}},"plugin_version":"2.7.1","empty_since":null,"reported_at":"2026-10-30T08:56:30Z","fresh":{{(fresh ? "true" : "false")}},"deploy_folder":{{Q(folder)}}}""";

    private static string Q(string? value) => value is null ? "null" : "\"" + value + "\"";

    public static string NetworkServers => $$"""
        {"data":[
          {{Server(ServerProxy, "rog-proxy", "RoG Proxy", true, "proxy", "proxy", true, false, State("proxy-1", "proxy", 20.0, 23, 200, true))}},
          {{Server(ServerLobby, "rog-lobby", "RoG Lobby", true, "lobby", "lobby", true, false, State("lobby-1", "lobby", 19.9, 9, 100, true))}},
          {{Server(ServerFreebuild, "rog-freebuild", "RoG Freebuild", true, "freebuild", "freebuild", true, true, State("freebuild-1", "plots", 18.7, 11, 60, true))}},
          {{Server(ServerBedwars1, "rog-bedwars-1", "BedWars 1", true, "minigame", "bedwars", true, false, State("bedwars-1", "minigame", 20.0, 3, 8, true, "bedwars", "bedwars"))}},
          {{Server(ServerBedwars3, "rog-bedwars-3", "BedWars 3 (Test)", false, null, null, false, false, null)}}
        ]}
        """;

    public static string Pool => $$$"""
        {"data":[
          {"game":"bedwars","configured":true,"enabled":true,"min_running":1,"start_threshold":60,"idle_minutes":10,"running":2,"joinable":1,"players":5,
           "instances":[
             {"id":"{{{ServerBedwars1}}}","name":"BedWars 1","linked":true,"excluded":false,"failures":0,"quarantined":false,"backoff":false,"status":"online","minigame_state":"waiting","arena":"Inselwelt","players_online":3,"capacity":8,"load":37.5,"joinable":true,"empty_since":null},
             {"id":"{{{ServerBedwars2}}}","name":"BedWars 2","linked":true,"excluded":false,"failures":2,"quarantined":false,"backoff":true,"status":"starting","minigame_state":null,"arena":null,"players_online":0,"capacity":8,"load":null,"joinable":false,"empty_since":null}
           ],
           "suggestions":[{"id":"{{{ServerBedwars3}}}","slug":"rog-bedwars-3","name":"BedWars 3 (Test)"}],
           "last_action":{"id":41,"game":"bedwars","server":{"id":"{{{ServerBedwars2}}}","name":"BedWars 2"},"action":"start","reason":"no_joinable","ok":true,"error":null,"created_at":"2026-10-30T08:40:00Z"}},
          {"game":"skywars","configured":false,"enabled":false,"min_running":null,"start_threshold":null,"idle_minutes":null,"running":0,"joinable":0,"players":0,
           "instances":[],"suggestions":[],"last_action":null}
        ],"meta":{"cooldown_minutes":3,"actions":[
          {"id":41,"game":"bedwars","server":{"id":"{{{ServerBedwars2}}}","name":"BedWars 2"},"action":"start","reason":"no_joinable","ok":true,"error":null,"created_at":"2026-10-30T08:40:00Z"},
          {"id":40,"game":"bedwars","server":{"id":"{{{ServerBedwars2}}}","name":"BedWars 2"},"action":"start","reason":"no_joinable","ok":false,"error":"Pterodactyl: 504","created_at":"2026-10-30T08:31:00Z"},
          {"id":39,"game":"bedwars","server":{"id":"{{{ServerBedwars1}}}","name":"BedWars 1"},"action":"stop","reason":"idle","ok":true,"error":null,"created_at":"2026-10-29T23:10:00Z"}
        ]}}
        """;

    public static string Release(string status) => $$$"""
        {"data":{"id":"{{{ReleaseId}}}","status":"{{{status}}}","error":null,"tag":"v2.8.0","published_at":"2026-10-29T17:00:00Z",
          "sha256":"9f2c1a0b7d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f90","size":4194304,"signed":true,"signature_required":true,"signature":{"key_id":"rog-release-2026"},
          "folders":{"proxy":[{"name":"rog-proxy-2.8.0.jar","size":1048576}],"lobby":[{"name":"rog-core-2.8.0.jar","size":2097152}],"bedwars":[{"name":"rog-core-2.8.0.jar","size":2097152},{"name":"rog-bedwars-1.4.0.jar","size":524288}]},
          "ignored":["README.md"],"expires_at":"2026-10-30T10:00:00Z"}}
        """;

    public static string Deploy(string id, string status) => $$"""
        {"data":{{DeployItem(id, status)}}}
        """;

    private static string DeployItem(string id, string status) => $$"""
        {"id":"{{id}}","status":"{{status}}","restart":true,"release":{"tag":"{{(id == DeployOld ? "v2.7.1" : "v2.8.0")}}","signed":true},"error":null,"started_by":{"name":"Wahke"},
         "created_at":"{{(id == DeployOld ? "2026-10-21T19:00:00Z" : "2026-10-30T09:00:00Z")}}","finished_at":null,
         "targets":[
           {"server":{"id":"{{ServerLobby}}","name":"RoG Lobby"},"folder":"lobby","status":"{{(status == "running" ? "running" : "done")}}","message":null,"uploaded":["rog-core.jar"],"restarted":{{(status == "running" ? "false" : "true")}}},
           {"server":{"id":"{{ServerFreebuild}}","name":"RoG Freebuild"},"folder":null,"status":"skipped","message":"role_mismatch","uploaded":[],"restarted":false}
         ]}
        """;

    public static string Deploys => $$"""{"data":[{{DeployItem(DeployOld, "done")}}]}""";

    // --- Weltkarte (Guide 48) --------------------------------------------------------------------------------------------

    public static string TileTemplate(string world) => Cdn + "/minecraft-map/" + world + "/9f2c1a0b7d3e/{zoom}/{x}_{z}.{version}.png";

    private static string World(string id, string world, string name, string env, int spawnX, int spawnZ, int tiles, string markers, bool hidden = false, bool admin = false) => $$"""
        {"id":"{{id}}","world":"{{world}}","name":"{{name}}","environment":"{{env}}","loaded":true,"spawn":{"x":{{spawnX}},"z":{{spawnZ}}},"tile_size":512,"zoom_levels":4,
         "tiles":{"count":{{tiles}},"region_size":32,"url":"{{TileTemplate(id)}}","updated_at":"2026-10-30T06:00:00Z"},
         "markers":[{{markers}}],"reported_at":"2026-10-30T08:56:00Z"{{(admin ? ",\"hidden\":" + (hidden ? "true" : "false") + ",\"names\":{\"de\":\"" + name + "\",\"en\":\"\",\"fr\":\"\"},\"bytes\":734003200" : string.Empty)}}}
        """;

    private const string FreebuildWarps = """
        {"type":"warp","id":"shop","label":"Shop","x":120,"z":-40},{"type":"warp","id":"arena","label":"Arena","x":-210,"z":160},
        {"type":"warp","id":"hafen","label":"Hafen","x":310,"z":250},{"type":"warp","id":"farm","label":"Farmen","x":-380,"z":-300},
        {"type":"warp","id":"rathaus","label":"Rathaus","x":40,"z":90}
        """;

    public static string Map(bool admin, string liveMode = "public", bool live = true) => $$$"""
        {"data":{"live_players":{{{(live ? "true" : "false")}}},"live_players_mode":"{{{liveMode}}}",
          "servers":[
            {"id":"rog-freebuild","network_id":"freebuild-1","name":"RoG Freebuild"{{{(admin ? ",\"hidden\":false,\"public\":true,\"visibility\":\"public\",\"bytes\":1288490188" : string.Empty)}}},
             "worlds":[{{{World(WorldFreebuild, "world", "Freebuild", "normal", 0, 0, 1342, FreebuildWarps, admin: admin)}}},{{{World(WorldNether, "world_nether", "Nether", "nether", 0, 0, 0, string.Empty, admin: admin)}}}]},
            {"id":"rog-plots","network_id":"plots-1","name":"RoG Plots"{{{(admin ? ",\"hidden\":false,\"public\":true,\"visibility\":\"members\",\"bytes\":52428800" : string.Empty)}}},
             "worlds":[{{{World(WorldPlots, "plotworld", "Plotwelt", "normal", 16, 16, 88, "{\"type\":\"warp\",\"id\":\"spawn\",\"label\":\"Plot-Spawn\",\"x\":16,\"z\":16}", admin: admin)}}}]}
          ]{{{(admin ? ",\"quota\":{\"used\":1340918988,\"total\":53687091200,\"warn_at\":42949672960,\"per_server\":10737418240,\"warned\":false}" : string.Empty)}}}}}
        """;

    public const string LiveFreebuild = $$$"""
        {"data":{"server":"rog-freebuild","enabled":true,"updated_at":"2026-10-30T08:57:00Z","players":[
          {"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Wahke","world":"world","world_id":"{{{WorldFreebuild}}}","x":95.5,"z":-22.3,"yaw":90,"bedrock":false,"head_url":"{{{Cdn}}}/minecraft/heads/069a79f4.png"},
          {"uuid":"00000000-0000-0000-0009-01f2c3d4e5f6","name":"BlockMeister","world":"world","world_id":"{{{WorldFreebuild}}}","x":-160.2,"z":120.8,"yaw":0,"bedrock":true,"head_url":null},
          {"uuid":"1b2c3d4e-5f60-4718-9a0b-c1d2e3f4a5b6","name":"Redstoner","world":"world_nether","world_id":"{{{WorldNether}}}","x":12,"z":4,"yaw":0,"bedrock":false,"head_url":null}
        ]}}
        """;

    /// <summary>Vorhandene Kacheln: rund um den Ursprung, je gröber die Stufe, desto weniger.</summary>
    public static string TileIndex(string world, string query)
    {
        var args = query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);
        var zoom = int.Parse(args["zoom"], CultureInfo.InvariantCulture);
        var region = args["region"].Split(',');
        var (rx, rz) = (int.Parse(region[0], CultureInfo.InvariantCulture), int.Parse(region[1], CultureInfo.InvariantCulture));
        var tiles = new List<string>();
        if (world != WorldNether)
        {
            var radius = Math.Max(1, 3 >> zoom);
            for (var x = -radius; x < radius; x++)
            {
                for (var z = -radius; z < radius; z++)
                {
                    if ((int)Math.Floor(x / 32.0) == rx && (int)Math.Floor(z / 32.0) == rz)
                    {
                        tiles.Add($"[{x},{z},\"a{zoom}b{Math.Abs(x)}c{Math.Abs(z)}\"]");
                    }
                }
            }
        }

        return $$$"""{"data":{"world":"{{{world}}}","zoom":{{{zoom}}},"region":{"x":{{{rx}}},"z":{{{rz}}},"size":32},"tiles":[{{{string.Join(",", tiles)}}}]}}""";
    }

    // --- Beitrittsdaten (für Verwaltende mit Hinweisen aller Sprachen) ------------------------------------------------------

    public const string JoinForManagers = """
        {"data":{"java":{"address":"mc.example.test","port":25565},"bedrock":{"address":"mc.example.test","port":19132},
          "console":{"enabled":true,"gamertag":"RoG6328","hint":"Der Konsolen-Beitritt kann am Wochenende kurz ausfallen.",
            "hints":{"de":"Der Konsolen-Beitritt kann am Wochenende kurz ausfallen.","en":"Console joining may briefly fail at weekends.","fr":""}}}}
        """;

    public const string UploadedPack = $$$"""
        {"data":{"url":"{{{Cdn}}}/minecraft/pack-7c1d.zip","latest_url":"{{{Cdn}}}/minecraft/pack.zip","sha1":"7c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d","size":9437184,"updated_at":"2026-10-30T09:10:00Z"}}
        """;

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = request.RequestUri!.Query;
        if (!path.StartsWith("/v1/minecraft/", StringComparison.Ordinal) && path != "/v1/roles")
        {
            return null;
        }

        return (method, path) switch
        {
            ("GET", "/v1/roles") => Roles,
            ("GET", "/v1/minecraft/accounts") => query.Contains("cursor=ACCOUNTS2", StringComparison.Ordinal) ? AccountsMore : Accounts,
            ("DELETE", var p) when p.StartsWith("/v1/minecraft/accounts/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("GET", "/v1/minecraft/group-mappings") => GroupMappings,
            ("PUT", var p) when p.StartsWith("/v1/minecraft/group-mappings/", StringComparison.Ordinal) => SavedMapping(p, body),
            ("DELETE", var p) when p.StartsWith("/v1/minecraft/group-mappings/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("DELETE", var p) when p.StartsWith("/v1/minecraft/retired-groups/", StringComparison.Ordinal) => """{"data":{"released":true}}""",
            ("GET", "/v1/minecraft/wallets") => Wallets,
            ("GET", var p) when p.StartsWith("/v1/minecraft/wallets/", StringComparison.Ordinal) && p.EndsWith("/transactions", StringComparison.Ordinal) => Transactions,
            ("POST", var p) when p.StartsWith("/v1/minecraft/wallets/", StringComparison.Ordinal) && p.EndsWith("/adjustments", StringComparison.Ordinal) => Adjusted(body),
            ("GET", "/v1/minecraft/economy") => Economy,
            ("PUT", "/v1/minecraft/economy") => "{\"data\":" + body + "}",
            ("GET", "/v1/minecraft/network/servers") => NetworkServers,
            ("PUT", var p) when p.StartsWith("/v1/minecraft/network/servers/", StringComparison.Ordinal) && p.EndsWith("/role", StringComparison.Ordinal) => SavedRole(p, body),
            ("GET", "/v1/minecraft/pool") => Pool,
            ("PUT", var p) when p.StartsWith("/v1/minecraft/pool/", StringComparison.Ordinal) => """{"data":{"game":"bedwars","configured":true,"enabled":true}}""",
            ("DELETE", var p) when p.StartsWith("/v1/minecraft/pool/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("PATCH", var p) when p.StartsWith("/v1/minecraft/pool/", StringComparison.Ordinal) => """{"data":{"updated":true}}""",
            ("POST", "/v1/minecraft/plugin-releases") => Release("checking"),
            ("GET", var p) when p.StartsWith("/v1/minecraft/plugin-releases/", StringComparison.Ordinal) => Release("ready"),
            ("GET", "/v1/minecraft/plugin-deploys") => Deploys,
            ("POST", "/v1/minecraft/plugin-deploys") => Deploy(DeployNew, "running"),
            ("GET", var p) when p.StartsWith("/v1/minecraft/plugin-deploys/", StringComparison.Ordinal) => Deploy(DeployNew, "done"),
            ("GET", "/v1/minecraft/map") => Map(admin: request.Headers.Authorization is not null),
            ("GET", var p) when p.StartsWith("/v1/minecraft/map/worlds/", StringComparison.Ordinal) && p.EndsWith("/tiles", StringComparison.Ordinal) =>
                TileIndex(p.Split('/')[5], query),
            ("GET", "/v1/minecraft/map/live") => query.Contains("freebuild", StringComparison.Ordinal) ? LiveFreebuild
                : """{"data":{"server":"rog-plots","enabled":true,"updated_at":"2026-10-30T08:57:00Z","players":[]}}""",
            ("PATCH", "/v1/minecraft/map/settings") => "{\"data\":{\"live_players\":true,\"live_players_mode\":" + JsonNode.Parse(body)!["live_players"]!.ToJsonString() + "}}",
            ("PATCH", var p) when p.StartsWith("/v1/minecraft/map/servers/", StringComparison.Ordinal) => "{\"data\":" + body + "}",
            ("PATCH", var p) when p.StartsWith("/v1/minecraft/map/worlds/", StringComparison.Ordinal) => """{"data":null}""",
            ("DELETE", var p) when p.StartsWith("/v1/minecraft/map/worlds/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("PUT", "/v1/minecraft/join") => SavedJoin(body),
            ("POST", "/v1/minecraft/resource-pack") => UploadedPack,
            ("DELETE", "/v1/minecraft/resource-pack") => """{"data":{"deleted":true}}""",
            _ => null,
        };
    }

    private static string SavedMapping(string path, string body)
    {
        var sent = JsonNode.Parse(body)!;
        var id = path[(path.LastIndexOf('/') + 1)..];
        return $$$"""{"data":{"role":{"id":"{{{id}}}","name":"builder","label":"Baumeister","type":"extra"},"group":"{{{sent["group"]}}}","priority":{{{sent["priority"]}}},"updated_at":"2026-10-30T09:00:00Z"}}""";
    }

    private static string Adjusted(string body)
    {
        var sent = JsonNode.Parse(body)!;
        var amount = sent["amount"]!.GetValue<int>();
        var balance = sent["direction"]!.GetValue<string>() == "credit" ? 12850 + amount : 12850 - amount;
        return $$$$"""{"data":{"balance":{{{{balance}}}},"transaction":{"id":"01JCT0000000000000000T0009"}}}""";
    }

    private static string SavedRole(string path, string body)
    {
        var sent = JsonNode.Parse(body)!;
        var id = path.Split('/')[5];
        var role = sent["role"]?.GetValue<string>();
        var folder = sent["folder"]?.GetValue<string>() ?? role;
        return "{\"data\":" + Server(id, "rog-freebuild", "RoG Freebuild", true, role, folder, role is not null, false, State("freebuild-1", "plots", 18.7, 11, 60, true)) + "}";
    }

    private static string SavedJoin(string body)
    {
        var sent = JsonNode.Parse(body)!;
        var hints = sent["console_hint"] as JsonObject;
        return $$$$"""
            {"data":{"java":{"address":{{{{sent["java_address"]!.ToJsonString()}}}},"port":{{{{sent["java_port"]}}}}},"bedrock":{"address":{{{{sent["bedrock_address"]!.ToJsonString()}}}},"port":{{{{sent["bedrock_port"]}}}}},
              "console":{"enabled":{{{{sent["console_enabled"]!.ToJsonString()}}}},"gamertag":{{{{sent["console_gamertag"]?.ToJsonString() ?? "null"}}}},"hint":{{{{hints?["de"]?.ToJsonString() ?? "null"}}}},
                "hints":{{{{hints?.ToJsonString() ?? "{}"}}}}}}}
            """;
    }
}

/// <summary>
/// Fake-Kacheln der Minecraft-Karte (512 px, Blöcke aus Gras, Sand, Wasser und Stein, je Stufe passend gerechnet) und
/// Köpfe; merkt sich jede Anfrage (Datenschutz-Test: nur Adressen der Plattform).
/// </summary>
public sealed class FakeMinecraftTiles : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        lock (Requests)
        {
            Requests.Add(uri);
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        byte[] png;
        if (uri.AbsolutePath.Contains("/heads/", StringComparison.Ordinal))
        {
            png = Head();
        }
        else
        {
            var zoom = int.Parse(parts[^2], CultureInfo.InvariantCulture);
            var xz = parts[^1].Split('.')[0].Split('_');
            png = Tile(zoom, int.Parse(xz[0], CultureInfo.InvariantCulture), int.Parse(xz[1], CultureInfo.InvariantCulture));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
    }

    private static byte[] Tile(int zoom, int tx, int tz)
    {
        const int size = 512;
        const int cell = 16;
        var scale = 1 << zoom;
        using var bitmap = new SkiaSharp.SKBitmap(size, size);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            using var paint = new SkiaSharp.SKPaint();
            for (var cx = 0; cx < size / cell; cx++)
            {
                for (var cz = 0; cz < size / cell; cz++)
                {
                    var bx = ((tx * size) + (cx * cell) + (cell / 2)) * (double)scale;
                    var bz = ((tz * size) + (cz * cell) + (cell / 2)) * (double)scale;
                    var h = Math.Sin(bx * 0.006) + Math.Cos(bz * 0.0075) + (0.6 * Math.Sin((bx + bz) * 0.013)) + (0.3 * Math.Cos((bx - (2 * bz)) * 0.021));
                    var shade = (byte)(((int)(Math.Abs(bx * 7) + Math.Abs(bz * 13)) / cell) % 3 * 6);
                    paint.Color = h switch
                    {
                        < -1.1 => new SkiaSharp.SKColor(0x2F, 0x5D, (byte)(0xB0 - shade)),
                        < -0.85 => new SkiaSharp.SKColor((byte)(0xD8 - shade), 0xC8, 0x8A),
                        < 1.35 => new SkiaSharp.SKColor((byte)(0x5A - shade), (byte)(0x9A - shade), 0x3C),
                        < 1.7 => new SkiaSharp.SKColor((byte)(0x3E - shade), (byte)(0x6E - shade), 0x2C),
                        _ => new SkiaSharp.SKColor((byte)(0x8A - shade), (byte)(0x8A - shade), (byte)(0x8E - shade)),
                    };
                    canvas.DrawRect(cx * cell, cz * cell, cell, cell, paint);
                }
            }

            // Wege und ein paar Häuser rund um den Spawn
            if (zoom <= 1)
            {
                using var path = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x9C, 0x84, 0x5A), StrokeWidth = 8 / scale + 2 };
                var ox = -tx * size;
                var oz = -tz * size;
                canvas.DrawLine(ox - 2000, oz, ox + 2000, oz, path);
                canvas.DrawLine(ox, oz - 2000, ox, oz + 2000, path);
                using var roof = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x8E, 0x3B, 0x2E) };
                foreach (var (hx, hz) in new[] { (30, 60), (60, 70), (-60, 30), (100, -60), (40, 100), (-40, -50) })
                {
                    canvas.DrawRect(ox + (hx / scale), oz + (hz / scale), 18f / scale, 14f / scale, roof);
                }
            }
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static byte[] Head()
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 8);
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                bitmap.SetPixel(x, y, y < 3 ? new SkiaSharp.SKColor(0x4A, 0x2E, 0x1C) : (y == 4 && (x == 2 || x == 5)) ? new SkiaSharp.SKColor(0x30, 0x4F, 0xA0) : new SkiaSharp.SKColor(0xD9, 0xA0, 0x7A));
            }
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
