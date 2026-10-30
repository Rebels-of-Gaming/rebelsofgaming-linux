using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using RoG.Core.Features.Servers;
using RoG.Core.Realtime;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Server-Steuerung (Guide 18): Ressourcen, Power, Befehle, Konsolen-Ticket (Level je Server, Demo), RCON, Sperren,
/// Whitelist, Config-Anträge. Antworten in der Form der API; Server <c>et-sniper</c> (Befehle erlaubt),
/// <c>cs2-retake</c> (Ticket nur „read“) und <c>demo-et</c> (Demo-Server).
/// </summary>
[FakeRoutes]
internal static class FakeServerControlRoutes
{
    private static int s_tickets;

    /// <summary><c>/v1/me</c> mit allen Steuer-Scopes (Verwaltungs-Scopes gibt es nur mit Recht und 2FA).</summary>
    public static string MeWithServerScopes => FakeData.Me.Replace(
        "\"notifications:read\"]",
        "\"notifications:read\",\"servers:power\",\"servers:console\",\"servers:files\",\"servers:backups\",\"servers:rcon\",\"gamebans:manage\"]",
        StringComparison.Ordinal);

    /// <summary><c>control</c> eines Server-Admins (alles außer Freigaben).</summary>
    public const string AdminControl = """
        {"config_approval":false,"can_command":true,"abilities":{"power":true,"console":true,"console_command":true,"files_read":true,"files_write":true,"files_approve":true,"backups":true,"rcon":true,"bans":true}}
        """;

    /// <summary><c>/v1/servers</c>, der ET-Server mit Steuerungsrechten.</summary>
    public static string ServersWithControl
    {
        get
        {
            var json = FakeData.Servers;
            var at = json.IndexOf("\"control\":null", StringComparison.Ordinal);
            return json[..at] + "\"control\":" + AdminControl.Trim() + json[(at + "\"control\":null".Length)..];
        }
    }

    /// <summary>Server, wie <c>ServerService</c> ihn liefert – für das Fenster ohne Umweg über die Liste.</summary>
    public static GameServer Server(string slug = "et-sniper", ServerAbilities? abilities = null, string name = "RoG ET: Sniper") => new(
        "01SV1", slug, name, "Wolfenstein: Enemy Territory", "ET", "et.rogclan.de:27960", ServerQueryType.Quake3, "et://et.rogclan.de:27960",
        true, "supply", 9, 20, false, null,
        abilities ?? new ServerAbilities(Power: true, Console: true, ConsoleCommand: true, CanCommand: true, FilesRead: true, FilesWrite: true,
            FilesApprove: true, Backups: true, Rcon: true, Bans: true));

    private static string? Route(HttpRequestMessage request, string path, string body)
    {
        const string prefix = "/v1/servers/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return null;
        }

