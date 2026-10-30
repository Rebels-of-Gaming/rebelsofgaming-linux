using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Platform.Security;

namespace RoG.Linux.App.Services;

/// <summary>Ergebnis eines PIN-Versuchs auf dem Sperrbildschirm bzw. in den Einstellungen.</summary>
public enum PinUnlockResult
{
    Unlocked,
    Wrong,

    /// <summary>Zu viele Fehlversuche – Wartezeit läuft.</summary>
    Blocked,

    /// <summary>Im Schlüsselbund liegt keine PIN (mehr) – nur Abmelden hilft.</summary>
    Missing,

    /// <summary>Schlüsselbund nicht erreichbar oder Entsperren abgelehnt.</summary>
    KeyringUnavailable,
}

/// <summary>
/// App-Sperre der Linux-App (Gegenstück zu Windows Hello): Zeitpunkt aus dem Kern (<see cref="RoG.Core.Security.AppLock"/>:
/// beim Start und nach <c>AppLockMinutes</c> Abwesenheit), Entsperren per PIN (PBKDF2-Hash im Schlüsselbund) oder
/// Fingerabdruck (fprintd). Fehlversuche mit wachsender Wartezeit, gespeichert in <c>linux.json</c>. Gesperrt wird nur
/// angemeldet – Gäste haben nichts zu schützen. Ohne Schlüsselbund lässt sich die Sperre nicht einschalten.
/// </summary>
public sealed partial class AppLockService : ObservableObject
{
    /// <summary>So lange wartet die Fingerabdruck-Prüfung auf einen Finger.</summary>
    public static readonly TimeSpan FingerprintTimeout = TimeSpan.FromSeconds(30);

    private readonly AppServices _services;
    private readonly RoG.Core.Security.AppLock _lock;
    private readonly AppLockPinStore? _pins;

    public AppLockService(AppServices services, ISecretBackend? secrets, IFingerprintVerifier fingerprint, Func<DateTimeOffset> clock, int pinIterations = PinHash.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Fingerprint = fingerprint;
        _lock = new RoG.Core.Security.AppLock(clock);
        _pins = secrets is null ? null : new AppLockPinStore(secrets, pinIterations);
        Throttle = new PinAttemptThrottle(clock);
        Throttle.Restore(services.LinuxSettings.AppLockFailures, services.LinuxSettings.AppLockBlockedUntil);
    }

    public IFingerprintVerifier Fingerprint { get; }

    public PinAttemptThrottle Throttle { get; }

    /// <summary>Gerade gesperrt? Der Sperrbildschirm liegt dann über allem.</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; private set; }

