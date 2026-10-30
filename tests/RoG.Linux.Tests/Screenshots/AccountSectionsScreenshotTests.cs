using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 6 (Teil C): Konto mit aufgeklappten Bereichen, Löschen-Ablauf, Einstellungen mit Benachrichtigungen.</summary>
[Trait("Category", "Screenshots")]
public class AccountSectionsScreenshotTests
{
    private static async Task<TestServices> SignedInAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeAccountRoutes.MeFull;
        t.Api.Overrides["/v1/me/profile"] = """{"data":{"visibility":{"coins":"members","minecraft_stats":"private"}}}""";
        return t;
    }

    private static async Task<AccountViewModel> OpenAccountAsync(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Account");
        var account = (AccountViewModel)shell.CurrentPage!;
        await ScreenshotTests.Settle(100);
        return account;
    }

    /// <summary>Bewerbungen und Abwesenheit aufgeklappt (oberer Teil der Seite).</summary>
    private static async Task ExpandApplicationsAndAbsences(ShellViewModel shell)
    {
        var account = await OpenAccountAsync(shell);
        account.Applications.IsExpanded = true;
        account.Absences.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => account.Applications.ShowContent && account.Absences.ShowContent);
        await ScreenshotTests.Settle(300);
    }

    /// <summary>Minecraft mit Vorschau „bist das du?“ und Geräte &amp; Daten aufgeklappt, nach unten gescrollt.</summary>
    private static async Task ExpandMinecraftAndData(ShellViewModel shell)
    {
        var account = await OpenAccountAsync(shell);
        account.Minecraft.IsExpanded = true;
        account.Data.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => account.Minecraft.HasWallet && account.Data.ShowContent);
        account.Minecraft.Code = FakeAccountRoutes.LinkCode;
        _ = account.Minecraft.CheckCommand.ExecuteAsync(null);
        await ViewModelTests.WaitUntil(() => account.Minecraft.Confirm.IsOpen);
        await ScreenshotTests.Settle(400);
    }

    /// <summary>Löschen-Bereich nach „leave_clan_first“: Austritt angeboten.</summary>
    private static async Task DeleteFlow(ShellViewModel shell, TestServices t)
    {
        var account = await OpenAccountAsync(shell);
        account.Data.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => account.Data.ShowContent);
        account.Data.OpenDeleteCommand.Execute(null);
        account.Data.DeleteConfirmation = "Wahke";
        t.Api.Overrides["/v1/me"] = FakeAccountRoutes.Problem(409, "leave_clan_first");
        await account.Data.DeleteAccountCommand.ExecuteAsync(null);
        await account.Data.LeaveClanCommand.ExecuteAsync(null); // „Wirklich austreten – erneut klicken“
        await ScreenshotTests.Settle(400);
    }

    [AvaloniaFact]
    public async Task Account_sections_steam_deck()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_sections_1280x800", 1280, 800, t, ExpandApplicationsAndAbsences);
    }

    [AvaloniaFact]
    public async Task Account_sections_full()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_sections_full_1280x2600", 1280, 2600, t, ExpandApplicationsAndAbsences);
    }

    [AvaloniaFact]
    public async Task Account_minecraft_and_data_full()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_minecraft_data_1280x2600", 1280, 2600, t, ExpandMinecraftAndData);
    }

    [AvaloniaFact]
    public async Task Account_minecraft_narrow()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_minecraft_narrow_480x2400", 480, 2400, t, ExpandMinecraftAndData, AssertNoHorizontalOverflow);
    }

    [AvaloniaFact]
    public async Task Account_sections_narrow()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_sections_narrow_480x2400", 480, 2400, t, ExpandApplicationsAndAbsences, AssertNoHorizontalOverflow);
    }

    [AvaloniaFact]
    public async Task Account_delete_flow()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_delete_1280x1600", 1280, 1600, t, shell => DeleteFlow(shell, t));
    }

    [AvaloniaFact]
    public async Task Account_delete_flow_narrow()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("account_delete_narrow_480x1800", 480, 1800, t, shell => DeleteFlow(shell, t), AssertNoHorizontalOverflow);
    }

    [AvaloniaFact]
    public async Task Settings_notifications_steam_deck()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("settings_notifications_1280x800", 1280, 800, t, async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            var settings = (SettingsViewModel)shell.CurrentPage!;
            settings.NotificationSettings.IsExpanded = true;
            await ViewModelTests.WaitUntil(() => settings.NotificationSettings.ShowContent);
            await ScreenshotTests.Settle(300);
        });
    }

    [AvaloniaFact]
    public async Task Settings_notifications_narrow()
    {
        var t = await SignedInAsync();
        await ScreenshotTests.Shoot("settings_notifications_narrow_480x1400", 480, 1400, t, async shell =>
        {
            shell.NavigateTo("Nav_Settings");
            var settings = (SettingsViewModel)shell.CurrentPage!;
            settings.NotificationSettings.IsExpanded = true;
            await ViewModelTests.WaitUntil(() => settings.NotificationSettings.ShowContent);
            await ScreenshotTests.Settle(300);
        }, AssertNoHorizontalOverflow);
    }

    /// <summary>Kein Steuerelement der Seite ragt rechts über das Fenster hinaus (kein horizontales Scrollen).</summary>
    private static void AssertNoHorizontalOverflow(Avalonia.Controls.Window window)
    {
        foreach (var scroll in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ScrollViewer>())
        {
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"horizontal overflow: {scroll.Extent.Width} > {scroll.Viewport.Width}");
        }
    }
}
