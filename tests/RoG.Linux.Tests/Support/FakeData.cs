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
          "squads":[],"permissions":{"global":["news.write"]},"token":{"scopes":["news:read","messages:read","profile:read","squads:read","events:read","forum:read","members:read","servers:read","notifications:read"]} }}
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

    // ---- Stufe 2: Community ----

    public static string Squads => $$$"""
        {"data":[
          {{{Squad("01SQ1", "enemy-territory", "Enemy Territory", "Objective-Spezialisten seit Tag eins", "ET", "Wolfenstein: Enemy Territory", "open", 12, "Wahke")}}},
          {{{Squad("01SQ2", "counter-strike-2", "Counter-Strike 2", "Premier, Faceit und Clanwars mit Teamplay", "CS2", "Counter-Strike 2", "waitlist", 8, "Nyx")}}},
          {{{Squad("01SQ3", "minecraft", "Minecraft", "Freebuild, Plots und Minigames", "MC", "Minecraft", "full", 21, "Kaito")}}}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    public static string SquadDetail =>
        "{\"data\":" + Squad("01SQ1", "enemy-territory", "Enemy Territory", "Objective-Spezialisten seit Tag eins", "ET", "Wolfenstein: Enemy Territory", "open", 12, "Wahke",
            "Wolfenstein: Enemy Territory ist da, wo Rebels of Gaming angefangen hat. Bei uns zählt das Objective, nicht das Scoreboard: Wir planen Pushes, decken unsere Engineers und spielen jede Klasse so, wie das Team sie braucht.",
            "Dienstag und Donnerstag ab 20 Uhr") + "}";

    public static string SquadMembers => $$$"""
        {"data":[
          {"id":"01USERWAHKE","slug":"wahke","name":"Wahke","avatar_url":"{{{Cdn}}}/avatars/wahke.png","role":{"id":"r1","name":"squad-leader","label":"Squad-Leader","color":"#E0324A"},"is_substitute":false,"minecraft":null},
          {"id":"01U2","slug":"nyx","name":"Nyx","avatar_url":"{{{Cdn}}}/avatars/nyx.png","role":{"id":"r2","name":"member","label":"Member","color":"#F1EFEA"},"is_substitute":false,"minecraft":null},
          {"id":"01U3","slug":"kaito","name":"Kaito","avatar_url":null,"role":{"id":"r2","name":"member","label":"Member","color":"#F1EFEA"},"is_substitute":false,"minecraft":null},
          {"id":"01U4","slug":"blizzard","name":"Blizzard","avatar_url":"{{{Cdn}}}/avatars/blizzard.png","role":{"id":"r2","name":"member","label":"Member","color":"#F1EFEA"},"is_substitute":true,"minecraft":null}
        ]}
        """;

    public static string SquadInternal => """
        {"data":{"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory","url":"https://rebelsofgaming.com/de/squads/enemy-territory"},
          "forum":{"access":true,"board":{"name":"ET intern","slug":"et-intern"},"can_post":true,"url":"https://rebelsofgaming.com/de/forum/et-intern","new_url":null,"threads":[
            {"id":"01T1","title":"Taktik für Supply – wer übernimmt die Engineers?","url":"https://rebelsofgaming.com/de/forum/t/01T1/taktik","author":"Nyx","replies":7,"pinned":true,"locked":false,"pending":false,"last_post_at":"2026-10-29T20:10:00Z","last_poster":"Wahke"},
            {"id":"01T2","title":"Clanwar gegen [XYZ] am Samstag","url":"https://rebelsofgaming.com/de/forum/t/01T2/cw","author":"Kaito","replies":3,"pinned":false,"locked":false,"pending":false,"last_post_at":"2026-10-28T18:00:00Z","last_poster":"Kaito"}]},
          "conversation":{"access":true,"id":"01CONV2","url":"https://rebelsofgaming.com/de/messages/01CONV2","unread":true,"last_message":{"author":"Kaito","excerpt":"Training morgen fällt aus.","at":"2026-10-30T06:41:00Z","mine":false}},
          "events":{"access":true,"url":"https://rebelsofgaming.com/de/events","items":[
            {"id":"01EV1","title":"Scrim-Abend CS2","type":"training","type_label":"Training","event_type":{"key":"training"},"color":"#3DDC84","starts_at":"2026-11-03T19:00:00Z","ends_at":"2026-11-03T21:00:00Z","mandatory":true,"location":"ET: Sniper","yes":5,"my_rsvp":"yes"}]},
          "downloads":{"access":true,"url":"https://rebelsofgaming.com/de/downloads","items":[
            {"slug":"et-config","title":"ET-Config für Clanwars","visibility":"squad","visibility_label":"Squad","version":"2.1","size":2048,"updated_at":"2026-10-20T10:00:00Z","url":"https://rebelsofgaming.com/de/downloads/et-config"}]},
          "albums":{"access":true,"url":"https://rebelsofgaming.com/de/gallery","items":[
            {"slug":"lan-2026","title":"LAN 2026","visibility":"squad","items":42,"cover":null,"url":"https://rebelsofgaming.com/de/gallery/lan-2026"}]}},
         "meta":{"missing_scopes":["downloads:read"]}}
        """;

    public static string Servers => """
        {"data":[
          {"id":"01SV1","slug":"et-sniper","name":"RoG ET: Sniper","description":null,"game":{"id":"g1","name":"Wolfenstein: Enemy Territory","short":"ET"},"squad":{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"},
           "address":"et.rogclan.de:27960","query_type":"quake3","connect_url":"et://et.rogclan.de:27960","visibility":"public",
           "live":{"online":true,"name":"RoG Sniper","map":"supply","players":9,"max_players":20,"passworded":false,"player_list":[{"name":"Nyx","score":42,"ping":28,"member":{"name":"Nyx"}},{"name":"^1Kai^7to","score":30,"ping":35,"member":{"name":"Kaito"}},{"name":"Gast","score":12,"ping":80,"member":null}]},"control":null,"provisioning":null},
          {"id":"01SV2","slug":"cs2-retake","name":"RoG CS2: Retake","description":null,"game":{"id":"g2","name":"Counter-Strike 2","short":"CS2"},"squad":{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"},
           "address":"cs.rogclan.de:27015","query_type":"source","connect_url":"steam://connect/cs.rogclan.de:27015","visibility":"public",
           "live":{"online":true,"name":"RoG Retake","map":"de_mirage","players":4,"max_players":10,"passworded":true,"player_list":null},"control":null,"provisioning":null},
          {"id":"01SV3","slug":"rog-mc","name":"RoG Minecraft Netzwerk","description":null,"game":{"id":"g3","name":"Minecraft","short":"MC"},"squad":null,
           "address":"mc.rogclan.de:25565","query_type":"minecraft","connect_url":null,"visibility":"public",
           "live":{"online":true,"name":"RoG MC","map":null,"players":0,"max_players":100,"passworded":false,"player_list":null},"control":null,"provisioning":null},
          {"id":"01SV4","slug":"rtcw","name":"RoG RtCW Classic","description":null,"game":{"id":"g4","name":"Return to Castle Wolfenstein","short":"RtCW"},"squad":null,
           "address":"rtcw.rogclan.de:27960","query_type":"quake3","connect_url":"wolf://rtcw.rogclan.de:27960","visibility":"public",
           "live":{"online":false,"name":null,"map":null,"players":0,"max_players":0,"passworded":false,"player_list":null},"control":null,"provisioning":null}
        ],"meta":{"per_page":50,"next_cursor":null}}
        """;

    public static string Members => $$$"""
        {"data":[
          {{{Member("01USERWAHKE", "wahke", "Wahke", "owner", "Owner", "#D4AE3C", "online", null, "Enemy Territory")}}},
          {{{Member("01U2", "nyx", "Nyx", "member", "Member", "#F1EFEA", "voice", "Discord: Talk 1", "Counter-Strike 2")}}},
          {{{Member("01U3", "kaito", "Kaito", "moderator", "Moderator", "#3DDC84", "server", "RoG ET: Sniper", "Enemy Territory")}}},
          {{{Member("01U4", "blizzard", "Blizzard", "member", "Member", "#F1EFEA", "ingame", "Valorant", "Valorant")}}},
          {{{Member("01U5", "sariela", "Sariela", "owner", "Owner", "#D4AE3C", null, null, "Minecraft")}}},
          {{{Member("01U6", "fitti", "Fitti", "trial", "Trial", "#A8ACB6", null, null, null)}}}
        ],"meta":{"per_page":40,"next_cursor":"MEMBERS2"}}
        """;

    public static string MemberProfile => $$$"""
        {"data":{"id":"01U2","slug":"nyx","name":"Nyx","avatar_url":"{{{Cdn}}}/avatars/nyx.png","rank":{"id":"r","name":"member","label":"Member","color":"#E0324A"},
          "status":"voice","activity":{"label":"Discord: Talk 1"},"last_seen_at":"2026-10-30T07:58:00Z","joined_at":"2021-05-14T18:00:00Z",
          "squads":[{"id":"01SQ2","slug":"counter-strike-2","name":"Counter-Strike 2"},{"id":"01SQ1","slug":"enemy-territory","name":"Enemy Territory"}],
          "roles":null,
          "badges":[{"name":"Gründungsmitglied","description":"Seit 2020 dabei","color":"#D4AE3C"},{"name":"Turniersieger","description":"ET-Cup 2025","color":"#E0324A"},{"name":"Helfer","description":null,"color":"#3DDC84"}],
          "profile":{"bio":"Entry-Fragger in CS2, Engineer in ET. Organisiere die Scrim-Abende und bin fast jeden Abend im Voice.","motto":"Erst das Objective, dann der Spaß.",
            "banner":{"url":"{{{Cdn}}}/banners/nyx.png","url_small":"{{{Cdn}}}/banners/nyx-small.png"},"city":"Trier","country":"Deutschland",
            "platform_ids":[{"name":"Steam","handle":"nyx_rog"},{"name":"Faceit","handle":"NyxROG"}],"rebel_coins":{"balance":1250,"streak":4,"best_streak":12},"minecraft":null} } }
        """;

    public static string Team => $$$"""
        {"data":[
          {"id":"tg1","name":"Clanleitung","description":"Entscheidet über Richtung, Finanzen und Aufnahmen.","members":[
            {"id":"01U5","slug":"sariela","name":"Sariela","avatar_url":"{{{Cdn}}}/avatars/sariela.png","role":{"id":"r","name":"owner","label":"Owner","color":"#D4AE3C"},"squads":[],"responsibility":"Finanzen"},
            {"id":"01USERWAHKE","slug":"wahke","name":"Wahke","avatar_url":"{{{Cdn}}}/avatars/wahke.png","role":{"id":"r","name":"owner","label":"Owner","color":"#D4AE3C"},"squads":[],"responsibility":"Technik und Server"}]},
          {"id":"tg2","name":"Moderation","description":null,"members":[
            {"id":"01U3","slug":"kaito","name":"Kaito","avatar_url":null,"role":{"id":"r","name":"moderator","label":"Moderator","color":"#3DDC84"},"squads":[],"responsibility":null}]}
        ]}
        """;

    public static string Games => """
        {"data":[
          {"id":"g1","slug":"et","name":"Wolfenstein: Enemy Territory","short":"ET","platform":"pc","active":true,"genre":"Shooter","emoji":null,"clan_tag":"RoG|","header":null,"links":[{"label":"ET: Legacy","url":"https://www.etlegacy.com","short_url":null,"code":null}]},
          {"id":"g2","slug":"cs2","name":"Counter-Strike 2","short":"CS2","platform":"pc","active":true,"genre":"Shooter","emoji":null,"clan_tag":null,"header":null,"links":[{"label":"Steam","url":"https://store.steampowered.com/app/730","short_url":null,"code":null}]},
          {"id":"g3","slug":"mc","name":"Minecraft","short":"MC","platform":"pc","active":true,"genre":"Sandbox","emoji":null,"clan_tag":null,"header":null,"links":[]}
        ]}
        """;

    public static string EventDetail =>
        "{\"data\":" + Event("01EV1", "Scrim-Abend CS2", "2026-11-03T19:00:00Z", "2026-11-03T21:00:00Z")
            .Replace("\"description\":null", "\"description\":\"Wir spielen drei Maps gegen [XYZ]. Bitte 15 Minuten vorher im TeamSpeak sein.\"", StringComparison.Ordinal)
            .Replace("\"my_rsvp\":null", "\"my_rsvp\":{\"status\":\"yes\",\"comment\":null}", StringComparison.Ordinal)
            .Replace("\"location\":{\"type\":null,\"value\":null,\"label\":null,\"server\":null}",
                "\"location\":{\"type\":\"server\",\"value\":null,\"label\":\"RoG CS2: Retake\",\"server\":{\"id\":\"01SV2\",\"slug\":\"cs2-retake\",\"name\":\"RoG CS2: Retake\",\"game\":\"Counter-Strike 2\",\"address\":\"cs.rogclan.de:27015\",\"connect_url\":\"steam://connect/cs.rogclan.de:27015\",\"online\":true,\"map\":\"de_mirage\",\"players\":4,\"max_players\":10}}", StringComparison.Ordinal)
        + "}";

    /// <summary>Termin ohne eigene Antwort (Gastmodus).</summary>
    public static string EventItem(string id) => Event(id, "Clan-Abend November", "2026-11-06T19:30:00Z", null);

    public static string EventParticipants => """
        {"data":{"yes":[{"id":"01U2","slug":"nyx","name":"Nyx","absent":false,"comment":null},{"id":"01U3","slug":"kaito","name":"Kaito","absent":false,"comment":null}],
          "maybe":[{"id":"01U4","slug":"blizzard","name":"Blizzard","absent":false,"comment":"Wenn ich rechtzeitig da bin"}],
          "no":[],"open":[{"id":"01U5","slug":"sariela","name":"Sariela","absent":true,"comment":null}]}}
        """;

    public static string Rules => """
        {"data":[{"id":"01R1","slug":"clan","scope":"clan","title":"Clanregeln – Rebels of Gaming","intro":"Sei fair, sei respektvoll, hab Spaß.","visibility":"public","requires_acceptance":true,"version":1,"accepted":false,"updated_at":"2026-10-11T12:10:09Z"},
          {"id":"01R2","slug":"enemy-territory","scope":"game","title":"Server-Regeln Enemy Territory","intro":null,"visibility":"public","requires_acceptance":false,"version":3,"accepted":null,"updated_at":"2026-10-01T12:00:00Z"}]}
        """;

    public static string RulesDetail => """
        {"data":{"id":"01R1","slug":"clan","scope":"clan","title":"Clanregeln – Rebels of Gaming","intro":"Diese Regeln gelten für alle Mitglieder, Trials und Gäste – auf Website, Discord, TeamSpeak und unseren Gameservern.","visibility":"public","requires_acceptance":true,"version":1,"accepted":false,"updated_at":"2026-10-11T12:10:09Z",
          "sections":[
            {"number":1,"anchor":"regel-1","title":"Geltungsbereich","body":"","html":"<ul><li>Die Clanregeln gelten auf <strong>allen RoG-Plattformen</strong>.</li><li>Spiel- und Squad-Regeln ergänzen dieses Regelwerk.</li></ul>","important":false},
            {"number":2,"anchor":"regel-2","title":"Respekt","body":"","html":"<p>Beleidigungen, Rassismus und Hetze haben bei uns <strong>keinen Platz</strong>. Wer das ignoriert, fliegt.</p>","important":true},
            {"number":3,"anchor":"regel-3","title":"Fair Play","body":"","html":"<p>Cheats, Exploits und Smurfing sind verboten. Im Zweifel fragst du die Squad-Leitung.</p>","important":false}]}}
        """;

    public static string Pages => """
        {"data":[{"slug":"about","title":"Über uns","sort":1},{"slug":"partners","title":"Partner","sort":2}]}
        """;

    public static string Legal => """
        {"data":[{"type":"privacy","title":"Datenschutzerklärung","version":26,"published_at":"2026-10-29T07:57:29Z","requires_acceptance":false,"acceptable":true},
          {"type":"imprint","title":"Impressum","version":3,"published_at":"2026-09-01T07:00:00Z","requires_acceptance":false,"acceptable":false}]}
        """;

    public static string Search => """
        {"data":[
          {"key":"squads","label":"Squads","hits":[{"title":"Enemy Territory","excerpt":"Objective-Spezialisten seit Tag eins","url":"https://rebelsofgaming.com/de/squads/enemy-territory","meta":null}],"failed":false},
          {"key":"news","label":"News","hits":[{"title":"Enemy Territory: Neuer Server mit Map-Rotation","excerpt":"Ab sofort läuft unser neuer <b>ET</b>-Server …","url":"https://rebelsofgaming.com/de/news/neuer-et-server","meta":"27.10.2026"}],"failed":false},
          {"key":"forum","label":"Forum","hits":[{"title":"Taktik für Supply","excerpt":"Wer übernimmt die Engineers?","url":"https://rebelsofgaming.com/de/forum/t/01T1/taktik","meta":"Nyx"}],"failed":false},
          {"key":"wiki","label":"Wiki","hits":[],"failed":true}
        ]}
        """;

    public static string Notifications => $$$"""
        {"data":[
          {{{Notification("01N1", "forum.mention", "Nyx hat dich in „Taktik für Supply“ erwähnt", "@Wahke übernimmst du die Engineers?", "https://rebelsofgaming.com/de/forum/t/01T1/taktik", "forum", null)}}},
          {{{Notification("01N2", "events.reminder", "Scrim-Abend CS2 beginnt in einer Stunde", null, "https://rebelsofgaming.com/de/events/01EV1", "calendar", null)}}},
          {{{Notification("01N3", "news.published", "Neue News: Herbst-Scrims", "Wir suchen Gegner für die neue Serie.", "https://rebelsofgaming.com/de/news/scrim-serie-herbst-2026", "news", null)}}},
          {{{Notification("01N4", "squads.joined", "Willkommen im Squad Enemy Territory", null, "https://rebelsofgaming.com/de/squads/enemy-territory", "users", "2026-10-28T10:00:00Z")}}}
        ],"meta":{"per_page":30,"next_cursor":null}}
        """;

    public static string NotificationsUnread => $$$"""
        {"data":[
          {{{Notification("01N1", "forum.mention", "Nyx hat dich in „Taktik für Supply“ erwähnt", "@Wahke übernimmst du die Engineers?", "https://rebelsofgaming.com/de/forum/t/01T1/taktik", "forum", null)}}}
        ],"meta":{"per_page":30,"next_cursor":null}}
        """;

    private static string Notification(string id, string type, string title, string? body, string url, string icon, string? readAt)
    {
        var bodyJson = body is null ? "null" : System.Text.Json.JsonSerializer.Serialize(body);
        var readJson = readAt is null ? "null" : $"\"{readAt}\"";
        return $$$"""
            {"id":"{{{id}}}","type":"{{{type}}}","title":{{{System.Text.Json.JsonSerializer.Serialize(title)}}},"body":{{{bodyJson}}},"url":"{{{url}}}","icon":"{{{icon}}}","read_at":{{{readJson}}},"created_at":"2026-10-30T08:{{{id[^1]}}}0:00Z"}
            """;
    }

    private static string Squad(string id, string slug, string name, string tagline, string gameShort, string game, string recruiting, int members, string leader, string? description = null, string? training = null)
    {
        var desc = description is null ? "null" : System.Text.Json.JsonSerializer.Serialize(description);
        var train = training is null ? "\"\"" : System.Text.Json.JsonSerializer.Serialize(training);
        return $$$"""
            {"id":"{{{id}}}","slug":"{{{slug}}}","name":"{{{name}}}","tagline":"{{{tagline}}}","description":{{{desc}}},"training_times":{{{train}}},"recruiting_status":"{{{recruiting}}}",
             "header":{"url":"{{{Cdn}}}/squads/{{{slug}}}-header.png","variants":[]},"logo":{"url":"{{{Cdn}}}/squads/{{{slug}}}-logo.png","variants":[]},
             "game":{"id":"g-{{{slug}}}","name":"{{{game}}}","short":"{{{gameShort}}}"},"members_count":{{{members}}},"leader":{"id":"l","slug":"{{{leader.ToLowerInvariant()}}}","name":"{{{leader}}}"},
             "clan":{"tag":"RoG|","name":"Rebels of Gaming","level":7,"requirements":null},
             "links":[{"label":"Discord-Einladung","url":"https://discord.gg/rog","short_url":null,"code":null},{"label":"Ref-Link ET: Legacy","url":"https://www.etlegacy.com","short_url":null,"code":"ROG2026"}],"archived_at":null}
            """;
    }

    private static string Member(string id, string slug, string name, string rankName, string rankLabel, string color, string? status, string? activity, string? squad)
    {
        var statusJson = status is null ? "null" : $"\"{status}\"";
        var activityJson = activity is null ? "null" : $$$"""{"label":"{{{activity}}}"}""";
        var squads = squad is null ? "[]" : $$$"""[{"id":"s","slug":"s","name":"{{{squad}}}"}]""";
        var avatar = slug is "kaito" or "fitti" ? "null" : $"\"{Cdn}/avatars/{slug}.png\"";
        return $$$"""
            {"id":"{{{id}}}","slug":"{{{slug}}}","name":"{{{name}}}","avatar_url":{{{avatar}}},"rank":{"id":"r","name":"{{{rankName}}}","label":"{{{rankLabel}}}","color":"{{{color}}}"},
             "status":{{{statusJson}}},"activity":{{{activityJson}}},"last_seen_at":null,"joined_at":"2021-01-01T00:00:00Z","squads":{{{squads}}} }
            """;
    }

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
