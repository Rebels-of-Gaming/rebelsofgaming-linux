using System.Net;
using System.Text;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Fake der Plattform-API (Antworten in der Form der OpenAPI-Spec) – die Anfragen laufen durch die echte RoG-Pipeline
/// des Kerns (Token, Sprache, User-Agent, ETag). Inhalte sind erfunden, aber realistisch für Screenshots.
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Alle Antworten als Fehler (Fehlerzustand testen).</summary>
    public HttpStatusCode? FailWith { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (FailWith is { } status)
        {
            return Task.FromResult(Json(Problem((int)status, "server_error"), status, "application/problem+json"));
        }

        var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
        var body = (request.Method.Method, path) switch
        {
            ("GET", "/v1/news") => FakeData.NewsList,
            ("GET", "/v1/news/scrim-serie-herbst-2026") => FakeData.NewsArticle,
            ("GET", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) && p.EndsWith("/comments", StringComparison.Ordinal) => FakeData.NewsComments,
            ("GET", var p) when p.StartsWith("/v1/news/", StringComparison.Ordinal) => FakeData.NewsArticle,
            ("GET", "/v1/conversations") => FakeData.Conversations,
            ("GET", var p) when p.EndsWith("/messages", StringComparison.Ordinal) => FakeData.Messages,
            ("POST", var p) when p.EndsWith("/read", StringComparison.Ordinal) => """{"data":{"unread":1}}""",
            ("GET", "/v1/me") => FakeData.Me,
            ("GET", "/v1") => FakeData.Platform,
            ("GET", "/v1/live") => FakeData.Live,
            ("GET", "/v1/events") => FakeData.Events,
            ("GET", "/v1/votes") => FakeData.Votes,
            ("GET", "/v1/me/onboarding") => FakeData.Onboarding,
            ("POST", "/v1/auth/logout") => """{"data":{"revoked":true}}""",
            ("GET", "/v1/squads") => FakeData.Squads,
            ("GET", var p) when p.StartsWith("/v1/squads/", StringComparison.Ordinal) && p.EndsWith("/members", StringComparison.Ordinal) => FakeData.SquadMembers,
            ("GET", var p) when p.StartsWith("/v1/squads/", StringComparison.Ordinal) && p.EndsWith("/intern", StringComparison.Ordinal) => FakeData.SquadInternal,
            ("GET", var p) when p.StartsWith("/v1/squads/", StringComparison.Ordinal) => FakeData.SquadDetail,
            ("GET", "/v1/servers") => FakeData.Servers,
            ("GET", "/v1/members") => FakeData.Members,
            ("GET", var p) when p.StartsWith("/v1/members/", StringComparison.Ordinal) => FakeData.MemberProfile,
            ("GET", "/v1/team") => FakeData.Team,
            ("GET", "/v1/games") => FakeData.Games,
            ("GET", var p) when p.StartsWith("/v1/events/", StringComparison.Ordinal) && p.EndsWith("/participants", StringComparison.Ordinal) => FakeData.EventParticipants,
            ("GET", var p) when p.StartsWith("/v1/events/", StringComparison.Ordinal) && p.EndsWith("/lineup", StringComparison.Ordinal) => null,
            ("GET", var p) when p.StartsWith("/v1/events/", StringComparison.Ordinal) =>
                request.Headers.Authorization is null ? "{\"data\":" + FakeData.EventItem("01EV2") + "}" : FakeData.EventDetail,
            ("GET", "/v1/rules") => FakeData.Rules,
            ("GET", var p) when p.StartsWith("/v1/rules/", StringComparison.Ordinal) => FakeData.RulesDetail,
            ("GET", "/v1/pages") => FakeData.Pages,
            ("GET", "/v1/legal") => FakeData.Legal,
            ("GET", "/v1/search") => FakeData.Search,
            ("GET", "/v1/notifications") => request.RequestUri!.Query.Contains("unread", StringComparison.Ordinal) ? FakeData.NotificationsUnread : FakeData.Notifications,
            ("GET", "/v1/notifications/unread-count") => """{"data":{"unread":3}}""",
            ("GET", "/v1/messages/unread-count") => """{"data":{"unread":2}}""",
            ("POST", "/v1/notifications/read-all") => """{"data":{"unread":0}}""",
            ("POST", var p) when p.StartsWith("/v1/notifications/", StringComparison.Ordinal) && p.EndsWith("/read", StringComparison.Ordinal) => """{"data":{"unread":2}}""",
            _ => null,
        };
        return Task.FromResult(body is null
            ? Json(Problem(404, "not_found"), HttpStatusCode.NotFound, "application/problem+json")
            : Json(body, HttpStatusCode.OK, "application/json"));
    }

    private static string Problem(int status, string code) =>
        $$"""{"type":"about:blank","title":"Fehler","status":{{status}},"code":"{{code}}"}""";

    private static HttpResponseMessage Json(string body, HttpStatusCode status, string type) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, type),
    };
}

/// <summary>Erzeugt farbige Platzhalterbilder (Titelbilder, Avatare) – je Adresse ein anderer Farbton.</summary>
public sealed class FakeImages : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var hue = (uint)uri.AbsolutePath.Sum(c => c * 7) % 360; // stabil je Adresse (string.GetHashCode ist je Prozess zufällig)
        var avatar = uri.AbsolutePath.Contains("avatar", StringComparison.Ordinal);
        var png = avatar ? Draw(96, 96, hue, true) : Draw(1280, 720, hue, false);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
    }

    private static byte[] Draw(int width, int height, uint hue, bool avatar)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            var a = SkiaSharp.SKColor.FromHsl(hue, 55, 38);
            var b = SkiaSharp.SKColor.FromHsl((hue + 40) % 360, 60, 18);
            using var paint = new SkiaSharp.SKPaint
            {
                Shader = SkiaSharp.SKShader.CreateLinearGradient(new(0, 0), new(width, height), [a, b], SkiaSharp.SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, paint);
            using var accent = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0xF1, 0xEF, 0xEA, 40), IsAntialias = true };
            if (avatar)
            {
                canvas.DrawCircle(width / 2f, height * 0.42f, width * 0.2f, accent);
                canvas.DrawCircle(width / 2f, height * 1.05f, width * 0.42f, accent);
            }
            else
            {
                canvas.DrawCircle(width * 0.78f, height * 0.3f, height * 0.42f, accent);
                canvas.DrawCircle(width * 0.15f, height * 0.95f, height * 0.3f, accent);
            }
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
