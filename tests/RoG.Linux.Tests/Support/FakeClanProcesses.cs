namespace RoG.Linux.Tests.Support;

/// <summary>
/// Stufe 6 (Teil A): Votes, Bewerbung, Regeln/Rechtstexte bestätigen – Antworten in der Form der API (Guides 07, 27, 40).
/// Standard: nichts offen (kein Hinweisbalken in anderen Tests); Inhalte per <c>t.Api.Overrides</c> aus den Konstanten hier.
/// </summary>
[FakeRoutes]
internal static class FakeClanProcessesRoutes
{
    private static string? Route(HttpRequestMessage request, string path, string body) => (request.Method.Method, path) switch
    {
        ("GET", "/v1/me/rules/pending") => FakeClanProcesses.NothingPendingRules,
        ("GET", "/v1/me/legal") => FakeClanProcesses.NothingPendingLegal,
        ("POST", var p) when p.StartsWith("/v1/rules/", StringComparison.Ordinal) && p.EndsWith("/accept", StringComparison.Ordinal) =>
            """{"data":{"version":2,"accepted_at":"2026-10-30T09:12:00Z"}}""",
        ("POST", var p) when p.StartsWith("/v1/me/legal/", StringComparison.Ordinal) && p.EndsWith("/accept", StringComparison.Ordinal) =>
            "{\"data\":{\"type\":\"" + p.Split('/')[4] + "\",\"version\":27,\"accepted_at\":\"2026-10-30T09:12:00Z\"}}",
        ("PUT", var p) when p.StartsWith("/v1/applications/", StringComparison.Ordinal) && p.EndsWith("/vote", StringComparison.Ordinal) =>
            "{\"data\":{\"vote\":" + System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("vote").GetRawText() + ",\"round\":1}}",
        ("POST", var p) when p.StartsWith("/v1/squads/", StringComparison.Ordinal) && p.EndsWith("/applications", StringComparison.Ordinal) =>
            """{"data":{"id":"01APPNEW","status":"submitted","step":0}}""",
        _ => null,
    };
}

internal static class FakeClanProcesses
{
    public const string FormPath = "/v1/squads/enemy-territory/application-form";

    public const string NothingPendingRules = """{"data":[],"meta":{"write_blocked":false}}""";

    public const string NothingPendingLegal = """
        {"data":[{"type":"privacy","accepted_version":26,"required_version":null,"pending":false},{"type":"terms","accepted_version":2,"required_version":null,"pending":false}]}
        """;

    /// <summary>Clanregeln (sperren das Schreiben) offen.</summary>
    public const string PendingRules = """
        {"data":[{"id":"01R1","slug":"clan","scope":"clan","game_id":null,"squad_id":null,"title":"Clanregeln – Rebels of Gaming","intro":null,"visibility":"public",
          "requires_acceptance":true,"enforced":true,"wiki_page":null,"version":2,"accepted":false,"updated_at":"2026-10-28T08:00:00Z"}],
         "meta":{"write_blocked":true}}
        """;

    /// <summary>Neue Datenschutzerklärung zu bestätigen.</summary>
    public const string PendingLegal = """
        {"data":[{"type":"privacy","accepted_version":26,"required_version":27,"pending":true},{"type":"terms","accepted_version":2,"required_version":null,"pending":false}]}
        """;

    public const string LegalIndex = """
        {"data":[{"type":"privacy","title":"Datenschutzerklärung","version":27,"published_at":"2026-10-29T07:57:29Z","requires_acceptance":true,"acceptable":true,"url":"https://rebelsofgaming.com/de/privacy"},
          {"type":"terms","title":"Nutzungsbedingungen","version":2,"published_at":"2026-10-18T07:00:00Z","requires_acceptance":false,"acceptable":true,"url":"https://rebelsofgaming.com/de/terms"},
          {"type":"imprint","title":"Impressum","version":3,"published_at":"2026-09-01T07:00:00Z","requires_acceptance":false,"acceptable":false,"url":"https://rebelsofgaming.com/de/imprint"}]}
        """;

