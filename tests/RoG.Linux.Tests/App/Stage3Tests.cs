using Avalonia.Headless.XUnit;
using RoG.Core.Features.Notifications;
using RoG.Core.Navigation;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 3: Glocke, Zähler, Desktop-Benachrichtigungen mit Klick, alle <c>rog://</c>-Ziele.</summary>
public class Stage3Tests
{
    private static SilentNotifier Notifier(TestServices t) => (SilentNotifier)t.Services.Notifier;

    [AvaloniaFact]
    public async Task Shell_start_fetches_counters_for_bell_and_messages()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.Equal(3, t.Services.Realtime.UnreadNotifications);
        Assert.Equal(2, t.Services.Realtime.UnreadMessages);
        Assert.Equal(3, shell.FooterItems.First(i => i.Key == "Nav_Notifications").Badge);
        Assert.Equal(2, shell.MainItems.First(i => i.Key == "Nav_Messages").Badge);
    }

    [AvaloniaFact]
    public async Task Guests_have_no_counters()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.Equal(0, t.Services.Realtime.UnreadNotifications);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("unread-count", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Bell_lists_marks_read_and_opens_the_target_in_the_app()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://notifications")));
        var bell = Assert.IsType<NotificationsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => bell.ShowContent);

        Assert.Equal(4, bell.Items.Count);
        Assert.True(bell.Items[0].IsUnread);
        Assert.False(bell.Items[3].IsUnread);
        Assert.True(bell.HasUnread);

        // Termin-Benachrichtigung → Kalender in der App, gelesen markiert, Zähler sinkt
        await bell.Items[1].OpenCommand.ExecuteAsync(null);
        Assert.False(bell.Items[1].IsUnread);
        Assert.Equal(2, t.Services.Realtime.UnreadNotifications);
        Assert.Contains(t.Api.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/v1/notifications/01N2/read");
        var events = Assert.IsType<EventsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => events.Detail.ShowContent);
        Assert.Equal("01EV1", events.Detail.Id);

        // Forum-Thema → Forum in der App (seit Stufe 4)
        shell.NavigateTo("Nav_Notifications");
        await bell.Items[0].OpenCommand.ExecuteAsync(null);
        var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => forum.Thread.ShowContent);
        Assert.Equal("01T1", forum.Thread.ThreadId);

        shell.NavigateTo("Nav_Notifications");
        await bell.MarkAllReadCommand.ExecuteAsync(null);
        Assert.All(bell.Items, i => Assert.False(i.IsUnread));
        Assert.Equal(0, t.Services.Realtime.UnreadNotifications);
        Assert.Equal(0, shell.FooterItems.First(i => i.Key == "Nav_Notifications").Badge);

        bell.UnreadOnly = true;
        await ViewModelTests.WaitUntil(() => bell.Items.Count == 1);
    }

    [AvaloniaFact]
    public async Task Bell_for_guests_offers_sign_in()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var bell = new NotificationsViewModel(t.Services, () => { });
        await bell.LoadAsync();
        Assert.True(bell.IsGuest);
        Assert.False(bell.ShowList);
    }

    [AvaloniaFact]
    public async Task Live_notification_shows_a_clickable_desktop_notification_and_lands_in_the_bell()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        shell.NavigateTo("Nav_Notifications");
        var bell = Assert.IsType<NotificationsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => bell.ShowContent);
        object? opened = null;
        t.Services.Realtime.ActivationRequested += (_, target) => opened = target;

        var item = new NotificationItem("01N9", "forum.mention", "Kaito hat dich erwähnt", "Schau mal ins Squad-Forum", new Uri("https://rebelsofgaming.com/de/squads/enemy-territory"), "forum", null, DateTimeOffset.UtcNow);
        t.Services.Realtime.OnNotification(item);

        Assert.Equal(4, t.Services.Realtime.UnreadNotifications);
        Assert.Equal("01N9", bell.Items[0].Item.Id);
        var shown = Assert.Single(Notifier(t).Shown);
        Assert.Equal("Kaito hat dich erwähnt", shown.Title);
        Assert.Equal(t.Services.T("Linux_NotificationOpen"), shown.ActionLabel);

        Notifier(t).Click(1);
        var route = Assert.IsType<AppRoute>(opened);
        Assert.Equal(AppSection.Squads, route.Section);
        Assert.Equal("enemy-territory", route.Key);
    }

    [AvaloniaFact]
    public async Task No_desktop_notification_when_the_window_has_focus_or_the_setting_is_off()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var item = new NotificationItem("01N9", null, "Titel", null, null, "bell", null, DateTimeOffset.UtcNow);

        t.Services.Realtime.IsAppFocused = () => true;
        t.Services.Realtime.OnNotification(item);
        Assert.Empty(Notifier(t).Shown);

        t.Services.Realtime.IsAppFocused = () => false;
        await t.Services.UpdateSettingsAsync(s => s with { ToastNotifications = false });
        t.Services.Realtime.OnNotification(item);
        Assert.Empty(Notifier(t).Shown);
        Assert.Equal(5, t.Services.Realtime.UnreadNotifications); // Zähler zählt trotzdem
    }

    [AvaloniaFact]
    public async Task New_direct_messages_show_one_desktop_notification_each_and_open_the_chat()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var realtime = t.Services.Realtime;
        realtime.MessagesSeenUntil = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        object? opened = null;
        realtime.ActivationRequested += (_, target) => opened = target;

        await realtime.OnMessagesUnreadAsync(2);
        Assert.Equal(2, realtime.UnreadMessages);
        Assert.Equal(2, Notifier(t).Shown.Count); // Nyx (1:1) und Squad CS2 – ältere ungelesene nicht
        Assert.Equal("Nyx", Notifier(t).Shown[0].Title);
        Assert.Equal("Scrim heute 20 Uhr? Wir bräuchten noch einen fünften.", Notifier(t).Shown[0].Body);
        Assert.Equal("im.received", Notifier(t).Shown[0].Category);
        Assert.StartsWith("Kaito: ", Notifier(t).Shown[1].Body, StringComparison.Ordinal);

        await realtime.OnMessagesUnreadAsync(2); // gleiche Nachrichten → keine zweite Benachrichtigung
        Assert.Equal(2, Notifier(t).Shown.Count);

        Notifier(t).Click(1);
        Assert.Equal(new AppRoute(AppSection.Messages, "01CONV1"), opened);
        Assert.True(shell.Navigate((AppRoute)opened!));
        var messages = Assert.IsType<MessagesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => messages.Chat?.ShowContent == true);
        Assert.Equal("01CONV1", messages.Selected?.Id);
    }

    [AvaloniaTheory]
    [InlineData("rog://admin", "https://admin.rebelsofgaming.com/")]
    [InlineData("rog://wiki/Minecraft/Befehle", "https://wiki.rebelsofgaming.com/Minecraft/Befehle")]
    [InlineData("rog://open?url=https://rebelsofgaming.com/de/tools/zeitrechner", "https://rebelsofgaming.com/de/tools/zeitrechner")]
    public async Task Deep_links_without_a_native_page_open_the_exact_website_page(string link, string web)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri(link)));
        Assert.Equal(web, t.Browser.Opened[^1].ToString());
    }

    [AvaloniaTheory]
    [InlineData("rog://home", "Nav_Home")]
    [InlineData("rog://news", "Nav_News")]
    [InlineData("rog://squads", "Nav_Squads")]
    [InlineData("rog://members", "Nav_Members")]
    [InlineData("rog://calendar", "Nav_Calendar")]
    [InlineData("rog://servers", "Nav_Servers")]
    [InlineData("rog://info", "Nav_Info")]
    [InlineData("rog://messages", "Nav_Messages")]
    [InlineData("rog://notifications", "Nav_Notifications")]
    [InlineData("rog://account", "Nav_Account")]
    [InlineData("rog://settings", "Nav_Settings")]
    [InlineData("rog://friends", "Nav_Friends")]
    [InlineData("rog://matches", "Nav_Matches")]
    [InlineData("rog://bans", "Nav_Bans")]
    [InlineData("rog://forum", "Nav_Forum")]
    [InlineData("rog://wiki", "Nav_Wiki")]
    [InlineData("rog://tools", "Nav_Tools")]
    [InlineData("rog://media", "Nav_Media")]
    [InlineData("rog://uploads", "Nav_Uploads")]
    [InlineData("rog://search?q=et", "Nav_Search")]
    [InlineData("rog://votes", "Nav_Votes")]
    public async Task Every_section_link_opens_its_page(string link, string section)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri(link)));
        Assert.Equal(section, shell.CurrentItem?.Key);
        (shell.CurrentPage as ServersViewModel)?.Pause();
    }

    [AvaloniaFact]
    public async Task Links_wait_while_the_login_is_shown()
    {
        using var t = await TestServices.CreateAsync();
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.IsLoginVisible);
        Assert.True(shell.OpenDeepLink(new Uri("rog://squads/enemy-territory")));
        Assert.Null(shell.CurrentPage);

        await shell.Login!.BrowseAsGuestCommand.ExecuteAsync(null);
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent);
        Assert.Equal("enemy-territory", squads.Detail.Slug);
    }
}
