using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Friends;
using RoG.Core.Features.Members;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Mitglieder: Suche (verzögert), „nur online“, Team-Ansicht; Profil daneben bzw. darüber.</summary>
public sealed partial class MembersViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    private readonly MemberService _members;
    private string? _cursor;
    private int _searchVersion;

    public MembersViewModel(AppServices services)
        : base(services)
    {
        _members = new MemberService(services.Api.Client);
        Detail = new MemberDetailViewModel(services, _members);
        services.CurrentUserChanged += (_, _) => OnPropertyChanged(nameof(CanShowMap));
    }

    /// <summary>„Karte“: nur angemeldet (die API prüft Clan-Mitgliedschaft und Scope).</summary>
    public bool CanShowMap => Services.CurrentUser is not null;

    /// <summary>Mitgliederkarte öffnen – die Ansicht zeigt sie im eigenen Fenster.</summary>
    public event EventHandler<MemberMapViewModel>? MapRequested;

    [RelayCommand]
    private void OpenMap()
    {
        if (CanShowMap)
        {
            MapRequested?.Invoke(this, new MemberMapViewModel(Services));
        }
    }

    public ObservableCollection<MemberListItemViewModel> Items { get; } = [];

    public MemberDetailViewModel Detail { get; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool OnlineOnly { get; set; }

    /// <summary>true = Team-Seite (Gruppen mit Zuständigkeiten), false = alle Mitglieder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilters), nameof(AllMode))]
    public partial bool TeamMode { get; set; }

    /// <summary>Gegenstück zu <see cref="TeamMode"/> für den Umschalter „Alle“.</summary>
    public bool AllMode
    {
        get => !TeamMode;
        set => TeamMode = !value;
    }

    public bool ShowFilters => !TeamMode;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial MemberListItemViewModel? Selected { get; set; }

    /// <summary>Profil per Link geöffnet, ohne dass es in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var items = new List<MemberListItemViewModel>();
        if (TeamMode)
        {
            foreach (var group in await _members.GetTeamAsync(cancellationToken))
            {
                foreach (var m in group.Members)
                {
                    var subtitle = string.Join(" · ", new[] { group.Name, m.Responsibility }.Where(s => !string.IsNullOrEmpty(s)));
                    items.Add(new MemberListItemViewModel(m.Slug, m.Name, m.Avatar, subtitle, m.RoleLabel, m.RoleColor, null, Services));
                }
            }

            _cursor = null;
            HasMore = false;
        }
        else
        {
            var page = await _members.ListAsync(SearchText, OnlineOnly, null, cancellationToken);
            items.AddRange(page.Items.Select(Item));
            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }

        var selected = Selected?.Slug;
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        if (selected is not null && Items.FirstOrDefault(i => i.Slug == selected) is { } again)
        {
            Selected = again;
        }
    }

    /// <summary>Profil per Slug öffnen (Line-up, Suche, <c>rog://u/slug</c>) – direkt, auch wenn es nicht in der Liste steht.</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Items.FirstOrDefault(i => i.Slug == key) is { } item)
        {
            Selected = item;
            return;
        }

        Selected = null;
        DetailForced = true;
        _ = Detail.ShowAsync(key);
    }

    [RelayCommand]
    public void CloseDetail()
    {
        if (Detail.Report.Close())
        {
            return; // Escape schließt zuerst „Melden“
        }

        Selected = null;
        DetailForced = false;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || TeamMode || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            var page = await _members.ListAsync(SearchText, OnlineOnly, _cursor);
            foreach (var m in page.Items)
            {
                Items.Add(Item(m));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // „Mehr laden“ bleibt – erneut versuchen genügt
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private MemberListItemViewModel Item(MemberSummary m) =>
        new(m.Slug, m.Name, m.Avatar, m.Activity ?? string.Join(" · ", m.Squads), m.RankLabel, m.RankColor, m.Presence, Services);

    // Suche mit kurzer Verzögerung, damit nicht jeder Tastendruck eine Anfrage auslöst
    partial void OnSearchTextChanged(string? value) => _ = ReloadDelayedAsync();

    partial void OnOnlineOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnTeamModeChanged(bool value) => _ = LoadAsync();

    private async Task ReloadDelayedAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(350);
        if (version == _searchVersion)
        {
            await LoadAsync();
        }
    }

    partial void OnSelectedChanged(MemberListItemViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
            _ = Detail.ShowAsync(value.Slug);
        }
    }
}