    /// <summary>Angemeldet (Token vorhanden)? Von der Shell gesetzt – nur dann wird gesperrt.</summary>
    public bool SignedIn
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                Unlock();
            }
        }
    }

    public bool IsEnabled => _services.Settings.AppLock;

    /// <summary>Darf zusätzlich der Fingerabdruck entsperren (Einstellung, Standard an)?</summary>
    public bool FingerprintAllowed => _services.LinuxSettings.AppLockFingerprint;

    private void SyncSettings()
    {
        _lock.Enabled = _services.Settings.AppLock;
        _lock.IdleTimeout = TimeSpan.FromMinutes(Math.Max(1, _services.Settings.AppLockMinutes));
    }

    /// <summary>App startet (bzw. Shell nach dem Laden des Tokens): gesperrt, wenn eingeschaltet und angemeldet.</summary>
    public bool OnStart(bool signedIn)
    {
        SignedIn = signedIn;
        SyncSettings();
        var locked = _lock.OnStart();
        if (locked && signedIn)
        {
            IsLocked = true;
        }
        else
        {
            _lock.Unlock();
        }

        return IsLocked;
    }

    /// <summary>Fenster verliert den Vordergrund, wird minimiert oder in den Infobereich gelegt.</summary>
    public void OnLeft() => _lock.OnLeft();

    /// <summary>
    /// Fenster kommt zurück (Aktivierung, Tray, Deep Link, zweiter Start): sperren, wenn lange genug weg. Rückgabe =
    /// jetzt gesperrt. Vor dem Zeigen des Fensters aufrufen, damit kein Inhalt aufblitzt.
    /// </summary>
    public bool OnReturned()
    {
        SyncSettings();
        if (_lock.OnReturned() && SignedIn)
        {
            IsLocked = true;
        }
        else if (!IsLocked)
        {
            _lock.Unlock();
        }

        return IsLocked;
    }

    /// <summary>Nach erfolgreicher Prüfung (oder Abmelden).</summary>
    public void Unlock()
    {
        _lock.Unlock();
        IsLocked = false;
    }

    /// <summary>Lässt sich die Sperre einschalten (Schlüsselbund nutzbar)?</summary>
    public async Task<bool> IsKeyringAvailableAsync() => _pins is not null && await _pins.IsAvailableAsync();

    /// <summary>Fingerabdruck möglich (fprintd + Gerät + eingelernter Finger)? Fehler = nein.</summary>
    public async Task<bool> IsFingerprintAvailableAsync()
    {
        try
        {
            return await Fingerprint.IsAvailableAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>PIN prüfen (Sperrbildschirm). Richtig → entsperrt; falsch → Fehlversuch mit ggf. Wartezeit.</summary>
    public async Task<PinUnlockResult> UnlockWithPinAsync(string pin)
    {
        var result = await CheckPinAsync(pin);
        if (result == PinUnlockResult.Unlocked)
        {
            Unlock();
        }

        return result;
    }

    /// <summary>PIN prüfen, ohne zu entsperren (PIN ändern) – zählt Fehlversuche genauso.</summary>
    public async Task<PinUnlockResult> CheckPinAsync(string pin)
    {
        if (Throttle.IsBlocked)
        {
            return PinUnlockResult.Blocked;
        }

        if (_pins is null)
        {
            return PinUnlockResult.KeyringUnavailable;
        }

        PinCheck check;
        try
        {
            check = await _pins.VerifyAsync(pin ?? string.Empty);
        }
        catch (SecretStoreUnavailableException)
        {
            return PinUnlockResult.KeyringUnavailable;
        }

        switch (check)
        {
            case PinCheck.Correct:
                if (Throttle.Failures > 0 || Throttle.BlockedUntil is not null)
                {
                    Throttle.Reset();
                    await SaveThrottleAsync();
                }

                return PinUnlockResult.Unlocked;
            case PinCheck.Missing:
                return PinUnlockResult.Missing;
            default:
                var wait = Throttle.RegisterFailure();
                await SaveThrottleAsync();
                return wait > TimeSpan.Zero ? PinUnlockResult.Blocked : PinUnlockResult.Wrong;
        }
    }

    /// <summary>Fingerabdruck prüfen; Treffer entsperrt. Fehlschläge zählen nicht als PIN-Fehlversuch (fprintd begrenzt selbst).</summary>
    public async Task<FingerprintResult> UnlockWithFingerprintAsync(IProgress<FingerprintPrompt>? progress, CancellationToken cancellationToken)
    {
        if (!FingerprintAllowed)
        {
            return FingerprintResult.Unavailable;
        }

        FingerprintResult result;
        try
        {
            result = await Fingerprint.VerifyAsync(progress, FingerprintTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = FingerprintResult.Failed;
        }

        if (result == FingerprintResult.Match)
        {
            Unlock();
        }

        return result;
    }

    /// <summary>Sperre einschalten bzw. PIN ersetzen: PIN-Hash in den Schlüsselbund, dann Einstellung setzen.</summary>
    /// <returns><c>false</c>, wenn der Schlüsselbund nicht nutzbar ist (Sperre bleibt aus).</returns>
    public async Task<bool> SetPinAsync(string pin)
    {
        if (_pins is null || !PinHash.IsValidFormat(pin))
        {
            return false;
        }

        try
        {
            await _pins.SetAsync(pin);
        }
        catch (SecretStoreUnavailableException)
        {
            return false;
        }

        Throttle.Reset();
        await SaveThrottleAsync();
        if (!_services.Settings.AppLock)
        {
            await _services.UpdateSettingsAsync(s => s with { AppLock = true });
        }

        // Wer die Sperre gerade einschaltet, sitzt vor dem Rechner – erst nach der nächsten Abwesenheit sperren
        _lock.Unlock();
        return true;
    }

    /// <summary>Sperre ausschalten und PIN aus dem Schlüsselbund löschen (auch „PIN vergessen“ → Abmelden).</summary>
    public async Task DisableAsync()
    {
        if (_services.Settings.AppLock)
        {
            await _services.UpdateSettingsAsync(s => s with { AppLock = false });
        }

        if (_pins is not null)
        {
            try
            {
                await _pins.ClearAsync();
            }
            catch (SecretStoreUnavailableException)
            {
                // Eintrag bleibt ungenutzt liegen – ohne Einstellung „an“ wird er nie gelesen
            }
        }

        Throttle.Reset();
        await SaveThrottleAsync();
        Unlock();
    }

    public Task SetMinutesAsync(int minutes) =>
        _services.Settings.AppLockMinutes == minutes ? Task.CompletedTask : _services.UpdateSettingsAsync(s => s with { AppLockMinutes = Math.Max(1, minutes) });

    public Task SetFingerprintAllowedAsync(bool allowed) =>
        _services.LinuxSettings.AppLockFingerprint == allowed ? Task.CompletedTask : _services.UpdateLinuxSettingsAsync(s => s with { AppLockFingerprint = allowed });

    private Task SaveThrottleAsync() =>
        _services.LinuxSettings.AppLockFailures == Throttle.Failures && _services.LinuxSettings.AppLockBlockedUntil == Throttle.BlockedUntil
            ? Task.CompletedTask
            : _services.UpdateLinuxSettingsAsync(s => s with { AppLockFailures = Throttle.Failures, AppLockBlockedUntil = Throttle.BlockedUntil });
}
