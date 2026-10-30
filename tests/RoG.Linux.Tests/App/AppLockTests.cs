using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Platform.Security;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 7: App-Sperre – Sperren beim Start/nach Abwesenheit, PIN, Wartezeit, Fingerabdruck, Einstellungen.</summary>
public class AppLockTests
{
    private const string Pin = "2580";

    /// <summary>Angemeldet mit eingeschalteter Sperre und PIN <see cref="Pin"/>.</summary>
    private static async Task<TestServices> LockedSetupAsync(bool fingerprint = false)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Fingerprint.Available = fingerprint;
        Assert.True(await t.Services.AppLock.SetPinAsync(Pin));
        return t;
    }

    [AvaloniaFact]
    public async Task Locks_at_start_only_when_enabled_and_signed_in()
    {
        using (var t = await LockedSetupAsync())
        {
            var shell = new ShellViewModel(t.Services);
            await shell.InitializeAsync();
            Assert.True(shell.LockScreen.IsLocked);
            Assert.True(t.Services.AppLock.IsLocked);
        }

        using (var guest = await TestServices.CreateAsync(guest: true))
        {
            await guest.Services.UpdateSettingsAsync(s => s with { AppLock = true });
            var shell = new ShellViewModel(guest.Services);
            await shell.InitializeAsync();
            Assert.False(shell.LockScreen.IsLocked); // Gäste haben nichts zu schützen
        }

        using (var off = await TestServices.CreateAsync(TestTokens.Wahke))
        {
            var shell = new ShellViewModel(off.Services);
            await shell.InitializeAsync();
            Assert.False(shell.LockScreen.IsLocked);
        }
    }

    [AvaloniaFact]
    public async Task Locks_after_the_configured_absence()
    {
        using var t = await LockedSetupAsync();
        var appLock = t.Services.AppLock;
        appLock.OnStart(signedIn: true);
        appLock.Unlock();

        appLock.OnLeft();
        t.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(appLock.OnReturned()); // Standard 5 Minuten

        appLock.OnLeft();
        t.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(appLock.OnReturned());
        appLock.Unlock();

        await appLock.SetMinutesAsync(15);
        Assert.Equal(15, t.Services.Settings.AppLockMinutes);
        appLock.OnLeft();
        t.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(appLock.OnReturned());

        // Abgemeldet wird nie gesperrt
        appLock.SignedIn = false;
        appLock.OnLeft();
        t.Clock.Advance(TimeSpan.FromHours(1));
        Assert.False(appLock.OnReturned());
    }

    [AvaloniaFact]
    public async Task Pin_unlocks_and_wrong_pins_lead_to_a_growing_wait()
    {
        using var t = await LockedSetupAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var screen = shell.LockScreen;
        Assert.True(screen.IsLocked);
        Assert.False(screen.UnlockCommand.CanExecute(null)); // leeres Feld

        screen.Pin = "0000";
        await screen.UnlockCommand.ExecuteAsync(null);
        Assert.True(screen.IsLocked);
        Assert.Equal(t.Services.T("Linux_LockWrongPin"), screen.ErrorText);
        Assert.Equal(string.Empty, screen.Pin); // Feld geleert

        for (var i = 0; i < 4; i++)
        {
            screen.Pin = "1111";
            await screen.UnlockCommand.ExecuteAsync(null);
        }

        Assert.True(screen.IsBlocked);
        Assert.Equal(t.Services.T("Linux_LockTooManyAttempts"), screen.ErrorText);
        Assert.Equal(t.Services.Format("Linux_LockBlockedSeconds", 30), screen.BlockedText);
        Assert.Equal(5, t.Services.LinuxSettings.AppLockFailures); // übersteht einen Neustart
        Assert.NotNull(t.Services.LinuxSettings.AppLockBlockedUntil);

        // Während der Wartezeit zählt auch die richtige PIN nicht
        screen.Pin = Pin;
        Assert.False(screen.UnlockCommand.CanExecute(null));
        Assert.Equal(PinUnlockResult.Blocked, await t.Services.AppLock.UnlockWithPinAsync(Pin));

        t.Clock.Advance(TimeSpan.FromSeconds(31));
        screen.UpdateBlocked();
        Assert.False(screen.IsBlocked);
        screen.Pin = Pin;
        await screen.UnlockCommand.ExecuteAsync(null);
        Assert.False(screen.IsLocked);
        Assert.False(shell.LockScreen.IsLocked);
        Assert.Equal(0, t.Services.LinuxSettings.AppLockFailures);
        Assert.Null(t.Services.LinuxSettings.AppLockBlockedUntil);
    }

    [AvaloniaFact]
    public async Task Long_waits_are_shown_in_minutes_and_restored_after_restart()
    {
        using var t = await LockedSetupAsync();
        for (var i = 0; i < 8; i++)
        {
            t.Clock.Advance(TimeSpan.FromMinutes(5));
            await t.Services.AppLock.CheckPinAsync("9999");
        }

        // 8. Fehlversuch → 4 Minuten Wartezeit
        Assert.Equal(TimeSpan.FromMinutes(4), t.Services.AppLock.Throttle.Remaining);
        var restarted = new AppLockService(t.Services, t.Secrets, t.Fingerprint, () => t.Clock.Now, 1_000);
        Assert.Equal(8, restarted.Throttle.Failures);
        Assert.True(restarted.Throttle.IsBlocked);

        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        shell.LockScreen.UpdateBlocked();
        Assert.Equal(t.Services.Format("Linux_LockBlockedMinutes", 4), shell.LockScreen.BlockedText);
    }

    [AvaloniaFact]
    public async Task Fingerprint_unlocks_when_a_finger_is_enrolled()
    {
        using var t = await LockedSetupAsync(fingerprint: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var screen = shell.LockScreen;
        await ViewModelTests.WaitUntil(() => screen.CanUseFingerprint);

        t.Fingerprint.Result = FingerprintResult.NoMatch;
        await screen.UnlockWithFingerprintCommand.ExecuteAsync(null);
        Assert.True(screen.IsLocked);
        Assert.Equal(t.Services.T("Linux_LockFingerprintNoMatch"), screen.FingerprintStatus);
        Assert.Equal(0, t.Services.AppLock.Throttle.Failures); // zählt nicht als PIN-Fehlversuch

        t.Fingerprint.Result = FingerprintResult.TimedOut;
        await screen.UnlockWithFingerprintCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_LockFingerprintTimeout"), screen.FingerprintStatus);

        t.Fingerprint.Result = FingerprintResult.Match;
        await screen.UnlockWithFingerprintCommand.ExecuteAsync(null);
        Assert.False(screen.IsLocked);
        Assert.Equal(3, t.Fingerprint.Verifications);
    }

    [AvaloniaFact]
    public async Task Pin_entry_cancels_a_running_fingerprint_scan_and_disabled_fingerprint_is_hidden()
    {
        using var t = await LockedSetupAsync(fingerprint: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var screen = shell.LockScreen;
        await ViewModelTests.WaitUntil(() => screen.CanUseFingerprint);

        t.Fingerprint.WaitForCancel = true;
        var scan = screen.UnlockWithFingerprintCommand.ExecuteAsync(null);
        Assert.True(screen.IsScanning);
        Assert.Equal(t.Services.T("Linux_LockFingerprintScan"), screen.FingerprintStatus);
        screen.Pin = Pin;
        await screen.UnlockCommand.ExecuteAsync(null);
        await scan;
        Assert.False(screen.IsLocked);
        Assert.False(screen.IsScanning);

        // Fingerabdruck in den Einstellungen aus → nur PIN
        await t.Services.AppLock.SetFingerprintAllowedAsync(false);
        t.Services.AppLock.OnStart(signedIn: true);
        await screen.OnLockedAsync();
        Assert.False(screen.CanUseFingerprint);
        Assert.Equal(FingerprintResult.Unavailable, await t.Services.AppLock.UnlockWithFingerprintAsync(null, CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task Forgot_pin_signs_out_and_turns_the_lock_off()
    {
        using var t = await LockedSetupAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.LockScreen.IsLocked);

        await shell.LockScreen.SignOutCommand.ExecuteAsync(null);

        Assert.False(shell.LockScreen.IsLocked);
        Assert.False(t.Services.Settings.AppLock);
        Assert.DoesNotContain(t.Secrets.Items, i => i.Key.Contains("kind=app-lock-pin", StringComparison.Ordinal));
        Assert.Null(await t.Services.Tokens.GetAsync());
        Assert.True(shell.IsLoginVisible);
    }

    [AvaloniaFact]
    public async Task Missing_pin_keeps_the_app_locked()
    {
        using var t = await LockedSetupAsync();
        t.Secrets.Items.Clear(); // Eintrag von Hand gelöscht – Token ist auch weg, aber die Shell lief schon
        t.Services.AppLock.OnStart(signedIn: true);
        var screen = new LockScreenViewModel(t.Services, () => Task.CompletedTask);
        screen.Pin = Pin;
        await screen.UnlockCommand.ExecuteAsync(null);
        Assert.True(t.Services.AppLock.IsLocked);
        Assert.Equal(t.Services.T("Linux_LockPinMissing"), screen.ErrorText);

        t.Secrets.Unavailable = true;
        screen.Pin = Pin;
        await screen.UnlockCommand.ExecuteAsync(null);
        Assert.True(t.Services.AppLock.IsLocked);
        Assert.Equal(t.Services.T("Linux_LockKeyringUnavailable"), screen.ErrorText);
    }

    [AvaloniaFact]
    public async Task Locked_window_hides_the_shell_traps_shortcuts_and_locks_before_tray_open()
    {
        using var t = await LockedSetupAsync();
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        Assert.True(shell.LockScreen.IsLocked);
        var start = shell.CurrentItem?.Key;

        window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.D3, RawInputModifiers.Control, PhysicalKey.None, null);
        Assert.Equal(start, shell.CurrentItem?.Key); // Strg+3 wirkt nicht hinter der Sperre
        var lockView = window.GetVisualDescendants().OfType<RoG.Linux.App.Views.LockScreenView>().Single();
        Assert.True(lockView.IsVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<RoG.Linux.App.Views.NavPane>(), p => p.IsEffectivelyVisible);
        shell.LockScreen.Pin = Pin;
        await shell.LockScreen.UnlockCommand.ExecuteAsync(null);
        Assert.False(lockView.IsVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<RoG.Linux.App.Views.NavPane>(), p => p.IsEffectivelyVisible);

        // In den Infobereich, lange weg, über das Tray zurück → gesperrt, bevor das Fenster erscheint
        t.Services.AppLock.OnLeft();
        window.Hide();
        t.Clock.Advance(TimeSpan.FromMinutes(6));
        RoG.Linux.App.App.BringToFront(window);
        Assert.True(shell.LockScreen.IsLocked);
        Assert.True(window.IsVisible);
        await RoG.Linux.Tests.Screenshots.ScreenshotTests.Settle(100);
        window.Close();
    }

    // ---- Einstellungen ----

    [AvaloniaFact]
    public async Task Settings_enable_with_new_pin_change_and_disable()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var settings = new AppLockSettingsViewModel(t.Services);
        await settings.LoadAsync();
        Assert.True(settings.CanUseAppLock);
        Assert.False(settings.ShowKeyringHint);
        Assert.False(settings.FingerprintAvailable);

        // Einschalten öffnet das PIN-Formular; Abbrechen lässt die Sperre aus
        settings.Enabled = true;
        Assert.True(settings.IsPinFormOpen);
        Assert.False(settings.IsChangingPin);
        settings.CancelPinCommand.Execute(null);
        Assert.False(settings.Enabled);
        Assert.False(t.Services.Settings.AppLock);

        settings.Enabled = true;
        settings.NewPin = "12";
        settings.ConfirmPin = "12";
        await settings.SavePinCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_AppLockPinFormat"), settings.FormError);
        settings.NewPin = "4711";
        settings.ConfirmPin = "4712";
        await settings.SavePinCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_AppLockPinMismatch"), settings.FormError);
        settings.ConfirmPin = "4711";
        await settings.SavePinCommand.ExecuteAsync(null);
        Assert.False(settings.IsPinFormOpen);
        Assert.True(settings.Enabled);
        Assert.True(settings.ShowOptions);
        Assert.True(t.Services.Settings.AppLock);
        Assert.Equal(t.Services.T("Linux_AppLockEnabled"), settings.Feedback.Message);
        var entry = Assert.Single(t.Secrets.Items, i => i.Key.Contains("kind=app-lock-pin", StringComparison.Ordinal));
        Assert.DoesNotContain("4711", System.Text.Encoding.UTF8.GetString(entry.Value.Secret), StringComparison.Ordinal);
        Assert.False(t.Services.AppLock.IsLocked); // erst nach der nächsten Abwesenheit

        // PIN ändern: aktuelle PIN nötig
        settings.ChangePinCommand.Execute(null);
        Assert.True(settings.IsChangingPin);
        settings.CurrentPin = "0000";
        settings.NewPin = "86420";
        settings.ConfirmPin = "86420";
        await settings.SavePinCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_LockWrongPin"), settings.FormError);
        settings.CurrentPin = "4711";
        await settings.SavePinCommand.ExecuteAsync(null);
        Assert.False(settings.IsPinFormOpen);
        Assert.Equal(t.Services.T("Linux_AppLockPinChanged"), settings.Feedback.Message);
        Assert.Equal(PinUnlockResult.Unlocked, await t.Services.AppLock.CheckPinAsync("86420"));
        Assert.Equal(PinUnlockResult.Wrong, await t.Services.AppLock.CheckPinAsync("4711"));

        // Minuten
        settings.SelectedMinutes = settings.MinutesOptions.Single(o => o.Minutes == 30);
        await ViewModelTests.WaitUntil(() => t.Services.Settings.AppLockMinutes == 30);
        Assert.Equal(["1 Minute", "5 Minuten", "15 Minuten", "30 Minuten", "60 Minuten"], settings.MinutesOptions.Select(o => o.Label));

        // Ausschalten löscht die PIN
        settings.Enabled = false;
        await ViewModelTests.WaitUntil(() => !t.Services.Settings.AppLock);
        await ViewModelTests.WaitUntil(() => t.Secrets.Items.Count == 1); // nur noch der Token
        Assert.Equal(t.Services.T("Linux_AppLockDisabled"), settings.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Settings_without_keyring_cannot_enable_and_fingerprint_toggle_is_saved()
    {
        using (var t = await TestServices.CreateAsync(TestTokens.Wahke, keyring: false))
        {
            var settings = new AppLockSettingsViewModel(t.Services);
            await settings.LoadAsync();
            Assert.False(settings.CanUseAppLock);
            Assert.True(settings.ShowKeyringHint);
            Assert.False(await t.Services.AppLock.SetPinAsync(Pin));
            Assert.False(t.Services.Settings.AppLock);
        }

        using (var t = await TestServices.CreateAsync(TestTokens.Wahke))
        {
            t.Fingerprint.Available = true;
            var page = new SettingsViewModel(t.Services);
            await page.AppLock.LoadAsync();
            Assert.True(page.AppLock.FingerprintAvailable);
            Assert.True(page.AppLock.Fingerprint);
            page.AppLock.Fingerprint = false;
            await ViewModelTests.WaitUntil(() => !t.Services.LinuxSettings.AppLockFingerprint);
        }
    }
}
