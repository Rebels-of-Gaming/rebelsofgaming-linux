using System.Security.Cryptography;
using RoG.Core.Security;

namespace RoG.Linux.Platform.Secrets;

/// <summary>
/// Verschlüsselung lokaler Caches (Gegenstück zu DPAPI der Windows-App): AES-256-GCM mit einem zufälligen Schlüssel,
/// der nur im Schlüsselbund liegt (Secret Service, Eintrag „Cache-Schlüssel“ je Umgebung). Ohne Schlüsselbund gibt es
/// keinen Schlüssel – dann schreibt die App nichts auf die Platte (<see cref="TryCreateAsync"/> liefert <c>null</c>).
/// Format: <c>RGC1</c> · Nonce (12) · Tag (16) · Chiffrat; der Kopf ist zusätzlich authentifiziert.
/// </summary>
public sealed class KeyringPayloadProtector : IPayloadProtector, IDisposable
{
    private static readonly byte[] s_magic = "RGC1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly AesGcm _aes;

    private KeyringPayloadProtector(byte[] key)
    {
        // AesGcm kopiert den Schlüssel; die eigene Kopie danach überschreiben (das Original gehört dem Aufrufer)
        var copy = (byte[])key.Clone();
        _aes = new AesGcm(copy, TagSize);
        CryptographicOperations.ZeroMemory(copy);
    }

    public static IReadOnlyDictionary<string, string> AttributesFor(string environment) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["application"] = AppIdentity.AppId,
        ["kind"] = "cache-key",
        ["environment"] = environment,
    };

    /// <summary>Schlüssel aus dem Schlüsselbund holen bzw. beim ersten Mal anlegen; <c>null</c> ohne nutzbaren Schlüsselbund.</summary>
    public static async Task<KeyringPayloadProtector?> TryCreateAsync(ISecretBackend backend, string environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var attributes = AttributesFor(environment);
        try
        {
            var key = await backend.LookupAsync(attributes, cancellationToken).ConfigureAwait(false);
            if (key is not { Length: 32 })
            {
                key = RandomNumberGenerator.GetBytes(32);
                await backend.StoreAsync("Rebels of Gaming (Cache-Schlüssel)", attributes, key, cancellationToken).ConfigureAwait(false);
            }

            return new KeyringPayloadProtector(key);
        }
        catch (SecretStoreUnavailableException)
        {
            return null;
        }
    }

    /// <summary>Für Tests: fester Schlüssel.</summary>
    internal static KeyringPayloadProtector FromKey(byte[] key) => new(key);

    public Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var output = new byte[s_magic.Length + NonceSize + TagSize + data.Length];
        s_magic.CopyTo(output, 0);
        var nonce = output.AsSpan(s_magic.Length, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = output.AsSpan(s_magic.Length + NonceSize, TagSize);
        var cipher = output.AsSpan(s_magic.Length + NonceSize + TagSize);
        _aes.Encrypt(nonce, data, cipher, tag, s_magic);
        return Task.FromResult(output);
    }

    public Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < s_magic.Length + NonceSize + TagSize || !data.AsSpan(0, s_magic.Length).SequenceEqual(s_magic))
        {
            throw new CryptographicException("Unknown cache format.");
        }

        var nonce = data.AsSpan(s_magic.Length, NonceSize);
        var tag = data.AsSpan(s_magic.Length + NonceSize, TagSize);
        var cipher = data.AsSpan(s_magic.Length + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        _aes.Decrypt(nonce, cipher, tag, plain, s_magic);
        return Task.FromResult(plain);
    }

    public void Dispose() => _aes.Dispose();
}
