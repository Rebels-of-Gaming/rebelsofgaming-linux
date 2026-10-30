using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Hauptfenster: Login (solange weder Token noch Gastmodus) oder Seitenleiste + Seite – Bereiche und Reihenfolge wie in
/// der Windows-App (NavigationView „Auto“): ab 840 px aufgeklappt, ab 600 px nur Symbole, darunter Kopfleiste mit
/// Menü-Knopf und Seitenleiste als Überlagerung.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public const double ExpandedThreshold = 840;
    public const double CompactThreshold = 600;

    private readonly AppServices _services;
    private readonly Dictionary<string, object> _pages = new(StringComparer.Ordinal);

    public ShellViewModel(AppServices services)
    {
        _services = services;
        MainItems =
        [
            Item("Nav_Home", "Shell_NavHome.Content", "IconHome", string.Empty, true),
            Item("Nav_News", "Shell_NavNews.Content", "IconNews", "news", true),
            Item("Nav_Squads", "Shell_NavSquads.Content", "IconSquads", "squads", true),
            Item("Nav_Messages", "Shell_NavMessages.Content", "IconMessages", "messages", true),
            Item("Nav_Friends", "Shell_NavFriends.Content", "IconFriends", "friends", false),
            Item("Nav_Calendar", "Shell_NavCalendar.Content", "IconCalendar", "events", true),
            Item("Nav_Servers", "Shell_NavServers.Content", "IconServers", "servers", true),
            Item("Nav_Forum", "Shell_NavForum.Content", "IconForum", "forum", true),
            Item("Nav_Wiki", "Shell_NavWiki.Content", "IconWiki", string.Empty, false),
            Item("Nav_Tools", "Shell_NavTools.Content", "IconTools", "tools", false),
            Item("Nav_Members", "Shell_NavMembers.Content", "IconMembers", "members", true),
            Item("Nav_Matches", "Shell_NavMatches.Content", "IconMatches", "matches", false),
            Item("Nav_Media", "Shell_NavMedia.Content", "IconMedia", "gallery", true),
            Item("Nav_Uploads", "Shell_NavUploads.Content", "IconUploads", "uploads", true),
            Item("Nav_Info", "Shell_NavInfo.Content", "IconInfo", "about", true),
        ];
        FooterItems =
        [
            Item("Nav_Notifications", "Shell_NavNotifications.Content", "IconBell", "notifications", true),
            Item("Nav_Account", "Shell_NavAccount.Content", "IconAccount", "settings/account", true),
            Item("Nav_Settings", "Linux_NavSettings", "IconSettings", "settings", true),
        ];
        SearchItem = Item("Nav_Search", "Linux_SearchTitle", "IconSearch", "search", true);

        // Bereiche ohne Eintrag in der Seitenleiste (geöffnet über Startseite, Links, andere Seiten)
        HiddenItems =
        [
            Item("Nav_Votes", "VotesPage_Title.Text", "IconVote", "votes", true),
            Item("Nav_Leaderboards", "LeaderboardsPage_Title.Text", "IconTrophy", "minecraft/leaderboards", false),
        ];
        Acceptance = new AcceptanceViewModel(services);
        _services.Unauthorized += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = OnUnauthorizedAsync());
        _services.Navigator = Navigate;
        _services.IsNative = route => s_sections.TryGetValue(route.Section, out var key) && AllItems.FirstOrDefault(i => i.Key == key)?.Implemented == true;

        // Echtzeit: Zähler an Glocke und Nachrichten, Live-Ereignisse an die offenen Seiten
        var realtime = _services.Realtime;
        realtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RealtimeService.UnreadNotifications))
            {
                SetBadge("Nav_Notifications", realtime.UnreadNotifications);
            }
            else if (e.PropertyName == nameof(RealtimeService.UnreadMessages))
            {
                SetBadge("Nav_Messages", realtime.UnreadMessages);
            }
        };
        realtime.NotificationReceived += (_, n) => (Page("Nav_Notifications") as NotificationsViewModel)?.Prepend(n);
        realtime.ServerStatusChanged += (_, s) => (Page("Nav_Servers") as ServersViewModel)?.Apply(s);
        realtime.LiveChanged += (_, _) =>
        {
            if (Page("Nav_Home") is HomeViewModel home && home.ShowContent)
            {
                _ = home.LoadAsync(silent: true);
            }

            if (CurrentPage is ServersViewModel servers)
            {
                servers.OnLiveChanged();
            }
        };
    }

    private object? Page(string key) => _pages.GetValueOrDefault(key);

    private void SetBadge(string key, int count)
    {
        if (AllItems.FirstOrDefault(i => i.Key == key) is { } item)
        {
            item.Badge = count;
        }
    }

    public ObservableCollection<NavItem> MainItems { get; }

    public ObservableCollection<NavItem> FooterItems { get; }

    /// <summary>Globale Suche (Strg+K, Lupe in der Seitenleiste) – eigene Seite, nicht in der Liste der Bereiche.</summary>
    public NavItem SearchItem { get; }

    /// <summary>Seiten ohne eigenen Eintrag in der Seitenleiste (Votes, Bestenlisten …).</summary>
    public IReadOnlyList<NavItem> HiddenItems { get; }

    /// <summary>Offene Pflicht-Bestätigungen (Regeln, Rechtstexte) – Hinweisbalken über der Seite.</summary>
    public AcceptanceViewModel Acceptance { get; }

    public string SearchText => _services.T("Shell_Search.PlaceholderText");

    public string MenuToggleText => _services.T("Linux_MenuToggle");

    public string OpenMenuText => _services.T("Linux_OpenMenu");

    public string CloseMenuText => _services.T("Linux_CloseMenu");

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    [ObservableProperty]
    public partial NavItem? CurrentItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShellVisible))]
    public partial LoginViewModel? Login { get; set; }

    public bool IsLoginVisible => Login is not null;

    public bool IsShellVisible => Login is null;

    /// <summary>Seitenleiste aufgeklappt (mit Beschriftung) statt nur Symbole – nur in der normalen Ansicht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneWidth), nameof(ShowLabels))]
    public partial bool IsPaneExpanded { get; set; } = true;

    /// <summary>Breite der festen Seitenleiste; in der schmalen Ansicht liegt sie als Überlagerung darüber.</summary>
    public double PaneWidth => IsPaneExpanded ? 240 : 60;

    public bool ShowLabels => IsPaneExpanded;

    /// <summary>Fensterbreite ≥ <see cref="ExpandedThreshold"/>? Vom Fenster gesetzt.</summary>
    [ObservableProperty]
    public partial bool IsWide { get; set; } = true;

    /// <summary>Fensterbreite &lt; <see cref="CompactThreshold"/>: Kopfleiste + Menü als Überlagerung.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSidePane))]
    public partial bool IsNarrow { get; set; }

    public bool ShowSidePane => !IsNarrow;

    /// <summary>Überlagerte Seitenleiste (schmale Ansicht) offen?</summary>
    [ObservableProperty]
    public partial bool IsOverlayOpen { get; set; }

    /// <summary>Titel der aktuellen Seite für die Kopfleiste der schmalen Ansicht.</summary>
    public string CurrentTitle => CurrentItem?.Label ?? string.Empty;

    partial void OnLoginChanged(LoginViewModel? value) => OnPropertyChanged(nameof(IsLoginVisible));

    partial void OnIsWideChanged(bool value) => IsPaneExpanded = value;

    partial void OnIsNarrowChanged(bool value)
    {
        if (!value)
        {
            IsOverlayOpen = false;
        }
    }

    partial void OnCurrentItemChanged(NavItem? value) => OnPropertyChanged(nameof(CurrentTitle));

    /// <summary>Fensterbreite (DIP) übernehmen: aufgeklappt / Symbole / schmal.</summary>
    public void UpdateWidth(double width)
    {
        IsWide = width >= ExpandedThreshold;
        IsNarrow = width < CompactThreshold;
    }

    /// <summary>Start: Token vorhanden oder Gastmodus → Shell, sonst Login. Danach Profil laden und Gerät melden.</summary>
    public async Task InitializeAsync()
    {
        var token = await _services.Tokens.GetAsync();
        if (token is null && !_services.Settings.GuestMode)
        {
            ShowLogin();
            return;
        }

        ShowShell();
        if (token is not null)
        {
            await LoadUserAsync();
            _ = _services.OutboxRunner.StartAsync();
        }

        await _services.Realtime.StartAsync(token?.UserId);
    }

    [RelayCommand]
    private void TogglePane()
    {
        if (IsNarrow)
        {
            IsOverlayOpen = !IsOverlayOpen;
        }
        else
        {
            IsPaneExpanded = !IsPaneExpanded;
        }
    }

    [RelayCommand]
    private void CloseOverlay() => IsOverlayOpen = false;

    [RelayCommand]
    public void Navigate(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (var i in AllItems)
        {
            i.IsSelected = ReferenceEquals(i, item);
        }

        // Verlauf für Alt+←/→ (nicht beim Blättern selbst und nicht doppelt)
        if (!_historyMove && (CurrentItem is null || CurrentItem.Key != item.Key))
        {
            if (_historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }

            _history.Add(item.Key);
            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
            }

            _historyIndex = _history.Count - 1;
        }

        if (CurrentPage is ServersViewModel servers && item.Key != "Nav_Servers")
        {
            servers.Pause();
        }

        CurrentItem = item;
        IsOverlayOpen = false;
        if (!_pages.TryGetValue(item.Key, out var page))
        {
            page = CreatePage(item);
            _pages[item.Key] = page;
        }

        CurrentPage = page;
        if (page is ServersViewModel resumed)
        {
            resumed.Resume();
        }

        if (page is LoadableViewModel loadable && !loadable.ShowContent && !loadable.IsLoading)
        {
            _ = loadable.LoadAsync();
        }

        _ = _services.UpdateSettingsAsync(s => s with { LastSection = item.Key });
    }

    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private bool _historyMove;

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex < _history.Count - 1;

    /// <summary>Alt+←: vorheriger Bereich.</summary>
    public bool GoBack() => MoveInHistory(-1);

    /// <summary>Alt+→: nächster Bereich.</summary>
    public bool GoForward() => MoveInHistory(1);

    private bool MoveInHistory(int step)
    {
        var target = _historyIndex + step;
        if (IsLoginVisible || target < 0 || target >= _history.Count)
        {
            return false;
        }

        _historyIndex = target;
        _historyMove = true;
        try
        {
            return NavigateTo(_history[target]);
        }
        finally
        {
            _historyMove = false;
        }
    }

    /// <summary>Bereich per Schlüssel öffnen (Tastenkürzel, Deep Links, Tests).</summary>
    public bool NavigateTo(string key)
    {
        var item = AllItems.FirstOrDefault(i => i.Key == key);
        Navigate(item);
        return item is not null;
    }

    /// <summary>Ziel aus dem Kern (Startseite, Deep Link) öffnen; nicht nativ vorhandene Bereiche → Platzhalter.</summary>
    public bool Navigate(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (IsLoginVisible)
        {
            return false;
        }

        // Bereiche ohne eigene Seite (Verwaltung, Bans, Bestenlisten, Karte) bzw. Einträge in Bereichen, die die
        // App noch nicht nativ hat (Forum-Thema, Wiki-Seite …): genau diese Seite der Website öffnen
        var web = WebFallback(route);
        if (!s_sections.TryGetValue(route.Section, out var key))
        {
            if (web is null)
            {
                return false;
            }

            _ = _services.Browser.OpenAsync(web);
            return true;
        }

        if (!NavigateTo(key))
        {
            return false;
        }

        if (web is not null && CurrentItem is { Implemented: false })
        {
            _ = _services.Browser.OpenAsync(web);
        }

        if (route.Key is { Length: > 0 } key2 && CurrentPage is IRoutablePage page)
        {
            page.Open(key2);
        }

        return true;
    }

    /// <summary>
    /// <c>rog://…</c> (Startargument, zweite Instanz, Benachrichtigung). Unbekannte Links werden ignoriert; solange der
    /// Login zu sehen ist, wartet der Link und öffnet sich nach Anmeldung bzw. „Ohne Anmeldung umsehen“.
    /// </summary>
    public bool OpenDeepLink(Uri link)
    {
        if (AppRoute.FromAppUri(link, _services.Environment) is not { } route)
        {
            return false;
        }

        if (IsLoginVisible)
        {
            _pendingRoute = route;
            return true;
        }

        return Navigate(route);
    }

    private AppRoute? _pendingRoute;

    private static readonly Dictionary<AppSection, string> s_sections = new()
    {
        [AppSection.Home] = "Nav_Home",
        [AppSection.News] = "Nav_News",
        [AppSection.Squads] = "Nav_Squads",
        [AppSection.Messages] = "Nav_Messages",
        [AppSection.Friends] = "Nav_Friends",
        [AppSection.Calendar] = "Nav_Calendar",
        [AppSection.Servers] = "Nav_Servers",
        [AppSection.Forum] = "Nav_Forum",
        [AppSection.Wiki] = "Nav_Wiki",
        [AppSection.Tools] = "Nav_Tools",
        [AppSection.Members] = "Nav_Members",
        [AppSection.Matches] = "Nav_Matches",
        [AppSection.Media] = "Nav_Media",
        [AppSection.Uploads] = "Nav_Uploads",
        [AppSection.Info] = "Nav_Info",
        [AppSection.Notifications] = "Nav_Notifications",
        [AppSection.Account] = "Nav_Account",
        [AppSection.Settings] = "Nav_Settings",
        [AppSection.Search] = "Nav_Search",
        [AppSection.Votes] = "Nav_Votes",
        [AppSection.Leaderboards] = "Nav_Leaderboards",
    };

    /// <summary>Adresse der Website für Ziele, die die App (noch) nicht nativ zeigt; <c>null</c> = nativ bzw. ohne Eintrag.</summary>
    private Uri? WebFallback(AppRoute route)
    {
        var env = _services.Environment;
        var lang = _services.Language;
        string Escaped(string k) => string.Join('/', k.Split('/').Select(Uri.EscapeDataString));
        return route.Section switch
        {
            AppSection.Admin => env.AdminBaseUrl ?? env.WebPage(lang, "admin"),
            AppSection.Bans => env.WebPage(lang, route.Key is { } b ? "bans/" + Escaped(b) : "bans"),
            AppSection.Leaderboards => env.WebPage(lang, "minecraft/leaderboards"),
            AppSection.MinecraftMap => env.WebPage(lang, "minecraft/karte"),
            AppSection.Wiki when route.Key is { } w => new Uri(env.WikiBaseUrl, Escaped(w)),
            AppSection.Tools when route.Key is { } k => env.WebPage(lang, "tools/" + Escaped(k)),
            AppSection.Matches when route.Key is { } m => env.WebPage(lang, "matches/" + Escaped(m)),
            _ => null,
        };
    }

    private IEnumerable<NavItem> AllItems => MainItems.Concat(FooterItems).Append(SearchItem).Concat(HiddenItems);

    private object CreatePage(NavItem item) => item.Key switch
    {
        "Nav_Home" => new HomeViewModel(_services),
        "Nav_Settings" => new SettingsViewModel(_services),
        "Nav_News" => new NewsViewModel(_services),
        "Nav_Squads" => new SquadsViewModel(_services),
        "Nav_Members" => new MembersViewModel(_services),
        "Nav_Servers" => new ServersViewModel(_services, ShowLogin),
        "Nav_Calendar" => new EventsViewModel(_services),
        "Nav_Info" => new InfoViewModel(_services, Acceptance),
        "Nav_Votes" => new VotesViewModel(_services, ShowLogin),
        "Nav_Search" => new SearchViewModel(_services),
        "Nav_Notifications" => new NotificationsViewModel(_services, ShowLogin),
        "Nav_Messages" => new MessagesViewModel(_services, ShowLogin),
        "Nav_Forum" => new ForumViewModel(_services),
        "Nav_Uploads" => new UploadsViewModel(_services, ShowLogin),
        "Nav_Media" => new MediaViewModel(_services),
        "Nav_Account" => new AccountViewModel(_services, SignOutAsync, ShowLogin),
        _ => new PlaceholderViewModel(_services, item),
    };

    private void ShowLogin()
    {
        Login?.Dispose();
        var login = new LoginViewModel(_services);
        login.Completed += async (_, _) =>
        {
            ShowShell();
            if (_pendingRoute is { } pending)
            {
                _pendingRoute = null;
                Navigate(pending);
            }

            var token = await _services.Tokens.GetAsync();
            if (token is not null)
            {
                await LoadUserAsync();
                _ = _services.OutboxRunner.StartAsync();
            }

            await _services.Realtime.StartAsync(token?.UserId);
        };
        Login = login;
    }

    private void ShowShell()
    {
        Login?.Dispose();
        Login = null;
        // Seiten neu aufbauen: Inhalte gehören zum Konto (bzw. Gastmodus)
        _pages.Clear();
        _history.Clear();
        _historyIndex = -1;
        CurrentItem = null;
        var last = _services.Settings.LastSection;
        var start = MainItems.Concat(FooterItems).FirstOrDefault(i => i.Key == last && i.Implemented) ?? MainItems.First(i => i.Key == "Nav_Home");
        Navigate(start);
    }

    private async Task LoadUserAsync()
    {
        try
        {
            _services.CurrentUser = await _services.Account.GetMeAsync();
            await _services.Devices.RegisterAsync(AppServices.AppVersion);
        }
        catch (Exception ex) when (ex is ApiProblemException or GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            // offline: Name aus dem Token-Speicher genügt; 401 behandelt OnUnauthorizedAsync
        }
    }

    /// <summary>Abmelden: Token auf dem Server widerrufen und lokal löschen, Caches leeren, Login zeigen.</summary>
    public async Task SignOutAsync()
    {
        _services.OutboxRunner.Stop();
        await _services.Realtime.StopAsync();
        await _services.Account.LogoutAsync();
        _services.CurrentUser = null;
        _services.ClearCaches();
        await _services.UpdateSettingsAsync(s => s with { GuestMode = false });
        _pages.Clear();
        CurrentPage = null;
        ShowLogin();
    }

    private async Task OnUnauthorizedAsync()
    {
        await _services.Realtime.StopAsync();
        await _services.Tokens.ClearAsync();
        _services.CurrentUser = null;
        _services.ClearCaches();
        if (!_services.Settings.GuestMode)
        {
            ShowLogin();
        }
    }

    private NavItem Item(string key, string labelKey, string iconKey, string webPath, bool implemented)
    {
        Geometry? icon = Application.Current?.TryGetResource(iconKey, null, out var resource) == true ? resource as Geometry : null;
        return new NavItem(key, _services.T(labelKey), icon, webPath, implemented);
    }
}
