using System.Text.Json;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Wiki (Guide 26) im Fake-Server: Bereiche „Clan“ (öffentlich), „Technik“ (Mitglieder, Prüfpflicht) und „Intern“
/// (Team), Seitenbaum, Seiten mit HTML/Inhaltsverzeichnis/Wiki-Links, Versionen, Suche, Kategorien, Vorschau,
/// Speichern mit Bearbeitungskonflikt (<see cref="ConflictSummary"/>), neue Seite, Dateien.
/// </summary>
[FakeRoutes]
internal static class FakeWikiRoutes
{
    public const string Wiki = "https://wiki.rebelsofgaming.com";
    public const string Rules = "01JWIKIREGELN0000000000001";
    public const string Voice = "01JWIKIVOICE00000000000002";
    public const string NewMembers = "01JWIKINEU000000000000003";
    public const string Servers = "01JWIKISERVER0000000000004";
    public const string EtServer = "01JWIKIETSERVER000000000005";
    public const string Cs2Server = "01JWIKICS2SERVER00000000006";
    public const string CurrentRevision = "01JWIKIREV3000000000000003";
    public const string PendingRevision = "01JWIKIREV4000000000000004";
    public const string FirstRevision = "01JWIKIREV1000000000000001";

    /// <summary>Fassung, die „Kaito“ beim Konflikt inzwischen gespeichert hat.</summary>
    public const string ConflictRevision = "01JWIKIREVNEU0000000000009";

    /// <summary>
    /// Zusammenfassung, mit der das Speichern auf der ausgelieferten Fassung (<see cref="CurrentRevision"/>) einen
    /// <c>409 edit_conflict</c> auslöst – wie wenn jemand anderes inzwischen gespeichert hätte.
    /// </summary>
    public const string ConflictSummary = "Konflikt";

    public const string CurrentContent = "== Allgemein ==\nRespekt und Fairplay gegenüber allen.\n== Voice ==\nIm Voice wird Deutsch gesprochen.\nPush-to-Talk ist Pflicht.";
    public const string FirstContent = "== Allgemein ==\nRespekt gegenüber allen.\n== Voice ==\nIm Voice wird Deutsch gesprochen.";
    public const string ConflictContent = "== Allgemein ==\nRespekt und Fairplay gegenüber allen – auch im Chat.\n== Voice ==\nIm Voice wird Deutsch gesprochen.\nPush-to-Talk ist Pflicht.";

    private static readonly Dictionary<string, (string Ns, string NsName, string Slug, string Title, string Html, string Toc, string? Content, string Categories)> s_pages = new()
    {
        [Rules] = ("clan", "Clan", "regeln", "Clan-Regeln",
            "<h2 id=\"Allgemein\">Allgemein</h2><p>Respekt und <strong>Fairplay</strong> gegenüber allen. Wer neu ist, liest zuerst "
            + $"<a href=\"{Wiki}/clan/neu-im-clan\">Neu im Clan</a>.</p>"
            + "<h2 id=\"Voice\">Voice</h2><p>Im Voice wird Deutsch gesprochen. Push-to-Talk ist Pflicht – Details unter "
            + $"<a href=\"{Wiki}/clan/regeln/voice\">Verhalten im Voice</a>.</p>"
            + "<h2 id=\"Server\">Server</h2><ul><li>Kein Cheaten, kein Exploiten.</li><li>Admins haben das letzte Wort.</li></ul>"
            + $"<p>Technik: <a href=\"{Wiki}/technik/server/et-server\">ET-Server einrichten</a></p>"
            + "<h2 id=\"Strafen\">Strafen</h2><table><tr><th>Verstoß</th><th>Folge</th></tr><tr><td>Beleidigung</td><td>Verwarnung</td></tr><tr><td>Cheats</td><td>Bann</td></tr></table>",
            """[{"level":2,"id":"Allgemein","text":"Allgemein"},{"level":2,"id":"Voice","text":"Voice"},{"level":2,"id":"Server","text":"Server"},{"level":2,"id":"Strafen","text":"Strafen"}]""",
            CurrentContent, """[{"slug":"clan","name":"Clan","hidden":false},{"slug":"regeln","name":"Regeln","hidden":false}]"""),
        [Voice] = ("clan", "Clan", "voice", "Verhalten im Voice",
            "<p>Discord und TeamSpeak: Push-to-Talk, kein Musikbot im Squad-Channel.</p>", "[]", "Discord und TeamSpeak: Push-to-Talk.", "[]"),
        [NewMembers] = ("clan", "Clan", "neu-im-clan", "Neu im Clan",
            "<p>Willkommen bei den Rebels! Melde dich im Discord bei deinem Squad-Leader.</p>", "[]", "Willkommen bei den Rebels!", """[{"slug":"clan","name":"Clan","hidden":false}]"""),
        [Servers] = ("technik", "Technik", "server", "Server",
            "<p>Übersicht unserer Gameserver: ET, CS2 und Minecraft.</p>", "[]", "Übersicht unserer Gameserver.", "[]"),
        [EtServer] = ("technik", "Technik", "et-server", "ET-Server einrichten",
            "<h2 id=\"Ports\">Ports</h2><p>UDP 27960 freigeben.</p><h2 id=\"Mods\">Mods</h2><p>ETPro und Jaymod.</p><pre><code>+set fs_game etpro +map supply</code></pre>",
            """[{"level":2,"id":"Ports","text":"Ports"},{"level":2,"id":"Mods","text":"Mods"}]""", null, """[{"slug":"server","name":"Server","hidden":false}]"""),
        [Cs2Server] = ("technik", "Technik", "cs2-server", "CS2-Server",
            "<p>Retake- und Scrim-Server, Tickrate 128.</p>", "[]", "Retake- und Scrim-Server.", """[{"slug":"server","name":"Server","hidden":false}]"""),
    };

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        if (!path.StartsWith("/v1/wiki/", StringComparison.Ordinal))
        {
            return null;
        }

