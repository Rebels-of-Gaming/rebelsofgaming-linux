using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Bildschirmfotos für AppStream/Flathub (Stufe 10): 1280×800 (16:10), Deutsch und Englisch, dunkles Design →
/// <c>artifacts/screenshots/store_&lt;szene&gt;_&lt;sprache&gt;_1280x800.png</c>. Ausgewählte Bilder liegen fertig unter
/// <c>packaging/screenshots/</c> (siehe packaging/README.md) – nach Änderungen an der Oberfläche neu erzeugen und kopieren.
/// </summary>
[Trait("Category", "Screenshots")]
public class StoreScreenshotTests
{
    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task Home(string language) =>
        await ScreenshotTests.Shoot($"store_home_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            var home = Assert.IsType<HomeViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => home.ShowContent && home.HasTeasers);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task News(string language) =>
        await ScreenshotTests.Shoot($"store_news_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            shell.NavigateTo("Nav_News");
            var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => news.ShowContent);
            news.Selected = news.Items[0];
            await ViewModelTests.WaitUntil(() => news.Detail.ShowContent && news.Detail.HasCover);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task Messages(string language) =>
        await ScreenshotTests.Shoot($"store_messages_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            shell.NavigateTo("Nav_Messages");
            var messages = Assert.IsType<MessagesViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => messages.ShowContent);
            messages.Selected = messages.Items[0];
            await ViewModelTests.WaitUntil(() => messages.Chat?.ShowContent == true);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task Servers(string language) =>
        await ScreenshotTests.Shoot($"store_servers_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            shell.NavigateTo("Nav_Servers");
            var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => servers.ShowContent);
            await ScreenshotTests.Settle(400);
            servers.Pause();
        });

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task Calendar(string language) =>
        await ScreenshotTests.Shoot($"store_calendar_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            Assert.True(shell.OpenDeepLink(new Uri("rog://events/01EV1")));
            var events = Assert.IsType<EventsViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => events.Detail.ShowContent && events.Detail.HasParticipants);
            events.MonthMode = true;
            await ViewModelTests.WaitUntil(() => events.MonthDays.Count == 42);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task Forum(string language) =>
        await ScreenshotTests.Shoot($"store_forum_{language}_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke, language: language), async shell =>
        {
            Assert.True(shell.OpenDeepLink(new Uri("rog://forum/allgemein")));
            var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => forum.Threads.Count > 0);
            forum.SelectedThread = forum.Threads[0];
            await ViewModelTests.WaitUntil(() => forum.Thread.ShowContent);
            await ScreenshotTests.Settle(400);
        });
}
