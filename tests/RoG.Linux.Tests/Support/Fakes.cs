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

public sealed class SilentNotifier : IDesktopNotifier
{
    public Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public static class TestTokens
{
    public static StoredToken Wahke { get; } = new("test-token", "01USERWAHKE", "Wahke", ["news:read", "messages:read", "profile:read"], "Linux-App – PC von wahke", DateTimeOffset.UnixEpoch);
}
