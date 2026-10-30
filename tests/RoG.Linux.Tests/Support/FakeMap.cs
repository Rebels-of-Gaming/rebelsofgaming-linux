using System.Net;

namespace RoG.Linux.Tests.Support;

/// <summary>Mitgliederkarte (Guide 36): Orte mit Opt-in (Stadt-Genauigkeit) und signierte Kachel-Vorlage der API.</summary>
[FakeRoutes]
internal static class FakeMapRoutes
{
    public const string TileTemplate = "https://api.rebelsofgaming.com/v1/map/tiles/{z}/{x}/{y}.png?user=01USERWAHKE&token=42&expires=4070908800&signature=5f3c9a";

    private static string M(string slug, string name, params string[] squads) =>
        $$$"""{"id":"01M{{{slug.ToUpperInvariant()}}}","slug":"{{{slug}}}","name":"{{{name}}}","avatar_url":"https://cdn.example.test/avatars/{{{slug}}}.png","squads":[{{{string.Join(",", squads.Select(s => "\"" + s + "\""))}}}]}""";

    public static string Map(string template = TileTemplate, string expires = "2099-01-01T00:00:00Z") => $$$"""
        {"data":[
          {"lat":49.7499,"lng":6.6371,"city":"Trier","country":"DE","members":[{{{M("wahke", "Wahke", "Wolfenstein: ET")}}},{{{M("nyx", "Nyx", "Counter-Strike 2")}}},{{{M("mara", "Mara", "Minecraft")}}}]},
          {"lat":49.6116,"lng":6.1319,"city":"Luxemburg","country":"LU","members":[{{{M("kaito", "Kaito", "Counter-Strike 2", "Wolfenstein: ET")}}},{{{M("leise", "Leise")}}}]},
          {"lat":49.2402,"lng":6.9969,"city":"Saarbrücken","country":"DE","members":[{{{M("rookie", "Rookie", "Minecraft")}}}]},
          {"lat":50.9375,"lng":6.9603,"city":"Köln","country":"DE","members":[{{{M("blackwolf", "BlackWolf", "Wolfenstein: ET")}}},{{{M("frostbyte", "FrostByte", "Counter-Strike 2")}}}]},
          {"lat":53.5511,"lng":9.9937,"city":"Hamburg","country":"DE","members":[{{{M("valkyrie", "Valkyrie", "Counter-Strike 2")}}}]},
          {"lat":52.5200,"lng":13.4050,"city":"Berlin","country":"DE","members":[{{{M("sniperjoe", "SniperJoe", "RtCW")}}},{{{M("pixel", "Pixel", "Minecraft")}}}]},
          {"lat":48.1351,"lng":11.5820,"city":"München","country":"DE","members":[{{{M("hopper", "Hopper")}}}]},
          {"lat":48.2082,"lng":16.3738,"city":"Wien","country":"AT","members":[{{{M("glitch", "Glitch", "Minecraft")}}}]}
        ],"meta":{"tiles":{"url":"{{{template}}}","expires_at":"{{{expires}}}","min_zoom":2,"max_zoom":12,"tile_size":256,
          "attribution":"© OpenStreetMap","attribution_url":"https://www.openstreetmap.org/copyright"} } }
        """;

    public const string EmptyMap = """{"data":[],"meta":{"tiles":{"url":null,"expires_at":null,"min_zoom":2,"max_zoom":12,"tile_size":256,"attribution":"© OpenStreetMap","attribution_url":"https://www.openstreetmap.org/copyright"}}}""";

    private static string? Route(HttpRequestMessage request, string path, string body) => (request.Method.Method, path) switch
    {
        ("GET", "/v1/map") => Map(),
        _ => null,
    };
}

/// <summary>Fake-Kacheln (256 px, „Land“ und „Wasser“ je Kachel) – merkt sich jede Anfrage (Datenschutz-Test).</summary>
public sealed class FakeTiles : HttpMessageHandler
{
    public List<(Uri Url, bool HasAuthorization)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        lock (Requests)
        {
            Requests.Add((uri, request.Headers.Authorization is not null));
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var seed = parts.Length >= 3 ? string.Join('/', parts[^3..]) : uri.AbsolutePath;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Draw(seed)) });
    }

    private static byte[] Draw(string seed)
    {
        const int size = 256;
        var hash = (uint)seed.Sum(c => c * 31);
        using var bitmap = new SkiaSharp.SKBitmap(size, size);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(new SkiaSharp.SKColor(0x23, 0x2B, 0x38)); // Wasser
            using var land = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x34, 0x3A, 0x45), IsAntialias = true };
            for (var i = 0; i < 4; i++)
            {
                var h = hash * (uint)(i + 7);
                canvas.DrawCircle(h % size, (h / 7) % size, 60 + (h % 70), land);
            }

            using var road = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x4A, 0x50, 0x5E), StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(0, hash % size, size, (hash / 3) % size, road);
            canvas.DrawLine((hash / 5) % size, 0, (hash / 11) % size, size, road);
            using var grid = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x3A, 0x40, 0x50), StrokeWidth = 1, Style = SkiaSharp.SKPaintStyle.Stroke };
            canvas.DrawRect(0, 0, size - 1, size - 1, grid);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
