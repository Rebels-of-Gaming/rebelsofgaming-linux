using System.Text.Json;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake-Antworten für Configs, Dateien und Backups eines Servers (Guide 18) – Form wie die API (OpenAPI-Spec),
/// Inhalte erfunden: ET-Public-Server des Clans mit server.cfg, Map-Rotation und Bannern.
/// </summary>
[FakeRoutes]
internal static class FakeServerFilesRoutes
{
    public const string Slug = "rog-et-public";

    public const string ServerCfgId = "01JCFG0000000000000000001";

    public const string MapcycleId = "01JCFG0000000000000000002";

    public const string BannersId = "01JCFG0000000000000000003";

    private const string Prefix = "/v1/servers/" + Slug;

    public static readonly string ServerCfg = string.Join('\n',
        "// RoG ET Public – Grundeinstellungen",
        "set sv_hostname \"^1RoG ^7Public ^3| ^7rebelsofgaming.com\"",
        "set g_motd \"Willkommen bei den Rebels of Gaming – TS: rogclan.de/ts\"",
        "set sv_maxclients 24",
        "set sv_privateclients 2",
        "set g_password \"\"",
        "set g_gametype 4",
        "set g_friendlyfire 1",
        "set g_antilag 1",
        "set sv_fps 20",
        "set g_warmup 15",
        "set g_doWarmup 1",
        "set team_maxPanzers 1",
        "set team_maxMortars 1",
        "set team_maxFlamers 1",
        "exec mapcycle.cfg");

    public static readonly string Mapcycle = string.Join('\n',
        "set d1 \"map supply ; set nextmap vstr d2\"",
        "set d2 \"map radar ; set nextmap vstr d3\"",
        "set d3 \"map goldrush ; set nextmap vstr d4\"",
        "set d4 \"map oasis ; set nextmap vstr d1\"",
        "vstr d1");

    public static readonly string Banners = string.Join('\n',
        "set g_msg1 \"^7Discord: ^3rogclan.de/discord\"",
        "set g_msg2 \"^7Clanwar? Melde dich bei ^1Wahke\"",
        "set g_msgpos 0",
        "set g_msgs 2");

