using System.Reflection;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// Zusätzliche Antworten des Fake-Servers, je Bereich in einer eigenen Datei: jede statische Klasse mit
/// <see cref="FakeRoutesAttribute"/> und einer Methode <c>static string? Route(HttpRequestMessage, string path, string body)</c>
/// wird gefragt (erste Antwort ≠ <c>null</c> gewinnt). So bleibt <see cref="FakeApi"/> klein.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FakeRoutesAttribute : Attribute;

public static class FakeRoutes
{
    private static readonly Func<HttpRequestMessage, string, string, string?>[] s_routes = typeof(FakeRoutes).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<FakeRoutesAttribute>() is not null)
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .Select(t => t.GetMethod("Route", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<Func<HttpRequestMessage, string, string, string?>>())
        .ToArray();

    public static string? Resolve(HttpRequestMessage request, string path, string body)
    {
        foreach (var route in s_routes)
        {
            if (route(request, path, body) is { } answer)
            {
                return answer;
            }
        }

        return null;
    }
}
