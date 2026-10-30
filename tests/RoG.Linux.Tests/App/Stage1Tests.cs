using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using RoG.Core.Navigation;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 1: Startseite, Einstellungen, Shell-Breiten, Navigation per Route und Deep Link.</summary>
public class Stage1Tests
{
    [AvaloniaFact]
    public async Task Home_shows_live_ticker_news_events_and_onboarding_when_signed_in()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var home = new HomeViewModel(t.Services);
        await home.LoadAsync();

        Assert.True(home.ShowContent);
        Assert.Equal(6, home.Stats.Count);
        Assert.Equal("MITGLIEDER ONLINE", home.Stats[0].Label);
        Assert.True(home.Stats[0].IsGreen);
        Assert.Equal("Di., 20:00", home.Stats[4].Value);
        Assert.NotNull(home.Stats[5].Icon); // unbekanntes Symbol → Ersatz
        Assert.Equal(3, home.LatestNews.Count);
        Assert.Equal(3, home.NextEvents.Count);
        Assert.True(home.HasTeasers);
        Assert.True(home.HasOnboarding);
        Assert.Equal(5, home.OnboardingSteps.Count);
        Assert.False(home.HasVotes);
        Assert.False(home.IsGuest);
        Assert.Contains("Wahke", home.Greeting, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Home_in_guest_mode_shows_hint_and_skips_personal_requests()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var home = new HomeViewModel(t.Services);
        await home.LoadAsync();

        Assert.True(home.IsGuest);
        Assert.True(home.ShowGuestHint);
        home.DismissGuestHintCommand.Execute(null);
        Assert.False(home.ShowGuestHint);
        Assert.False(home.HasOnboarding);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath is "/v1/me/onboarding" or "/v1/votes");
        Assert.Equal(t.Services.T("Home_GreetingGuest"), home.Greeting);
    }

    [AvaloniaFact]
    public async Task Home_error_keeps_retry_and_teasers_stay_quiet()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.FailWith = System.Net.HttpStatusCode.ServiceUnavailable;
        var home = new HomeViewModel(t.Services);
        await home.LoadAsync();

        Assert.True(home.HasError);
        Assert.False(home.HasTeasers);

        t.Api.FailWith = null;
        await home.RefreshCommand.ExecuteAsync(null);
        Assert.False(home.HasError);
        Assert.True(home.ShowContent);
    }

    [AvaloniaFact]
    public async Task Home_news_item_opens_the_article_in_the_news_page()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        var home = Assert.IsType<HomeViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => home.LatestNews.Count == 3);

        home.LatestNews[1].OpenCommand.Execute(null);

        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.Selected?.Slug == "neuer-et-server");
        Assert.True(news.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Deep_links_open_sections_and_unknown_links_are_ignored()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://settings")));
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Assert.True(shell.OpenDeepLink(new Uri("rog://news/scrim-serie-herbst-2026")));
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
        Assert.True(shell.OpenDeepLink(new Uri("rog://servers")));
        Assert.IsType<PlaceholderViewModel>(shell.CurrentPage);

        Assert.False(shell.OpenDeepLink(new Uri("rog://auth/callback?code=x")));
        Assert.False(shell.OpenDeepLink(new Uri("rog://admin")));
        Assert.Equal("Nav_Servers", shell.CurrentItem?.Key);
    }

    [AvaloniaFact]
    public async Task Deep_link_to_an_older_article_opens_it_directly()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        shell.NavigateTo("Nav_News");
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.ShowContent);

        Assert.True(shell.Navigate(new AppRoute(AppSection.News, "ganz-alter-artikel")));
        Assert.True(news.IsDetailOpen);
        Assert.Null(news.Selected);
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
        news.CloseDetailCommand.Execute(null);
        Assert.False(news.IsDetailOpen);
    }

    [AvaloniaFact]
    public async Task Shell_width_modes_follow_the_windows_navigation_view()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        shell.UpdateWidth(1280);
        Assert.True(shell.ShowLabels);
        Assert.True(shell.ShowSidePane);
        Assert.Equal(240, shell.PaneWidth);

        shell.UpdateWidth(700);
        Assert.False(shell.ShowLabels);
        Assert.Equal(60, shell.PaneWidth);
        Assert.True(shell.ShowSidePane);

        shell.UpdateWidth(400);
        Assert.True(shell.IsNarrow);
        Assert.False(shell.ShowSidePane);
        shell.TogglePaneCommand.Execute(null);
        Assert.True(shell.IsOverlayOpen);
        shell.NavigateTo("Nav_Settings");
        Assert.False(shell.IsOverlayOpen);
        Assert.Equal(t.Services.T("Linux_NavSettings"), shell.CurrentTitle);

        shell.TogglePaneCommand.Execute(null);
        shell.UpdateWidth(900);
        Assert.False(shell.IsOverlayOpen);
    }

    [AvaloniaFact]
    public async Task Settings_change_theme_immediately_and_language_after_restart()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services); // setzt das Design aus den Einstellungen
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        var settings = new SettingsViewModel(t.Services);

        settings.SelectedTheme = settings.Themes.Single(o => o.Value == LinuxSettings.ThemeLight);
        await ViewModelTests.WaitUntil(() => Avalonia.Application.Current!.RequestedThemeVariant == ThemeVariant.Light);
        Assert.Equal(LinuxSettings.ThemeLight, new LinuxSettingsStore(Path.Combine(t.Folder, "linux.json")).Load().Theme);

        settings.SelectedTheme = settings.Themes.Single(o => o.Value == LinuxSettings.ThemeSystem);
        await ViewModelTests.WaitUntil(() => Avalonia.Application.Current!.RequestedThemeVariant == ThemeVariant.Default);

        Assert.False(settings.RestartRequired);
        settings.SelectedLanguage = settings.Languages.Single(o => o.Value == "en");
        Assert.True(settings.RestartRequired);
        await ViewModelTests.WaitUntil(() => t.Services.Settings.Language == "en");

        settings.Notifications = false;
        await ViewModelTests.WaitUntil(() => !t.Services.Settings.ToastNotifications);

        await ViewModelTests.WaitUntil(() => settings.PlatformText.Contains("0.53.0", StringComparison.Ordinal));
        Assert.Contains("production", settings.EnvironmentText, StringComparison.Ordinal);
        await settings.TestNotificationCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_TestNotificationSent"), settings.NotificationFeedback);

        await settings.OpenPrivacyCommand.ExecuteAsync(null);
        Assert.Equal("https://rebelsofgaming.com/de/privacy", Assert.Single(t.Browser.Opened).ToString());

        settings.SelectedTheme = settings.Themes.Single(o => o.Value == LinuxSettings.ThemeDark);
        await ViewModelTests.WaitUntil(() => Avalonia.Application.Current!.RequestedThemeVariant == ThemeVariant.Dark);
        window.Close();
    }
}
