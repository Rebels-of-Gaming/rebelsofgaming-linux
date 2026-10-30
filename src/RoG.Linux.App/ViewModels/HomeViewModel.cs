using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Applications;
using RoG.Core.Features.Events;
using RoG.Core.Features.Home;
using RoG.Core.Features.News;
using RoG.Core.Features.Stats;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Startseite wie in der Windows-App: Begrüßung, Gast-Hinweis, offene Abstimmungen, Onboarding-Checkliste,
/// Live-Ticker aus <c>/v1/live</c> (Lade-/Fehler-/Leerzustand) und je drei neueste News und nächste Termine.
/// News, Termine, Abstimmungen und Onboarding-Schritte öffnen die Seite in der App.
/// </summary>
public sealed partial class HomeViewModel : LoadableViewModel
{
    private readonly LiveTickerService _ticker;
    private readonly NewsService _news;
    private readonly EventService _events;

    public HomeViewModel(AppServices services)
        : base(services)
    {
        _ticker = new LiveTickerService(services.Api.Http);
        _news = new NewsService(services.Api.Client, services.Environment.WebBaseUrl);
        _events = new EventService(services.Api.Client);
        services.CurrentUserChanged += (_, _) => UpdateGreeting();
        UpdateGreeting();
    }

    public ObservableCollection<LiveStatItem> Stats { get; } = [];

    public ObservableCollection<HomeLinkItem> LatestNews { get; } = [];

    public ObservableCollection<HomeLinkItem> NextEvents { get; } = [];

    public ObservableCollection<OnboardingStepViewModel> OnboardingSteps { get; } = [];

    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGuest { get; set; }

    /// <summary>Gast-Hinweis weggeklickt (bis zum nächsten Start).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGuestHint))]
    public partial bool GuestHintDismissed { get; set; }

    public bool ShowGuestHint => IsGuest && !GuestHintDismissed;

    [ObservableProperty]
    public partial bool HasNews { get; set; }

    [ObservableProperty]
    public partial bool HasEvents { get; set; }

    /// <summary>Kachel-Raster: News und Termine nebeneinander, wenn beide da sind.</summary>
    public bool HasTeasers => HasNews || HasEvents;

    [ObservableProperty]
    public partial string? VotesText { get; set; }

    [ObservableProperty]
    public partial bool HasVotes { get; set; }

    [ObservableProperty]
    public partial bool HasOnboarding { get; set; }

    [ObservableProperty]
    public partial double OnboardingProgress { get; set; }

    [ObservableProperty]
    public partial string OnboardingText { get; set; } = string.Empty;

    protected override bool HasData => Stats.Count > 0;

    partial void OnIsGuestChanged(bool value) => OnPropertyChanged(nameof(ShowGuestHint));

    partial void OnHasNewsChanged(bool value) => OnPropertyChanged(nameof(HasTeasers));

    partial void OnHasEventsChanged(bool value) => OnPropertyChanged(nameof(HasTeasers));

    private async void UpdateGreeting()
    {
        var token = Services.CurrentUser is null ? await Services.Tokens.GetAsync() : null;
        var name = Services.CurrentUser?.Name ?? token?.UserName;
        IsGuest = name is null;
        Greeting = name is null ? Services.T(StringKeys.HomeGreetingGuest) : Services.Format(StringKeys.HomeGreeting, name);
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        UpdateGreeting();
        // Beiwerk parallel: Fehler dort bleiben still und blockieren den Ticker nicht
        var teasers = LoadTeasersAsync(cancellationToken);
        try
        {
            var stats = await _ticker.GetAsync(cancellationToken);
            Stats.Clear();
            foreach (var stat in stats)
            {
                Stats.Add(new LiveStatItem(stat));
            }
        }
        finally
        {
            await teasers;
        }
    }

