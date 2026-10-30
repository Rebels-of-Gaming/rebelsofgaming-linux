namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Matches, Hall of Fame, Turniere, Statistiken (Guide 14/15/19) und Minecraft-Bestenlisten
/// (Guide 44) – Form wie die API, Inhalte erfunden.
/// </summary>
[FakeRoutes]
internal static class FakeMatchesRoutes
{
    public const string FirstSlug = "2026-10-06-et-vs-wolfpack-esports";

    /// <summary>Match, das nicht in der ersten Seite der Liste steht (Deep Link).</summary>
    public const string OldSlug = "2025-11-20-cs2-vs-night-owls";

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        if (request.Method != HttpMethod.Get)
        {
            return null;
        }

        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        return path switch
        {
            "/v1/matches" when query.Contains("cursor=page2", StringComparison.Ordinal) => MatchesPage2,
            "/v1/matches" => Matches,
            "/v1/matches/" + FirstSlug or "/v1/matches/01JMATCH0000000000000001" => MatchDetail,
            "/v1/matches/" + OldSlug => OldMatch,
            "/v1/hall-of-fame" => HallOfFame,
            "/v1/tournaments" => Tournaments,
            "/v1/stats/games" => request.Headers.Authorization is null ? null : StatsGames,
            "/v1/stats/et/leaderboard" => request.Headers.Authorization is null ? null : Leaderboard(query),
            "/v1/stats/cs2/leaderboard" => request.Headers.Authorization is null ? null : """{"data":[],"meta":{"game":{"slug":"cs2"},"period":"30d","metric":"kills","me":null}}""",
            "/v1/stats/et/awards" => request.Headers.Authorization is null ? null : Awards,
            "/v1/stats/cs2/awards" => request.Headers.Authorization is null ? null : """{"data":[],"meta":{"game":{"slug":"cs2"},"week":"2026-W43"}}""",
            "/v1/minecraft/leaderboards" => MinecraftBoard(query),
            _ => null,
        };
    }

    private static string Match(string id, string slug, string squad, string squadSlug, string game, string opponent, string? tag, string playedAt,
        int? own, int? opp, string? result, string maps, string extra = "", string type = "clanwar", string status = "finished") =>
        $$$"""
        {"id":"{{{id}}}","slug":"{{{slug}}}","type":"{{{type}}}","status":"{{{status}}}",
         "squad":{"id":"01JSQ{{{squadSlug.ToUpperInvariant()}}}","slug":"{{{squadSlug}}}","name":"{{{squad}}}","game":"{{{game}}}"},
         "opponent":{"name":"{{{opponent}}}","tag":{{{(tag is null ? "null" : "\"" + tag + "\"")}}},"url":null},
         "played_at":"{{{playedAt}}}","score":{"own":{{{(own?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}}},"opponent":{{{(opp?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}}}},
         "result":{{{(result is null ? "null" : "\"" + result + "\"")}}},"published":true,"event_id":null,"tournament":null,
         "maps":[{{{maps}}}],{{{extra}}}
         "web_url":"https://rebelsofgaming.com/de/matches/{{{slug}}}","created_at":"{{{playedAt}}}","updated_at":"{{{playedAt}}}"}
        """;

    private static string Map(int position, string map, int own, int opp, string result) =>
        $$$"""{"position":{{{position}}},"map":"{{{map}}}","score":{"own":{{{own}}},"opponent":{{{opp}}}},"result":"{{{result}}}"}""";

    private static readonly string s_first = Match("01JMATCH0000000000000001", FirstSlug, "RoG ET", "et", "ET", "Wolfpack eSports", "WP", "2026-10-06T16:30:00Z", 2, 0, "win",
        Map(1, "supply", 2, 0, "win") + "," + Map(2, "radar", 2, 1, "win"));

    public static string Matches => $$$"""
        {"data":[
          {{{s_first}}},
          {{{Match("01JMATCH0000000000000002", "2026-10-02-cs2-vs-night-owls", "RoG CS2", "cs2", "CS2", "Night Owls", "NO", "2026-10-02T18:00:00Z", 11, 13, "loss",
              Map(1, "de_mirage", 11, 13, "loss"), type: "scrim")}}},
          {{{Match("01JMATCH0000000000000003", "2026-09-27-et-vs-frontline", "RoG ET", "et", "ET", "Frontline", "FL", "2026-09-27T19:00:00Z", 1, 1, "draw",
              Map(1, "goldrush", 1, 0, "win") + "," + Map(2, "oasis", 0, 1, "loss"))}}},
          {{{Match("01JMATCH0000000000000004", "2026-11-08-et-vs-iron-legion", "RoG ET", "et", "ET", "Iron Legion", null, "2026-11-08T19:00:00Z", null, null, null,
              "", status: "scheduled")}}}
        ],"links":{"first":null,"last":null,"prev":null,"next":"/v1/matches?cursor=page2"},"meta":{"path":"/v1/matches","per_page":30,"next_cursor":"page2","prev_cursor":null}}
        """;

    public static string MatchesPage2 => $$$"""
        {"data":[
          {{{Match("01JMATCH0000000000000005", "2026-09-12-rtcw-vs-old-guard", "RoG RtCW", "rtcw", "RTCW", "Old Guard", "OG", "2026-09-12T19:00:00Z", 2, 1, "win",
              Map(1, "mp_beach", 2, 1, "win"))}}}
        ],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"/v1/matches","per_page":30,"next_cursor":null,"prev_cursor":null}}
        """;

    public static string MatchDetail => "{\"data\":" + Match("01JMATCH0000000000000001", FirstSlug, "RoG ET", "et", "ET", "Wolfpack eSports", "WP", "2026-10-06T16:30:00Z", 2, 0, "win",
        Map(1, "supply", 2, 0, "win") + "," + Map(2, "radar", 2, 1, "win"),
        """
        "lineup":[
          {"member":{"id":"01JMEMKOLIBRI","slug":"kolibri","name":"Kolibri"},"slot":"starter","position":"Medic"},
          {"member":{"id":"01JMEMWAHKE","slug":"wahke","name":"Wahke"},"slot":"starter","position":"Engineer"},
          {"member":{"id":"01JMEMKAITO","slug":"kaito","name":"Kaito"},"slot":"substitute","position":null},
          {"member":{"id":"01JMEMNOVA","slug":"nova","name":"Nova"},"slot":"starter","position":"Field Ops"}
        ],
        "links":[{"type":"vod","label":"Twitch-VOD","url":"https://www.twitch.tv/videos/2268000000"},{"type":"demo","label":null,"url":"https://cdn.rogclan.de/demos/supply.dm_84"}],
        "attachments":[],
        "report":"Starker Auftritt auf **Supply**.",
        "report_html":"<p>Starker Auftritt auf <strong>Supply</strong>: Der Tank war nach sieben Minuten durch, auf Radar hielt die Verteidigung bis zum Schluss.</p>",
        """) + "}";

    public static string OldMatch => "{\"data\":" + Match("01JMATCH0000000000000099", OldSlug, "RoG CS2", "cs2", "CS2", "Night Owls", "NO", "2025-11-20T19:00:00Z", 16, 9, "win",
        Map(1, "de_inferno", 16, 9, "win"), "\"lineup\":[],\"links\":[],\"attachments\":[],\"report\":null,\"report_html\":null,") + "}";

    public const string HallOfFame = """
        {"data":{
          "trophies":[
            {"id":"01JTR1","kind":"trophy","title":"ET Legacy Open 2026 – Sieger","description":"Fünf Spiele ohne Niederlage, Finale 2:0 gegen Wolfpack.","achieved_on":"2026-05-17","placement":1,"medal":"gold","squad":{"id":"01JSQET","slug":"et","name":"RoG ET"},"game":{"id":"01JGET","slug":"et","short":"ET"},"tournament_id":null,"sort":10},
            {"id":"01JTR2","kind":"trophy","title":"ET Nations Cup 2026 – Platz 2","description":null,"achieved_on":"2026-07-12","placement":2,"medal":"silver","squad":{"id":"01JSQET","slug":"et","name":"RoG ET"},"game":{"id":"01JGET","slug":"et","short":"ET"},"tournament_id":null,"sort":20},
            {"id":"01JTR3","kind":"trophy","title":"CS2 Community Cup – Platz 3","description":null,"achieved_on":"2024-06-08","placement":3,"medal":"bronze","squad":{"id":"01JSQCS2","slug":"cs2","name":"RoG CS2"},"game":{"id":"01JGCS2","slug":"cs2","short":"CS2"},"tournament_id":null,"sort":30}
          ],
          "milestones":[
            {"id":"01JMS1","kind":"milestone","title":"100 Mitglieder","description":"Der Clan knackt die Hundert.","achieved_on":"2022-09-01","placement":null,"medal":null,"squad":null,"game":null,"tournament_id":null,"sort":10},
            {"id":"01JMS2","kind":"milestone","title":"Eigener Minecraft-Server","description":null,"achieved_on":"2023-03-15","placement":null,"medal":null,"squad":null,"game":null,"tournament_id":null,"sort":11}
          ],
          "honorary":[
            {"slug":"snoop","name":"Snoop","avatar_url":null,"period":"2020–2024","tribute":"Gründungsmitglied, erster Squad-Leader von ET und Seele jedes Clanabends.","featured":true}
          ],
          "former":[]
        }}
        """;

    public const string Tournaments = """
        {"data":[
          {"id":"01JTO1","slug":"et-nations-cup-2026","name":"ET Nations Cup 2026","organizer":"ET Nations","organizer_type":"external","game":{"id":"01JGET","slug":"et","short":"ET"},"squad":{"id":"01JSQET","slug":"et","name":"RoG ET"},"starts_on":"2026-06-01","ends_on":"2026-07-12","placement":2,"participants_count":16,"url":"https://etnations.example.org/cup-2026","description":null},
          {"id":"01JTO2","slug":"rog-winter-scrims","name":"RoG Winter-Scrims","organizer":"Rebels of Gaming","organizer_type":"internal","game":{"id":"01JGCS2","slug":"cs2","short":"CS2"},"squad":{"id":"01JSQCS2","slug":"cs2","name":"RoG CS2"},"starts_on":"2026-11-15","ends_on":null,"placement":null,"participants_count":8,"url":null,"description":null}
        ]}
        """;

    public const string StatsGames = """
        {"data":[{"id":"01JGET","slug":"et","name":"Wolfenstein: Enemy Territory"},{"id":"01JGCS2","slug":"cs2","name":"Counter-Strike 2"}]}
        """;

    private static string Leaderboard(string query)
    {
        var metric = query.Contains("metric=kd", StringComparison.Ordinal) ? "kd" : "kills";
        return $$$"""
            {"data":[
              {"rank":1,"member":{"id":"01JMEMKOLIBRI","slug":"kolibri","name":"Kolibri"},"rounds":31,"kills":402,"deaths":190,"suicides":7,"teamkills":2,"seconds":37200,"kd":2.12,"headshots":0},
              {"rank":2,"member":{"id":"01JMEMNOVA","slug":"nova","name":"Nova"},"rounds":28,"kills":355,"deaths":201,"suicides":3,"teamkills":1,"seconds":33100,"kd":1.77,"headshots":0},
              {"rank":3,"member":{"id":"01JMEMWAHKE","slug":"wahke","name":"Wahke"},"rounds":24,"kills":298,"deaths":180,"suicides":4,"teamkills":0,"seconds":28800,"kd":1.66,"headshots":0},
              {"rank":4,"member":{"id":"01JMEMKAITO","slug":"kaito","name":"Kaito"},"rounds":19,"kills":204,"deaths":166,"suicides":2,"teamkills":3,"seconds":21600,"kd":1.23,"headshots":0}
            ],"meta":{"game":{"id":"01JGET","slug":"et","name":"Wolfenstein: Enemy Territory"},"period":"30d","metric":"{{{metric}}}","from":"2026-09-30","me":3}}
            """;
    }

    public const string Awards = """
        {"data":[
          {"award":"most_kills","value":148,"members":[{"id":"01JMEMKOLIBRI","slug":"kolibri","name":"Kolibri"}]},
          {"award":"best_kd","value":2.43,"members":[{"id":"01JMEMNOVA","slug":"nova","name":"Nova"}]},
          {"award":"most_playtime","value":14400,"members":[{"id":"01JMEMWAHKE","slug":"wahke","name":"Wahke"},{"id":"01JMEMKAITO","slug":"kaito","name":"Kaito"}]}
        ],"meta":{"game":{"slug":"et"},"week":"2026-W43","from":"2026-10-19","to":"2026-10-25"}}
        """;

    private static string MinecraftBoard(string query)
    {
        if (query.Contains("board=parkour", StringComparison.Ordinal))
        {
            var course = query.Contains("course=nether", StringComparison.Ordinal) ? "nether" : "lobby";
            return $$$"""
                {"data":[
                  {"rank":1,"value":48250,"member":{"id":"01JMEMNOVA","slug":"nova","name":"Nova","avatar_url":null}},
                  {"rank":2,"value":51900,"member":{"id":"01JMEMKAITO","slug":"kaito","name":"Kaito","avatar_url":null}}
                ],"meta":{"board":"parkour","period":"all","sort":null,"course":"{{{course}}}","courses":[{"id":"lobby","label":"Lobby-Parkour"},{"id":"nether","label":"Nether-Lauf"}],"from":null}}
                """;
        }

        if (query.Contains("board=bedwars", StringComparison.Ordinal))
        {
            var rate = query.Contains("sort=win_rate", StringComparison.Ordinal);
            return $$$"""
                {"data":[
                  {"rank":1,"value":{{{(rate ? "71.4" : "25")}}},"played":35,"won":25,"win_rate":71.4,"member":{"id":"01JMEMKOLIBRI","slug":"kolibri","name":"Kolibri","avatar_url":null}},
                  {"rank":2,"value":{{{(rate ? "58.3" : "14")}}},"played":24,"won":14,"win_rate":58.3,"member":{"id":"01JMEMWAHKE","slug":"wahke","name":"Wahke","avatar_url":null}}
                ],"meta":{"board":"bedwars","period":"all","sort":"{{{(rate ? "win_rate" : "wins")}}}","min_matches":10,"from":null}}
                """;
        }

        return """
            {"data":[
              {"rank":1,"value":403500,"member":{"id":"01JMEMWAHKE","slug":"wahke","name":"Wahke","avatar_url":null}},
              {"rank":2,"value":288120,"member":{"id":"01JMEMKOLIBRI","slug":"kolibri","name":"Kolibri","avatar_url":null}},
              {"rank":3,"value":97260,"member":{"id":"01JMEMNOVA","slug":"nova","name":"Nova","avatar_url":null}},
              {"rank":4,"value":1800,"member":{"id":"01JMEMKAITO","slug":"kaito","name":"Kaito","avatar_url":null}}
            ],"meta":{"board":"playtime","period":"all","sort":null,"from":"2026-09-23T00:00:00Z"}}
            """;
    }
}
