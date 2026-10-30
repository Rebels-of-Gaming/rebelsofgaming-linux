using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Hauptfenster: Login (solange weder Token noch Gastmodus) oder Seitenleiste + Seite – Bereiche und Reihenfolge wie in
/// der Windows-App. Breit (≥ 840 px) aufgeklappt, sonst nur Symbole.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public const double ExpandedThreshold = 840;

    private readonly AppServices _services;
    private readonly Dictionary<string, object> _pages = new(StringComparer.Ordinal);

    public ShellViewModel(AppServices services)
    {
        _services = services;
        MainItems =
        [
            Item("Nav_Home", "Shell_NavHome.Content", "IconHome", string.Empty, false),
            Item("Nav_News", "Shell_NavNews.Content", "IconNews", "news", true),
            Item("Nav_Squads", "Shell_NavSquads.Content", "IconSquads", "squads", false),
            Item("Nav_Messages", "Shell_NavMessages.Content", "IconMessages", "messages", true),
            Item("Nav_Friends", "Shell_NavFriends.Content", "IconFriends", "friends", false),
            Item("Nav_Calendar", "Shell_NavCalendar.Content", "IconCalendar", "events", false),
            Item("Nav_Servers", "Shell_NavServers.Content", "IconServers", "servers", false),
            Item("Nav_Forum", "Shell_NavForum.Content", "IconForum", "forum", false),
            Item("Nav_Wiki", "Shell_NavWiki.Content", "IconWiki", string.Empty, false),
            Item("Nav_Tools", "Shell_NavTools.Content", "IconTools", "tools", false),
            Item("Nav_Members", "Shell_NavMembers.Content", "IconMembers", "members", false),
            Item("Nav_Matches", "Shell_NavMatches.Content", "IconMatches", "matches", false),
            Item("Nav_Media", "Shell_NavMedia.Content", "IconMedia", "gallery", false),
            Item("Nav_Uploads", "Shell_NavUploads.Content", "IconUploads", "uploads", false),
            Item("Nav_Info", "Shell_NavInfo.Content", "IconInfo", "about", false),
        ];
        FooterItems =
        [
            Item("Nav_Notifications", "Shell_NavNotifications.Content", "IconBell", "notifications", false),
            Item("Nav_Account", "Shell_NavAccount.Content", "IconAccount", "settings/account", true),
        ];
        _services.Unauthorized += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = OnUnauthorizedAsync());
    }

    public ObservableCollection<NavItem> MainItems { get; }

    public ObservableCollection<NavItem> FooterItems { get; }

    public string MenuToggleText => _services.T("Linux_MenuToggle");

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    [ObservableProperty]
    public partial NavItem? CurrentItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShellVisible))]
    public partial LoginViewModel? Login { get; set; }

    public bool IsLoginVisible => Login is not null;

    public bool IsShellVisible => Login is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneWidth))]
    public partial bool IsPaneExpanded { get; set; } = true;

    /// <summary>Seitenleiste: aufgeklappt mit Beschriftung, sonst nur Symbole.</summary>
    public double PaneWidth => IsPaneExpanded ? 248 : 64;

    /// <summary>Fensterbreite ≥ <see cref="ExpandedThreshold"/>? Vom Fenster gesetzt.</summary>
    [ObservableProperty]
    public partial bool IsWide { get; set; } = true;

    partial void OnLoginChanged(LoginViewModel? value) => OnPropertyChanged(nameof(IsLoginVisible));

    partial void OnIsWideChanged(bool value) => IsPaneExpanded = value;

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
        }
    }

    [RelayCommand]
    private void TogglePane() => IsPaneExpanded = !IsPaneExpanded;

    [RelayCommand]
    public void Navigate(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (var i in MainItems.Concat(FooterItems))
        {
            i.IsSelected = ReferenceEquals(i, item);
        }

        CurrentItem = item;
        if (!_pages.TryGetValue(item.Key, out var page))
        {
            page = CreatePage(item);
            _pages[item.Key] = page;
        }

        CurrentPage = page;
        if (page is LoadableViewModel loadable && !loadable.ShowContent && !loadable.IsLoading)
        {
            _ = loadable.LoadAsync();
        }

        _ = _services.UpdateSettingsAsync(s => s with { LastSection = item.Key });
    }

    /// <summary>Bereich per Schlüssel öffnen (Tastenkürzel, Deep Links, Tests).</summary>
    public bool NavigateTo(string key)
    {
        var item = MainItems.Concat(FooterItems).FirstOrDefault(i => i.Key == key);
        Navigate(item);
        return item is not null;
    }

    private object CreatePage(NavItem item) => item.Key switch
    {
        "Nav_News" => new NewsViewModel(_services),
        "Nav_Messages" => new MessagesViewModel(_services, ShowLogin),
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
            if (await _services.Tokens.GetAsync() is not null)
            {
                await LoadUserAsync();
            }
        };
        Login = login;
    }

    private void ShowShell()
    {
        Login?.Dispose();
        Login = null;
        // Seiten neu aufbauen: Inhalte gehören zum Konto (bzw. Gastmodus)
        _pages.Clear();
        var last = _services.Settings.LastSection;
        var start = MainItems.Concat(FooterItems).FirstOrDefault(i => i.Key == last && i.Implemented) ?? MainItems.First(i => i.Key == "Nav_News");
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
