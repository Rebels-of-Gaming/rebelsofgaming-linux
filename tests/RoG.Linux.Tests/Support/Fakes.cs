using RoG.Core.Auth;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Platform.Secrets;

namespace RoG.Linux.Tests.Support;

/// <summary>Schlüsselbund im Speicher; <see cref="Unavailable"/> simuliert „kein Secret Service“.</summary>
public sealed class FakeSecretBackend : ISecretBackend
{
    public Dictionary<string, (string Label, byte[] Secret)> Items { get; } = [];

    public bool Unavailable { get; set; }

    public Task<byte[]?> LookupAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        Check();
        return Task.FromResult(Items.TryGetValue(Key(attributes), out var item) ? item.Secret : null);
    }

    public Task StoreAsync(string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, CancellationToken cancellationToken = default)
    {
        Check();
        Items[Key(attributes)] = (label, secret);
        return Task.CompletedTask;
    }

    public Task ClearAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        Check();
        Items.Remove(Key(attributes));
        return Task.CompletedTask;
    }

    private void Check()
    {
        if (Unavailable)
        {
            throw new SecretStoreUnavailableException("test");
        }
    }

    private static string Key(IReadOnlyDictionary<string, string> attributes) =>
        string.Join(";", attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"));
}

public sealed class RecordingProcessLauncher : IProcessLauncher
{
    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

    /// <summary>Antwort für <see cref="ReadAsync"/> (z. B. Ausgabe von <c>xdg-mime</c>); <c>null</c> = Programm fehlt.</summary>
    public string? Output { get; set; }

    public Task<string?> ReadAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(Output);
    }

    public Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(true);
    }
}

public sealed class RecordingBrowser : IBrowserLauncher
{
    public List<Uri> Opened { get; } = [];

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        Opened.Add(uri);
        return Task.FromResult(true);
    }
}

/// <summary>Zwischenablage im Speicher; <see cref="Locked"/> simuliert eine belegte Zwischenablage.</summary>
public sealed class FakeClipboard : RoG.Core.Features.Servers.IClipboardService
{
    public string? Text { get; private set; }

    public bool Locked { get; set; }

    public void SetText(string text)
    {
        if (Locked)
        {
            throw new InvalidOperationException("gesperrt");
        }

        Text = text;
    }
}

/// <summary>Spiele-Links: merkt sich gestartete Adressen; <see cref="Installed"/> = Schemata mit Programm.</summary>
public sealed class FakeLauncher : RoG.Core.Features.Servers.IExternalLauncher
{
    public List<Uri> Launched { get; } = [];

    public HashSet<string> Installed { get; } = new(StringComparer.OrdinalIgnoreCase) { "steam" };

    public Task<bool> CanLaunchAsync(Uri uri) => Task.FromResult(Installed.Contains(uri.Scheme));

    public Task<bool> LaunchAsync(Uri uri)
    {
        Launched.Add(uri);
        return Task.FromResult(true);
    }
}

public sealed class SilentNotifier : IDesktopNotifier
{
    public Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public static class TestTokens
{
    public static StoredToken Wahke { get; } = new("test-token", "01USERWAHKE", "Wahke", ["news:read", "messages:read", "profile:read"], "Linux-App – PC von wahke", DateTimeOffset.UnixEpoch);
}
