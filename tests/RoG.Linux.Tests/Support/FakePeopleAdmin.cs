using System.Text.Json.Nodes;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Stufe 9 „K“: Verwaltung – Mitglieder (Guide 36/39), Bewerbungen (Guide 07 „Leadership“) und
/// Squad-Verwaltung (Squad-Rollen, Line-up, Profil, Archiv, Logo/Header). Form wie die API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakePeopleAdminRoutes
{
    public const string SquadSlug = "enemy-territory";
    public const string SquadId = "01SQ1";
    public const string AppNyx = "01JAPP00000000000000000001";
    public const string AppRex = "01JAPP00000000000000000002";
    public const string AppEmpty = "01JAPP00000000000000000003";

    /// <summary>Leitung mit globalen Rechten: Mitglieder sehen, Rollen vergeben, Bewerbungen sichten/entscheiden, Squads verwalten.</summary>
    public const string MeLeader = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["members.view","roles.assign","applications.view","applications.review","squads.manage","squad.members","moderation.warn"]},
          "token":{"scopes":["members:read","roles:read","roles:write","applications:read","applications:write","squads:read","squads:write","uploads:write"]} }}
        """;

    /// <summary>Squad-Leader von ET: Rechte nur im Squad-Scope, Mitglieder nur lesend, keine Rollen.</summary>
    public const string MeSquadLeader = """
        {"data":{"id":"01U2","slug":"nyx","name":"Nyx","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#F1EFEA"},"two_factor_enabled":true,"joined_at":"2021-05-01",
          "squads":[],"permissions":{"global":[],"squad:01SQ1":["members.view","applications.view","applications.review","squad.members"]},
          "token":{"scopes":["members:read","applications:read","applications:write","squads:read","squads:write"]} }}
        """;

    /// <summary>Mitglied ohne Verwaltungsrechte (Token mit allen Scopes – Rechte fehlen trotzdem).</summary>
    public const string MeMember = """
        {"data":{"id":"01U3","slug":"kaito","name":"Kaito","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#F1EFEA"},"two_factor_enabled":false,"joined_at":"2022-01-01",
          "squads":[],"permissions":{"global":[]},
          "token":{"scopes":["members:read","roles:write","applications:read","applications:write","squads:read","squads:write"]} }}
        """;

    /// <summary>Squad-Rollen zur Auswahl (<c>GET /v1/squads/{squad}/roles</c> – per Override, weil FakeApi GETs auf Squads selbst beantwortet).</summary>
    public const string SquadRoles = """
        {"data":[
          {"id":"r1","name":"squad-leader","label":"Squad-Leader","color":"#E0324A","icon":"crown","sort":10,"assignable":false},
          {"id":"r3","name":"squad-co-leader","label":"Co-Leader","color":"#D4AE3C","icon":"star","sort":20,"assignable":true},
          {"id":"r2","name":"member","label":"Member","color":"#F1EFEA","icon":null,"sort":30,"assignable":true}
        ]}
        """;

    /// <summary>Overrides für GETs, die <see cref="FakeApi"/> sonst selbst beantwortet.</summary>
    public static void Install(FakeApi api, string me)
    {
        api.Overrides["/v1/me"] = me;
        api.Overrides["/v1/squads/" + SquadSlug + "/roles"] = SquadRoles;
        api.Overrides["/v1/roles"] = Roles; // die Minecraft-Fakes beantworten /v1/roles mit eigenen Rollen
    }

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        return (method, path) switch
        {
            // Mitglieder-Verwaltung
            ("GET", "/v1/admin/members") => AdminMembers(query),
            ("GET", "/v1/admin/members/nyx") => "{\"data\":" + MemberNyx + "}",
            ("GET", "/v1/admin/members/blizzard") => "{\"data\":" + MemberBlizzard + "}",
            ("GET", "/v1/roles") => Roles,
            ("POST", "/v1/members/nyx/roles") => Assigned(body),
            ("POST", "/v1/members/blizzard/roles") => Problem(403, "forbidden", "Diese Rolle liegt über deiner eigenen Position."),
            ("DELETE", var p) when p.StartsWith("/v1/members/", StringComparison.Ordinal) && p.Contains("/roles/", StringComparison.Ordinal) => "{\"data\":null}",

            // Bewerbungen
            ("GET", "/v1/applications") => Applications(query),
            ("GET", "/v1/applications/" + AppNyx) => "{\"data\":" + ApplicationNyx + "}",
            ("GET", "/v1/applications/" + AppRex) => "{\"data\":" + ApplicationRex + "}",
            ("POST", var p) when p.StartsWith("/v1/applications/" + AppRex + "/decisions/", StringComparison.Ordinal) =>
                Problem(409, "invalid_transition", "Die Bewerbung ist nicht mehr in diesem Schritt."),
            ("POST", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.Contains("/decisions/", StringComparison.Ordinal) =>
                """{"data":{"id":"01JAPP00000000000000000001","status":"trial"}}""",
            ("POST", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.EndsWith("/remind", StringComparison.Ordinal) => """{"data":{"reminded":2}}""",
            ("POST", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.EndsWith("/messages", StringComparison.Ordinal) =>
                """{"data":{"id":"01JM9","internal":true,"body":"ok","at":"2026-10-30T10:00:00Z"}}""",
            ("POST", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.EndsWith("/tasks", StringComparison.Ordinal) =>
                """{"data":{"id":"t9","title":"Neu","done":false,"done_at":null}}""",
            ("PATCH" or "DELETE", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.Contains("/tasks/", StringComparison.Ordinal) =>
                """{"data":{"id":"t1","title":"Clanabend","done":true,"done_at":"2026-10-30T10:00:00Z"}}""",

            // Squad-Verwaltung
            ("PATCH", "/v1/squads/" + SquadSlug) => "{\"data\":" + JsonNode.Parse(body)?.ToJsonString() + "}",
            ("PUT", "/v1/squads/" + SquadSlug + "/members/blizzard") => Problem(403, "forbidden", "Diese Rolle liegt über deiner eigenen Position."),
            ("PUT", var p) when p.StartsWith("/v1/squads/" + SquadSlug + "/members/", StringComparison.Ordinal) => """{"data":{"saved":true}}""",
            ("DELETE", var p) when p.StartsWith("/v1/squads/" + SquadSlug + "/members/", StringComparison.Ordinal) => """{"data":null}""",
            ("POST", "/v1/squads/" + SquadSlug + "/archive") => """{"data":{"archived":true}}""",
            ("POST", "/v1/squads/" + SquadSlug + "/logo") or ("POST", "/v1/squads/" + SquadSlug + "/header") => """{"data":{"status":"processing"}}""",
            ("DELETE", "/v1/squads/" + SquadSlug + "/logo") or ("DELETE", "/v1/squads/" + SquadSlug + "/header") => """{"data":null}""",
            _ => null,
        };
    }

    private static string Problem(int status, string code, string detail) =>
        $$"""{"type":"about:blank","title":"Fehler","status":{{status}},"code":"{{code}}","detail":"{{detail}}"}""";

    private static string AdminMembers(string query)
    {
        if (query.Contains("filter[search]=zzz", StringComparison.Ordinal))
        {
            return """{"data":[],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"/v1/admin/members","per_page":50,"next_cursor":null,"prev_cursor":null}}""";
        }

        if (query.Contains("cursor=c2", StringComparison.Ordinal))
        {
            return "{\"data\":[" + Row("01U9", "rookie", "Rookie", "Trial", "[]", null) + "],"
                + "\"links\":{\"first\":null,\"last\":null,\"prev\":null,\"next\":null},\"meta\":{\"path\":\"/v1/admin/members\",\"per_page\":50,\"next_cursor\":null,\"prev_cursor\":null}}";
        }

        var former = query.Contains("filter[status]=former", StringComparison.Ordinal);
        var rows = former
            ? Row("01U7", "oldschool", "OldSchool", null, "[]", "former")
            : string.Join(",",
                Row("01U2", "nyx", "Nyx", "Member", """[{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"}]""", null),
                Row("01U3", "kaito", "Kaito", "Member", """[{"id":"01SQ3","slug":"minecraft","name":"Minecraft"}]""", null),
                Row("01U4", "blizzard", "Blizzard", "Admin", """[{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"}]""", null));
        return "{\"data\":[" + rows + "],\"links\":{\"first\":null,\"last\":null,\"prev\":null,\"next\":null},"
            + "\"meta\":{\"path\":\"/v1/admin/members\",\"per_page\":50,\"next_cursor\":" + (former ? "null" : "\"c2\"") + ",\"prev_cursor\":null}}";
    }

    private static string Row(string id, string slug, string name, string? rank, string squads, string? status)
    {
        var rankJson = rank is null
            ? "null"
            : "{\"id\":\"rk-" + rank.ToLowerInvariant() + "\",\"name\":\"" + rank.ToLowerInvariant() + "\",\"label\":\"" + rank + "\",\"color\":\"#F1EFEA\"}";
        return "{\"id\":\"" + id + "\",\"slug\":\"" + slug + "\",\"name\":\"" + name + "\",\"avatar_url\":null,\"rank\":" + rankJson
            + ",\"status\":" + (status is null ? "null" : "\"" + status + "\"") + ",\"activity\":null,\"last_seen_at\":null,\"joined_at\":\"2021-05-01T18:00:00Z\",\"squads\":" + squads
            + ",\"roles\":null,\"account\":{\"status\":\"" + (status ?? "active") + "\",\"email\":null,\"two_factor\":null,\"created_at\":\"2021-05-01T18:00:00Z\"}}";
    }

    private static string MemberNyx => """
        {"id":"01U2","slug":"nyx","name":"Nyx","avatar_url":null,"rank":{"id":"rk-member","name":"member","label":"Member","color":"#F1EFEA"},
         "status":null,"activity":null,"last_seen_at":"2026-10-29T21:14:00Z","joined_at":"2021-05-01T18:00:00Z",
         "squads":[{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"}],
         "roles":[
           {"id":"01RA1","role":{"id":"rk-member","name":"member","label":"Member","type":"rank","color":"#F1EFEA","sort":50,"is_system":false},"scope_type":"global","scope_id":null,"expires_at":null},
           {"id":"01RA2","role":{"id":"r3","name":"squad-co-leader","label":"Co-Leader","type":"squad","color":"#D4AE3C","sort":20,"is_system":false},"scope_type":"squad","scope_id":"01SQ1","expires_at":"2026-12-31T23:00:00Z"}
         ],
         "badges":[],"profile":null,
         "account":{"status":"active","email":"nyx@example.test","two_factor":true,"created_at":"2021-05-01T18:00:00Z"}}
        """;

    private static string MemberBlizzard => """
        {"id":"01U4","slug":"blizzard","name":"Blizzard","avatar_url":null,"rank":{"id":"rk-admin","name":"admin","label":"Admin","color":"#E0324A"},
         "status":null,"activity":null,"last_seen_at":null,"joined_at":"2020-04-01T18:00:00Z",
         "squads":[{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"}],
         "roles":[{"id":"01RA9","role":{"id":"rk-admin","name":"admin","label":"Admin","type":"rank","color":"#E0324A","sort":5,"is_system":false},"scope_type":"global","scope_id":null,"expires_at":null}],
         "badges":[],"profile":null,
         "account":{"status":"active","email":null,"two_factor":null,"created_at":"2020-04-01T18:00:00Z"}}
        """;

    private static string Roles => """
        {"data":[
          {"id":"rk-admin","name":"admin","label":"Admin","type":"rank","color":"#E0324A","sort":5,"is_system":false},
          {"id":"r3","name":"squad-co-leader","label":"Co-Leader","type":"squad","color":"#D4AE3C","sort":20,"is_system":false},
          {"id":"rk-member","name":"member","label":"Member","type":"rank","color":"#F1EFEA","sort":50,"is_system":false},
          {"id":"rk-eventteam","name":"event-team","label":"Event-Team","type":"function","color":"#3DDC84","sort":60,"is_system":false}
        ],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"/v1/roles","per_page":100,"next_cursor":null,"prev_cursor":null}}
        """;

    private static string Assigned(string body)
    {
        var json = JsonNode.Parse(body);
        var scope = json?["scope_type"]?.GetValue<string>();
        return "{\"data\":{\"id\":\"01RA7\",\"role\":{\"id\":\"" + json?["role_id"]?.GetValue<string>() + "\",\"name\":\"event-team\",\"label\":\"Event-Team\",\"type\":\"function\",\"color\":\"#3DDC84\",\"sort\":60,\"is_system\":false},"
            + "\"scope_type\":" + (scope is null ? "\"global\"" : "\"" + scope + "\"") + ",\"scope_id\":" + (json?["scope_id"]?.GetValue<string>() is { } id ? "\"" + id + "\"" : "null") + ",\"expires_at\":null}}";
    }

    private static string Applications(string query)
    {
        if (query.Contains("filter[status]=rejected", StringComparison.Ordinal))
        {
            return """{"data":[],"meta":{"next_cursor":null}}""";
        }

        return """
            {"data":[
              {"id":"01JAPP00000000000000000001","status":"trial","applicant":{"id":"01U2","slug":"nyx","name":"Nyx"},"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},
               "submitted_at":"2026-10-12T17:30:00Z","vote":null},
              {"id":"01JAPP00000000000000000002","status":"voting","applicant":{"id":"01U8","slug":"rex","name":"Rex"},"squad":{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"},
               "submitted_at":"2026-10-01T10:00:00Z","vote":{"cast":4,"eligible":6,"participation":67,"result":"pending"}}
            ],"meta":{"next_cursor":null}}
            """;
    }

    private static string ApplicationNyx => """
        {"id":"01JAPP00000000000000000001","status":"trial","applicant":{"id":"01U2","slug":"nyx","name":"Nyx"},"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},
         "age":24,"referral":"Kaito",
         "answers":[
           {"question_id":"q1","label":"Wie lange spielst du schon ET?","type":"text","answer":"Seit 2006, mit Pausen."},
           {"question_id":"q2","label":"Welche Klassen spielst du am liebsten?","type":"textarea","answer":"Engineer und Field Ops – Objective first."},
           {"question_id":"q3","label":"Hast du ein funktionierendes Headset?","type":"yes_no","answer":true}
         ],
         "ban_check":[{"key":"discord","status":"ok","message":"Keine Sperre auf Discord"},{"key":"gamebans","status":"warn","message":"Abgelaufener Gameserver-Ban (2024)"}],
         "tasks":[{"id":"t1","title":"Am Clanabend teilnehmen","done":true,"done_at":"2026-10-20T21:00:00Z"},{"id":"t2","title":"Zwei Trainings mitspielen","done":false,"done_at":null}],
         "activity":[{"date":"2026-10-20","minutes":{"discord_voice":95,"teamspeak":30}},{"date":"2026-10-22","minutes":{"discord_voice":60,"gameserver":120}}],
         "timeline":[{"type":"submitted","label":"Bewerbung eingereicht","public":true,"actor":null,"at":"2026-10-12T17:30:00Z"},
                     {"type":"trial","label":"Probezeit gestartet","public":true,"actor":"Wahke","at":"2026-10-14T19:00:00Z"},
                     {"type":"note","label":"Interne Notiz","public":false,"actor":"Wahke","at":"2026-10-21T22:10:00Z"}],
         "messages":[{"id":"m1","internal":false,"author":"Nyx","from_applicant":true,"body":"Freue mich auf die Probezeit!","at":"2026-10-14T19:30:00Z"},
                     {"id":"m2","internal":true,"author":"Wahke","from_applicant":false,"body":"Sehr zuverlässig, war bei jedem Training.","at":"2026-10-21T22:10:00Z"}],
         "vote":null,"my_vote":null,"can_vote":false,"trial_ends_at":"2026-11-04T19:00:00Z"}
        """;

    private static string ApplicationRex => """
        {"id":"01JAPP00000000000000000002","status":"voting","applicant":{"id":"01U8","slug":"rex","name":"Rex"},"squad":{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"},
         "age":19,"referral":null,
         "answers":[{"question_id":"q4","label":"Premier-Rating?","type":"number","answer":14200}],
         "ban_check":[{"key":"discord","status":"ok","message":"Keine Sperre auf Discord"}],
         "tasks":[],"activity":[],"timeline":[{"type":"vote","label":"Abstimmung gestartet","public":true,"actor":"Nyx","at":"2026-10-28T18:00:00Z"}],
         "messages":[],
         "vote":{"round":1,"eligible":6,"cast":4,"yes":3,"no":1,"abstain":0,"participation":67,"quorum_percent":50,"quorum_met":true,"result":"accept","missing":[{"id":"01U3","name":"Kaito"},{"id":"01U4","name":"Blizzard"}]},
         "my_vote":null,"can_vote":true,"vote_ends_at":"2026-11-01T18:00:00Z"}
        """;
}
