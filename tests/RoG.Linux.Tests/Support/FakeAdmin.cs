namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Stufe 9 „J“: Verwaltung – Übersicht (Kennzahlen, Systemstatus, Audit-Log, Guide 31), Mod-Log
/// (Guide 28) und der allgemeine Editor (Bereich „Spiele“, Einstellungen „Clan“) – Form wie die API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakeAdminRoutes
{
    /// <summary>Wahke mit Verwaltungsrechten: Übersicht, Mod-Log, Mitglieder, Bereiche Spiele/Abzeichen/Team/Partner/Clan.</summary>
    public const string MeAdmin = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["admin.access","system.view","audit.view","moderation.log","moderation.reports","moderation.manage",
            "members.view","games.manage","badges.manage","team.manage","partners.manage","settings.manage","roles.view","news.write"]},
          "token":{"scopes":["admin:read","system:read","audit:read","moderation:read","moderation:manage","members:read","members:write",
            "games:read","games:write","cms:read","cms:write","settings:read","settings:write","roles:read","news:read","servers:read"]} }}
        """;

    /// <summary>Moderator: nur Mod-Log (Recht <c>moderation.log</c> + Scope <c>moderation:read</c>).</summary>
    public const string MeModerator = """
        {"data":{"id":"01USERNYX","slug":"nyx","name":"Nyx","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"moderator","label":"Moderator","color":"#E0324A"},"two_factor_enabled":true,"joined_at":"2021-05-01",
          "squads":[],"permissions":{"global":["moderation.log"]},"token":{"scopes":["moderation:read","members:read"]} }}
        """;

    /// <summary>Verwaltungsrecht vorhanden, aber das Gerät hat keine Verwaltungs-Scopes (alte Anmeldung).</summary>
    public const string MeAdminWithoutScopes = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["admin.access","moderation.log","games.manage"]},"token":{"scopes":["news:read","members:read"]} }}
        """;

    /// <summary>Mitglied ohne Verwaltungsrecht.</summary>
    public const string MeMember = """
        {"data":{"id":"01USERKAITO","slug":"kaito","name":"Kaito","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#A8ACB6"},"two_factor_enabled":false,"joined_at":"2022-01-01",
          "squads":[],"permissions":{"global":["news.write"]},"token":{"scopes":["news:read","members:read"]} }}
        """;

    public const string Dashboard = """
        {"data":{"kpis":[
          {"key":"members","label":"Mitglieder","value":42,"url":"https://admin.rebelsofgaming.com/members"},
          {"key":"online","label":"Online","value":7,"url":null},
          {"key":"applications","label":"Offene Bewerbungen","value":3,"url":"https://admin.rebelsofgaming.com/applications"},
          {"key":"reports","label":"Offene Meldungen","value":2,"url":"https://admin.rebelsofgaming.com/moderation"},
          {"key":"servers","label":"Gameserver online","value":"5/6","url":null},
          {"key":"events","label":"Termine diese Woche","value":null,"url":null}],
          "system":{"status":"degraded","checked_at":"2026-10-30T08:00:00Z"},
          "app":{"version":"0.53.2","commit":"4deeb27","deployed_at":null}}}
        """;

    public const string SystemStatus = """
        {"data":{"status":"degraded","checked_at":"2026-10-30T08:00:00Z",
          "app":{"version":"0.53.2","commit":"4deeb27","deployed_at":"2026-10-29T18:00:00Z"},
          "checks":[
            {"key":"database","label":"Datenbank","ok":true,"value":"mariadb 11.4.3","critical":true,"checked_at":"2026-10-30T08:00:00Z","help":null},
            {"key":"queue","label":"Warteschlange","ok":true,"value":"0 wartend, 0 fehlgeschlagen","critical":true,"checked_at":"2026-10-30T08:00:00Z","help":null},
            {"key":"mail","label":"Mailversand","ok":false,"value":"smtp.example:587 – auth failed","critical":false,"checked_at":"2026-10-30T07:52:00Z","help":"SMTP-Anmeldung fehlgeschlagen: Zugangsdaten in den Einstellungen prüfen."},
            {"key":"pterodactyl","label":"Pterodactyl","ok":true,"value":"6 Server","critical":false,"checked_at":"2026-10-30T08:00:00Z","help":null},
            {"key":"twitch","label":"Twitch","ok":null,"value":"nicht eingerichtet","critical":false,"checked_at":"2026-10-30T08:00:00Z","help":null}]}}
        """;

    private const string AuditPage1 = """
        {"data":[
          {"id":"01AUD1","action":"role.assigned","actor":{"id":"01USERWAHKE","name":"Wahke"},"subject":{"type":"user","label":"Kaito"},"created_at":"2026-10-30T07:45:00Z"},
          {"id":"01AUD2","action":"server.restarted","actor":{"id":"01USERNYX","name":"Nyx"},"subject":{"type":"server","label":"RoG ET: Sniper"},"created_at":"2026-10-30T06:10:00Z"},
          {"id":"01AUD3","action":"news.published","actor":{"id":"01USERWAHKE","name":"Wahke"},"subject":{"type":"news","label":"Scrim-Serie Herbst 2026"},"created_at":"2026-10-29T19:30:00Z"}],
         "meta":{"per_page":50,"next_cursor":"c2"}}
        """;

    private const string AuditPage2 = """
        {"data":[
          {"id":"01AUD4","action":"settings.updated","actor":{"id":"01USERWAHKE","name":"Wahke"},"subject":{"type":"settings","label":"Clan"},"created_at":"2026-10-28T12:00:00Z"}],
         "meta":{"per_page":50,"next_cursor":null}}
        """;

    private const string AuditSearch = """
        {"data":[
          {"id":"01AUD2","action":"server.restarted","actor":{"id":"01USERNYX","name":"Nyx"},"subject":{"type":"server","label":"RoG ET: Sniper"},"created_at":"2026-10-30T06:10:00Z"}],
         "meta":{"per_page":50,"next_cursor":null}}
        """;

    private const string ModLogPage1 = """
        {"data":[
          {"id":"01ML1","action":"warn","details":{"points":2},"actor":{"id":"01USERNYX","name":"Nyx"},"target":{"id":"01USERX1","name":"Sn1perKing"},"created_at":"2026-10-30T07:20:00Z"},
          {"id":"01ML2","action":"ban","details":{"scope":"forum","days":7},"actor":{"id":"01USERWAHKE","name":"Wahke"},"target":{"id":"01USERX2","name":"Trollface"},"created_at":"2026-10-29T21:05:00Z"},
          {"id":"01ML3","action":"report_resolved","details":null,"actor":{"id":"01USERNYX","name":"Nyx"},"target":null,"created_at":"2026-10-29T18:40:00Z"}],
         "meta":{"next_cursor":"m2","prev_cursor":null}}
        """;

    private const string ModLogPage2 = """
        {"data":[
          {"id":"01ML4","action":"unban","details":null,"actor":{"id":"01USERWAHKE","name":"Wahke"},"target":{"id":"01USERX3","name":"Kaito"},"created_at":"2026-10-27T10:00:00Z"}],
         "meta":{"next_cursor":null,"prev_cursor":"m1"}}
        """;

    public const string GameEt = """
        {"data":{"id":"g1","slug":"et","name":"Wolfenstein: Enemy Territory","short":"ET","platform":"pc","genre":"Shooter","color":"#D4AE3C",
          "emoji":null,"channel_prefix":"et","clan_tag":"RoG|","clan_name":"Rebels of Gaming","id_format":"guid","stats_source":"server",
          "stats_provider":null,"sort":10,"active":true}}
        """;

    public const string SettingsClan = """
        {"data":{"name":"Rebels of Gaming","short":"RoG","founded":2020,"discord_invite":"https://discord.gg/rog",
          "maintenance":{"enabled":false,"message":{"de":"Wir sind gleich zurück.","en":"We'll be right back.","fr":"Nous revenons tout de suite."}},
          "apps":{"ios_app_id":"6738291045","android_package":"com.rebelsofgaming.app","banner":true}}}
        """;

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = request.RequestUri!.Query;
        return (method, path) switch
        {
            ("GET", "/v1/admin/dashboard") => Dashboard,
            ("GET", "/v1/system/status") => SystemStatus,
            ("GET", "/v1/audit-log") => query.Contains("filter%5Bsearch%5D", StringComparison.Ordinal)
                ? (query.Contains("Sniper", StringComparison.Ordinal) ? AuditSearch : """{"data":[],"meta":{"next_cursor":null}}""")
                : query.Contains("cursor=c2", StringComparison.Ordinal) ? AuditPage2 : AuditPage1,
            ("GET", "/v1/moderation/log") => query.Contains("cursor=m2", StringComparison.Ordinal) ? ModLogPage2 : ModLogPage1,
            ("GET", "/v1/games/et") => GameEt,
            ("PATCH", "/v1/games/et") => GameEt.Replace("\"short\":\"ET\"", "\"short\":\"W:ET\"", StringComparison.Ordinal),
            ("POST", "/v1/games") => """{"data":{"id":"g9","slug":"rtcw","name":"Return to Castle Wolfenstein","short":"RTCW","platform":"pc","active":true}}""",
            ("GET", "/v1/games/mc") => """{"data":{"id":"g3","slug":"mc","name":"Minecraft","short":"MC","platform":"pc","genre":"Sandbox","sort":30,"active":true}}""",
            ("DELETE", "/v1/games/mc") => """{"data":{"deleted":true}}""",
            ("GET", "/v1/settings/clan") => SettingsClan,
            ("PATCH", "/v1/settings/clan") => SettingsClan,
            _ => null,
        };
    }
}
