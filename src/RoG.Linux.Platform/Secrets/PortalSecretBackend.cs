using System.Security.Cryptography;
using System.Text;

namespace RoG.Linux.Platform.Secrets;

/// <summary>
/// Schlüsselbund für die Flatpak-Sandbox ohne <c>--talk-name=org.freedesktop.secrets</c> (Audit M1): Das Secret-Portal
/// liefert genau ein Geheimnis je App-ID. Daraus leitet die App per HKDF-SHA256 für jeden Eintrag (Geräte-Token je
/// Umgebung, Cache-Schlüssel je Umgebung, PIN der App-Sperre) einen eigenen Schlüssel ab und legt den Eintrag
/// verschlüsselt (Format <c>RGC1</c> von <see cref="KeyringPayloadProtector"/>, AES-256-GCM) im Datenordner der App
/// ab (<c>~/.var/app/&lt;App-ID&gt;/data/rebelsofgaming/secrets/</c>, Rechte 0600). Ohne das Geheimnis aus dem
/// Schlüsselbund sind die Dateien wertlos. Der Dateiname ist ein HMAC der Attribute – er verrät weder Umgebung noch Art.
/// Außerhalb von Flatpak (.deb, Tarball) bleibt es beim direkten Secret Service (<see cref="SecretServiceBackend"/>).
/// </summary>
public sealed class PortalSecretBackend(ISecretPortal portal, string directory) : ISecretBackend, IDisposable
{
    private static readonly byte[] s_salt = "RoG-Linux/secret-portal/v1"u8.ToArray();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _master;

    public string Directory => directory;

    public async Task<byte[]?> LookupAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var master = await GetMasterAsync(cancellationToken).ConfigureAwait(false);
        var file = FileFor(master, attributes);
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreUnavailableException("Gespeichertes Geheimnis nicht lesbar.", ex);
        }

        var key = DeriveKey(master, "entry", attributes);
        try
        {
            using var protector = KeyringPayloadProtector.FromKey(key);
            return await protector.UnprotectAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            // Anderes Geheimnis im Portal (z. B. Schlüsselbund neu angelegt) oder beschädigt: wie nicht vorhanden
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task StoreAsync(string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(secret);
        var master = await GetMasterAsync(cancellationToken).ConfigureAwait(false);
        var key = DeriveKey(master, "entry", attributes);
        byte[] data;
        try
        {
            using var protector = KeyringPayloadProtector.FromKey(key);
            data = await protector.ProtectAsync(secret, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var file = FileFor(master, attributes);
        var temp = file + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            else
            {
                System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new SecretStoreUnavailableException("Geheimnis nicht speicherbar.", ex);
        }
    }

    public async Task ClearAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var master = await GetMasterAsync(cancellationToken).ConfigureAwait(false);
        TryDelete(FileFor(master, attributes));
    }

    /// <summary>Kanonische Form der Attribute (sortiert, längenpräfixiert – keine Mehrdeutigkeit durch Trennzeichen).</summary>
    internal static byte[] Canonical(IReadOnlyDictionary<string, string> attributes)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            builder.Append(key.Length).Append(':').Append(key).Append(value.Length).Append(':').Append(value).Append(';');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>HKDF-SHA256(Geheimnis, Salz, Zweck + Attribute) → 32 Byte; je Eintrag und Zweck ein eigener Schlüssel.</summary>
    internal static byte[] DeriveKey(byte[] master, string purpose, IReadOnlyDictionary<string, string> attributes)
    {
        var info = Encoding.UTF8.GetBytes(purpose + "\n").Concat(Canonical(attributes)).ToArray();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, s_salt, info);
    }

    private string FileFor(byte[] master, IReadOnlyDictionary<string, string> attributes)
    {
        var nameKey = DeriveKey(master, "file-name", new Dictionary<string, string>());
        try
        {
            var name = Convert.ToHexStringLower(HMACSHA256.HashData(nameKey, Canonical(attributes)).AsSpan(0, 16));
            return Path.Combine(directory, name + ".secret");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nameKey);
        }
    }

    private async Task<byte[]> GetMasterAsync(CancellationToken cancellationToken)
    {
        if (_master is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Nur ein Aufruf des Portals je App-Lauf; schlägt er fehl, beim nächsten Zugriff erneut versuchen
            return _master ??= await portal.RetrieveSecretAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // bleibt liegen; ohne Geheimnis aus dem Portal unlesbar
        }
    }

    public void Dispose()
    {
        if (_master is { } master)
        {
            CryptographicOperations.ZeroMemory(master);
            _master = null;
        }

        _gate.Dispose();
    }
}
