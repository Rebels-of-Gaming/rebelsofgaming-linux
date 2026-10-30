using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Servers;
using RoG.Linux.App.Services;
using RoG.Linux.Platform.Security;

namespace RoG.Linux.App.ViewModels;

/// <summary>Eine Wahl „Sperren nach … Minuten“.</summary>
public sealed record LockMinutesOption(int Minutes, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Abschnitt „App-Sperre“ der Einstellungen (Windows: Schalter „App-Sperre mit Windows Hello“): ein/aus, PIN setzen bzw.
/// ändern (Formular im Inhalt), Minuten bis zur Sperre, Fingerabdruck erlauben. Einschalten geht nur über eine neue
/// PIN – sonst sperrt man sich aus; ohne Schlüsselbund bleibt der Schalter aus (Hinweis).
/// </summary>
public sealed partial class AppLockSettingsViewModel : ObservableObject
{
    private static readonly int[] s_minutes = [1, 5, 15, 30, 60];

    private readonly AppServices _services;
    private readonly AppLockService _lock;
    private bool _applying;

    public AppLockSettingsViewModel(AppServices services)
    {
        _services = services;
        _lock = services.AppLock;
        MinutesOptions = [.. s_minutes.Select(m => new LockMinutesOption(m, m == 1 ? services.T("Linux_Minute1") : services.Format("Linux_MinutesN", m)))];
        var current = services.Settings.AppLockMinutes;
        SelectedMinutes = MinutesOptions.FirstOrDefault(o => o.Minutes == current)
            ?? MinutesOptions.Where(o => o.Minutes <= Math.Max(1, current)).LastOrDefault()
            ?? MinutesOptions[1];
        _applying = true;
        Enabled = services.Settings.AppLock;
        Fingerprint = services.LinuxSettings.AppLockFingerprint;
        _applying = false;
    }

    public FeedbackViewModel Feedback { get; } = new();

    public IReadOnlyList<LockMinutesOption> MinutesOptions { get; }

    /// <summary>Schlüsselbund nutzbar → Schalter bedienbar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyringHint))]
    public partial bool CanUseAppLock { get; set; }

    /// <summary>Stand geprüft (Schlüsselbund gefragt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyringHint))]
    public partial bool IsLoaded { get; set; }

    public bool ShowKeyringHint => IsLoaded && !CanUseAppLock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    public partial bool Enabled { get; set; }

    /// <summary>Minuten, Fingerabdruck, „PIN ändern“ – nur bei eingeschalteter Sperre.</summary>
    public bool ShowOptions => Enabled && !IsPinFormOpen;

    [ObservableProperty]
    public partial LockMinutesOption SelectedMinutes { get; set; }

    [ObservableProperty]
    public partial bool FingerprintAvailable { get; set; }

    [ObservableProperty]
    public partial bool Fingerprint { get; set; }

    /// <summary>PIN-Formular offen (Einschalten oder Ändern).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    public partial bool IsPinFormOpen { get; set; }

    /// <summary>Ändern (aktuelle PIN nötig) statt Einschalten.</summary>
    [ObservableProperty]
    public partial bool IsChangingPin { get; set; }

    [ObservableProperty]
    public partial string CurrentPin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPin { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    public partial string? FormError { get; set; }

    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePinCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Schlüsselbund und Fingerabdruck prüfen (beim Öffnen der Einstellungen).</summary>
    public async Task LoadAsync()
    {
        CanUseAppLock = await _lock.IsKeyringAvailableAsync();
        IsLoaded = true;
        FingerprintAvailable = await _lock.IsFingerprintAvailableAsync();
        if (!CanUseAppLock && Enabled && !_lock.IsEnabled)
        {
            SetEnabledSilently(false);
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        if (value && !_lock.IsEnabled)
        {
            // Einschalten erst mit gespeicherter PIN
            OpenPinForm(change: false);
        }
        else if (!value && IsPinFormOpen && !IsChangingPin)
        {
            ClosePinForm();
        }
        else if (!value && _lock.IsEnabled)
        {
            ClosePinForm();
            _ = DisableAsync();
        }
    }

    private async Task DisableAsync()
    {
        await _lock.DisableAsync();
        Feedback.Show(_services.T("Linux_AppLockDisabled"), MessageTone.Info);
    }

    partial void OnSelectedMinutesChanged(LockMinutesOption value)
    {
        if (!_applying && value is not null)
        {
            _ = _lock.SetMinutesAsync(value.Minutes);
        }
    }

    partial void OnFingerprintChanged(bool value)
    {
        if (!_applying)
        {
            _ = _lock.SetFingerprintAllowedAsync(value);
        }
    }

    [RelayCommand]
    private void ChangePin() => OpenPinForm(change: true);

    private void OpenPinForm(bool change)
    {
        CurrentPin = string.Empty;
        NewPin = string.Empty;
        ConfirmPin = string.Empty;
        FormError = null;
        IsChangingPin = change;
        IsPinFormOpen = true;
    }

    private void ClosePinForm()
    {
        IsPinFormOpen = false;
        CurrentPin = string.Empty;
        NewPin = string.Empty;
        ConfirmPin = string.Empty;
        FormError = null;
    }

    /// <summary>Abbrechen (auch Escape): beim Einschalten bleibt die Sperre aus.</summary>
    [RelayCommand]
    public void CancelPin()
    {
        var wasEnabling = IsPinFormOpen && !IsChangingPin;
        ClosePinForm();
        if (wasEnabling)
        {
            SetEnabledSilently(false);
        }
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SavePinAsync()
    {
        FormError = null;
        if (!PinHash.IsValidFormat(NewPin))
        {
            FormError = _services.T("Linux_AppLockPinFormat");
            return;
        }

        if (!string.Equals(NewPin, ConfirmPin, StringComparison.Ordinal))
        {
            FormError = _services.T("Linux_AppLockPinMismatch");
            return;
        }

        IsBusy = true;
        try
        {
            if (IsChangingPin)
            {
                var check = await _lock.CheckPinAsync(CurrentPin);
                if (check != PinUnlockResult.Unlocked)
                {
                    CurrentPin = string.Empty;
                    FormError = check switch
                    {
                        PinUnlockResult.Wrong => _services.T("Linux_LockWrongPin"),
                        PinUnlockResult.Blocked => _services.T("Linux_LockTooManyAttempts"),
                        PinUnlockResult.Missing => null, // keine alte PIN mehr – neue einfach setzen
                        _ => _services.T("Linux_LockKeyringUnavailable"),
                    };
                    if (FormError is not null)
                    {
                        return;
                    }
                }
            }

            var changing = IsChangingPin;
            if (!await _lock.SetPinAsync(NewPin))
            {
                FormError = _services.T("Linux_AppLockSaveFailed");
                return;
            }

            ClosePinForm();
            SetEnabledSilently(true);
            Feedback.Show(_services.T(changing ? "Linux_AppLockPinChanged" : "Linux_AppLockEnabled"), MessageTone.Success);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetEnabledSilently(bool value)
    {
        _applying = true;
        Enabled = value;
        _applying = false;
    }
}
