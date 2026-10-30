using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

public class ViewModelTests
{
    [AvaloniaFact]
    public async Task Requests_identify_as_linux_app_with_token_and_language()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke, language: "en");
        var news = new NewsViewModel(t.Services);
        await news.LoadAsync();

        var request = t.Api.Requests.First(r => r.RequestUri!.AbsolutePath == "/v1/news");
        Assert.Equal("RoG-Linux", Assert.Single(request.Headers.UserAgent).Product?.Name);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("en", request.Headers.AcceptLanguage.First().Value);
        Assert.Equal("api.rebelsofgaming.com", request.RequestUri!.Host);
    }

    [AvaloniaFact]
    public async Task News_list_and_article_load()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var news = new NewsViewModel(t.Services);
        await news.LoadAsync();

        Assert.Equal(6, news.Items.Count);
        Assert.True(news.HasMore);
        Assert.True(news.ShowContent);
        Assert.Equal("TURNIERE", news.Items[0].Category);

        news.Selected = news.Items[0];
        await WaitUntil(() => news.Detail.ShowContent);
        Assert.Equal("Herbst-Scrims: Wir suchen Gegner für die neue Serie", news.Detail.Title);
        Assert.NotNull(news.Detail.Body);
        Assert.True(news.Detail.Body.Blocks.Count >= 5);
        Assert.Equal(2, news.Detail.Comments.Count);

        await news.Detail.OpenInBrowserCommand.ExecuteAsync(null);
        Assert.Equal("https://rebelsofgaming.com/de/news/scrim-serie-herbst-2026", Assert.Single(t.Browser.Opened).ToString());
    }

    [AvaloniaFact]
    public async Task Server_error_shows_translated_message()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.FailWith = System.Net.HttpStatusCode.InternalServerError;
        var news = new NewsViewModel(t.Services);
        await news.LoadAsync();

        Assert.True(news.HasError);
        Assert.False(news.ShowContent);
        Assert.Equal(t.Services.T("Error_server_error"), news.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Messages_need_sign_in_in_guest_mode()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var messages = new MessagesViewModel(t.Services, () => { });
        await messages.LoadAsync();

        Assert.True(messages.IsGuest);
        Assert.False(messages.ShowConversations);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/conversations", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Conversation_opens_read_only_chat_and_marks_read()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var messages = new MessagesViewModel(t.Services, () => { });
        await messages.LoadAsync();
        Assert.Null(messages.ErrorMessage);
        Assert.Equal(5, messages.Items.Count);
        Assert.True(messages.Items[0].Unread);

        messages.Selected = messages.Items[0];
        await WaitUntil(() => messages.Chat?.ShowContent == true);

        var chat = messages.Chat!;
        Assert.Equal(6, chat.Messages.Count);
        Assert.Equal("Hey! Hast du die Map-Rotation schon angepasst?", chat.Messages[0].Body);
        Assert.True(chat.Messages[^2].IsMine);
        Assert.True(chat.Messages[1].IsDeleted);
        Assert.Equal(t.Services.T("Messages_Deleted"), chat.Messages[1].Body);
        Assert.True(chat.HasOlder);
        Assert.False(messages.Items[0].Unread);
        await WaitUntil(() => t.Api.Requests.Any(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/read", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task Shell_starts_with_login_without_token_and_news_in_guest_mode()
    {
        using (var t = await TestServices.CreateAsync())
        {
            var shell = new ShellViewModel(t.Services);
            await shell.InitializeAsync();
            Assert.True(shell.IsLoginVisible);
        }

        using (var t = await TestServices.CreateAsync(guest: true))
        {
            var shell = new ShellViewModel(t.Services);
            await shell.InitializeAsync();
            Assert.False(shell.IsLoginVisible);
            Assert.Equal("Nav_Home", shell.CurrentItem?.Key);
            Assert.IsType<HomeViewModel>(shell.CurrentPage);
        }
    }

    [AvaloniaFact]
    public async Task Sign_out_revokes_token_and_shows_login()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.Equal("Wahke", t.Services.CurrentUser?.Name);

        await shell.SignOutAsync();

        Assert.True(shell.IsLoginVisible);
        Assert.Null(t.Services.CurrentUser);
        Assert.Empty(t.Secrets.Items);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/auth/logout");
    }

    [AvaloniaFact]
    public async Task Missing_keyring_is_shown_on_login()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke, keyring: false);
        Assert.False(t.Services.TokensPersistent);
        Assert.True(new LoginViewModel(t.Services).ShowKeyringWarning);
    }

    [AvaloniaFact]
    public async Task Placeholder_opens_the_website_page_in_app_language()
    {
        using var t = await TestServices.CreateAsync(guest: true, language: "fr");
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.NavigateTo("Nav_Tools"));
        var page = Assert.IsType<PlaceholderViewModel>(shell.CurrentPage);
        await page.OpenInBrowserCommand.ExecuteAsync(null);
        Assert.Equal("https://rebelsofgaming.com/fr/tools", Assert.Single(t.Browser.Opened).ToString());
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new TimeoutException("Bedingung nicht erfüllt");
            }

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
