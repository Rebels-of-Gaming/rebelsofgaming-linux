namespace RoG.Linux.Tests.Support;

/// <summary>Antworten des Fake-Servers (Form wie in der OpenAPI-Spec der Website).</summary>
public static class FakeData
{
    private const string Cdn = "https://cdn.example.test";

    public static string NewsList => $$$"""
        {"data":[
          {{{News("01NEWS1", "scrim-serie-herbst-2026", "Herbst-Scrims: Wir suchen Gegner für die neue Serie", "Turniere", true, "2026-10-29T18:00:00Z", 4, "cover-1")}}},
          {{{News("01NEWS2", "neuer-et-server", "Enemy Territory: Neuer Server mit Map-Rotation ist online", "Server", false, "2026-10-27T09:30:00Z", 2, "cover-2")}}},
          {{{News("01NEWS3", "minecraft-weltkarte", "Minecraft: Die Weltkarte zeigt jetzt Live-Spieler", "Minecraft", false, "2026-10-24T15:10:00Z", 3, "cover-3")}}},
          {{{News("01NEWS4", "clan-abend-november", "Clan-Abend im November – Abstimmung über die Spiele läuft", "Community", false, "2026-10-20T19:45:00Z", 2, null)}}},
          {{{News("01NEWS5", "app-fuer-linux", "Die RoG-App kommt auf Linux und das Steam Deck", "Apps", false, "2026-10-18T12:00:00Z", 5, "cover-5")}}},
          {{{News("01NEWS6", "regeln-aktualisiert", "Regelwerk aktualisiert: Was sich für Squads ändert", "Clan", false, "2026-10-12T08:00:00Z", 6, "cover-6")}}}
        ],"links":{"next":null},"meta":{"per_page":20,"next_cursor":"CURSOR2"}}
        """;

    private const string ArticleHtml = "<p>Ab November spielen unsere Squads wieder eine feste <strong>Scrim-Serie</strong> – jeden Dienstag und Donnerstag ab 20 Uhr. Gesucht sind Teams aus <em>CS2</em>, <em>Enemy Territory</em> und <em>Valorant</em>.</p><h2>So läuft es ab</h2><ul><li>Anmeldung über den Kalender auf der Website</li><li>Jedes Team spielt mindestens drei Maps</li><li>Ergebnisse landen automatisch in der Match-Übersicht</li></ul><blockquote><p>Fair Play steht über allem – wer cheatet, fliegt.</p></blockquote><h3>Server-Einstellungen</h3><pre><code>tickrate 128\nmp_maxrounds 24\nsv_cheats 0</code></pre><p>Fragen? Schreib uns im <a href=\"https://rebelsofgaming.com/de/forum\">Forum</a> oder direkt an die Squad-Leitung.</p>";

    public static string NewsArticle =>
        "{\"data\":" + News("01NEWS1", "scrim-serie-herbst-2026", "Herbst-Scrims: Wir suchen Gegner für die neue Serie", "Turniere", true, "2026-10-29T18:00:00Z", 4, "cover-1", ArticleHtml) + "}";

    public static string NewsComments => """
        {"data":[
          {"id":"01C1","author":{"id":"01U2","name":"Nyx","slug":"nyx"},"body":"Bin dabei – ET am Donnerstag passt perfekt!","created_at":"2026-10-29T19:12:00Z","hidden":false,"author_blocked":false},
          {"id":"01C2","author":{"id":"01U3","name":"Kaito","slug":"kaito"},"body":"Gibt es auch eine Bracket-Übersicht?","created_at":"2026-10-29T20:40:00Z","hidden":false,"author_blocked":false}
        ]}
        """;

    public static string Conversations => $$$"""
        {"data":[
          {{{Conversation("01CONV1", "direct", "Nyx", true, "2026-10-30T07:58:00Z", "Scrim heute 20 Uhr? Wir bräuchten noch einen fünften.", "Nyx")}}},
          {{{Conversation("01CONV2", "squad", "Squad CS2", true, "2026-10-30T06:41:00Z", "Training morgen fällt aus, neuer Termin im Kalender.", "Kaito")}}},
          {{{Conversation("01CONV3", "direct", "Kaito", false, "2026-10-29T21:15:00Z", "Danke für die Config!", "Kaito")}}},
          {{{Conversation("01CONV4", "group", "Server-Team", false, "2026-10-28T17:02:00Z", "Backup vom ET-Server ist durch.", "Wahke")}}},
          {{{Conversation("01CONV5", "direct", "Blizzard", false, "2026-10-25T13:30:00Z", "Bewerbung ist raus, danke!", "Blizzard")}}}
        ],"meta":{"next_cursor":null,"per_page":50}}
        """;

