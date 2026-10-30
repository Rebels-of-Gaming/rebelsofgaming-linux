using Avalonia.Headless.XUnit;
using RoG.Core.Features.Account;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 6 (Teil C): Konto „Paket A“ – Bewerbungen, Abwesenheit, Minecraft/Rebel-Coins, Geräte &amp; Daten mit
/// Konto löschen – und Benachrichtigungs-Einstellungen je Kanal auf der Einstellungsseite.
/// </summary>
public class AccountSectionsTests
{
    private static async Task<TestServices> SignedInAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeAccountRoutes.MeFull;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static AccountViewModel Account(TestServices t, Func<Task>? signOut = null) =>
        new(t.Services, signOut ?? (() => Task.CompletedTask), () => { });

    [AvaloniaFact]
    public async Task Sections_load_only_when_expanded()
    {
        using var t = await SignedInAsync();
        var account = Account(t);
        await Task.Delay(50);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath is "/v1/me/applications" or "/v1/me/absences" or "/v1/devices" or "/v1/me/minecraft/accounts");

        account.Applications.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => account.Applications.ShowContent);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/me/applications");
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/devices");
    }

    [AvaloniaFact]
    public async Task Applications_show_timeline_tasks_messages_and_withdraw_after_confirmation()
    {
        using var t = await SignedInAsync();
        var apps = Account(t).Applications;
        await apps.LoadAsync();
        Assert.Equal(2, apps.Items.Count);
        var trial = apps.Items[0];
        Assert.Equal("CS2-Squad – Probezeit", trial.Title);
        Assert.Equal(75, trial.Progress);
        Assert.Equal(2, trial.Timeline.Count);
        Assert.Equal(3, trial.Tasks.Count);
        Assert.Contains(trial.Tasks, x => !x.Done);
        Assert.Equal("Krater", trial.Messages[0].Author);
        Assert.Equal(t.Services.T("Apply_You"), trial.Messages[1].Author);
        Assert.True(trial.IsOpen);
        Assert.False(apps.Items[1].IsOpen);
        Assert.Contains("Frühjahr", apps.Items[1].Details, StringComparison.Ordinal);

        // Nachricht senden
        Assert.False(trial.SendCommand.CanExecute(null));
        trial.Draft = "Bin Dienstag dabei.";
        await trial.SendCommand.ExecuteAsync(null);
        var sent = t.Api.Writes.Single(w => w.Path.EndsWith("/messages", StringComparison.Ordinal));
        Assert.Contains("Bin Dienstag dabei.", sent.Body, StringComparison.Ordinal);
        Assert.True(apps.Feedback.IsSuccess);

        // Zurückziehen: erst Rückfrage, Abbrechen sendet nichts
        var withdraw = trial.WithdrawCommand.ExecuteAsync(null);
        Assert.True(apps.Confirm.IsOpen);
        Assert.Contains("CS2-Squad", apps.Confirm.Text, StringComparison.Ordinal);
        apps.Confirm.Cancel();
        await withdraw;
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/withdraw", StringComparison.Ordinal));

        withdraw = trial.WithdrawCommand.ExecuteAsync(null);
        apps.Confirm.AcceptCommand.Execute(null);
        await withdraw;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/me/applications/01JBAPPCS2TRIAL0000000001/withdraw");
        Assert.Equal(t.Services.T("Linux_ApplicationWithdrawn"), apps.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Applications_empty_and_error_states()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/me/applications"] = """{"data":[]}""";
        var apps = Account(t).Applications;
        await apps.LoadAsync();
        Assert.True(apps.IsEmpty);

        t.Api.Overrides["/v1/me/applications"] = FakeAccountRoutes.Problem(403, "forbidden");
        var again = Account(t).Applications;
        await again.LoadAsync();
        Assert.True(again.HasError);
    }

    [AvaloniaFact]
    public async Task Absences_report_validate_and_delete()
    {
        using var t = await SignedInAsync();
        var absences = Account(t).Absences;
        await absences.LoadAsync();
        Assert.Equal(2, absences.Items.Count);
        Assert.True(absences.Items[0].IsCurrent);
        Assert.Contains("Urlaub an der Mosel", absences.Items[0].Detail, StringComparison.Ordinal);

        absences.From = new DateTime(2026, 11, 12);
        absences.To = new DateTime(2026, 11, 10);
        await absences.ReportCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_AbsenceRange"), absences.Feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/absences");

        absences.To = new DateTime(2026, 11, 14);
        absences.Reason = "LAN-Party";
        await absences.ReportCommand.ExecuteAsync(null);
        var write = t.Api.Writes.Single(w => w.Path == "/v1/me/absences");
        Assert.Contains("\"starts_on\":\"2026-11-12\"", write.Body, StringComparison.Ordinal);
        Assert.Contains("\"ends_on\":\"2026-11-14\"", write.Body, StringComparison.Ordinal);
        Assert.True(absences.Feedback.IsSuccess);
        Assert.Null(absences.Reason);

        await absences.Items[1].DeleteCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/me/absences/01JBABS0000000000000000002");
        Assert.Single(absences.Items);
    }

    [AvaloniaFact]
    public async Task Minecraft_link_shows_preview_and_links_only_after_yes()
    {
        using var t = await SignedInAsync();
        var mc = Account(t).Minecraft;
        await mc.LoadAsync();
        Assert.True(mc.CanRead);
        Assert.True(mc.CanLink);
        Assert.False(mc.NeedsSignIn);
        Assert.Equal(2, mc.Items.Count);
        Assert.Contains("Xbox", mc.Items[1].Meta, StringComparison.Ordinal);
        Assert.True(mc.HasWallet);
        Assert.Contains("3", mc.BalanceText, StringComparison.Ordinal);
        Assert.Equal(3, mc.Transactions.Count);
        Assert.StartsWith("−", mc.Transactions[0].Amount, StringComparison.Ordinal);

        // ungültiger Code wird gar nicht gesendet
        mc.Code = "abc";
        await mc.CheckCommand.ExecuteAsync(null);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/minecraft/link");

        // Nein: nichts verknüpft
        mc.Code = "k7m2-qx";
        var check = mc.CheckCommand.ExecuteAsync(null);
        await ViewModelTests.WaitUntil(() => mc.Confirm.IsOpen);
        Assert.Contains("Krater", mc.Confirm.Text, StringComparison.Ordinal);
        Assert.Contains("RoG Network", mc.Confirm.Text, StringComparison.Ordinal);
        mc.Confirm.Cancel();
        await check;
        Assert.DoesNotContain(t.Api.Writes, w => w.Body.Contains("\"confirm\":true", StringComparison.Ordinal));
        Assert.Equal(t.Services.T("Minecraft_Declined"), mc.Feedback.Message);

        // Ja: verknüpfen
        check = mc.CheckCommand.ExecuteAsync(null);
        await ViewModelTests.WaitUntil(() => mc.Confirm.IsOpen);
        mc.Confirm.AcceptCommand.Execute(null);
        await check;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/me/minecraft/link" && w.Body.Contains("\"confirm\":true", StringComparison.Ordinal) && w.Body.Contains(FakeAccountRoutes.LinkCode, StringComparison.Ordinal));
        Assert.True(mc.Feedback.IsSuccess);
        Assert.Equal(string.Empty, mc.Code);

        // Trennen mit Rückfrage
        var unlink = mc.Items[0].UnlinkCommand.ExecuteAsync(null);
        Assert.True(mc.Confirm.IsOpen);
        mc.Confirm.AcceptCommand.Execute(null);
        await unlink;
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/me/minecraft/accounts/01JCMCJAVA0000000000000001");
    }

    [AvaloniaFact]
    public async Task Minecraft_settings_and_visibility_are_saved()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/me/profile"] = """{"data":{"visibility":{"coins":"members","minecraft_stats":"private"}}}""";
        var mc = Account(t).Minecraft;
        await mc.LoadAsync();
        Assert.True(mc.CanEditVisibility);
        Assert.Equal("members", mc.SelectedCoinsVisibility.Value);
        Assert.Equal("private", mc.SelectedStatsVisibility.Value);

        mc.ShowWebsiteMessages = true;
        await ViewModelTests.WaitUntil(() => t.Api.Writes.Any(w => w.Path == "/v1/me/minecraft/settings"));
        mc.SelectedStatsVisibility = mc.VisibilityOptions[2];
        await ViewModelTests.WaitUntil(() => t.Api.Writes.Any(w => w.Method == "PATCH" && w.Path == "/v1/me/profile" && w.Body.Contains("\"minecraft_stats\":\"public\"", StringComparison.Ordinal)));
        Assert.True(mc.Feedback.IsSuccess);

        mc.OpenLeaderboardsCommand.Execute(null); // ohne Shell: kein Absturz
    }

    [AvaloniaFact]
    public async Task Old_tokens_without_minecraft_scopes_get_a_sign_in_hint()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync(); // FakeData.Me: ohne minecraft:*
        var mc = Account(t).Minecraft;
        await mc.LoadAsync();
        Assert.True(mc.NeedsSignIn);
        Assert.False(mc.CanLink);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/me/minecraft/accounts");
    }

    [AvaloniaFact]
    public async Task Minecraft_link_opens_the_section_and_prefills_the_code()
    {
        using var t = await SignedInAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://account/minecraft?code=k7m2qx")));
        var account = Assert.IsType<AccountViewModel>(shell.CurrentPage);
        Assert.True(account.Minecraft.IsExpanded);
        Assert.Equal(FakeAccountRoutes.LinkCode, account.Minecraft.Code);
        Assert.True(account.Minecraft.Feedback.IsInfo);
        await ViewModelTests.WaitUntil(() => !account.Minecraft.IsLoading);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/minecraft/link"); // nie automatisch einlösen

        // Escape schließt die Vorschau
        var check = account.Minecraft.CheckCommand.ExecuteAsync(null);
        await ViewModelTests.WaitUntil(() => account.Minecraft.Confirm.IsOpen);
        Assert.True(account.IsDetailOpen);
        account.CloseDetail();
        await check;
        Assert.False(account.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Devices_exports_and_download_to_the_downloads_folder()
    {
        using var t = await SignedInAsync();
        var signedOut = 0;
        var data = Account(t, () => { signedOut++; return Task.CompletedTask; }).Data;
        await data.LoadAsync();
        Assert.Equal(3, data.Devices.Count);
        Assert.True(data.Devices[0].IsCurrent);
        Assert.True(data.HasExports);

        await data.Devices[1].RemoveCommand.ExecuteAsync(null); // fremdes Gerät: ohne Rückfrage
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/devices/01JCDEVANDROID000000000002");
        Assert.Equal(2, data.Devices.Count);
        Assert.Equal(0, signedOut);

        await data.Exports[0].DownloadCommand.ExecuteAsync(null);
        var saved = data.Exports[0].SavedPath;
        Assert.NotNull(saved);
        Assert.Equal(t.Services.DownloadDirectory, Path.GetDirectoryName(saved));
        Assert.True(File.Exists(saved));
        await data.Exports[0].ShowInFolderCommand.ExecuteAsync(null);
        Assert.Equal(saved, t.Files.Shown.Single());

        await data.RequestExportCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "POST" && w.Path == "/v1/me/exports");

        // dieses Gerät: Rückfrage, danach abgemeldet
        var remove = data.Devices[0].RemoveCommand.ExecuteAsync(null);
        Assert.True(data.Confirm.IsOpen);
        data.Confirm.AcceptCommand.Execute(null);
        await remove;
        Assert.Equal(1, signedOut);
        Assert.Null(await t.Services.Tokens.GetAsync());
    }

    [AvaloniaFact]
    public async Task Delete_account_leaves_the_clan_first_then_deletes_and_signs_out()
    {
        using var t = await SignedInAsync();
        var signedOut = 0;
        t.Api.Overrides.Remove("/v1/me"); // DELETE /v1/me soll die Fake-Route treffen
        var account = Account(t, () => { signedOut++; return Task.CompletedTask; });
        var data = account.Data;
        data.OpenDeleteCommand.Execute(null);
        Assert.True(data.IsDeleteOpen);
        Assert.True(account.IsDetailOpen);
        Assert.False(data.DeleteAccountCommand.CanExecute(null)); // ohne Bestätigung

        // falsches Passwort: Feldfehler, Bereich bleibt
        data.DeleteConfirmation = "falsch";
        await data.DeleteAccountCommand.ExecuteAsync(null);
        Assert.True(data.DeleteHint.IsError);
        Assert.Equal("Das Passwort stimmt nicht.", data.DeleteHint.Message);
        Assert.Equal(0, signedOut);

        // aktives Mitglied: erst austreten
        t.Api.Overrides["/v1/me"] = FakeAccountRoutes.Problem(409, "leave_clan_first");
        data.DeleteConfirmation = "geheim";
        await data.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Equal(AccountDeletionStage.LeaveRequired, data.Flow.Stage);
        Assert.True(data.CanLeave);
        Assert.False(data.DeleteAccountCommand.CanExecute(null));
        Assert.Equal(t.Services.T("Account_DeleteLeaveText"), data.DeleteHint.Message);

        await data.LeaveClanCommand.ExecuteAsync(null); // erster Klick fragt nach
        Assert.True(data.LeaveArmed);
        Assert.Equal(t.Services.T("Account_DeleteLeaveConfirm"), data.LeaveLabel);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/leave-clan");
        await data.LeaveClanCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/me/leave-clan" && w.Body.Contains("geheim", StringComparison.Ordinal));
        Assert.Equal(AccountDeletionStage.Left, data.Flow.Stage);
        Assert.True(data.DeleteHint.IsSuccess);

        t.Api.Overrides.Remove("/v1/me");
        await data.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/me");
        Assert.Equal(1, signedOut);
        Assert.False(data.IsDeleteOpen);
        Assert.Null(await t.Services.Tokens.GetAsync());
    }

    [AvaloniaFact]
    public async Task Owner_is_blocked_and_escape_cancels_the_delete_area()
    {
        using var t = await SignedInAsync();
        var account = Account(t);
        var data = account.Data;
        data.OpenDeleteCommand.Execute(null);
        t.Api.Overrides["/v1/me"] = FakeAccountRoutes.Problem(409, "owner_cannot_delete");
        data.DeleteConfirmation = "Wahke";
        await data.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Equal(AccountDeletionStage.Blocked, data.Flow.Stage);
        Assert.True(data.DeleteHint.IsError);
        Assert.False(data.DeleteAccountCommand.CanExecute(null));

        account.CloseDetail();
        Assert.False(data.IsDeleteOpen);
        Assert.Equal(string.Empty, data.DeleteConfirmation);
        data.OpenDeleteCommand.Execute(null);
        Assert.Equal(AccountDeletionStage.Confirm, data.Flow.Stage);
    }

    [AvaloniaFact]
    public async Task Notification_settings_per_channel_digest_and_quiet_hours()
    {
        using var t = await SignedInAsync();
        var settings = new SettingsViewModel(t.Services);
        var n = settings.NotificationSettings;
        Assert.True(n.IsSignedIn);
        Assert.False(n.ShowContent);
        n.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => n.ShowContent);
        Assert.Equal(6, n.Items.Count);
        Assert.Equal("daily", n.SelectedDigest?.Value);
        Assert.Equal("22:00", n.QuietFrom);

        var reply = n.Items.Single(i => i.Key == "forum.reply");
        Assert.False(reply.ShowBell); // Glocke nicht abschaltbar
        Assert.True(reply.ShowMail);
        Assert.True(reply.Mail);
        Assert.False(reply.Discord);
        var news = n.Items.Single(i => i.Key == "news.published");
        Assert.True(news.ShowBell);
        Assert.False(news.Bell); // abgeschaltet
        Assert.False(news.ShowDiscord); // Kanal gibt es für News nicht

        reply.Discord = true;
        await ViewModelTests.WaitUntil(() => t.Api.Writes.Any(w => w.Path == "/v1/me/notification-settings"));
        var write = t.Api.Writes.Last(w => w.Path == "/v1/me/notification-settings");
        Assert.Contains("\"forum.reply\":{\"discord\":true}", write.Body, StringComparison.Ordinal);
        await ViewModelTests.WaitUntil(() => n.Feedback.IsSuccess);

        n.QuietFrom = "23";
        n.QuietUntil = "";
        await n.SaveQuietHoursCommand.ExecuteAsync(null);
        Assert.True(n.Feedback.IsError);
        n.QuietFrom = "23:30";
        n.QuietUntil = "7:00";
        await n.SaveQuietHoursCommand.ExecuteAsync(null);
        Assert.Contains("\"quiet_until\":\"07:00\"", t.Api.Writes.Last().Body, StringComparison.Ordinal);

        n.SelectedDigest = n.DigestOptions[2];
        await ViewModelTests.WaitUntil(() => t.Api.Writes.Last().Body.Contains("weekly", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Guests_see_no_account_sections_and_no_notification_settings()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var account = Account(t);
        await ViewModelTests.WaitUntil(() => account.IsGuest);
        Assert.False(new SettingsViewModel(t.Services).NotificationSettings.IsSignedIn);
    }
}