    private async Task LoadTeasersAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(LoadNewsAsync(cancellationToken), LoadEventsAsync(cancellationToken), LoadPersonalAsync(cancellationToken));
    }

    private async Task LoadNewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var news = await _news.ListAsync(null, cancellationToken);
            LatestNews.Clear();
            foreach (var n in news.Items.Take(3))
            {
                LatestNews.Add(new HomeLinkItem(n.Title, NewsText.Meta(n, Services), () => Services.Navigate(new AppRoute(AppSection.News, n.Slug))));
            }
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            // News-Kachel bleibt leer
        }

        HasNews = LatestNews.Count > 0;
    }

    private async Task LoadEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var events = await _events.ListAsync(DateTimeOffset.Now.Date, 30, cancellationToken: cancellationToken);
            NextEvents.Clear();
            foreach (var e in events.Items.Where(e => !e.Cancelled).Take(3))
            {
                var when = e.StartsAt is { } start ? EventTimes.Range(start, e.EndsAt, Services.Culture) : string.Empty;
                var id = e.Id;
                NextEvents.Add(new HomeLinkItem(e.Title, when, () => Services.Navigate(new AppRoute(AppSection.Calendar, id))));
            }
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            // Termin-Kachel bleibt leer
        }

        HasEvents = NextEvents.Count > 0;
    }

    /// <summary>Nur angemeldet: offene Abstimmungen (Stimmberechtigte) und Onboarding-Checkliste.</summary>
    private async Task LoadPersonalAsync(CancellationToken cancellationToken)
    {
        if (await Services.Tokens.GetAsync(cancellationToken) is null)
        {
            HasVotes = false;
            HasOnboarding = false;
            return;
        }

        try
        {
            var open = await new VoteService(Services.Api.Client, Services.Api.Http).OpenAsync(cancellationToken);
            VotesText = Services.Format(StringKeys.HomeOpenVotes, open.Count);
            HasVotes = open.Count > 0;
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            HasVotes = false;
        }

        try
        {
            var onboarding = await new StatsService(Services.Api.Client).OnboardingAsync(cancellationToken);
            OnboardingSteps.Clear();
            foreach (var step in onboarding.Steps)
            {
                OnboardingSteps.Add(new OnboardingStepViewModel(step, Services));
            }

            OnboardingProgress = onboarding.Progress;
            OnboardingText = Services.Format(StringKeys.HomeOnboardingProgress, onboarding.Progress);
            HasOnboarding = !onboarding.Complete && OnboardingSteps.Count > 0;
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            HasOnboarding = false;
        }
    }

    private static bool IsQuiet(Exception ex) =>
        ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    [RelayCommand]
    private void DismissGuestHint() => GuestHintDismissed = true;

    [RelayCommand]
    private void SignIn() => Services.Navigate(new AppRoute(AppSection.Account));

    /// <summary>Offene Abstimmungen in der App (Seite „Abstimmungen“).</summary>
    [RelayCommand]
    private void OpenVotes() => Services.Navigate(new AppRoute(AppSection.Votes));
}

/// <summary>Kachel des Live-Tickers: Wert, Beschriftung (übersetzt von der API), Symbol und Status-Farbe.</summary>
public sealed class LiveStatItem(LiveStat stat)
{
    public string Label { get; } = stat.Label.ToUpper(CultureInfo.CurrentCulture);

    public string Value => stat.Value;

    public string? Hint { get; } = stat.Hint;

    public bool HasHint => !string.IsNullOrEmpty(stat.Hint);

    /// <summary>Symbol zum Icon-Namen der Website (Lucide-Namen), wie die Glyphen der Windows-App.</summary>
    public Geometry? Icon { get; } = Resource<Geometry>(stat.Icon switch
    {
        "users" or "user" => "IconSquads",
        "activity" => "IconPulse",
        "headset" or "headphones" or "mic" or "volume" or "volume-2" => "IconHeadset",
        "server" or "servers" => "IconServers",
        "calendar" or "calendar-days" => "IconCalendar",
        "gamepad" or "gamepad-2" or "swords" => "IconGamepad",
        "twitch" or "video" or "tv" => "IconVideo",
        "trophy" or "award" => "IconTrophy",
        "message" or "message-circle" => "IconMessages",
        "download" => "IconDownload",
        _ => "IconChart",
    });

    /// <summary>Status-Farbe als Klasse (<c>tone-green</c> …) – Farben kommen aus dem Theme.</summary>
    public bool IsGreen { get; } = stat.Tone == "green";

    public bool IsGold { get; } = stat.Tone == "gold";

    public bool IsRed { get; } = stat.Tone == "red";

    public bool IsBlue { get; } = stat.Tone == "blue";

    public string AutomationName { get; } = $"{stat.Label}: {stat.Value}";

    private static T? Resource<T>(string key)
        where T : class =>
        Avalonia.Application.Current?.TryGetResource(key, null, out var value) == true ? value as T : null;
}

/// <summary>Anklickbarer Eintrag auf der Startseite (News, Termin).</summary>
public sealed partial class HomeLinkItem(string title, string meta, Func<bool> open)
{
    public string Title => title;

    public string Meta => meta;

    [RelayCommand]
    private void Open() => open();
}

/// <summary>Schritt der Onboarding-Checkliste; „Erledigen“ öffnet die Website-Seite dazu.</summary>
public sealed partial class OnboardingStepViewModel(OnboardingStep step, AppServices services)
{
    public string Label => step.Label;

    public bool Done => step.Done;

    public bool CanOpen => !step.Done && step.Url is not null;

    [RelayCommand]
    private async Task OpenAsync()
    {
        // Regeln, Termine … nativ in der App, sonst Website
        if (step.Url is { } url)
        {
            await services.OpenLinkAsync(url);
        }
    }
}
