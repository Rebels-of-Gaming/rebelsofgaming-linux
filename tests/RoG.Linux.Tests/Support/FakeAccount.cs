namespace RoG.Linux.Tests.Support;

/// <summary>
/// Konto „Paket A“ und Benachrichtigungs-Einstellungen: eigene Bewerbungen (Guide 07), Abwesenheit (16), Minecraft-Accounts,
/// Wallet und Einstellungen (44/46), Geräte und Datenexport (35/10), Konto löschen/Clan verlassen, Benachrichtigungstypen
/// und -einstellungen (12). Antworten in der Form der API; Fehler (RFC 9457) über <see cref="Problem"/>.
/// </summary>
[FakeRoutes]
internal static class FakeAccountRoutes
{
    /// <summary>Gültiger Link-Code der Fake-Vorschau (6 Zeichen ohne 0/O/1/I).</summary>
    public const string LinkCode = "K7M2QX";

    /// <summary>Wie <c>FakeData.Me</c>, aber mit allen Scopes für Minecraft, Profil, Geräte und Bewerbungen.</summary>
    public const string MeFull = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":"wahke@example.test","avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[{"slug":"cs2","name":"CS2-Squad"}],"permissions":{"global":[]},
          "token":{"scopes":["profile:read","profile:write","minecraft:read","minecraft:write","devices:manage","applications:read","applications:write","absences:read","absences:write","notifications:read","notifications:write"]} }}
        """;

    /// <summary>Fehlerantwort (RFC 9457); <c>FakeApi</c> setzt den Status aus <c>status</c>.</summary>
    public static string Problem(int status, string code, string? field = null, string? message = null) =>
        field is null
            ? $$"""{"type":"about:blank","title":"Fehler","status":{{status}},"code":"{{code}}"}"""
            : $$$"""{"type":"about:blank","title":"Fehler","status":{{{status}}},"code":"{{{code}}}","detail":"{{{message}}}","errors":{"{{{field}}}":["{{{message}}}"]}}""";

    public const string Applications = """
        {"data":[
          {"id":"01JBAPPCS2TRIAL0000000001","squad":{"slug":"cs2","name":"CS2-Squad","game":"Counter-Strike 2"},"status":"trial","status_label":"Probezeit",
           "step":3,"submitted_at":"2026-10-12T18:04:00Z","trial_started_at":"2026-10-16T19:00:00Z","trial_ends_at":"2026-11-13T23:00:00Z",
           "vote_ends_at":null,"decision_message":null,"vote":null,
           "tasks":[{"id":"t1","title":"An zwei Scrim-Abenden teilnehmen","done":true},{"id":"t2","title":"Retake-Server: 10 Runden mit dem Squad","done":false},{"id":"t3","title":"Vorstellung im Forum","done":true}],
           "timeline":[{"type":"submitted","label":"Bewerbung eingereicht","at":"2026-10-12T18:04:00Z"},{"type":"accepted_trial","label":"Zur Probezeit eingeladen","at":"2026-10-16T19:00:00Z"}],
           "messages":[{"id":"m1","body":"Willkommen zur Probezeit! Dienstag 20 Uhr ist Scrim – komm gern in den Voice.","from_squad":true,"author":"Krater","at":"2026-10-16T19:05:00Z"},
                       {"id":"m2","body":"Danke, bin dabei!","from_squad":false,"author":null,"at":"2026-10-16T19:40:00Z"}],
           "can_withdraw":true,"links":{"squad":"https://example.test/de/squads/cs2"}},
          {"id":"01JBAPPETOLD0000000000002","squad":{"slug":"et","name":"ET-Veteranen","game":"Wolfenstein: Enemy Territory"},"status":"rejected","status_label":"Abgelehnt",
           "step":4,"submitted_at":"2026-03-02T17:00:00Z","trial_started_at":null,"trial_ends_at":null,"vote_ends_at":null,
           "decision_message":"Der Squad ist gerade voll – versuch es im Frühjahr wieder.","vote":{"participation":80,"quorum_percent":60,"quorum_met":true,"ends_at":"2026-03-09T17:00:00Z"},
           "tasks":[],"timeline":[{"type":"submitted","label":"Bewerbung eingereicht","at":"2026-03-02T17:00:00Z"},{"type":"rejected","label":"Abgelehnt","at":"2026-03-10T09:00:00Z"}],
           "messages":[],"can_withdraw":false,"links":{"squad":"https://example.test/de/squads/et"}}
        ]}
        """;

    public const string Absences = """
        {"data":[
          {"id":"01JBABS0000000000000000001","starts_on":"2026-10-28","ends_on":"2026-11-02","state":"active","reason":"Urlaub an der Mosel","absent_role":true,"reason_removed_at":null,"created_at":"2026-10-20T10:00:00Z"},
          {"id":"01JBABS0000000000000000002","starts_on":"2026-12-22","ends_on":"2027-01-03","state":"upcoming","reason":null,"absent_role":false,"reason_removed_at":null,"created_at":"2026-10-21T10:00:00Z"}
        ]}
        """;

    public const string MinecraftAccounts = """
        {"data":[
          {"id":"01JCMCJAVA0000000000000001","uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","xuid":null,"name":"Wahke","edition":"java","platform":"java",
           "linked_at":"2026-09-14T17:20:00Z","last_seen_at":"2026-10-29T21:12:00Z"},
          {"id":"01JCMCBEDROCK0000000000002","uuid":"00000000-0000-0000-0009-01f2a3b4c5d6","xuid":"2535412345678901","name":".WahkeDeck","edition":"bedrock","platform":"xbox",
           "linked_at":"2026-10-02T12:00:00Z","last_seen_at":null}
        ]}
        """;

    public const string Wallet = """
        {"data":{"balance":3240,
          "daily":{"streak":5,"best_streak":12,"last_claimed_on":"2026-10-29","claimed_today":false,"next_at":"2026-10-30T22:00:00Z"},
          "transactions":[
            {"id":"01JB1","type":"shop_buy","direction":"out","counterparty":"system","amount":120,"reason":"2x DIAMOND","source":null,"period_start":null,"server":"freebuild","created_at":"2026-10-29T20:00:00Z"},
            {"id":"01JB2","type":"earning","direction":"in","counterparty":"system","amount":37,"reason":null,"source":"job:miner","period_start":"2026-10-29T19:00:00Z","server":null,"created_at":"2026-10-29T19:59:00Z"},
            {"id":"01JB3","type":"daily","direction":"in","counterparty":"system","amount":50,"reason":null,"source":null,"period_start":null,"server":null,"created_at":"2026-10-29T08:00:00Z"}]}}
        """;

    public const string Devices = """
        {"data":[
          {"id":"01JCDEVLINUX00000000000001","name":"Linux-App – PC von wahke","platform":"linux","app_version":"0.6.0","push":false,"push_provider":null,"scopes":["*"],"current":true,"last_active_at":"2026-10-30T13:50:00Z","created_at":"2026-10-01T10:00:00Z"},
          {"id":"01JCDEVANDROID000000000002","name":"Android – Pixel 8","platform":"android","app_version":"0.5.1","push":true,"push_provider":"fcm","scopes":["*"],"current":false,"last_active_at":"2026-10-29T22:10:00Z","created_at":"2026-08-11T10:00:00Z"},
          {"id":"01JCDEVWINDOWS000000000003","name":"Windows-App – Gaming-PC","platform":"windows","app_version":"2.5.26","push":false,"push_provider":null,"scopes":["*"],"current":false,"last_active_at":"2026-10-27T18:30:00Z","created_at":"2026-05-03T10:00:00Z"}
        ]}
        """;

    public const string Exports = """
        {"data":[
          {"id":"01JCEXP0000000000000000001","status":"ready","size":4718592,"files_included":12,"files_listed":3,"created_at":"2026-10-28T09:00:00Z","ready_at":"2026-10-28T09:02:00Z","expires_at":"2026-11-04T09:02:00Z","downloaded_at":null,"download_url":null}
        ]}
        """;

    public const string NotificationTypes = """
        {"data":[
          {"key":"forum.reply","group":"forum","label":"Antwort auf dein Thema","defaults":["in_app","mail"],"channels":["in_app","mail","discord","push"],"inbox_optional":false},
          {"key":"forum.mention","group":"forum","label":"Erwähnung im Forum","defaults":["in_app"],"channels":["in_app","mail","discord","push"],"inbox_optional":false},
          {"key":"messages.received","group":"messages","label":"Neue Direktnachricht","defaults":["in_app","discord"],"channels":["in_app","mail","discord","push"],"inbox_optional":false},
          {"key":"events.reminder","group":"events","label":"Erinnerung an ein Event","defaults":["in_app","discord"],"channels":["in_app","mail","discord","push"],"inbox_optional":false},
          {"key":"news.published","group":"news","label":"Neue News","defaults":["in_app"],"channels":["in_app","mail","push"],"inbox_optional":true},
          {"key":"applications.vote_opened","group":"squads","label":"Neue Bewerbung zur Abstimmung","defaults":["in_app","mail"],"channels":["in_app","mail","discord"],"inbox_optional":false}
        ]}
        """;

    public const string NotificationSettings = """
        {"data":{"preferences":{"forum.reply":["mail"],"news.published":[]},"digest":"daily","quiet_from":"22:00","quiet_until":"08:00",
          "available":{"mail":true,"discord":true,"push":false},"vapid_public_key":null}}
        """;

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        return (request.Method.Method, path) switch
        {
            ("GET", "/v1/me/applications") => Applications,
            ("POST", var p) when p.StartsWith("/v1/me/applications/", StringComparison.Ordinal) && p.EndsWith("/messages", StringComparison.Ordinal) =>
                """{"data":{"id":"m3","body":"ok","from_squad":false,"author":null,"at":"2026-10-30T12:00:00Z"}}""",
            ("POST", var p) when p.StartsWith("/v1/me/applications/", StringComparison.Ordinal) && p.EndsWith("/withdraw", StringComparison.Ordinal) =>
                """{"data":{"id":"01JBAPPCS2TRIAL0000000001","status":"withdrawn"}}""",
            ("GET", "/v1/me/absences") => Absences,
            ("POST", "/v1/me/absences") =>
                """{"data":{"id":"01JBABS0000000000000000003","starts_on":"2026-11-10","ends_on":"2026-11-12","state":"upcoming","reason":null,"absent_role":false,"reason_removed_at":null,"created_at":"2026-10-30T12:00:00Z"}}""",
            ("DELETE", var p) when p.StartsWith("/v1/me/absences/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("GET", "/v1/me/minecraft/accounts") => MinecraftAccounts,
            ("DELETE", var p) when p.StartsWith("/v1/me/minecraft/accounts/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("POST", "/v1/me/minecraft/link") when body.Contains("\"confirm\":true", StringComparison.Ordinal) =>
                """{"data":{"id":"01JCMCJAVANEW0000000000003","uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","xuid":null,"name":"Krater","edition":"java","platform":"java","linked_at":"2026-10-30T12:00:00Z","last_seen_at":null}}""",
            ("POST", "/v1/me/minecraft/link") => body.Contains(LinkCode, StringComparison.Ordinal)
                ? """{"data":{"name":"Krater","edition":"java","platform":"java","server":"RoG Network","requested_at":"2026-10-30T11:58:00Z","minutes_ago":2,"expires_at":"2026-10-30T12:08:00Z"}}"""
                : Problem(422, "invalid_link_code"),
            ("GET", "/v1/me/minecraft/settings") => """{"data":{"show_website_messages":false}}""",
            ("PATCH", "/v1/me/minecraft/settings") => body.Contains("true", StringComparison.Ordinal)
                ? """{"data":{"show_website_messages":true}}"""
                : """{"data":{"show_website_messages":false}}""",
            ("GET", "/v1/me/minecraft/wallet") => Wallet,
            ("GET", "/v1/devices") => Devices,
            ("DELETE", var p) when p.StartsWith("/v1/devices/", StringComparison.Ordinal) => """{"data":{"deleted":true}}""",
            ("GET", "/v1/me/exports") => Exports,
            ("POST", "/v1/me/exports") =>
                """{"data":{"id":"01JCEXP0000000000000000002","status":"pending","size":null,"files_included":0,"files_listed":0,"created_at":"2026-10-30T12:00:00Z","ready_at":null,"expires_at":null,"downloaded_at":null,"download_url":null}}""",
            ("GET", var p) when p.StartsWith("/v1/me/exports/", StringComparison.Ordinal) && p.EndsWith("/download", StringComparison.Ordinal) => "PK-fake-zip",
            ("DELETE", "/v1/me") => body.Contains("\"falsch\"", StringComparison.Ordinal)
                ? Problem(422, "validation_failed", "confirmation", "Das Passwort stimmt nicht.")
                : """{"data":{"deleted":true}}""",
            ("POST", "/v1/me/leave-clan") => """{"data":{"is_member":false,"status":"former","rank":null}}""",
            ("GET", "/v1/notifications/types") => NotificationTypes,
            ("GET", "/v1/me/notification-settings") => NotificationSettings,
            ("PATCH", "/v1/me/notification-settings") => """{"data":{"saved":true}}""",
            _ => null,
        };
    }
}