/// <summary>Zeile: Avatar mit Status-Abzeichen, Name, Rang in Rangfarbe, Zeile darunter (Aktivität bzw. Squads).</summary>
public sealed partial class MemberListItemViewModel : ObservableObject
{
    public MemberListItemViewModel(string slug, string name, Uri? avatar, string? subtitle, string? rank, string? rankColor, Presence? presence, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Slug = slug;
        Name = name;
        Subtitle = subtitle ?? string.Empty;
        Rank = rank?.ToUpper(services.Culture) ?? string.Empty;
        RankBrush = UiResources.FromHex(rankColor, "RoGGoldBrush");
        Presence = presence;
        PresenceText = presence is { } p ? UiResources.PresenceLabel(p, services.Localizer) : null;
        AutomationName = string.Join(", ", new[] { name, rank, PresenceText }.Where(s => !string.IsNullOrEmpty(s)));
        _ = LoadAvatarAsync(services, avatar);
    }

    public string Slug { get; }

    public string Name { get; }

    public string Subtitle { get; }

    public bool HasSubtitle => Subtitle.Length > 0;

    public string Rank { get; }

    public bool HasRank => Rank.Length > 0;

    public IBrush RankBrush { get; }

    public Presence? Presence { get; }

    public string? PresenceText { get; }

    public string AutomationName { get; }

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    private async Task LoadAvatarAsync(AppServices services, Uri? avatar) => Avatar = await services.Images.LoadAsync(avatar, 96);
}

/// <summary>Profil: Banner, Avatar mit Status, Rang, Motto, Über mich, Fakten, Squads, Abzeichen, Spiele-Accounts.</summary>
public sealed partial class MemberDetailViewModel(AppServices services, MemberService members) : LoadableViewModel(services)
{
    private string? _slug;

    public string? Slug => _slug;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial Bitmap? Banner { get; set; }

    public bool HasBanner => Banner is not null;

    [ObservableProperty]
    public partial string? Rank { get; set; }

    [ObservableProperty]
    public partial IBrush? RankBrush { get; set; }

    [ObservableProperty]
    public partial Presence? Presence { get; set; }

    [ObservableProperty]
    public partial string? PresenceText { get; set; }

