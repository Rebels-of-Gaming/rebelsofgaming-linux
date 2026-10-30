using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>App-Sperre: Sperrbildschirm (mit Fingerabdruck, Wartezeit, schmal) und Einstellungen → <c>artifacts/screenshots/</c>.</summary>
[Trait("Category", "Screenshots")]
public class AppLockScreenshotTests
{
    private static async Task<TestServices> LockedAsync(bool fingerprint)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Fingerprint.Available = fingerprint;
        await t.Services.AppLock.SetPinAsync("2580");
        return t;
    }

    [AvaloniaFact]
    public async Task Lock_screen_steam_deck() =>
        await ScreenshotTests.Shoot("applock_1280x800", 1280, 800, await LockedAsync(fingerprint: true), async shell =>
        {
            await ViewModelTests.WaitUntil(() => shell.LockScreen.CanUseFingerprint);
            await ScreenshotTests.Settle(200);
        }, AssertNothingBehindTheLock);

    [AvaloniaFact]
    public async Task Lock_screen_wrong_pin_wait_narrow()
    {
        var t = await LockedAsync(fingerprint: true);
        await ScreenshotTests.Shoot("applock_wait_narrow_480x800", 480, 800, t, async shell =>
        {
            for (var i = 0; i < 5; i++)
            {
                shell.LockScreen.Pin = "1111";
                await shell.LockScreen.UnlockCommand.ExecuteAsync(null);
            }

            await ViewModelTests.WaitUntil(() => shell.LockScreen.CanUseFingerprint);
            shell.LockScreen.FingerprintStatus = t.Services.T("Linux_LockFingerprintRetry");
            await ScreenshotTests.Settle(200);
        }, AssertNothingBehindTheLock);
    }

    [AvaloniaFact]
    public async Task Lock_screen_pin_only_english() =>
        await ScreenshotTests.Shoot("applock_en_1280x800", 1280, 800, await CreateEnglishAsync(), async shell =>
        {
            shell.LockScreen.Pin = "0000";
            await shell.LockScreen.UnlockCommand.ExecuteAsync(null);
            shell.LockScreen.Pin = "25";
            await ScreenshotTests.Settle(200);
        });

    [AvaloniaFact]
    public async Task Settings_section() =>
        await ShootSettings("applock_settings_1280x800", 1280, 800, await CreateSettingsAsync(), async shell =>
        {
            shell.LockScreen.Pin = "2580";
            await shell.LockScreen.UnlockCommand.ExecuteAsync(null);
            shell.NavigateTo("Nav_Settings");
            var settings = (SettingsViewModel)shell.CurrentPage!;
            await ViewModelTests.WaitUntil(() => settings.AppLock.IsLoaded && settings.AppLock.FingerprintAvailable);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Settings_pin_form_narrow() =>
        await ShootSettings("applock_settings_pin_narrow_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            var settings = (SettingsViewModel)shell.CurrentPage!;
            await ViewModelTests.WaitUntil(() => settings.AppLock.IsLoaded);
            settings.AppLock.Enabled = true;
            settings.AppLock.NewPin = "4711";
            settings.AppLock.ConfirmPin = "4712";
            await settings.AppLock.SavePinCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Settings_without_keyring() =>
        await ShootSettings("applock_settings_no-keyring_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true, keyring: false), async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            var settings = (SettingsViewModel)shell.CurrentPage!;
            await ViewModelTests.WaitUntil(() => settings.AppLock.IsLoaded);
            await ScreenshotTests.Settle(300);
        });

    private static async Task<TestServices> CreateEnglishAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke, language: "en");
        await t.Services.AppLock.SetPinAsync("2580");
        return t;
    }

    private static async Task<TestServices> CreateSettingsAsync()
    {
        var t = await LockedAsync(fingerprint: true);
        await t.Services.AppLock.SetMinutesAsync(15);
        return t;
    }

    /// <summary>
    /// Wie <see cref="ScreenshotTests.Shoot"/>, aber mit Zugriff auf das Fenster – um den Abschnitt „App-Sperre“ der
    /// Einstellungen vor der Aufnahme nach oben zu scrollen.
    /// </summary>
    private static async Task ShootSettings(string name, int width, int height, TestServices t, Func<ShellViewModel, Task> arrange)
    {
        using (t)
        {
            var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
            window.Width = width;
            window.Height = height;
            window.Show();
            var shell = (ShellViewModel)window.DataContext!;
            await shell.InitializeAsync();
            await ScreenshotTests.Settle(200);
            await arrange(shell);

            var title = RoG.Linux.App.Localization.Loc.Get("Linux_AppLockTitle");
            var block = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().First(b => b.Text == title);
            var scroll = window.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First();
            var top = Avalonia.VisualExtensions.TranslatePoint(block, new Avalonia.Point(0, 0), scroll) ?? default;
            scroll.Offset = new Avalonia.Vector(0, Math.Max(0, scroll.Offset.Y + top.Y - 60));
            await ScreenshotTests.Settle(200);

            var folder = Path.Combine(RoG.Linux.Tests.Platform.LocalizationTests.RepoRoot(), "artifacts", "screenshots");
            Directory.CreateDirectory(folder);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            foreach (var viewer in window.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>())
            {
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 1, $"horizontal overflow: {viewer.Extent.Width} > {viewer.Viewport.Width}");
            }

            window.Close();
        }
    }

    private static void AssertNothingBehindTheLock(Avalonia.Controls.Window window)
    {
        Assert.Single(window.GetVisualDescendants().OfType<LockScreenView>(), v => v.IsEffectivelyVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<NavPane>(), p => p.IsEffectivelyVisible);
    }
}
