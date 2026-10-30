using System.Text.Json.Nodes;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Stufe 9 „N – Inhalte“: News-Redaktion (Guide 22), Matches eintragen (Guide 14), Galerie-Freigaben
/// (Guide 24), Upload-Meldungen und Speicher (Guide 20) und Social-Media-Status an der Quelle (Guide 41) – Form wie die
/// API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakeContentAdminRoutes
{
    private const string Cdn = "https://cdn.example.test";

    public const string DraftSlug = "turnier-am-samstag";
    public const string DraftId = "01JNEWSDRAFT0000000000001";
    public const string ScheduledSlug = "neue-et-server";
    public const string NewNewsId = "01JNEWSNEW00000000000009";
    public const string MatchId = "01JMATCHEDIT0000000000001";
    public const string NewMatchId = "01JMATCHNEW00000000000009";
    public const string SocialPostId = "01JB3D4E5F6G7H8J9K0M1N2P3Q";

    /// <summary>Wahke mit Redaktions-, Match-, Galerie-, Upload- und Social-Rechten.</summary>
    public const string MeEditor = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["news.write","news.publish","news.manage","gallery.manage","uploads.manage","social.publish"],
            "squad:01SQ1":["matches.manage"],"squad:01SQ2":["matches.manage"]},
          "token":{"scopes":["news:read","news:write","matches:read","matches:write","gallery:read","gallery:manage","uploads:manage","social:read","social:write","squads:read","members:read","events:read"]} }}
        """;

    /// <summary>Autor mit <c>news.write</c>: nur Entwürfe, keine Matches, keine Verwaltung.</summary>
    public const string MeAuthor = """
        {"data":{"id":"01U3","slug":"kaito","name":"Kaito","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"member","label":"Member","color":"#A8ACB6"},"two_factor_enabled":false,"joined_at":"2022-01-01",
          "squads":[],"permissions":{"global":["news.write"]},"token":{"scopes":["news:read","news:write","squads:read"]} }}
        """;

    /// <summary>Hat Rechte, aber der Token nicht die Scopes (z. B. App-Login ohne 2FA): nichts sichtbar.</summary>
    public const string MeNoScopes = """
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":null,"status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["news.publish","gallery.manage","uploads.manage","social.publish","matches.manage"]},
          "token":{"scopes":["news:read","squads:read"]} }}
        """;

    /// <summary>Entwurf mit allen Übersetzungen (für <c>t.Api.Overrides["/v1/news/" + DraftSlug]</c>).</summary>
    public const string DraftDetail = """
        {"data":{"id":"01JNEWSDRAFT0000000000001","slug":"turnier-am-samstag","url":"https://rebelsofgaming.com/de/news/turnier-am-samstag",
          "title":"Turnier am Samstag","summary":"Unser ET-Squad tritt beim Herbst-Cup an.","status":"draft","visibility":"public","pinned":false,
          "comments_enabled":true,"published_at":null,"reading_minutes":1,"category":{"slug":"turniere","name":"Turniere","color":"#D4AE3C"},
          "author":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},"cover":null,"comments_count":0,
          "body":"Anmeldung bis **Freitag**.","body_html":"<p>Anmeldung bis <strong>Freitag</strong>.</p>",
          "translations":{
            "title":{"de":"Turnier am Samstag","en":"Tournament on Saturday"},
            "teaser":{"de":"Unser ET-Squad tritt beim Herbst-Cup an.","en":"Our ET squad enters the autumn cup."},
            "body":{"de":"## Ablauf\n\nAnmeldung bis **Freitag**, Treffpunkt im TeamSpeak.\n\n- 19:30 Uhr Aufwärmen auf *supply*\n- 20:00 Uhr erstes Match\n\n> Wer zuschauen will: Stream läuft auf Twitch.\n\nhttps://www.twitch.tv/rebelsofgaming","en":"Sign-up until **Friday**."}
          }}}
        """;

    /// <summary>Album mit Freigabe und zwei eingereichten Einträgen (für <c>t.Api.Overrides["/v1/gallery/albums/clanabend-2026"]</c>).</summary>
    public static string AlbumWithPending => $$$"""
        {"data":{"id":"01GA1","slug":"clanabend-2026","title":"Clanabend Oktober 2026","can_upload":true,"can_manage":true,"requires_approval":true,
          "items":[
            {"id":"01GI1","kind":"image","status":"published","caption":"Gruppenfoto","user":{"name":"Nyx"},"image":{"thumb":"{{{Cdn}}}/gallery/1-thumb.webp","full":"{{{Cdn}}}/gallery/1.webp"},"created_at":"2026-10-25T21:00:00Z"},
            {"id":"01GIP1","kind":"image","status":"pending","caption":"Siegerfoto nach dem Scrim","user":{"name":"Fitti"},"image":{"thumb":"{{{Cdn}}}/gallery/p1-thumb.webp","full":"{{{Cdn}}}/gallery/p1.webp"},"created_at":"2026-10-26T11:00:00Z"},
            {"id":"01GIP2","kind":"video","status":"pending","caption":null,"user":{"name":"Kaito"},"poster_url":"{{{Cdn}}}/gallery/p2-poster.webp","video_url":"{{{Cdn}}}/gallery/p2.mp4","created_at":"2026-10-27T18:30:00Z"}
          ]}}
        """;

    /// <summary>Zweites Album ohne offene Einträge.</summary>
    public const string AlbumNothingPending = """
        {"data":{"id":"01GA2","slug":"scrims","title":"Scrim-Highlights","can_upload":true,"can_manage":true,"requires_approval":true,"items":[]}}
        """;

    public static string? Route(HttpRequestMessage request, string path, string body)
    {
        var method = request.Method.Method;
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        return (method, path) switch
        {
            // News-Redaktion
            ("GET", "/v1/news-categories") => Categories,
            ("GET", "/v1/news") when query.Contains("filter[status]=draft", StringComparison.Ordinal) => Drafts,
            ("GET", "/v1/news") when query.Contains("filter[status]=scheduled", StringComparison.Ordinal) => Scheduled,
            ("POST", "/v1/news") => Saved(NewNewsId, JsonNode.Parse(body)?["title"]?["de"]?.GetValue<string>() ?? "neu", warning: false),
            ("PATCH", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) =>
                Saved(p["/v1/news/".Length..], "turnier-am-samstag", warning: JsonNode.Parse(body)?["visibility"]?.GetValue<string>() == "public"),
            ("DELETE", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) && !p.EndsWith("/cover", StringComparison.Ordinal) => "{\"data\":null}",
            ("POST", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) && p.EndsWith("/cover", StringComparison.Ordinal) => """{"data":{"status":"processing"}}""",
            ("DELETE", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) && p.EndsWith("/cover", StringComparison.Ordinal) => "{\"data\":null}",

            // Matches eintragen
            ("GET", "/v1/matches/" + MatchId) => MatchDetail,
            ("POST", "/v1/matches") => "{\"data\":{\"id\":\"" + NewMatchId + "\",\"slug\":\"2026-10-30-et-vs-iron-legion\"}}",
            ("PATCH", var p) when p.StartsWith("/v1/matches/", StringComparison.Ordinal) => "{\"data\":{\"id\":\"" + p["/v1/matches/".Length..] + "\"}}",
            ("POST", var p) when p.StartsWith("/v1/matches/", StringComparison.Ordinal) && p.EndsWith("/attachments", StringComparison.Ordinal) => """{"data":[{"id":"01MA9","name":"scoreboard.png"}]}""",
            ("DELETE", var p) when p.StartsWith("/v1/matches/", StringComparison.Ordinal) => "{\"data\":null}",

            // Galerie-Freigaben
            ("POST", var p) when p.StartsWith("/v1/gallery/items/", StringComparison.Ordinal) && p.EndsWith("/approve", StringComparison.Ordinal) => """{"data":{"status":"published"}}""",
            ("POST", var p) when p.StartsWith("/v1/gallery/items/", StringComparison.Ordinal) && p.EndsWith("/reject", StringComparison.Ordinal) => "{\"data\":null}",

            // Upload-Meldungen
            ("GET", "/v1/admin/uploads/reports") => Reports,
            ("POST", var p) when p.StartsWith("/v1/admin/uploads/reports/", StringComparison.Ordinal) && p.EndsWith("/resolve", StringComparison.Ordinal) =>
                "{\"data\":{\"id\":\"" + p.Split('/')[5] + "\",\"resolution\":\"" + (JsonNode.Parse(body)?["resolution"]?.GetValue<string>() ?? "kept") + "\"}}",
            ("GET", "/v1/admin/uploads/storage") => Storage,

            // Social Media an der Quelle
            ("GET", "/v1/social/sources/news/" + DraftId) => Social("news", DraftId, "partial"),
            ("GET", var p) when p.StartsWith("/v1/social/sources/", StringComparison.Ordinal) && p.Count(c => c == '/') == 5 =>
                Social(p.Split('/')[4], p.Split('/')[5], "none"),
            ("POST", "/v1/social/posts/" + SocialPostId + "/retry-failed") =>
                """{"data":{"count":1,"channel_ids":["01JB2C3D4E5F6G7H8J9K0M1N2Q"],"unusable":[{"channel_id":"01JB2C3D4E5F6G7H8J9K0M1N2R","name":"RoG auf X","reason":"reauth"}]}}""",
            ("POST", var p) when p.StartsWith("/v1/social/posts/", StringComparison.Ordinal) && p.EndsWith("/retry", StringComparison.Ordinal) => """{"data":{"status":"pending"}}""",
            ("POST", var p) when p.StartsWith("/v1/social/sources/", StringComparison.Ordinal) && p.EndsWith("/redraft", StringComparison.Ordinal) => """{"data":{"id":"01JB9NEWDRAFT0000000000001"}}""",
            _ => null,
        };
    }

    private const string Categories = """
        {"data":[
          {"id":"01NC1","slug":"clan","name":"Clan","color":"#E0324A"},
          {"id":"01NC2","slug":"server","name":"Server","color":"#3DDC84"},
          {"id":"01NC3","slug":"turniere","name":"Turniere","color":"#D4AE3C"}
        ]}
        """;

    private const string Drafts = """
        {"data":[
          {"id":"01JNEWSDRAFT0000000000001","slug":"turnier-am-samstag","title":"Turnier am Samstag","status":"draft","visibility":"public","published_at":null},
          {"id":"01JNEWSDRAFT0000000000002","slug":"minecraft-season-4","title":"Minecraft: Season 4 startet","status":"draft","visibility":"members","published_at":null}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    private const string Scheduled = """
        {"data":[
          {"id":"01JNEWSPLAN00000000000001","slug":"neue-et-server","title":"Zwei neue ET-Server online","status":"scheduled","visibility":"public","published_at":"2026-11-02T17:00:00Z"}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    private static string Saved(string id, string slug, bool warning) =>
        "{\"data\":{\"id\":\"" + id + "\",\"slug\":\"" + Slug(slug) + "\",\"visibility\":\"public\"},\"meta\":{\"warnings\":["
        + (warning ? """{"code":"image_visibility_conflict","message":"Ein Bild im Text ist nur für Mitglieder sichtbar.","target_visibility":"public","upload_ids":["01JB0000000000000000000001"]}""" : string.Empty)
        + "]}}";

    private static string Slug(string text) => string.Concat(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');

    private const string MatchDetail = """
        {"data":{"id":"01JMATCHEDIT0000000000001","slug":"2026-10-28-et-vs-iron-legion","type":"clanwar","status":"finished",
          "squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory","game":"ET"},
          "opponent":{"name":"Iron Legion","tag":"IL","url":"https://iron-legion.example.org"},
          "played_at":"2026-10-28T19:00:00Z","score":{"own":3,"opponent":1},"result":"win","published":true,
          "maps":[{"position":1,"map":"supply","score":{"own":2,"opponent":0},"result":"win"},{"position":2,"map":"radar","score":{"own":1,"opponent":1},"result":"draw"}],
          "lineup":[{"member":{"id":"01USERWAHKE","name":"Wahke"},"slot":"starter","position":"Medic"},{"member":{"id":"01U2","name":"Nyx"},"slot":"starter","position":"Engineer"},{"member":{"id":"01U4","name":"Blizzard"},"slot":"substitute","position":null}],
          "links":[{"type":"vod","label":"Twitch-VOD","url":"https://www.twitch.tv/videos/2281234567"},{"type":"demo","label":null,"url":"https://rebelsofgaming.com/files/demo-supply.dm_84"}],
          "attachments":[{"id":"01MA1","name":"scoreboard-supply.png","image":true,"url":"https://cdn.example.test/match/01MA1.png"},{"id":"01MA2","name":"demos.zip","image":false,"url":null}],
          "report":"Starker Auftritt auf **supply**, radar war knapp.",
          "web_url":"https://rebelsofgaming.com/de/matches/2026-10-28-et-vs-iron-legion"}}
        """;

    private const string Reports = """
        {"data":[
          {"id":"01UR1","reason":"","category":"child_safety","reporter":{"name":"Nyx"},"created_at":"2026-10-29T21:14:00Z","resolution":null,
           "upload":{"id":"01UP7","title":null,"original_name":"bild_2026-10-29.png","user":{"name":"Gast123"},"links":{"thumb":"https://cdn.example.test/up/7-thumb.webp","direct":"https://i.example.test/7.png"}}},
          {"id":"01UR2","reason":"Beleidigendes Meme über einen anderen Clan.","category":null,"reporter":{"name":"Kaito"},"created_at":"2026-10-28T10:02:00Z","resolution":null,
           "upload":{"id":"01UP8","title":"Scrim-Meme","original_name":"meme.jpg","user":{"name":"Blizzard"},"links":{"thumb":"https://cdn.example.test/up/8-thumb.webp","direct":"https://i.example.test/8.jpg"}}}
        ]}
        """;

    private const string Storage = """
        {"data":{"uploads":1284,"bytes":7340032000,"users":[
          {"user":{"name":"Wahke"},"uploads":412,"bytes":2147483648},
          {"user":{"name":"Nyx"},"uploads":233,"bytes":1288490188},
          {"user":{"name":"Kaito"},"uploads":120,"bytes":524288000}
        ]}}
        """;

    private static string Social(string source, string id, string state) => state == "none"
        ? "{\"data\":{\"source\":\"" + source + "\",\"source_id\":\"" + id + "\",\"state\":\"none\",\"eligible\":true,\"can_redraft\":true,\"channels\":{\"usable\":2,\"any\":true},\"post\":null,\"history\":[]}}"
        : "{\"data\":{\"source\":\"" + source + "\",\"source_id\":\"" + id + "\",\"state\":\"partial\",\"eligible\":true,\"can_redraft\":true,\"channels\":{\"usable\":2,\"any\":true},"
          + "\"post\":{\"id\":\"" + SocialPostId + "\",\"status\":\"completed\",\"title\":\"Turnier am Samstag\",\"scheduled_at\":\"2026-10-29T17:00:00Z\",\"completed_at\":\"2026-10-29T17:00:20Z\",\"created_at\":\"2026-10-29T16:40:02Z\",\"targets\":["
          + """{"id":"01JB3D4E5F6G7H8J9K0M1N2P3R","channel_id":"01JB2C3D4E5F6G7H8J9K0M1N2P","channel":{"name":"RoG auf Bluesky","provider":"bluesky","provider_label":"Bluesky","usable":true,"status":"connected"},"status":"published","skip_reason":null,"published_at":"2026-10-29T17:00:11Z","remote_url":"https://bsky.app/profile/rebelsofgaming.bsky.social/post/3l6xyz","error":null,"retryable":false},"""
          + """{"id":"01JB3D4E5F6G7H8J9K0M1N2P3S","channel_id":"01JB2C3D4E5F6G7H8J9K0M1N2Q","channel":{"name":"RoG auf Mastodon","provider":"mastodon","provider_label":"Mastodon","usable":true,"status":"connected"},"status":"failed","skip_reason":null,"published_at":null,"remote_url":null,"error":"Mastodon: HTTP 503","retryable":true}"""
          + "]},\"history\":[]}}";
}
