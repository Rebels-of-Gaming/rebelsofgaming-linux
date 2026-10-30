namespace RoG.Linux.Tests.Support;

/// <summary>Freunde (Guide 45): Liste mit Status, offene Anfragen in beide Richtungen, Aktionen.</summary>
[FakeRoutes]
internal static class FakeFriendsRoutes
{
    public const string IncomingId = "01JBA2M4K6P8R0T2V4X6Z8B0D2";
    public const string OutgoingId = "01JBA3N5Q7S9V1X3Z5B7D9F1H3";

    private static string User(string id, string slug, string name) =>
        $$$"""{"id":"{{{id}}}","slug":"{{{slug}}}","name":"{{{name}}}","avatar_url":"https://cdn.example.test/avatars/{{{slug}}}.png"}""";

    public static string Friends => $$$"""
        {"data":[
          {"user":{{{User("01JB8Y3Q2X5V7N9R4T6W8Z0C2D", "nyx", "Nyx")}}},"status":"ingame","since":"2026-10-22T18:04:11Z"},
          {"user":{{{User("01JB8Y4R3Y6W8P0S5V7X9A1D3E", "kaito", "Kaito")}}},"status":"voice","since":"2026-09-14T20:11:00Z"},
          {"user":{{{User("01JB8Y5S4Z7X9Q1T6W8Y0B2E4F", "mara", "Mara")}}},"status":"server","since":"2026-08-02T17:30:00Z"},
          {"user":{{{User("01JB8Y6T5A8Y0R2V7X9Z1C3F5G", "rookie", "Rookie")}}},"status":"online","since":"2026-07-19T12:00:00Z"},
          {"user":{{{User("01JB8Y7V6B9Z1S3W8Y0A2D4G6H", "blackwolf", "BlackWolf")}}},"status":"offline","since":"2026-05-03T09:45:00Z"},
          {"user":{{{User("01JB8Y8W7C0A2T4X9Z1B3E5H7J", "leise", "Leise")}}},"status":null,"since":"2026-03-28T21:15:00Z"}
        ],"meta":{"next_cursor":null,"per_page":100,"total":6,"limit":100}}
        """;

    public static string Requests => $$$"""
        {"data":[
          {"id":"{{{IncomingId}}}","direction":"incoming","status":"pending","user":{{{User("01JB9A1B2C3D4E5F6G7H8J9K0M", "sniperjoe", "SniperJoe")}}},"created_at":"2026-10-29T17:58:00Z"},
          {"id":"{{{OutgoingId}}}","direction":"outgoing","status":"pending","user":{{{User("01JB9B2C3D4E5F6G7H8J9K0M1N", "valkyrie", "Valkyrie")}}},"created_at":"2026-10-27T20:12:00Z"}
        ],"meta":{"next_cursor":null,"per_page":100}}
        """;

    public const string Empty = """{"data":[],"meta":{"next_cursor":null,"per_page":100,"total":0,"limit":100}}""";

    private static string? Route(HttpRequestMessage request, string path, string body) => (request.Method.Method, path) switch
    {
        ("GET", "/v1/me/friends") => Friends,
        ("GET", "/v1/me/friends/requests") => Requests,
        ("POST", "/v1/me/friends/requests") => body.Contains("\"valkyrie\"", StringComparison.Ordinal)
            ? $$$"""{"data":{"id":"{{{OutgoingId}}}","direction":"incoming","status":"accepted","user":{{{User("01JB9B2C3D4E5F6G7H8J9K0M1N", "valkyrie", "Valkyrie")}}},"created_at":"2026-10-30T10:00:00Z"}}"""
            : $$$"""{"data":{"id":"01JBC4P6R8T0W2Y4A6C8E0G2J4","direction":"outgoing","status":"pending","user":{{{User("01JB9C3D4E5F6G7H8J9K0M1N2P", "frostbyte", "FrostByte")}}},"created_at":"2026-10-30T10:00:00Z"}}""",
        ("POST", var p) when p.StartsWith("/v1/me/friends/requests/", StringComparison.Ordinal) && p.EndsWith("/accept", StringComparison.Ordinal) =>
            $$$"""{"data":{"user":{{{User("01JB9A1B2C3D4E5F6G7H8J9K0M", "sniperjoe", "SniperJoe")}}},"status":"online","since":"2026-10-30T10:00:00Z"}}""",
        ("POST", var p) when p.StartsWith("/v1/me/friends/requests/", StringComparison.Ordinal) && p.EndsWith("/decline", StringComparison.Ordinal) => "{}",
        ("DELETE", var p) when p.StartsWith("/v1/me/friends/", StringComparison.Ordinal) => "{}",
        _ => null,
    };
}