        var guest = request.Headers.Authorization is null;
        var method = request.Method.Method;
        return (method, path) switch
        {
            ("GET", "/v1/wiki/namespaces") => Namespaces(guest),
            ("GET", "/v1/wiki/namespaces/clan/pages") => """
                {"data":[
                  {"id":"01JWIKIREGELN0000000000001","slug":"regeln","title":"Clan-Regeln","locales":["de","en"],"tags":[],"updated_at":"2026-10-28T18:00:00Z",
                   "children":[{"id":"01JWIKIVOICE00000000000002","slug":"voice","title":"Verhalten im Voice","locales":["de"],"tags":[],"updated_at":"2026-10-20T12:00:00Z","children":[]}]},
                  {"id":"01JWIKINEU000000000000003","slug":"neu-im-clan","title":"Neu im Clan","locales":["de"],"tags":[],"updated_at":"2026-10-12T09:00:00Z","children":[]}
                ]}
                """,
            ("GET", "/v1/wiki/namespaces/technik/pages") => """
                {"data":[
                  {"id":"01JWIKISERVER0000000000004","slug":"server","title":"Server","locales":["de"],"tags":[],"updated_at":"2026-10-25T10:00:00Z",
                   "children":[
                     {"id":"01JWIKIETSERVER000000000005","slug":"et-server","title":"ET-Server einrichten","locales":["de"],"tags":[],"updated_at":"2026-10-26T10:00:00Z","children":[]},
                     {"id":"01JWIKICS2SERVER00000000006","slug":"cs2-server","title":"CS2-Server","locales":["de"],"tags":[],"updated_at":"2026-10-27T10:00:00Z","children":[]}]}
                ]}
                """,
            ("GET", "/v1/wiki/namespaces/intern/pages") => """{"data":[]}""",
            ("GET", var p) when p.StartsWith("/v1/wiki/namespaces/", StringComparison.Ordinal) && p.Contains("/pages/", StringComparison.Ordinal) => BySlug(p),
            ("POST", var p) when p.StartsWith("/v1/wiki/namespaces/", StringComparison.Ordinal) && p.EndsWith("/pages", StringComparison.Ordinal) =>
                p.Contains("/technik/", StringComparison.Ordinal)
                    ? """{"data":{"id":"01JWIKIREVNEU00000000000000","page_id":"01JWIKINEWPAGE0000000000007","locale":"de","format":"wikitext","status":"pending"}}"""
                    : """{"data":{"id":"01JWIKIREVNEU00000000000000","page_id":"01JWIKINEWPAGE0000000000007","locale":"de","format":"wikitext","status":"approved"}}""",
            ("GET", "/v1/wiki/pages/01JWIKINEWPAGE0000000000007") => Page("01JWIKINEWPAGE0000000000007", ("clan", "Clan", "clan-abend", "Clan-Abend", "<p>Jeden Freitag um 20 Uhr.</p>", "[]", "Jeden Freitag um 20 Uhr.", "[]")),
            ("GET", var p) when p.StartsWith("/v1/wiki/pages/", StringComparison.Ordinal) && p.EndsWith("/revisions", StringComparison.Ordinal) => Revisions,
            ("GET", var p) when p.StartsWith("/v1/wiki/pages/", StringComparison.Ordinal) =>
                s_pages.TryGetValue(p["/v1/wiki/pages/".Length..], out var page) ? Page(p["/v1/wiki/pages/".Length..], page) : null,
            ("PUT", var p) when p.StartsWith("/v1/wiki/pages/", StringComparison.Ordinal) => Save(body),
            ("POST", var p) when p.StartsWith("/v1/wiki/pages/", StringComparison.Ordinal) && p.EndsWith("/watch", StringComparison.Ordinal) => """{"data":{"watching":true}}""",
            ("GET", "/v1/wiki/revisions/" + CurrentRevision) => Revision(CurrentRevision, CurrentContent, "approved", current: true),
            ("GET", "/v1/wiki/revisions/" + PendingRevision) => Revision(PendingRevision, CurrentContent + "\nMusikbots nur im Musik-Channel.", "pending", current: false),
            ("GET", "/v1/wiki/revisions/" + FirstRevision) => Revision(FirstRevision, FirstContent, "approved", current: false),
            ("POST", var p) when p.StartsWith("/v1/wiki/revisions/", StringComparison.Ordinal) =>
                """{"data":{"id":"01JWIKIREV5000000000000005","status":"approved"}}""",
            ("POST", "/v1/wiki/render") => """{"data":{"format":"wikitext","html":"<h2 id=\"Allgemein\">Allgemein</h2><p><strong>Vorschau:</strong> Respekt und Fairplay.</p>","toc":[],"categories":[],"interlanguage":[],"display_title":null,"switches":[]}}""",
            ("GET", "/v1/wiki/search") => """
                {"data":[
                  {"id":"01JWIKIETSERVER000000000005","slug":"et-server","namespace":"technik","locale":"de","title":"ET-Server einrichten","snippet":"UDP 27960 für den <mark>ET</mark>-Server freigeben"},
                  {"id":"01JWIKIREGELN0000000000001","slug":"regeln","namespace":"clan","locale":"de","title":"Clan-Regeln","snippet":"Technik: <mark>ET</mark>-Server einrichten"}
                ]}
                """,
            ("GET", "/v1/wiki/categories") => """{"data":[{"slug":"clan","name":"Clan","count":2},{"slug":"regeln","name":"Regeln","count":1},{"slug":"server","name":"Server","count":2}]}""",
            ("GET", "/v1/wiki/categories/server") => """
                {"data":{"slug":"server","name":"Server","description_page_id":null,"pages":[
                  {"id":"01JWIKIETSERVER000000000005","namespace":"technik","slug":"et-server","title":"ET-Server einrichten","url":"https://wiki.rebelsofgaming.com/technik/server/et-server"},
                  {"id":"01JWIKICS2SERVER00000000006","namespace":"technik","slug":"cs2-server","title":"CS2-Server","url":"https://wiki.rebelsofgaming.com/technik/server/cs2-server"}],"subcategories":[]}}
                """,
            ("POST", "/v1/wiki/files") => """{"data":{"slug":"supply-karte.png","name":"Supply Karte.png","namespace":"clan","width":1920,"height":1080,"src":"https://cdn.rogclan.de/wiki/supply.webp","full":"https://i.rogclan.de/supply.png","url":"https://wiki.rebelsofgaming.com/file/supply-karte.png","created_at":"2026-10-30T10:00:00Z"}}""",
            _ => null,
        };
    }

    private static string Namespaces(bool guest)
    {
        var clan = """{"id":"01JWIKINS1","slug":"clan","name":"Clan","description":"Regeln, Abläufe, Neu im Clan","icon":"users","visibility":"public","upload_visibility":"public","requires_review":false,"game_id":null,"squad_id":null,"pages":3}""";
        var technik = """{"id":"01JWIKINS2","slug":"technik","name":"Technik","description":"Server und Tools","icon":"server","visibility":"members","upload_visibility":"members","requires_review":true,"game_id":null,"squad_id":null,"pages":3}""";
        var intern = """{"id":"01JWIKINS3","slug":"intern","name":"Intern","description":null,"icon":"lock","visibility":"team","upload_visibility":"private","requires_review":false,"game_id":null,"squad_id":null,"pages":0}""";
        return guest ? $$"""{"data":[{{clan}}]}""" : $$"""{"data":[{{clan}},{{technik}},{{intern}}]}""";
    }

    private static string? BySlug(string path)
    {
        // /v1/wiki/namespaces/{ns}/pages/{seite…}
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ns = parts[3];
        var slug = Uri.UnescapeDataString(parts[^1]);
        var hit = s_pages.FirstOrDefault(p => p.Value.Ns == ns && p.Value.Slug == slug);
        return hit.Key is null ? null : Page(hit.Key, hit.Value).Replace("\"meta\":null", "\"meta\":{\"redirected_from\":null}", StringComparison.Ordinal);
    }

    private static string Page(string id, (string Ns, string NsName, string Slug, string Title, string Html, string Toc, string? Content, string Categories) p) => $$"""
        {"data":{"id":"{{id}}","slug":"{{p.Slug}}","namespace":{"id":"01JWIKINS","slug":"{{p.Ns}}","name":"{{p.NsName}}","description":null,"icon":"book","visibility":"public","upload_visibility":"public","requires_review":false,"game_id":null,"squad_id":null},
         "parent_id":null,"tags":[],"locale":"de","title":"{{p.Title}}","format":"wikitext","content":{{(p.Content is null ? "null" : JsonSerializer.Serialize(p.Content))}},
         "html":{{JsonSerializer.Serialize(p.Html)}},"toc":{{p.Toc}},"interlanguage":[],"categories":{{p.Categories}},"display_title":null,"redirect":null,"switches":[],
         "translations":[],"url":"{{Wiki}}/{{p.Ns}}/{{p.Slug}}","revision_id":"{{CurrentRevision}}","updated_at":"2026-10-28T18:00:00Z",
         "last_editor":{"id":"01USERKAITO","name":"Kaito"},"watching":false},"meta":null}
        """;

    private static string Save(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("base_revision_id", out var b) && b.GetString() == CurrentRevision
            && root.TryGetProperty("summary", out var summary) && summary.GetString() == ConflictSummary)
        {
            return $$$"""
                {"type":"https://api.rebelsofgaming.com/docs/errors#edit_conflict","title":"Conflict","status":409,"detail":"Die Seite wurde inzwischen geändert.","code":"edit_conflict",
                 "current":{"revision_id":"{{{ConflictRevision}}}","locale":"de","title":"Clan-Regeln","format":"wikitext","content":{{{JsonSerializer.Serialize(ConflictContent)}}},
                   "updated_at":"2026-10-30T12:05:00Z","updated_by":{"id":"01USERKAITO","name":"Kaito"} } }
                """;
        }

        return """{"data":{"id":"01JWIKIREV5000000000000005","locale":"de","title":"Clan-Regeln","format":"wikitext","summary":null,"status":"approved","author":{"id":"01USERWAHKE","name":"Wahke"},"created_at":"2026-10-30T12:10:00Z","page_id":"01JWIKIREGELN0000000000001"}}""";
    }

    private const string Revisions = """
        {"data":[
          {"id":"01JWIKIREV4000000000000004","locale":"de","title":"Clan-Regeln","format":"wikitext","summary":"Musikbots ergänzt","status":"pending","author":{"id":"01USERKAITO","name":"Kaito"},"created_at":"2026-10-29T20:15:00Z"},
          {"id":"01JWIKIREV3000000000000003","locale":"de","title":"Clan-Regeln","format":"wikitext","summary":"Push-to-Talk ergänzt","status":"approved","author":{"id":"01USERWAHKE","name":"Wahke"},"created_at":"2026-10-28T18:00:00Z"},
          {"id":"01JWIKIREV1000000000000001","locale":"de","title":"Clan-Regeln","format":"wikitext","summary":"Erste Fassung","status":"approved","author":{"id":"01USERWAHKE","name":"Wahke"},"created_at":"2026-10-01T09:00:00Z"}
        ],"meta":{"next_cursor":null}}
        """;

    private static string Revision(string id, string content, string status, bool current) => $$$"""
        {"data":{"id":"{{{id}}}","locale":"de","title":"Clan-Regeln","format":"wikitext","summary":null,"status":"{{{status}}}","author":{"id":"01USERWAHKE","name":"Wahke"},
         "created_at":"2026-10-28T18:00:00Z","page_id":"01JWIKIREGELN0000000000001","namespace":"clan","content":{{{JsonSerializer.Serialize(content)}}},"current":{{{(current ? "true" : "false")}}}}}
        """;

    /// <summary><c>/v1/me</c> mit Wiki-Rechten (bearbeiten, freigeben) und den Scopes dazu.</summary>
    public static string MeWithWikiRights => FakeData.Me
        .Replace("\"permissions\":{\"global\":[\"news.write\"]}", "\"permissions\":{\"global\":[\"news.write\",\"wiki.edit\",\"wiki.approve\",\"uploads.create\"]}", StringComparison.Ordinal)
        .Replace("\"notifications:read\"]", "\"notifications:read\",\"wiki:read\",\"wiki:write\",\"uploads:write\"]", StringComparison.Ordinal);
}