    public static string Messages => """
        {"data":[
          {"id":"01M6","conversation_id":"01CONV1","author":{"id":"01U2","slug":"nyx","name":"Nyx"},"body":"Scrim heute 20 Uhr? Wir bräuchten noch einen fünften.","deleted":false,"quote":null,"attachments":[],"source":null,"created_at":"2026-10-30T07:58:00Z"},
          {"id":"01M5","conversation_id":"01CONV1","author":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke"},"body":"Server läuft, Passwort steht im Squad-Chat.","deleted":false,"quote":{"id":"01M4","author":"Nyx","body":"Ist der Server schon oben?"},"attachments":[],"source":null,"created_at":"2026-10-29T22:10:00Z"},
          {"id":"01M4","conversation_id":"01CONV1","author":{"id":"01U2","slug":"nyx","name":"Nyx"},"body":"Ist der Server schon oben?","deleted":false,"quote":null,"attachments":[],"source":"minecraft","created_at":"2026-10-29T22:05:00Z"},
          {"id":"01M3","conversation_id":"01CONV1","author":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke"},"body":"Hier die neue Config für den Scrim-Server.","deleted":false,"quote":null,"attachments":[{"id":"01A1","name":"server.cfg","mime":"text/plain","size":2048,"image":false}],"source":null,"created_at":"2026-10-29T21:50:00Z"},
          {"id":"01M2","conversation_id":"01CONV1","author":{"id":"01U2","slug":"nyx","name":"Nyx"},"body":null,"deleted":true,"quote":null,"attachments":[],"source":null,"created_at":"2026-10-29T21:40:00Z"},
          {"id":"01M1","conversation_id":"01CONV1","author":{"id":"01U2","slug":"nyx","name":"Nyx"},"body":"Hey! Hast du die Map-Rotation schon angepasst?","deleted":false,"quote":null,"attachments":[],"source":null,"created_at":"2026-10-29T21:32:00Z"}
        ],"meta":{"next_cursor":"OLDER","per_page":50}}
        """;

    public static string Me => $$$"""
        {"data":{"id":"01USERWAHKE","slug":"wahke","name":"Wahke","email":null,"avatar_url":"{{{Cdn}}}/avatars/wahke.png","status":"active",
          "rank":{"name":"owner","label":"Owner","color":"#D4AE3C"},"two_factor_enabled":true,"joined_at":"2020-03-01",
          "squads":[],"permissions":{"global":["news.write"]},"token":{"scopes":["news:read","messages:read","profile:read"]} }}
        """;

    public static string Platform => """
        {"data":{"name":"Rebels of Gaming API","version":"v1","app":{"version":"0.53.0","commit":"15bea9f","deployed_at":"2026-10-30T08:37:28Z"},"time":"2026-10-30T08:57:35Z"},"links":{"docs":"https://api.example.test/docs"}}
        """;

    /// <summary>Wie die Produktion am 30.10.2026 (Werte erfunden) – inkl. unbekanntem Symbol („swords“).</summary>
    public static string Live => """
        {"data":[
          {"key":"online","label":"Mitglieder online","icon":"activity","tone":"green","value":12,"hint":null},
          {"key":"voice","label":"Im Voice","icon":"volume","tone":"blue","value":4,"hint":"Discord und TeamSpeak"},
          {"key":"servers","label":"Auf RoG-Servern","icon":"server","tone":"red","value":7,"hint":"ET: Sniper, CS2: Retake"},
          {"key":"members","label":"Rebels insgesamt","icon":"users","tone":"default","value":148,"hint":null},
          {"key":"next_event","label":"Nächstes Event","icon":"calendar","tone":"gold","value":"Di., 20:00","hint":"Scrim-Abend CS2"},
          {"key":"recruiting","label":"Squads suchen Spieler","icon":"swords","tone":"red","value":3,"hint":null}
        ],"meta":{"updated_at":"2026-10-30T08:57:19Z","ttl":60}}
        """;