    public const string LegalPrivacy = """
        {"data":{"type":"privacy","title":"Datenschutzerklärung","version":27,"published_at":"2026-10-29T07:57:29Z","requires_acceptance":true,"acceptable":true,"changes":"Neu: Abschnitt zur Linux-App (Schlüsselbund, keine Analyse).",
          "markdown":"","html":"<h2>1. Verantwortlicher</h2><p>Betreiber von Rebels of Gaming ist eine Privatperson in Luxemburg.</p><h2>2. Welche Daten wir speichern</h2><p>Konto, Profil, Beiträge und <strong>nur technisch notwendige Cookies</strong>. Kein Tracking, keine Analyse.</p><h2>3. Apps</h2><p>Die Linux-App speichert dein Geräte-Token im Schlüsselbund deines Systems.</p>",
          "url":"https://rebelsofgaming.com/de/privacy"}}
        """;

    /// <summary>Angemeldete Person, die (noch) in keinem Squad ist – darf sich bewerben.</summary>
    public const string MeRookie = """
        {"data":{"id":"01USERROOKIE","slug":"rookie","name":"Rookie","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"guest","label":"Gast","color":"#A8ACB6"},"two_factor_enabled":false,"joined_at":"2026-10-20",
          "squads":[],"permissions":{"global":[]},"token":{"scopes":["profile:read","profile:write","squads:read","applications:read","applications:write","rules:read","rules:accept"]} }}
        """;

    public const string Form = """
        {"data":{"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},"waitlist":false,"min_age":16,
          "questions":[
            {"id":"01Q1","type":"select","label":"Wie oft kannst du spielen?","help":null,"required":true,"options":[{"value":"1-2","label":"1–2 Abende pro Woche"},{"value":"3-4","label":"3–4 Abende pro Woche"},{"value":"5+","label":"Fast jeden Abend"}]},
            {"id":"01Q2","type":"number","label":"Wie viele Stunden hast du in ET?","help":"Ungefähr reicht.","required":false,"options":[]},
            {"id":"01Q3","type":"yesno","label":"Hast du ein funktionierendes Headset?","help":null,"required":true,"options":[]},
            {"id":"01Q4","type":"text","label":"Lieblingsklasse","help":null,"required":false,"options":[]},
            {"id":"01Q5","type":"textarea","label":"Erzähl uns kurz etwas über dich","help":"Was spielst du, was suchst du bei uns?","required":true,"options":[]}
          ],
          "rules":[{"id":"01R1","slug":"clan","title":"Clanregeln – Rebels of Gaming","version":2},{"id":"01R2","slug":"enemy-territory","title":"Server-Regeln Enemy Territory","version":3}]}}
        """;

    public const string FormWaitlist = """
        {"data":{"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},"waitlist":true,"min_age":18,"questions":[],"rules":[]}}
        """;

    public const string Votes = """
        {"data":[
          {"id":"01APP1","status":"voting","applicant":{"id":"01U9","slug":"frostbyte","name":"Frostbyte"},"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},
           "age":24,"vote_ends_at":"2026-11-02T18:00:00Z",
           "answers":[{"label":"Wie oft kannst du spielen?","answer":"3–4 Abende pro Woche"},{"label":"Erzähl uns kurz etwas über dich","answer":"Spiele ET seit 2005, meist Engineer oder Covert Ops. Suche einen festen Clan für Scrims und Clan-Abende."},{"label":"Hast du ein funktionierendes Headset?","answer":true}],
           "tasks":[{"id":"t1","title":"An zwei Clan-Abenden teilnehmen","done":true,"done_at":"2026-10-21T21:00:00Z"},{"id":"t2","title":"Ein Scrim auf Supply spielen","done":true,"done_at":"2026-10-24T21:00:00Z"},{"id":"t3","title":"Map-Rotation im Wiki lesen","done":false,"done_at":null}],
           "activity":[],"vote":{"round":1,"eligible":9,"cast":6,"participation":67,"quorum_percent":50,"quorum_met":true},"my_vote":"yes"},
          {"id":"01APP2","status":"voting","applicant":{"id":"01U10","slug":"lumen","name":"Lumen"},"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},
           "age":19,"vote_ends_at":"2026-11-04T18:00:00Z",
           "answers":[{"label":"Wie oft kannst du spielen?","answer":"1–2 Abende pro Woche"},{"label":"Wie viele Stunden hast du in ET?","answer":120}],
           "tasks":[],"activity":[],"vote":{"round":1,"eligible":9,"cast":2,"participation":22,"quorum_percent":50,"quorum_met":false},"my_vote":null}
        ]}
        """;
}
