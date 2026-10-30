namespace RoG.Linux.Platform.Secrets;

/// <summary>Schlüsselbund des Systems (Secret Service) – abstrahiert, damit Token-Logik ohne D-Bus testbar ist.</summary>
public interface ISecretBackend
{
    /// <summary>Geheimnis zu genau diesen Attributen lesen; <c>null</c> = nicht vorhanden.</summary>
    /// <exception cref="SecretStoreUnavailableException">Kein Schlüsselbund, gesperrt und Entsperren abgelehnt, D-Bus-Fehler.</exception>
    Task<byte[]?> LookupAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default);

    /// <summary>Geheimnis anlegen oder ersetzen (gleiche Attribute = gleicher Eintrag).</summary>
    Task StoreAsync(string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, CancellationToken cancellationToken = default);

    /// <summary>Alle Einträge mit diesen Attributen löschen.</summary>
    Task ClearAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default);
}

/// <summary>Der Schlüsselbund ist nicht nutzbar (kein Dienst, gesperrt, abgelehnt). Die App fällt dann auf „nur diese Sitzung“ zurück.</summary>
public sealed class SecretStoreUnavailableException : Exception
{
    public SecretStoreUnavailableException()
    {
    }

    public SecretStoreUnavailableException(string message)
        : base(message)
    {
    }

    public SecretStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
