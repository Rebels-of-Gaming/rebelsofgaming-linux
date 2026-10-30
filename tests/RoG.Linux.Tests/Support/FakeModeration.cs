using System.Text.Json.Nodes;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Stufe 9 „L“: Moderation (Guide 28: Meldungen, Mitglied, Verwarnen/Sperren, Sperrliste, Mod-Log,
/// Wortfilter, „Inhalt melden“) und Einsprüche gegen Gameserver-Bans (Guide 43) – Form wie die API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakeModerationRoutes
{
    public const string ChildSafetyReport = "01JRP0000000000000000000R1";
    public const string ForumReport = "01JRP0000000000000000000R2";
    public const string MinecraftReport = "01JRP0000000000000000000R3";
    public const string Kaito = "01U3";
    public const string SiteBan = "01JMB0000000000000000000M1";
    public const string ForumBan = "01JMB0000000000000000000M2";
    public const string OpenAppeal = "01JAP0000000000000000000C1";
    public const string OtherAppeal = "01JAP0000000000000000000C2";
    public const string Word = "01JWF0000000000000000000W1";

    private const string ReadScopes = "\"news:read\",\"messages:read\",\"profile:read\",\"forum:read\",\"members:read\",\"servers:read\",\"wiki:read\"";

    /// <summary>Moderator mit allen Moderationsrechten, Einspruchs-Verwaltung und Melde-Scope.</summary>
    public const string MeModerator = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["moderation.reports","moderation.warn","moderation.ban","moderation.log","moderation.manage","gamebans.manage"]},
          "token":{"scopes":[
        """ + ReadScopes + """
        ,"moderation:read","moderation:write","moderation:manage","gamebans:manage","reports:write"]} }}
        """;

    /// <summary>Darf Meldungen und Sperren lesen, aber nicht verwarnen/sperren (Token ohne <c>moderation:write</c>).</summary>
    public const string MeReader = """
        {"data":{"id":"01USERNYX","slug":"nyx","name":"Nyx","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"moderator","label":"Moderator","color":"#E0324A"},"two_factor_enabled":true,"joined_at":"2021-05-01",
          "squads":[],"permissions":{"global":["moderation.reports","moderation.warn","moderation.ban"]},
          "token":{"scopes":["moderation:read","members:read"]} }}
        """;

    /// <summary>Mitglied ohne Moderationsrechte, mit Melde-Scope.</summary>
    public const string MeMember = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#A8ACB6"},"two_factor_enabled":false,"joined_at":"2022-01-01",
          "squads":[],"permissions":{"global":[]},
          "token":{"scopes":[
        """ + ReadScopes + """
        ,"reports:write"]} }}
        """;

    /// <summary>Server-Admin mit <c>servers.bans</c>: sieht Einsprüche, verwaltet aber keinen Forum-Bereich.</summary>
    public const string MeServerAdmin = """
        {"data":{"id":"01USERNYX","slug":"nyx","name":"Nyx","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"admin","label":"Admin","color":"#E0324A"},"two_factor_enabled":true,"joined_at":"2021-05-01",
          "squads":[],"permissions":{"global":["servers.bans"]},"token":{"scopes":["servers:read","gamebans:manage"]} }}
        """;

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        return (method, path) switch
        {
            ("POST", "/v1/reports") => """{"data":{"id":"01JRP0000000000000000000R9","status":"open"}}""",
            ("GET", "/v1/moderation/reports") => Reports(query),
            ("PATCH", var p) when p.StartsWith("/v1/moderation/reports/", StringComparison.Ordinal) => "{\"data\":" + Patched(p.Split('/')[^1], body) + "}",
            ("GET", "/v1/moderation/members/" + Kaito) => Member,
            ("POST", "/v1/moderation/members/" + Kaito + "/warnings") => """{"data":{"id":"01JWN0000000000000000000N9","points":2,"expires_at":"2027-01-28T12:00:00Z","active_points":3}}""",
            ("POST", "/v1/moderation/members/" + Kaito + "/bans") => "{\"data\":" + BanJson("01JMB0000000000000000000M9", "forum", "Beleidigungen im Forum", true, "2026-11-06T12:00:00Z", user: false) + "}",
            ("POST", var p) when p.StartsWith("/v1/moderation/warnings/", StringComparison.Ordinal) && p.EndsWith("/revoke", StringComparison.Ordinal) =>
                "{\"data\":{\"id\":\"" + p.Split('/')[4] + "\",\"revoked_at\":\"2026-10-30T12:00:00Z\"}}",
            ("POST", var p) when p.StartsWith("/v1/moderation/bans/", StringComparison.Ordinal) && p.EndsWith("/lift", StringComparison.Ordinal) =>
                "{\"data\":" + BanJson(p.Split('/')[4], "site", "Aufgehoben", false, null, user: false) + "}",
            ("GET", "/v1/moderation/bans") => Bans,
            ("GET", "/v1/moderation/log") => Log,
            ("GET", "/v1/moderation/words") => Words,
            ("POST", "/v1/moderation/words") => "{\"data\":{\"id\":\"01JWF0000000000000000000W9\",\"pattern\":" + (JsonNode.Parse(body)?["pattern"]?.ToJsonString() ?? "\"?\"")
                + ",\"replacement\":null,\"action\":" + (JsonNode.Parse(body)?["action"]?.ToJsonString() ?? "\"replace\"") + "}}",
            ("DELETE", var p) when p.StartsWith("/v1/moderation/words/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",

            // Einsprüche
            ("GET", "/v1/game-ban-appeals") => Appeals(query),
            ("POST", var p) when p.StartsWith("/v1/game-ban-appeals/", StringComparison.Ordinal) && p.EndsWith("/decision", StringComparison.Ordinal) => Decided(p.Split('/')[3], body),
            ("GET", "/v1/game-bans/settings") => Settings("01FB9", true),
            ("PATCH", "/v1/game-bans/settings") => JsonNode.Parse(body)?["appeals_board"] is null && JsonNode.Parse(body)?.AsObject().ContainsKey("appeals_board") == true
                ? Settings(null, false)
                : Settings("01FB9", true),
            ("POST", "/v1/game-bans/settings/appeals-board") => Settings("01FB9", true),
            _ => null,
        };
    }

    private static string Reports(string query)
    {
        if (query.Contains("filter[status]=resolved", StringComparison.Ordinal) || query.Contains("filter[status]=dismissed", StringComparison.Ordinal))
        {
            return "{\"data\":[" + Report(ForumReport, "forum_post", "spam", 0, "resolved", "Werbung für Cheat-Seite", "Beitrag ausgeblendet", "website") + "],\"meta\":{\"next_cursor\":null,\"prev_cursor\":null}}";
        }

        var items = new List<string>();
        var onlyChild = query.Contains("filter[reason]=child_safety", StringComparison.Ordinal);
        var source = query.Contains("filter[source]=minecraft", StringComparison.Ordinal) ? "minecraft" : query.Contains("filter[source]=website", StringComparison.Ordinal) ? "website" : null;
        if (source != "minecraft")
        {
            items.Add(Report(ChildSafetyReport, "message", "child_safety", 1, "open", "Fragt eine 14-jährige Spielerin per DM nach Wohnort und privater Nummer.", null, "website",
                excerpt: "Kaito → Lena: Schick mir mal deine Nummer, dann zocken wir privat."));
            if (!onlyChild)
            {
                items.Add(Report(ForumReport, "forum_post", "insult", 0, "open", "Beleidigt andere im Scrim-Thread", null, "website",
                    excerpt: "„Ihr seid so schlecht, löscht das Spiel“ (Thema: Scrim-Serie Herbst 2026)"));
            }
        }

        if (source != "website" && !onlyChild)
        {
            items.Add(Report(MinecraftReport, "minecraft_player", "harassment", 0, "open", "Beleidigt mich seit 10 Minuten per /msg", null, "minecraft",
                excerpt: "[29.10. 21:14] Notch → jeb_: du bist so schlecht\n[29.10. 21:15] Notch → jeb_: verlass den Server"));
        }

        return "{\"data\":[" + string.Join(',', items) + "],\"meta\":{\"next_cursor\":null,\"prev_cursor\":null}}";
    }

    private static string Report(string id, string type, string reason, int priority, string status, string? comment, string? resolution, string source, string? excerpt = null)
    {
        var minecraft = source == "minecraft";
        var target = minecraft ? "null" : "{\"id\":\"" + Kaito + "\",\"name\":\"Kaito\",\"slug\":\"kaito\"}";
        var details = minecraft
            ? """{"reporter":{"uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","name":"jeb_","edition":"java"},"reported":{"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Notch","edition":"java"},"server":{"id":"01SVMC","name":"RoG Network"},"network_server":"lobby","messages":[{"from_uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","to_uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","message":"du bist so schlecht","at":"2026-10-29T20:14:00Z"}]}"""
            : "null";
        var url = type switch
        {
            "forum_post" => "\"https://rebelsofgaming.test/de/forum/scrims/scrim-serie-herbst-2026#post-01p7\"",
            _ => "null",
        };
        return $$"""
            {"id":"{{id}}","type":"{{type}}","reportable_id":"01X","reason":"{{reason}}","priority":{{priority}},"comment":{{Str(comment)}},"excerpt":{{Str(excerpt)}},
             "url":{{url}},"locale":null,"status":"{{status}}","source":"{{source}}","details":{{details}},"resolution":{{Str(resolution)}},
             "target":{{target}},"reporter":{"id":"01U2","name":"{{(minecraft ? "Jeb" : "Nyx")}}"},"created_at":"2026-10-29T21:20:00Z","handled_at":null}
            """;
    }

    private static string Patched(string id, string body)
    {
        var json = JsonNode.Parse(body);
        return Report(id, "forum_post", "insult", 0, json?["status"]?.GetValue<string>() ?? "open", null, json?["resolution"]?.GetValue<string>(), "website");
    }

    private static string Str(string? value) => value is null ? "null" : JsonValue.Create(value)!.ToJsonString();

    private const string Member = """
        {"data":{"user":{"id":"01U3","name":"Kaito","slug":"kaito","has_avatar_upload":false,"has_banner":false},"active_points":3,
          "warnings":[
            {"id":"01JWN0000000000000000000N1","reason":"Spam im Forum","points":1,"rule_set_id":null,"rule_anchor":null,"active":true,"created_at":"2026-10-01T18:00:00Z","expires_at":"2026-12-30T18:00:00Z"},
            {"id":"01JWN0000000000000000000N2","reason":"Beleidigung im TeamSpeak","points":2,"rule_set_id":null,"rule_anchor":null,"active":true,"created_at":"2026-10-20T21:00:00Z","expires_at":"2027-01-18T21:00:00Z"},
            {"id":"01JWN0000000000000000000N3","reason":"Teamkill auf ET-Public","points":1,"rule_set_id":null,"rule_anchor":null,"active":false,"created_at":"2026-05-02T20:00:00Z","expires_at":"2026-07-31T20:00:00Z"}],
          "bans":[
            {"id":"01JMB0000000000000000000M2","scope":"forum","reason":"Schreibsperre nach 3 Punkten","public":false,"active":true,"starts_at":"2026-10-20T21:05:00Z","ends_at":"2026-10-27T21:05:00Z","lifted_at":null,"gameservers":null}]}}
        """;

    private static string BanJson(string id, string scope, string reason, bool active, string? endsAt, bool user = true, string name = "Kaito", string userId = Kaito) =>
        $$"""
        {"id":"{{id}}","scope":"{{scope}}","reason":"{{reason}}","public":false,"active":{{(active ? "true" : "false")}},"starts_at":"2026-10-20T21:05:00Z",
         "ends_at":{{Str(endsAt)}},"lifted_at":null,"gameservers":null{{(user ? ",\"user\":{\"id\":\"" + userId + "\",\"name\":\"" + name + "\",\"slug\":\"" + name.ToLowerInvariant() + "\"}" : string.Empty)}}}
        """;

    private static string Bans => "{\"data\":["
        + BanJson(SiteBan, "site", "Account-Sharing und Cheat-Verkauf", true, null, name: "Gast_Rambo", userId: "01U9") + ","
        + BanJson(ForumBan, "forum", "Schreibsperre nach 3 Punkten", true, "2026-11-03T21:05:00Z") + ","
        + BanJson("01JMB0000000000000000000M3", "gameserver", "Wallhack auf CS2-Retake (Demo im Forum)", true, "2026-11-13T19:00:00Z", name: "Sn1perKing", userId: "01U8")
        + "],\"meta\":{\"next_cursor\":null,\"prev_cursor\":null}}";

    private const string Log = """
        {"data":[
          {"id":"01L1","action":"ban.created","details":null,"actor":{"id":"01USERWAHKE","name":"Wahke"},"target":{"id":"01U3","name":"Kaito","slug":"kaito"},"created_at":"2026-10-20T21:05:00Z"},
          {"id":"01L2","action":"warning.created","details":null,"actor":{"id":"01U2","name":"Nyx"},"target":{"id":"01U3","name":"Kaito","slug":"kaito"},"created_at":"2026-10-20T21:00:00Z"},
          {"id":"01L3","action":"report.resolved","details":null,"actor":{"id":"01U2","name":"Nyx"},"target":{"id":"01U8","name":"Sn1perKing","slug":"sn1perking"},"created_at":"2026-10-18T19:30:00Z"}],
         "meta":{"next_cursor":"c2","prev_cursor":null}}
        """;

    private const string Words = """
        {"data":[
          {"id":"01JWF0000000000000000000W1","pattern":"noob*","replacement":"Neuling","action":"replace"},
          {"id":"01JWF0000000000000000000W2","pattern":"cheat-shop.example","replacement":null,"action":"block"},
          {"id":"01JWF0000000000000000000W3","pattern":"discord.gg/*","replacement":null,"action":"review"}]}
        """;

    private static string Appeals(string query)
    {
        var open = Appeal(OpenAppeal, "open", "^1Sn1per^7King", "Kaito", "steam", null, canDecide: true);
        var rejected = Appeal(OtherAppeal, "rejected", "xXCamperXx", "Gast_42", "code", "Demo zeigt eindeutig Wallhack.", canDecide: false);
        var data = query.Contains("filter[status]=rejected", StringComparison.Ordinal) ? rejected
            : query.Contains("filter[status]=all", StringComparison.Ordinal) ? open + "," + rejected
            : query.Contains("filter[status]=accepted", StringComparison.Ordinal) ? string.Empty
            : open;
        return "{\"data\":[" + data + "],\"meta\":{\"next_cursor\":null}}";
    }

    private static string Appeal(string id, string status, string banName, string user, string proof, string? note, bool canDecide) => $$"""
        {"id":"{{id}}","ban":{"id":"01JPB0000000000000000000B1","name":"{{banName}}","status":"{{(status == "accepted" ? "lifted" : "active")}}","url":"https://rebelsofgaming.test/de/bans/01JPB0000000000000000000B1"},
         "status":"{{status}}","proof":"{{proof}}","thread_url":"https://rebelsofgaming.test/de/forum/ban-einsprueche/einspruch-{{id.ToLowerInvariant()}}",
         "user":{"name":"{{user}}","slug":"{{user.ToLowerInvariant()}}"},"created_at":"2026-10-28T17:30:00Z","decided_at":null,"decision_note":{{Str(note)}},"can_decide":{{(canDecide ? "true" : "false")}}}
        """;

    private static string Decided(string id, string body)
    {
        var json = JsonNode.Parse(body);
        var status = json?["decision"]?.GetValue<string>() ?? "rejected";
        return "{\"data\":" + Appeal(id, status, "^1Sn1per^7King", "Kaito", "steam", json?["note"]?.GetValue<string>(), canDecide: false) + "}";
    }

    private static string Settings(string? boardId, bool isPublic) => $$"""
        {"data":{"appeals_board":{{(boardId is null ? "null" : "{\"id\":\"" + boardId + "\",\"name\":\"Ban-Einsprüche\",\"public\":" + (isPublic ? "true" : "false") + "}")}},
          "board_options":[{"id":"01FB9","name":"Ban-Einsprüche"},{"id":"01FB2","name":"Off-Topic"}],
          "min_plugin_version":"1.0.0","min_plugin_versions_env":[{"plugin":"rog-proxy","version":"0.5.0"}],
          "min_plugin_versions":[{"plugin":"rog-paper","version":"0.6.0"}]
        """ + "}}";
}
