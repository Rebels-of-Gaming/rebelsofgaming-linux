using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Linux.App.Services;
using RoG.Linux.Platform.Security;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Sperrbildschirm über dem ganzen Fenster (wie das LockOverlay der Windows-App): PIN (Enter entsperrt), „Mit
/// Fingerabdruck entsperren“ (nur mit fprintd-Gerät und eingelerntem Finger), Wartezeit nach Fehlversuchen als
/// Countdown und „PIN vergessen? Abmelden“ (schaltet die Sperre aus und meldet ab).
/// </summary>
public sealed partial class LockScreenViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AppLockService _lock;
    private readonly Func<Task> _signOut;
    private readonly DispatcherTimer _countdown;
    private CancellationTokenSource? _fingerprintCts;

    public LockScreenViewModel(AppServices services, Func<Task> signOut)
    {
        _services = services;
        _lock = services.AppLock;
        _signOut = signOut;
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => UpdateBlocked();
        _lock.PropertyChanged += OnLockChanged;
    }

    public bool IsLocked => _lock.IsLocked;

    /// <summary>Eingabe im PIN-Feld (nur Ziffern zählen; wird nach jedem Versuch geleert).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial string Pin { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>Hinweis der Fingerabdruck-Prüfung („Finger auflegen“, „Nochmal“ …).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFingerprintStatus))]
    public partial string? FingerprintStatus { get; set; }

    public bool HasFingerprintStatus => !string.IsNullOrEmpty(FingerprintStatus);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(UnlockWithFingerprintCommand), nameof(SignOutCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockWithFingerprintCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool CanUseFingerprint { get; set; }

    /// <summary>Wartezeit nach zu vielen Fehlversuchen läuft – PIN-Feld gesperrt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterPin))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial bool IsBlocked { get; set; }

    [ObservableProperty]
    public partial string? BlockedText { get; set; }

    public bool CanEnterPin => !IsBlocked;

    /// <summary>Fenster soll das PIN-Feld fokussieren (nach dem Sperren und nach jedem Versuch).</summary>
    public event EventHandler? FocusRequested;

    private void OnLockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppLockService.IsLocked))
        {
            return;
        }

        OnPropertyChanged(nameof(IsLocked));
        if (_lock.IsLocked)
        {
            _ = OnLockedAsync();
        }
        else
        {
            CancelFingerprint();
            _countdown.Stop();
            Pin = string.Empty;
            ErrorText = null;
            FingerprintStatus = null;
        }
    }

    /// <summary>Gerade gesperrt: Felder leeren, Fingerabdruck prüfen, Wartezeit anzeigen.</summary>
    public async Task OnLockedAsync()
    {
        Pin = string.Empty;
        ErrorText = null;
        FingerprintStatus = null;
        UpdateBlocked();
        FocusRequested?.Invoke(this, EventArgs.Empty);
        CanUseFingerprint = _lock.FingerprintAllowed && await _lock.IsFingerprintAvailableAsync();
    }

    /// <summary>Countdown der Wartezeit aktualisieren (jede Sekunde, solange sie läuft).</summary>
    public void UpdateBlocked()
    {
        var remaining = _lock.Throttle.Remaining;
        IsBlocked = remaining > TimeSpan.Zero;
        if (IsBlocked)
        {
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            BlockedText = seconds >= 120
                ? _services.Format("Linux_LockBlockedMinutes", (int)Math.Ceiling(seconds / 60.0))
                : _services.Format("Linux_LockBlockedSeconds", seconds);
            if (!_countdown.IsEnabled)
            {
                _countdown.Start();
            }
        }
        else
        {
            BlockedText = null;
            _countdown.Stop();
        }
    }

    private bool CanUnlock() => !IsBusy && !IsBlocked && Pin.Length > 0;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        var pin = Pin;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var result = await _lock.UnlockWithPinAsync(pin);
            Pin = string.Empty;
            ErrorText = result switch
            {
                PinUnlockResult.Unlocked => null,
                PinUnlockResult.Wrong => _services.T("Linux_LockWrongPin"),
                PinUnlockResult.Blocked => _services.T("Linux_LockTooManyAttempts"),
                PinUnlockResult.Missing => _services.T("Linux_LockPinMissing"),
                _ => _services.T("Linux_LockKeyringUnavailable"),
            };
        }
        finally
        {
            IsBusy = false;
        }

        UpdateBlocked();
        if (_lock.IsLocked)
        {
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanScan() => !IsBusy && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task UnlockWithFingerprintAsync()
    {
        CancelFingerprint();
        var cts = new CancellationTokenSource();
        _fingerprintCts = cts;
        IsScanning = true;
        ErrorText = null;
        var progress = new Progress<FingerprintPrompt>(p => FingerprintStatus = _services.T(p == FingerprintPrompt.Retry ? "Linux_LockFingerprintRetry" : "Linux_LockFingerprintScan"));
        FingerprintStatus = _services.T("Linux_LockFingerprintScan");
        try
        {
            var result = await _lock.UnlockWithFingerprintAsync(progress, cts.Token);
            FingerprintStatus = result switch
            {
                FingerprintResult.Match or FingerprintResult.Cancelled => null,
                FingerprintResult.NoMatch => _services.T("Linux_LockFingerprintNoMatch"),
                FingerprintResult.TimedOut => _services.T("Linux_LockFingerprintTimeout"),
                FingerprintResult.Unavailable => _services.T("Linux_LockFingerprintUnavailable"),
                _ => _services.T("Linux_LockFingerprintFailed"),
            };
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_fingerprintCts, cts))
            {
                _fingerprintCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelFingerprint()
    {
        try
        {
            _fingerprintCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // bereits beendet
        }
    }

    /// <summary>„PIN vergessen? Abmelden“: Sperre aus, PIN aus dem Schlüsselbund, abmelden (Token weg) – wie Windows.</summary>
    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            CancelFingerprint();
            await _lock.DisableAsync();
            await _signOut();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignOut() => !IsBusy;
}