        var slug = rest[..slash];
        var action = rest[(slash + 1)..];
        return (request.Method.Method, action) switch
        {
            ("GET", "resources") => Resources,
            ("POST", "power") => "{}",
            ("POST", "commands") => """{"data":{"sent":true}}""",
            ("POST", "console-token") => Ticket(slug),
            ("POST", "rcon") => Rcon(body),
            ("GET", "bans") => Bans,
            ("POST", "bans") => """{"data":{"id":"01BAN9","status":"active"}}""",
            ("DELETE", var a) when a.StartsWith("bans/", StringComparison.Ordinal) => "{}",
            ("GET", "whitelist") => Whitelist,
            ("POST", "whitelist") => """{"data":{"ok":true}}""",
            ("GET", "config-changes") => ConfigChanges,
            ("POST", var a) when a.StartsWith("config-changes/", StringComparison.Ordinal) && a.EndsWith("/approve", StringComparison.Ordinal) =>
                """{"data":{"change":{"id":"01CHG1","status":"approved"},"version":{"id":"01VER7"}}}""",
            ("POST", var a) when a.StartsWith("config-changes/", StringComparison.Ordinal) && a.EndsWith("/reject", StringComparison.Ordinal) =>
                """{"data":{"change":{"id":"01CHG1","status":"rejected"}}}""",
            _ => null,
        };
    }

    // Ticket: kurzlebiges Token (jedes Mal neu), Level je Server; der Demo-Server hat keinen Node
    private static string Ticket(string slug)
    {
        var n = Interlocked.Increment(ref s_tickets);
        var level = slug == "cs2-retake" ? "read" : "power";
        var demo = slug == "demo-et" ? "true" : "false";
        return $$$"""{"data":{"token":"ticket-{{{n}}}","socket":"wss://node1.example.test:8080/api/servers/5b1c/ws","level":"{{{level}}}","demo":{{{demo}}},"origin":"https://admin.example.test"}}""";
    }

    public const string Resources = """
        {"data":{"state":"running","cpu":37.4,"memory":734003200,"memory_limit":1073741824,"disk":943718400,"disk_limit":null,"network_rx":48211000,"network_tx":13770000,"uptime":93780}}
        """;

    private static string Rcon(string body)
    {
        var command = JsonDocument.Parse(body).RootElement.GetProperty("command").GetString();
        var output = command == "status"
            ? "map: supply\\nnum score ping name            lastmsg address               qport rate\\n  0    42   28 Nyx                   0 192.0.2.10:27960      1337 25000\\n  1    30   35 Kaito                 0 192.0.2.11:27960      4242 25000"
            : "Unknown command";
        return $$$"""{"data":{"output":"{{{output}}}"}}""";
    }

    public const string Bans = """
        {"data":[
          {"id":"01BAN1","player_id":"76561198000000042","player_name":"Gr1efer","user":null,"reason":"Teamkills auf Supply","status":"active","error":null,
           "issued_by":{"id":"01USERWAHKE","name":"Wahke"},"created_at":"2026-10-29T20:15:00Z","ends_at":"2026-11-05T20:15:00Z","lifted_at":null},
          {"id":"01BAN2","player_id":null,"player_name":"aimbot_andi","user":null,"reason":"Cheats (Demo liegt im Forum)","status":"active","error":null,
           "issued_by":{"id":"01U3","name":"Kaito"},"created_at":"2026-10-12T18:00:00Z","ends_at":null,"lifted_at":null}
        ],"meta":{"supports":{"ban":true,"unban":true,"list":true}}}
        """;

    public const string Whitelist = """
        {"data":[
          {"line":"Nyx_MC","member":{"id":"01U2","name":"Nyx","slug":"nyx"}},
          {"line":"Kaito2020","member":{"id":"01U3","name":"Kaito","slug":"kaito"}},
          {"line":"Steve_vom_Nachbarn","member":null}
        ]}
        """;

    public const string ConfigChanges = """
        {"data":[
          {"id":"01CHG1","file":{"id":"01F1","path":"etmain/server.cfg"},"status":"pending","via":"editor","after":"restart","note":"Neue Map-Rotation für den Scrim-Abend",
           "author":{"id":"01U3","name":"Kaito","slug":"kaito"},"decided_by":null,"decided_at":null,"decision_note":null,"created_at":"2026-10-30T07:40:00Z",
           "diff":[{"type":"context","text":"set sv_hostname \"^1RoG ^7Sniper\""},{"type":"remove","text":"set g_maxlives 0"},{"type":"add","text":"set g_maxlives 1"},{"type":"add","text":"set sv_maprotation \"supply radar goldrush\""}],
           "outdated":false}
        ]}
        """;
}

/// <summary>Wings-WebSocket ohne Netz: der Test schiebt Nachrichten hinein und liest, was die App gesendet hat.</summary>
internal sealed class FakeConsoleSocket : IWebSocketConnection
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> _sent = Channel.CreateUnbounded<string>();

    public Uri? ConnectedTo { get; private set; }

    public string? Origin { get; init; }

    public ConcurrentQueue<string> Sent { get; } = new();

    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedTo = uri;
        return Task.CompletedTask;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        _sent.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) => await _incoming.Reader.ReadAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _incoming.Writer.TryWrite(null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Wings schickt ein Ereignis (<c>{"event": …, "args": [arg]}</c>).</summary>
    public void Send(string name, string? arg = null) =>
        _incoming.Writer.TryWrite(JsonSerializer.Serialize(new { @event = name, args = arg is null ? Array.Empty<string>() : new[] { arg } }));

    /// <summary>Node trennt die Verbindung.</summary>
    public void Drop() => _incoming.Writer.TryWrite(null);

    /// <summary>Gesendete Ereignisse mit diesem Namen (Argument).</summary>
    public IReadOnlyList<string?> SentArgs(string name) => Sent
        .Select(m => JsonDocument.Parse(m).RootElement)
        .Where(e => e.GetProperty("event").GetString() == name)
        .Select(e => e.GetProperty("args").GetArrayLength() > 0 ? e.GetProperty("args")[0].GetString() : null)
        .ToList();
}

/// <summary>Liefert je Verbindungsaufbau einen neuen <see cref="FakeConsoleSocket"/> und merkt sich alle.</summary>
internal sealed class FakeConsoleSockets
{
    public List<FakeConsoleSocket> Created { get; } = [];

    public FakeConsoleSocket Last => Created[^1];

    public IWebSocketConnection Create(string? origin)
    {
        var socket = new FakeConsoleSocket { Origin = origin };
        lock (Created)
        {
            Created.Add(socket);
        }

        return socket;
    }
}