    private static readonly Dictionary<string, (string Path, string Content)> s_files = new(StringComparer.Ordinal)
    {
        [ServerCfgId] = ("etmain/server.cfg", ServerCfg),
        [MapcycleId] = ("etmain/mapcycle.cfg", Mapcycle),
        [BannersId] = ("etmain/banners.cfg", Banners),
    };

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        if (!path.StartsWith(Prefix + "/", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path[Prefix.Length..];
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        var method = request.Method.Method;
        return (method, rest) switch
        {
            ("GET", "/configs") => Configs,
            ("GET", "/files/content") => Content(query),
            ("GET", "/files") => Files(query),
            ("POST", "/files/copy") => "{\"data\":{\"path\":" + JsonSerializer.Serialize(Field(body, "to")) + "}}",
            ("POST", "/files/rename") => "{\"data\":{\"path\":" + JsonSerializer.Serialize(Field(body, "to")) + "}}",
            ("POST", "/files/delete") => string.Empty,
            ("GET", var p) when p.StartsWith("/configs/", StringComparison.Ordinal) && p.EndsWith("/versions", StringComparison.Ordinal) => Versions,
            ("GET", var p) when p.StartsWith("/configs/", StringComparison.Ordinal) && p.EndsWith("/template", StringComparison.Ordinal) => Template,
            ("PUT", var p) when p.StartsWith("/configs/", StringComparison.Ordinal) && p.EndsWith("/template", StringComparison.Ordinal) => SavedVersion,
            ("POST", var p) when p.StartsWith("/configs/", StringComparison.Ordinal) && p.EndsWith("/preview", StringComparison.Ordinal) =>
                Preview(p.Split('/')[2], Field(body, "content") ?? string.Empty),
            ("POST", var p) when p.StartsWith("/configs/versions/", StringComparison.Ordinal) && p.EndsWith("/rollback", StringComparison.Ordinal) => SavedVersion,
            ("PUT", var p) when p.StartsWith("/configs/", StringComparison.Ordinal) => SavedVersion,
            ("GET", "/backups") => Backups,
            ("POST", "/backups") => """{"data":{"uuid":"9f1c2d3e-0000-4000-8000-00000000b004","name":"Vor Clanwar"}}""",
            ("GET", var p) when p.StartsWith("/backups/", StringComparison.Ordinal) && p.EndsWith("/download", StringComparison.Ordinal) =>
                """{"data":{"url":"https://node1.example.test/download/backup?token=kurzlebig"}}""",
            ("POST", var p) when p.StartsWith("/backups/", StringComparison.Ordinal) && p.EndsWith("/restore", StringComparison.Ordinal) => string.Empty,
            ("POST", var p) when p.StartsWith("/backups/", StringComparison.Ordinal) && p.EndsWith("/lock", StringComparison.Ordinal) =>
                "{\"data\":" + Backup("9f1c2d3e-0000-4000-8000-00000000b001", "Vor Map-Update", 734003200, "2026-10-28T20:00:00Z", true, true, true) + "}",
            ("DELETE", var p) when p.StartsWith("/backups/", StringComparison.Ordinal) => string.Empty,
            _ => null,
        };
    }

    private static string? Field(string body, string name)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string Configs => $$"""
        {"data":[
          {"id":"{{ServerCfgId}}","path":"etmain/server.cfg","syntax":"cfg","label":"Server-Einstellungen","reload_command":"exec server.cfg","template":"quake3_server","requires_approval":false,"latest_version":null},
          {"id":"{{MapcycleId}}","path":"etmain/mapcycle.cfg","syntax":"cfg","label":"Map-Rotation","reload_command":"exec mapcycle.cfg","template":"quake3_maprotation","requires_approval":true,"latest_version":null},
          {"id":"{{BannersId}}","path":"etmain/banners.cfg","syntax":"cfg","label":null,"reload_command":null,"template":null,"requires_approval":false,"latest_version":null}
        ]}
        """;

    private static string? Content(string query)
    {
        var path = query.Split('&').Select(p => p.TrimStart('?')).FirstOrDefault(p => p.StartsWith("path=", StringComparison.Ordinal))?[5..];
        var file = s_files.Values.FirstOrDefault(f => f.Path == path);
        var content = file.Path is null
            ? path switch
            {
                "etmain/server.cfg.bak" => ServerCfg.Replace("sv_maxclients 24", "sv_maxclients 20", StringComparison.Ordinal),
                "etmain/mapscripts/supply.script" => "// Supply – angepasstes Mapscript\ngame_manager\n{\n\tspawn\n\t{\n\t\twm_axis_respawntime 30\n\t}\n}",
                _ => null,
            }
            : file.Content;
        return content is null ? null : "{\"data\":{\"path\":" + JsonSerializer.Serialize(path) + ",\"content\":" + JsonSerializer.Serialize(content) + "}}";
    }

    private static string Entry(string name, string path, bool isFile, long size, string modified, bool editable = false, bool operable = false, bool backup = false) =>
        $$"""{"name":"{{name}}","is_file":{{(isFile ? "true" : "false")}},"size":{{size}},"modified_at":"{{modified}}","mimetype":{{(isFile ? "\"text/plain\"" : "null")}},"path":"{{path}}","editable":{{(editable ? "true" : "false")}},"operable":{{(operable ? "true" : "false")}},"backup":{{(backup ? "true" : "false")}}}""";

    private static string Files(string query) => query.Contains("directory=etmain/mapscripts", StringComparison.Ordinal)
        ? "{\"data\":[]}"
        : query.Contains("directory=etmain", StringComparison.Ordinal)
            ? "{\"data\":[" + string.Join(',',
                Entry("pak0.pk3", "etmain/pak0.pk3", true, 287_442_944, "2026-01-12T09:00:00Z"),
                Entry("server.cfg", "etmain/server.cfg", true, 612, "2026-10-28T19:40:00Z", editable: true, operable: true),
                Entry("mapscripts", "etmain/mapscripts", false, 0, "2026-09-02T18:00:00Z"),
                Entry("server.cfg.bak", "etmain/server.cfg.bak", true, 598, "2026-10-20T21:15:00Z", operable: true, backup: true),
                Entry("mapcycle.cfg", "etmain/mapcycle.cfg", true, 214, "2026-10-26T17:05:00Z", editable: true, operable: true),
                Entry("banners.cfg", "etmain/banners.cfg", true, 160, "2026-10-12T12:30:00Z", editable: true, operable: true),
                Entry("etconsole.log", "etmain/etconsole.log", true, 3_145_728, "2026-10-30T08:59:00Z")) + "]}"
            : "{\"data\":[" + string.Join(',',
                Entry("etmain", "etmain", false, 0, "2026-10-28T19:40:00Z"),
                Entry("legacy", "legacy", false, 0, "2026-10-01T10:00:00Z"),
                Entry("etlded.x86_64", "etlded.x86_64", true, 2_621_440, "2026-08-15T07:30:00Z")) + "]}";

    private static string Versions => """
        {"data":[
          {"id":"01JVER0000000000000000003","checksum":"c3","note":"Slots für Clanwar erhöht","user":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},"created_at":"2026-10-28T19:40:00Z","content":null},
          {"id":"01JVER0000000000000000002","checksum":"b2","note":"Warmup verkürzt","user":{"id":"01U3","name":"Kaito","slug":"kaito"},"created_at":"2026-10-20T21:15:00Z","content":null},
          {"id":"01JVER0000000000000000001","checksum":"a1","note":null,"user":null,"created_at":"2026-09-02T18:00:00Z","content":null}
        ]}
        """;

    private static string SavedVersion => """
        {"data":{"id":"01JVER0000000000000000009","checksum":"z9","note":null,"user":{"id":"01USERWAHKE","name":"Wahke","slug":"wahke"},"created_at":"2026-10-30T09:00:00Z","content":null}}
        """;

    private static string Template => """
        {"data":{"template":{"key":"quake3_server","label":"Server-Einstellungen (ET/RtCW)","description":"Name, Begrüßung, Slots und Passwort – alle anderen Zeilen bleiben unverändert.","syntax":"cfg",
          "fields":[{"name":"sv_hostname","type":"text","label":"Servername","max":64},{"name":"g_motd","type":"text","label":"Begrüßung","max":128},
                    {"name":"sv_maxclients","type":"number","label":"Slots","min":2,"max":64},{"name":"sv_privateclients","type":"number","label":"Private Slots","min":0,"max":32},
                    {"name":"g_password","type":"password","label":"Server-Passwort","max":32}]},
          "values":{"sv_hostname":"^1RoG ^7Public","g_motd":"Willkommen bei den Rebels of Gaming","sv_maxclients":24,"sv_privateclients":2,"g_password":null},
          "password_set":{"g_password":true}}}
        """;

    /// <summary>Diff wie die Website: geänderte Zeilen mit einer Zeile Kontext; <c>sv_cheats 1</c> ist ein Fehler.</summary>
    private static string Preview(string fileId, string content)
    {
        var original = s_files.TryGetValue(fileId, out var f) ? f.Content : string.Empty;
        var a = original.Split('\n');
        var b = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var ops = Diff(a, b);
        var lines = new List<string>();
        for (var i = 0; i < ops.Count; i++)
        {
            var near = ops.Skip(Math.Max(0, i - 1)).Take(3).Any(o => o.Type != "context");
            if (ops[i].Type != "context" || near)
            {
                var (type, text, old, @new) = ops[i];
                lines.Add($$"""{"type":"{{type}}","text":{{JsonSerializer.Serialize(text)}},"old":{{(old?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}},"new":{{(@new?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}}}""");
            }
        }

        var changed = original != content;
        var errors = b.Select((l, i) => (l, i)).Where(x => x.l.Contains("sv_cheats 1", StringComparison.Ordinal))
            .Select(x => JsonSerializer.Serialize($"Zeile {x.i + 1}: sv_cheats ist auf Clan-Servern nicht erlaubt."));
        return "{\"data\":{\"diff\":[" + string.Join(',', lines) + "],\"errors\":[" + string.Join(',', errors) + "],\"changed\":" + (changed ? "true" : "false") + "}}";
    }

    private static List<(string Type, string Text, int? Old, int? New)> Diff(string[] a, string[] b)
    {
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var ops = new List<(string, string, int?, int?)>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                ops.Add(("context", a[x], x + 1, y + 1));
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                ops.Add(("add", b[y], null, y + 1));
                y++;
            }
            else
            {
                ops.Add(("remove", a[x], x + 1, null));
                x++;
            }
        }

