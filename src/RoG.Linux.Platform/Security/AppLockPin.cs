using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoG.Linux.Platform.Secrets;

namespace RoG.Linux.Platform.Security;

/// <summary>
/// PIN der App-Sperre (Gegenstück zu Windows Hello): 4–8 Ziffern, gespeichert nur als PBKDF2-SHA256-Hash mit
/// zufälligem Salz – nie im Klartext. Der Vergleich ist zeitkonstant (<see cref="CryptographicOperations.FixedTimeEquals"/>).
/// </summary>
public static class PinHash
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    /// <summary>Iterationen für neue Hashes (≈ 50–100 ms auf einem Steam Deck – spürbar nur beim Entsperren).</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Algorithm = "PBKDF2-SHA256";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Nur ASCII-Ziffern, 4–8 Stellen.</summary>
    public static bool IsValidFormat(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Hash-Eintrag für den Schlüsselbund (JSON: Version, Algorithmus, Iterationen, Salz, Hash).</summary>
    public static byte[] Create(string pin, int iterations = DefaultIterations)
    {
        if (!IsValidFormat(pin))
        {
            throw new ArgumentException("PIN must be 4–8 digits.", nameof(pin));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(pin, salt, iterations);
        var record = new Record(1, Algorithm, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        CryptographicOperations.ZeroMemory(hash);
        return JsonSerializer.SerializeToUtf8Bytes(record, s_json);
    }

    /// <summary>Stimmt die PIN? Beschädigte oder unbekannte Einträge gelten als „falsch“.</summary>
    public static bool Verify(string? pin, byte[]? stored)
    {
        if (pin is null || stored is null || !TryRead(stored, out var salt, out var expected, out var iterations))
        {
            return false;
        }

        // Format nicht vorab prüfen: auch eine offensichtlich falsche Eingabe kostet die volle Ableitung
        var actual = Derive(pin, salt, iterations);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations)
    {
        var bytes = Encoding.UTF8.GetBytes(pin);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool TryRead(byte[] stored, out byte[] salt, out byte[] hash, out int iterations)
    {
        salt = [];
        hash = [];
        iterations = 0;
        try
        {
            var record = JsonSerializer.Deserialize<Record>(stored, s_json);
            if (record is not { V: 1, Alg: Algorithm, Iterations: >= 1 and <= 10_000_000 } || record.Salt is null || record.Hash is null)
            {
                return false;
            }

            salt = Convert.FromBase64String(record.Salt);
            hash = Convert.FromBase64String(record.Hash);
            iterations = record.Iterations;
            return salt.Length >= 8 && hash.Length == HashSize;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }
    }

    private sealed record Record(int V, string? Alg, int Iterations, string? Salt, string? Hash);
}

/// <summary>Ergebnis einer PIN-Prüfung gegen den Schlüsselbund.</summary>
public enum PinCheck
{
    Correct,
    Wrong,

    /// <summary>Kein Eintrag im Schlüsselbund (gelöscht, anderer Rechner).</summary>
    Missing,
}

/// <summary>
/// PIN-Hash im Schlüsselbund (Secret Service), eigener Eintrag „App-Sperre“ mit <c>kind=app-lock-pin</c>.
/// <b>Global, nicht je Umgebung:</b> Die Einstellung „App-Sperre“ gilt für die App auf diesem Gerät (eine
/// <c>settings.json</c> für alle Umgebungen) – eine PIN je Umgebung hätte nach einem Umgebungswechsel eine
/// eingeschaltete Sperre ohne PIN bedeutet.
/// </summary>
public sealed class AppLockPinStore(ISecretBackend backend, int iterations = PinHash.DefaultIterations)
{
    public const string Label = "Rebels of Gaming (App-Sperre)";

    public static IReadOnlyDictionary<string, string> Attributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["application"] = AppIdentity.AppId,
        ["kind"] = "app-lock-pin",
    };

    /// <summary>Ist der Schlüsselbund nutzbar? Ohne ihn lässt sich die Sperre nicht einschalten.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await backend.LookupAsync(Attributes, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SecretStoreUnavailableException)
        {
            return false;
        }
    }

    /// <exception cref="SecretStoreUnavailableException">Schlüsselbund nicht nutzbar.</exception>
    public async Task<bool> HasPinAsync(CancellationToken cancellationToken = default) =>
        await backend.LookupAsync(Attributes, cancellationToken).ConfigureAwait(false) is { Length: > 0 };

    /// <summary>PIN setzen bzw. ersetzen (Hash auf dem Thread-Pool – PBKDF2 kostet einige zehn Millisekunden).</summary>
    /// <exception cref="SecretStoreUnavailableException">Schlüsselbund nicht nutzbar.</exception>
    public async Task SetAsync(string pin, CancellationToken cancellationToken = default)
    {
        var record = await Task.Run(() => PinHash.Create(pin, iterations), cancellationToken).ConfigureAwait(false);
        await backend.StoreAsync(Label, Attributes, record, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="SecretStoreUnavailableException">Schlüsselbund nicht nutzbar.</exception>
    public async Task<PinCheck> VerifyAsync(string pin, CancellationToken cancellationToken = default)
    {
        var stored = await backend.LookupAsync(Attributes, cancellationToken).ConfigureAwait(false);
        if (stored is not { Length: > 0 })
        {
            return PinCheck.Missing;
        }

        return await Task.Run(() => PinHash.Verify(pin, stored), cancellationToken).ConfigureAwait(false) ? PinCheck.Correct : PinCheck.Wrong;
    }

    /// <exception cref="SecretStoreUnavailableException">Schlüsselbund nicht nutzbar.</exception>
    public Task ClearAsync(CancellationToken cancellationToken = default) => backend.ClearAsync(Attributes, cancellationToken);
}

/// <summary>
/// Fehlversuche mit wachsender Wartezeit: 4 Versuche frei, ab dem 5. Fehlversuch 30 s, danach je Fehlversuch
/// doppelt so lange, höchstens 15 Minuten. Erfolg setzt zurück. Die Uhr ist austauschbar (Tests).
/// </summary>
public sealed class PinAttemptThrottle(Func<DateTimeOffset> clock)
{
    public const int FreeAttempts = 4;

    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(15);

    public int Failures { get; private set; }

    public DateTimeOffset? BlockedUntil { get; private set; }

    /// <summary>Restliche Wartezeit; nie länger als <see cref="MaxWait"/> (auch wenn die Systemuhr zurückspringt).</summary>
    public TimeSpan Remaining
    {
        get
        {
            if (BlockedUntil is not { } until)
            {
                return TimeSpan.Zero;
            }

            var left = until - clock();
            return left <= TimeSpan.Zero ? TimeSpan.Zero : left > MaxWait ? MaxWait : left;
        }
    }

    public bool IsBlocked => Remaining > TimeSpan.Zero;

    /// <summary>Wartezeit nach so vielen Fehlversuchen in Folge.</summary>
    public static TimeSpan WaitAfter(int failures)
    {
        if (failures <= FreeAttempts)
        {
            return TimeSpan.Zero;
        }

        var exponent = Math.Min(failures - FreeAttempts - 1, 10);
        var wait = FirstWait * Math.Pow(2, exponent);
        return wait > MaxWait ? MaxWait : wait;
    }

    /// <summary>Gespeicherten Stand übernehmen (App-Neustart setzt die Wartezeit nicht zurück).</summary>
    public void Restore(int failures, DateTimeOffset? blockedUntil)
    {
        Failures = Math.Max(0, failures);
        BlockedUntil = blockedUntil;
    }

    /// <summary>Fehlversuch zählen; Rückgabe = jetzt fällige Wartezeit.</summary>
    public TimeSpan RegisterFailure()
    {
        Failures++;
        var wait = WaitAfter(Failures);
        BlockedUntil = wait > TimeSpan.Zero ? clock() + wait : null;
        return wait;
    }

    public void Reset()
    {
        Failures = 0;
        BlockedUntil = null;
    }
}
