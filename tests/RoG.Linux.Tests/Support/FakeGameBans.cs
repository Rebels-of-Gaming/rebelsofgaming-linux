using System.Text.Json.Nodes;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Stufe 7 „G“: zentrale Gameserver-Bans und Plugin-Schlüssel (Guide 43), öffentliche Ban-Liste mit
/// Einspruch (Guide 43) und „Neuer Gameserver“ (Provisioning, Guide 18) – Form wie die API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakeGameBansRoutes
{
    public const string ServerId = "01SV1";
    public const string Slug = "et-sniper";
    public const string ActiveBan = "01JGB0000000000000000000A1";
    public const string TempBan = "01JGB0000000000000000000A2";
    public const string LiftedBan = "01JGB0000000000000000000A3";
    public const string PublicActive = "01JPB0000000000000000000B1";
    public const string PublicEnded = "01JPB0000000000000000000B2";
    public const string ProvisionedSlug = "rog-cs2-scrim";

    /// <summary>Wahke mit Bans-, Server- und Einspruchs-Rechten (für die Reiter und das Provisioning).</summary>
    public const string MeManager = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["gamebans.manage","servers.create","servers.manage"]},
          "token":{"scopes":["servers:read","servers:manage","gamebans:manage","gamebans:appeal","members:read","squads:read"]} }}
        """;

    /// <summary>Server-Admin ohne <c>gamebans.manage</c>: nur Bans dieses Servers, Schlüssel nur „anlegen“.</summary>
    public const string MeServerAdmin = """
        {"data":{"id":"01USERNYX","slug":"nyx","name":"Nyx","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"admin","label":"Admin","color":"#E0324A"},"two_factor_enabled":true,"joined_at":"2021-05-01",
          "squads":[],"permissions":{"global":["servers.manage"]},
          "token":{"scopes":["servers:read","servers:manage","gamebans:manage"]} }}
        """;

    /// <summary>Mitglied mit Einspruchs-Scope, ohne Verwaltungsrechte.</summary>
    public const string MeMember = """
        {"data":{"id":"01USERKAITO","slug":"kaito","name":"Kaito","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#A8ACB6"},"two_factor_enabled":false,"joined_at":"2022-01-01",
          "squads":[],"permissions":{"global":[]},"token":{"scopes":["servers:read","gamebans:appeal"]} }}
        """;

    /// <summary>Serverliste mit einem über die Website angelegten Server (Installation läuft, Ablauf, drei Ports).</summary>
    public const string ServersWithProvisioning = """
        {"data":[
          {"id":"01SV1","slug":"et-sniper","name":"RoG ET: Sniper","description":null,"game":{"id":"g1","name":"Wolfenstein: Enemy Territory","short":"ET"},"squad":null,
           "address":"et.rogclan.de:27960","query_type":"quake3","connect_url":"et://et.rogclan.de:27960","visibility":"public",
           "live":{"online":true,"name":"RoG Sniper","map":"supply","players":9,"max_players":20,"passworded":false,"player_list":null},"control":null,"provisioning":null},
          {"id":"01SV9","slug":"rog-cs2-scrim","name":"RoG CS2: Scrim","description":null,"game":{"id":"g2","name":"Counter-Strike 2","short":"CS2"},"squad":null,
           "address":"cs.rogclan.de:27025","query_type":"source","connect_url":"steam://connect/cs.rogclan.de:27025","visibility":"members",
           "live":{"online":false,"name":null,"map":null,"players":0,"max_players":0,"passworded":false,"player_list":null},"control":null,
           "provisioning":{"provisioned":true,"status":"installing","provisioned_at":"2026-10-30T08:00:00Z","expires_at":"2026-11-06T08:00:00Z","deletable":true,
             "allocations":[{"ip":"203.0.113.10","alias":"cs.rogclan.de","port":27025,"primary":true},{"ip":"203.0.113.10","alias":"cs.rogclan.de","port":27026,"primary":false},{"ip":"203.0.113.10","alias":"cs.rogclan.de","port":27027,"primary":false}]}}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    /// <summary>Zuletzt gesendeter Plugin-Schlüssel (Tests prüfen, dass er nur einmal gezeigt wird).</summary>
    public const string PluginToken = "rogpk_7Hq2xW9vLmN4pR8sT1uY6zB3cD5eF0gJ";

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        return (method, path) switch
        {
            // Zentrale Bans
            ("GET", "/v1/game-bans") => GameBans(query),
            ("POST", "/v1/game-bans") => "{\"data\":" + Ban("01JGB0000000000000000000A9", "active", "server", JsonNode.Parse(body)?["name"]?.GetValue<string>() ?? "Neu", "guid",
                JsonNode.Parse(body)?["identifier"]?.GetValue<string>() ?? "?", JsonNode.Parse(body)?["reason"]?.GetValue<string>() ?? string.Empty, "2026-10-31T10:00:00Z") + "}",
            ("GET", "/v1/game-bans/" + ActiveBan) => "{\"data\":" + Ban(ActiveBan, "active", "game", "^1Sn1per^7King", "guid", "4A1F9C2B7D3E8F6A0B5C4D3E2F1A9B8C", "Wallhack, Demo im Forum", null,
                deliveries: """[{"server":{"name":"RoG ET: Sniper"},"status":"active","error":null,"updated_at":"2026-10-28T19:02:00Z"},{"server":{"name":"RoG ET: Public"},"status":"failed","error":"RCON-Timeout","updated_at":"2026-10-28T19:02:05Z"}]""") + "}",
            ("PATCH", var p) when p.StartsWith("/v1/game-bans/", StringComparison.Ordinal) => Patched(p["/v1/game-bans/".Length..], body),
            ("POST", var p) when p.StartsWith("/v1/game-bans/", StringComparison.Ordinal) && p.EndsWith("/lift", StringComparison.Ordinal) =>
                "{\"data\":" + Ban(p.Split('/')[3], "lifted", "game", "^1Sn1per^7King", "guid", "4A1F9C2B7D3E8F6A0B5C4D3E2F1A9B8C", "Wallhack, Demo im Forum", null, lifted: true) + "}",
            ("POST", "/v1/servers/" + Slug + "/game-bans/lift") => """{"data":{"lifted":3}}""",

            // Plugin-Schlüssel
            ("GET", "/v1/servers/" + Slug + "/plugin-key") => Key(exists: true),
            ("POST", "/v1/servers/" + Slug + "/plugin-key") => KeyCreated,
            ("PATCH", "/v1/servers/" + Slug + "/plugin-key") => Key(exists: true),
            ("DELETE", "/v1/servers/" + Slug + "/plugin-key") => """{"data":{"revoked":true}}""",

            // Öffentliche Ban-Liste
            ("GET", "/v1/bans") => query.Contains("filter[status]=ended", StringComparison.Ordinal) ? PublicListEnded : PublicList,
            ("GET", "/v1/bans/" + PublicActive) => "{\"data\":" + PublicBan(PublicActive, "^1Sn1per^7King", "active", withInfo: true) + "}",
            ("GET", "/v1/bans/" + PublicEnded) => "{\"data\":" + PublicBan(PublicEnded, "CampingCarl", "expired", withInfo: true) + "}",
            ("POST", "/v1/bans/" + PublicActive + "/appeals") =>
                """{"data":{"id":"01JAP0000000000000000000C1","status":"open","statement":"…","thread_url":"https://rebelsofgaming.com/de/forum/threads/ban-einspruch-sn1per","created_at":"2026-10-30T10:00:00Z"}}""",

            // Provisioning
            ("GET", "/v1/servers/provisioning/catalog") => Catalog,
            ("POST", "/v1/servers/provision") => Provisioned("installing"),
            ("POST", "/v1/servers/" + ProvisionedSlug + "/provision/refresh") => Provisioned("ready"),
            ("PATCH", "/v1/servers/" + ProvisionedSlug + "/expiry") => Provisioned("ready", expires: "2026-11-13T08:00:00Z"),
            ("DELETE", "/v1/servers/" + ProvisionedSlug + "/provision") => "{}",
            _ => null,
        };
    }

    private static string GameBans(string query)
    {
        if (query.Contains("filter[search]=leer", StringComparison.Ordinal))
        {
            return """{"data":[],"meta":{"next_cursor":null}}""";
        }

        if (query.Contains("cursor=page2", StringComparison.Ordinal))
        {
            return "{\"data\":[" + Ban(LiftedBan, "lifted", "server", "Kaito", "guid", "9F8E7D6C5B4A39281706F5E4D3C2B1A0", "Teamkill-Serie", null, lifted: true) + "],\"meta\":{\"next_cursor\":null}}";
        }

        return "{\"data\":["
            + Ban(ActiveBan, "active", "game", "^1Sn1per^7King", "guid", "4A1F9C2B7D3E8F6A0B5C4D3E2F1A9B8C", "Wallhack, Demo im Forum", null, appeal: "K7P2-QX9M") + ","
            + Ban(TempBan, "active", "server", "CampingCarl", "steamid", "STEAM_0:1:48213377", "Spawnkill trotz Verwarnung", "2026-11-02T18:00:00Z", issuedIngame: true, isPublic: false)
            + "],\"meta\":{\"next_cursor\":\"page2\"}}";
    }

    private static string Patched(string id, string body)
    {
        var json = JsonNode.Parse(body);
        var isPublic = json?["is_public"]?.GetValue<bool>() ?? true;
        return "{\"data\":" + Ban(id, "active", "server", "CampingCarl", "steamid", "STEAM_0:1:48213377", json?["reason"]?.GetValue<string>() ?? "Spawnkill trotz Verwarnung",
            json?["minutes"] is { } m && m.GetValue<int>() == 0 ? null : "2026-11-03T18:00:00Z", isPublic: isPublic) + "}";
    }

    private static string Ban(string id, string status, string scope, string name, string type, string identifier, string reason, string? expires,
        bool lifted = false, string? appeal = null, bool issuedIngame = false, bool isPublic = true, string deliveries = "[]") => $$$"""
        {"id":"{{{id}}}","status":"{{{status}}}","scope":"{{{scope}}}","game":{"id":"g1","name":"Wolfenstein: Enemy Territory"},
         "server":{{{(scope == "server" ? "{\"id\":\"01SV1\",\"name\":\"RoG ET: Sniper\"}" : "null")}}},
         "identifier_type":"{{{type}}}","identifier":"{{{identifier}}}","name":"{{{name}}}","ip":null,"reason":"{{{reason}}}",
         "expires_at":{{{(expires is null ? "null" : "\"" + expires + "\"")}}},"created_at":"2026-10-28T19:00:00Z","created_via":"{{{(issuedIngame ? "plugin" : "admin")}}}",
         "created_by":{{{(issuedIngame ? "null" : "{\"name\":\"Wahke\"}")}}},"issued_by":{{{(issuedIngame ? "{\"name\":\"Nyx\"}" : "null")}}},
         "source_server":{{{(issuedIngame ? "{\"name\":\"RoG ET: Sniper\"}" : "null")}}},"user":null,
         "lifted_at":{{{(lifted ? "\"2026-10-29T12:00:00Z\"" : "null")}}},"lifted_by":{{{(lifted ? "{\"name\":\"Wahke\"}" : "null")}}},"lift_reason":null,
         "is_public":{{{(isPublic ? "true" : "false")}}},"appeal_code":{{{(appeal is null ? "null" : "\"" + appeal + "\"")}}},
         "public_url":"https://rebelsofgaming.com/de/bans/{{{id}}}","deliveries":{{{deliveries}}} }
        """;

    private static string Key(bool exists) => $$"""
        {"data":{"exists":{{(exists ? "true" : "false")}},"active":{{(exists ? "true" : "false")}},"hint":"rogpk_…J0gF","may_issue":true,"may_lift":true,"may_lift_any":false,
          "may_ip":false,"may_ban_members":false,"max_scope":"server","may_link":false,"may_economy":false,
          "created_at":"2026-10-01T10:00:00Z","rotated_at":null,"revoked_at":null,"last_used_at":"2026-10-30T08:55:00Z",
          "last_heartbeat_at":"2026-10-30T08:59:00Z","plugin":"rog-etl-bans","plugin_version":"1.4.2","plugin_outdated":false,
          "game_version":"ET: Legacy 2.83","players_online":9,"plugin_active":true} }
        """;

    private static readonly string KeyCreated = Key(exists: true).Replace("\"plugin_active\":true}", "\"plugin_active\":true,\"token\":\"" + PluginToken + "\"}", StringComparison.Ordinal);

    private static string PublicBan(string id, string name, string status, bool withInfo) => $$$"""
        {"id":"{{{id}}}","url":"https://rebelsofgaming.com/de/bans/{{{id}}}","name":"{{{name}}}","identifier_type":"guid","identifier_masked":"4A1F…9B8C",
         "game":{"name":"Wolfenstein: Enemy Territory"},"server":null,"scope":"game","scope_label":"alle ET-Server","reason":"Wallhack, Demo im Forum",
         "status":"{{{status}}}","created_at":"2026-10-28T19:00:00Z","expires_at":null,"permanent":{{{(status == "active" ? "true" : "false")}}},
         "ended_at":{{{(status == "active" ? "null" : "\"2026-10-20T19:00:00Z\"")}}},"appeal":null
         {{{(withInfo ? status == "active"
            ? ",\"appeal_info\":{\"status\":null,\"thread_url\":null,\"available\":true,\"blocked\":null,\"cooldown_until\":null,\"proofs\":[]}"
            : ",\"appeal_info\":{\"status\":null,\"thread_url\":null,\"available\":false,\"blocked\":\"not_active\",\"cooldown_until\":null,\"proofs\":[]}" : string.Empty)}}} }
        """;

    private static readonly string PublicList = "{\"data\":["
        + PublicBan(PublicActive, "^1Sn1per^7King", "active", false) + ","
        + PublicBan("01JPB0000000000000000000B3", "xX_Aimbot_Xx", "active", false).Replace("\"appeal\":null", "\"appeal\":{\"status\":\"open\",\"created_at\":\"2026-10-29T10:00:00Z\",\"decided_at\":null}", StringComparison.Ordinal)
        + "],\"meta\":{\"next_cursor\":null}}";

    private static readonly string PublicListEnded = "{\"data\":[" + PublicBan(PublicEnded, "CampingCarl", "expired", false) + "],\"meta\":{\"next_cursor\":null}}";

    public const string Catalog = """
        {"data":{
          "locations":[{"id":1,"short":"fsn1","long":"Falkenstein"}],
          "nodes":[{"id":3,"name":"rog-node-1","location_id":1,"fqdn":"node1.rognode.de","memory":65536,"disk":1048576,
                    "allocations":[{"id":101,"ip":"203.0.113.10","ip_alias":"cs.rogclan.de","port":27025},{"id":102,"ip":"203.0.113.10","ip_alias":"cs.rogclan.de","port":27026},{"id":103,"ip":"203.0.113.10","ip_alias":"cs.rogclan.de","port":27027}]}],
          "nests":[{"id":2,"name":"Source Engine","eggs":[{"id":7,"name":"Counter-Strike 2","description":"CS2 Dedicated Server über SteamCMD.",
                      "docker_image":"ghcr.io/pterodactyl/games:source","docker_images":{"Source":"ghcr.io/pterodactyl/games:source"},
                      "startup":"./game/bin/linuxsteamrt64/cs2 -dedicated +map {{SRCDS_MAP}}",
                      "variables":[{"name":"Map","description":"Startkarte","env_variable":"SRCDS_MAP","default_value":"de_mirage","user_viewable":true,"user_editable":true,"rules":"required|string|max:20"},
                                   {"name":"Spielmodus","description":null,"env_variable":"GAME_MODE","default_value":"competitive","user_viewable":true,"user_editable":true,"rules":"required|in:competitive,casual,wingman"}]}]},
                   {"id":5,"name":"Wolfenstein","eggs":[{"id":11,"name":"ET: Legacy","description":"ET: Legacy 2.83 mit Omni-Bots.",
                      "docker_image":"ghcr.io/rog/etlegacy:2.83","docker_images":{},"startup":"./etlded +set fs_game legacy",
                      "variables":[]}]}],
          "expiry_presets":[1,3,7,14,30]}}
        """;

    private static string Provisioned(string status, string expires = "2026-11-06T08:00:00Z") => $$"""
        {"data":{"id":"01SV9","slug":"{{ProvisionedSlug}}","name":"RoG CS2: Scrim",
          "provisioning":{"provisioned":true,"status":"{{status}}","provisioned_at":"2026-10-30T08:00:00Z","expires_at":"{{expires}}","deletable":true,
            "allocations":[{"ip":"203.0.113.10","alias":"cs.rogclan.de","port":27025,"primary":true}]} } }
        """;
}