    /// <summary>„Im Voice · Discord: Talk“ – Status mit Aktivität.</summary>
    [ObservableProperty]
    public partial string? StatusLine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMotto))]
    public partial string? Motto { get; set; }

    public bool HasMotto => !string.IsNullOrEmpty(Motto);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBio))]
    public partial string? Bio { get; set; }

    public bool HasBio => !string.IsNullOrEmpty(Bio);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFacts))]
    public partial string? Facts { get; set; }

    public bool HasFacts => !string.IsNullOrEmpty(Facts);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSquads))]
    public partial string? Squads { get; set; }

    public bool HasSquads => !string.IsNullOrEmpty(Squads);

    public ObservableCollection<BadgeViewModel> Badges { get; } = [];

    public ObservableCollection<string> Platforms { get; } = [];

    [ObservableProperty]
    public partial bool HasBadges { get; set; }

    [ObservableProperty]
    public partial bool HasPlatforms { get; set; }

    [ObservableProperty]
    public partial string? CoinsText { get; set; }

    [ObservableProperty]
    public partial bool HasCoins { get; set; }

    protected override bool HasData => Name is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Name is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
        Report.Close();
        Name = null;
        Banner = null;
        Avatar = null;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var slug = _slug;
        var p = await members.GetAsync(slug, cancellationToken);
        if (slug != _slug)
        {
            return; // inzwischen anderes Profil gewählt
        }

        var s = p.Summary;
        _id = s.Id;
        var me = Services.CurrentUser?.Id ?? (await Services.Tokens.GetAsync(cancellationToken))?.UserId;
        CanMessage = me is not null && me != s.Id;
        CanBefriend = CanMessage && Services.CurrentUser?.HasScope(FriendsService.WriteScope) == true;
        CanReport = CanMessage && ReportFormViewModel.IsAllowed(Services);
        FriendFeedback.Close();
        Rank = s.RankLabel?.ToUpper(Services.Culture);
        RankBrush = UiResources.FromHex(s.RankColor, "RoGGoldBrush");
        Presence = s.Presence;
        PresenceText = UiResources.PresenceLabel(s.Presence, Services.Localizer);
        StatusLine = string.Join(" · ", new[] { PresenceText, s.Activity }.Where(x => !string.IsNullOrEmpty(x)));
        Motto = p.Motto;
        Bio = p.Bio;
        Squads = s.Squads.Count > 0 ? string.Join(" · ", s.Squads) : null;
        HasCoins = p.Coins is not null;
        CoinsText = p.Coins is { } coins ? Services.Format(StringKeys.CoinsBalance, UiResources.Number(coins.Balance, Services.Culture)) : null;

        var facts = new List<string>();
        if (p.Location is not null)
        {
            facts.Add(p.Location);
        }

        if (p.JoinedAt is { } joined)
        {
            facts.Add(Services.Format(StringKeys.AccountJoined, joined.ToLocalTime().ToString("d", Services.Culture)));
        }

        if (s.Presence == RoG.Core.Features.Members.Presence.Offline && p.LastSeenAt is { } seen)
        {
            facts.Add(Services.Format(StringKeys.MembersLastSeen, seen.ToLocalTime().ToString("g", Services.Culture)));
        }

        Facts = string.Join(" · ", facts);

        Badges.Clear();
        foreach (var badge in p.Badges)
        {
            Badges.Add(new BadgeViewModel(badge.Name, badge.Description, badge.Color));
        }

        HasBadges = Badges.Count > 0;

        Platforms.Clear();
        foreach (var (platform, handle) in p.Platforms)
        {
            Platforms.Add($"{platform}: {handle}");
        }

        HasPlatforms = Platforms.Count > 0;
        Name = s.Name;
        _ = LoadImagesAsync(slug, s.Avatar, p.Banner);
    }

    private async Task LoadImagesAsync(string slug, Uri? avatar, Uri? banner)
    {
        var avatarTask = Services.Images.LoadAsync(avatar, 192);
        var bannerTask = Services.Images.LoadAsync(banner, 1280);
        var a = await avatarTask;
        var b = await bannerTask;
        if (slug == _slug)
        {
            Avatar = a;
            Banner = b;
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() =>
        _slug is null ? Task.CompletedTask : Services.Browser.OpenAsync(Services.Environment.ProfilePage(Services.Language, _slug));

    private string? _id;

    /// <summary>Angemeldet und nicht das eigene Profil: „Nachricht senden“.</summary>
    [ObservableProperty]
    public partial bool CanMessage { get; set; }

    /// <summary>Freundschaftsanfrage senden (fremdes Profil, Scope <c>friends:write</c>; Grenzen/Sperren prüft der Server).</summary>
    [ObservableProperty]
    public partial bool CanBefriend { get; set; }

    /// <summary>„Melden“ wie Windows bei fremden Profilen (Scope <c>reports:write</c>).</summary>
    [ObservableProperty]
    public partial bool CanReport { get; set; }

    /// <summary>Profil melden – Formular unter den Aktionen.</summary>
    public ReportFormViewModel Report { get; } = new(services);

    [RelayCommand]
    private void ReportProfile()
    {
        if (_id is { } id)
        {
            Report.Open("profile", id, $"{Services.T(StringKeys.ModTypeProfile)} · {Name}");
        }
    }

    /// <summary>Rückmeldung zu „Als Freund hinzufügen“.</summary>
    public FeedbackViewModel FriendFeedback { get; } = new();

    [RelayCommand]
    private async Task BefriendAsync()
    {
        if (_id is not { } id || !CanBefriend)
        {
            return;
        }

        try
        {
            var result = await new FriendsService(Services.Api.Http).SendAsync(id);
            FriendFeedback.Show(Services.Format(result.IsAccepted ? StringKeys.FriendsNowFriends : StringKeys.FriendsRequestSent, result.User.Name), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            FriendFeedback.Show(
                Services.Localizer.GetError(ex.Problem.Code),
                ex.Problem.Code is "already_friends" or "already_requested" ? MessageTone.Info : MessageTone.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException or HttpRequestException)
        {
            FriendFeedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    /// <summary>1:1-Unterhaltung öffnen bzw. starten und in „Nachrichten“ zeigen.</summary>
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (_id is null)
        {
            return;
        }

        try
        {
            var conversation = await new RoG.Core.Features.Messages.MessageService(Services.Api.Client).StartDirectAsync(_id);
            Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Messages, conversation));
        }
        catch (RoG.Core.Api.ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            ErrorMessage = Services.Localizer.GetError(RoG.Core.Api.ApiProblem.Codes.Unknown);
        }
    }
}

/// <summary>Abzeichen als Chip mit farbiger Kante (Farbe aus der API).</summary>
public sealed class BadgeViewModel(string name, string? description, string color)
{
    public string Name => name;

    public string Description => description ?? name;

    public IBrush ChipBrush { get; } = UiResources.FromHex(color, "RoGGoldBrush");
}
