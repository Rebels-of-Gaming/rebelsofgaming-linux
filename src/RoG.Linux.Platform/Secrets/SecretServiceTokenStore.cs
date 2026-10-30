using System.Text.Json;
using RoG.Core.Auth;

namespace RoG.Linux.Platform.Secrets;

/// <summary>
/// Geräte-Token im Schlüsselbund des Systems (Gegenstück zu <c>PasswordVaultTokenStore</c> der Windows-App).
/// Ein Eintrag je Umgebung (Produktion/Staging/lokal), damit ein Wechsel der Umgebung keinen fremden Token schickt.
/// Der Token steht nie in Einstellungen, Logs oder Caches.
/// </summary>
public sealed class SecretServiceTokenStore(ISecretBackend backend, string environment) : ITokenStore
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal)
    {
        ["application"] = AppIdentity.AppId,
        ["kind"] = "device-token",
        ["environment"] = environment,
    };

    public IReadOnlyDictionary<string, string> Attributes => _attributes;

    public async Task<StoredToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await backend.LookupAsync(_attributes, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<TokenData>(bytes, s_json);
            return data is { AccessToken.Length: > 0 }
                ? new StoredToken(data.AccessToken, data.UserId ?? string.Empty, data.UserName ?? string.Empty, data.Scopes ?? [], data.DeviceName ?? string.Empty, data.CreatedAt)
                : null;
        }
        catch (JsonException)
        {
            return null; // beschädigter Eintrag = nicht angemeldet; der nächste Login überschreibt ihn
        }
    }

    public Task SetAsync(StoredToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        var data = new TokenData(token.AccessToken, token.UserId, token.UserName, [.. token.Scopes], token.DeviceName, token.CreatedAt);
        var label = string.IsNullOrEmpty(token.UserName) ? "Rebels of Gaming" : $"Rebels of Gaming ({token.UserName})";
        return backend.StoreAsync(label, _attributes, JsonSerializer.SerializeToUtf8Bytes(data, s_json), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => backend.ClearAsync(_attributes, cancellationToken);

    private sealed record TokenData(string AccessToken, string? UserId, string? UserName, List<string>? Scopes, string? DeviceName, DateTimeOffset CreatedAt);
}

/// <summary>
/// Schlüsselbund mit Rückfall: Ist kein Secret Service nutzbar (z. B. minimales System ohne Keyring, Sandbox ohne
/// Freigabe), bleibt der Token nur im Speicher – die Anmeldung gilt dann nur bis zum Beenden der App, und
/// <see cref="IsPersistent"/> wird <c>false</c>, damit die Oberfläche deutlich darauf hinweist. Nie Klartext auf der Platte.
/// </summary>
public sealed class FallbackTokenStore(ITokenStore primary) : ITokenStore
{
    private readonly InMemoryTokenStore _memory = new();
    private volatile bool _degraded;

    /// <summary>Wird gespeichert, solange die App läuft und darüber hinaus?</summary>
    public bool IsPersistent => !_degraded;

    /// <summary>Schlüsselbund fiel gerade aus – die Oberfläche zeigt einen Hinweis.</summary>
    public event EventHandler? Degraded;

    public async Task<StoredToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_degraded)
        {
            return await _memory.GetAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await primary.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SecretStoreUnavailableException)
        {
            MarkDegraded();
            return await _memory.GetAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetAsync(StoredToken token, CancellationToken cancellationToken = default)
    {
        await _memory.SetAsync(token, cancellationToken).ConfigureAwait(false);
        if (_degraded)
        {
            return;
        }

        try
        {
            await primary.SetAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (SecretStoreUnavailableException)
        {
            MarkDegraded();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _memory.ClearAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await primary.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SecretStoreUnavailableException)
        {
            MarkDegraded();
        }
    }

    private void MarkDegraded()
    {
        if (!_degraded)
        {
            _degraded = true;
            Degraded?.Invoke(this, EventArgs.Empty);
        }
    }
}