    public static string Events => $$$"""
        {"data":[
          {{{Event("01EV1", "Scrim-Abend CS2", "2026-11-03T19:00:00Z", "2026-11-03T21:00:00Z")}}},
          {{{Event("01EV2", "Clan-Abend November", "2026-11-06T19:30:00Z", null)}}},
          {{{Event("01EV3", "ET-Turnier: Qualifikation", "2026-11-14T18:00:00Z", "2026-11-14T22:00:00Z")}}}
        ],"links":{"next":null},"meta":{"per_page":50,"next_cursor":null}}
        """;

    public static string Votes => """
        {"data":[]}
        """;

    public static string Onboarding => """
        {"data":{"progress":60,"complete":false,"steps":[
          {"key":"profile","label":"Profil ausfüllen","done":true,"url":null},
          {"key":"discord","label":"Discord verbinden","done":true,"url":null},
          {"key":"squad","label":"Einem Squad beitreten","done":true,"url":null},
          {"key":"rules","label":"Regeln bestätigen","done":false,"url":"https://rebelsofgaming.com/de/rules"},
          {"key":"event","label":"Zu einem Termin zusagen","done":false,"url":"https://rebelsofgaming.com/de/events"}
        ]}}
        """;

    private static string Event(string id, string title, string starts, string? ends)
    {
        var endsJson = ends is null ? "null" : $"\"{ends}\"";
        return $$$"""
        {"id":"{{{id}}}","type":"event","type_label":"Event","color":"#E0324A","event_type":{"key":"event","system":true,"name":"Event","color":"#E0324A"},
         "title":"{{{title}}}","description":null,"starts_at":"{{{starts}}}","ends_at":{{{endsJson}}},"squad":null,"mandatory":false,
         "visibility":"public","location":{"type":null,"value":null,"label":null,"server":null},"max_participants":null,"detached":false,"cancelled":false,
         "rsvp_counts":{"yes":5,"maybe":2,"no":0},"my_rsvp":null,"url":"https://rebelsofgaming.com/de/events/{{{id}}}","ics_url":"https://rebelsofgaming.com/ics/{{{id}}}"}
        """;
    }

    private static string News(string id, string slug, string title, string category, bool pinned, string published, int minutes, string? cover, string? body = null)
    {
        var coverJson = cover is null ? "null" : $$$"""{"url":"{{{Cdn}}}/news/{{{cover}}}.png","variants":[],"alt":{"de":"{{{title}}}"} }""";
        var bodyJson = body is null ? "null" : System.Text.Json.JsonSerializer.Serialize(body);
        return $$$"""
            {"id":"{{{id}}}","slug":"{{{slug}}}","url":"https://rebelsofgaming.com/de/news/{{{slug}}}","title":"{{{title}}}","teaser":null,"summary":"{{{title}}}",
             "status":"published","visibility":"public","pinned":{{{(pinned ? "true" : "false")}}},"comments_enabled":true,"comments_count":2,
             "published_at":"{{{published}}}","reading_minutes":{{{minutes}}},"category":{"slug":"x","name":"{{{category}}}","color":"#E0324A"},
             "squad":null,"author":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},"cover":{{{coverJson}}},"body":null,"body_html":{{{bodyJson}}} }
            """;
    }

    private static string Conversation(string id, string type, string name, bool unread, string at, string preview, string author)
    {
        var partner = type == "direct" ? $$$"""{"id":"01P{{{id}}}","slug":"{{{name.ToUpperInvariant()}}}","name":"{{{name}}}","avatar_url":"{{{Cdn}}}/avatars/{{{name}}}.png"}""" : "null";
        return $$$"""
            {"id":"{{{id}}}","type":"{{{type}}}","name":"{{{name}}}","squad":null,"unread":{{{(unread ? "true" : "false")}}},"archived":false,"is_admin":false,
             "last_message_at":"{{{at}}}","partner":{{{partner}}},
             "last_message":{"id":"01L{{{id}}}","author":{"id":"01X","slug":"x","name":"{{{author}}}"},"body":"{{{preview}}}","deleted":false,"attachments":0,"created_at":"{{{at}}}"}}
            """;
    }
}