        // Entfernte Zeilen vor den hinzugefügten (wie ein Unified Diff)
        for (var i = 1; i < ops.Count; i++)
        {
            if (ops[i].Item1 == "remove" && ops[i - 1].Item1 == "add")
            {
                (ops[i], ops[i - 1]) = (ops[i - 1], ops[i]);
                i = Math.Max(0, i - 2);
            }
        }

        return ops;
    }

    private static string Backup(string uuid, string name, long bytes, string created, bool completed, bool successful, bool locked) =>
        $$"""{"uuid":"{{uuid}}","name":"{{name}}","bytes":{{bytes}},"created_at":"{{created}}","completed_at":{{(completed ? "\"" + created + "\"" : "null")}},"successful":{{(successful ? "true" : "false")}},"locked":{{(locked ? "true" : "false")}}}""";

    private static string Backups => "{\"data\":[" + string.Join(',',
        Backup("9f1c2d3e-0000-4000-8000-00000000b003", "Nach Herbst-Scrims", 0, "2026-10-30T08:55:00Z", false, false, false),
        Backup("9f1c2d3e-0000-4000-8000-00000000b001", "Vor Map-Update", 734003200, "2026-10-28T20:00:00Z", true, true, false),
        Backup("9f1c2d3e-0000-4000-8000-00000000b002", "Wöchentlich", 0, "2026-10-21T03:00:00Z", true, false, false),
        Backup("9f1c2d3e-0000-4000-8000-00000000b000", "Saisonstart 2026", 702545920, "2026-09-01T12:00:00Z", true, true, true)) + "]}";
}
