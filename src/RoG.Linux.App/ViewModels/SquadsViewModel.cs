using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Events;
using RoG.Core.Features.Games;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Squads: Übersicht (oder Spiele) und Squad-Seite mit Reitern Vorstellung, Line-up, Server und – nur wenn erlaubt – Intern.</summary>
public sealed partial class SquadsViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    private readonly SquadService _squads;

    public SquadsViewModel(AppServices services)
        : base(services)
    {
        _squads = new SquadService(services.Api.Client);
        Detail = new SquadDetailViewModel(services, _squads);
        Games = new GamesViewModel(services);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShowContent))
            {
                OnPropertyChanged(nameof(ShowSquads));
            }
        };
    }

    public ObservableCollection<SquadItemViewModel> Items { get; } = [];

    public SquadDetailViewModel Detail { get; }

    /// <summary>Umschalter „Spiele“ statt der Squad-Liste.</summary>
    public GamesViewModel Games { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSquads), nameof(SquadsMode), nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool GamesMode { get; set; }

    /// <summary>Gegenstück zu <see cref="GamesMode"/> für den Umschalter „Squads“.</summary>
    public bool SquadsMode
    {
        get => !GamesMode;
        set => GamesMode = !value;
    }

    public bool ShowSquads => ShowContent && !GamesMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial SquadItemViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => !GamesMode && (Selected is not null || DetailForced);

    public bool ShowSelectHint => !GamesMode && !IsDetailOpen;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var squads = await _squads.ListAsync(cancellationToken);
        var selected = Selected?.Slug;
        Items.Clear();
        foreach (var squad in squads)
        {
            Items.Add(new SquadItemViewModel(squad, Services));
        }

        if (selected is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Slug == selected);
        }

        if (GamesMode)
        {
            await Games.LoadAsync();
        }
    }

    partial void OnGamesModeChanged(bool value)
    {
        if (value && !Games.ShowContent && !Games.IsLoading)
        {
            _ = Games.LoadAsync();
        }
    }

    /// <summary>Squad per Slug öffnen (Suche, <c>rog://squads/slug</c>, Mitglieder-Profil).</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        GamesMode = false;
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
        // Escape/Zurück schließt zuerst das Bewerbungsformular
        if (Detail.IsApplying)
        {
            Detail.Application?.Cancel();
            return;
        }

        Selected = null;
        DetailForced = false;
    }

    partial void OnSelectedChanged(SquadItemViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
            _ = Detail.ShowAsync(value.Slug);
        }
    }

    internal static string RecruitingText(Recruiting recruiting, ILocalizer localizer) => recruiting switch
    {
        Recruiting.Open => localizer.GetString(StringKeys.SquadsRecruitingOpen),
        Recruiting.Waitlist => localizer.GetString(StringKeys.SquadsRecruitingWaitlist),
        Recruiting.Full => localizer.GetString(StringKeys.SquadsRecruitingFull),
        _ => string.Empty,
    };
}

/// <summary>Zeile der Squad-Liste: Logo, Name, Spiel · Clan-Tag · Motto, Rekrutierung als Chip.</summary>
public sealed partial class SquadItemViewModel : ObservableObject
{
    public SquadItemViewModel(SquadSummary squad, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(services);
        Slug = squad.Slug;
        Name = squad.Name;
        Subtitle = string.Join(" · ", new[] { squad.GameName, squad.ClanTagText, squad.Tagline }.Where(s => !string.IsNullOrEmpty(s)));
        RecruitingText = SquadsViewModel.RecruitingText(squad.Recruiting, services.Localizer);
        IsOpen = squad.Recruiting == Recruiting.Open;
        IsWaitlist = squad.Recruiting == Recruiting.Waitlist;
        _ = LoadLogoAsync(services, squad.Logo);
    }

    public string Slug { get; }

    public string Name { get; }

    public string Subtitle { get; }

    public string RecruitingText { get; }

    public bool HasRecruiting => RecruitingText.Length > 0;

    public bool IsOpen { get; }

    public bool IsWaitlist { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoLogo))]
    public partial Bitmap? Logo { get; set; }

    public bool HasNoLogo => Logo is null;

    private async Task LoadLogoAsync(AppServices services, Uri? logo) => Logo = await services.Images.LoadAsync(logo, 96);
}

/// <summary>Reiter der Squad-Seite.</summary>
public enum SquadTab
{
    About,
    Lineup,
    Servers,
    Internal,
}

public sealed partial class SquadDetailViewModel(AppServices services, SquadService squads) : LoadableViewModel(services)
{
    private readonly ServerService _servers = new(services.Api.Client);
    private readonly SquadInternalService _internal = new(services.Api.Client);
    private string? _slug;
    private string? _id;
    private bool _internalLoaded;

    public string? Slug => _slug;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string? Subtitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeader))]
    public partial Bitmap? Header { get; set; }

    public bool HasHeader => Header is not null;

    [ObservableProperty]
    public partial Bitmap? Logo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecruiting))]
    public partial string? RecruitingText { get; set; }

    public bool HasRecruiting => !string.IsNullOrEmpty(RecruitingText);

    [ObservableProperty]
    public partial bool IsRecruitingOpen { get; set; }

    [ObservableProperty]
    public partial bool IsRecruitingWaitlist { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string? Description { get; set; }

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrainingTimes))]
    public partial string? TrainingTimes { get; set; }

    public bool HasTrainingTimes => !string.IsNullOrEmpty(TrainingTimes);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClanInfo))]
    public partial string? ClanInfo { get; set; }

    public bool HasClanInfo => !string.IsNullOrEmpty(ClanInfo);

    [ObservableProperty]
    public partial string? MembersHeader { get; set; }

    public ObservableCollection<SquadMemberViewModel> Members { get; } = [];

    public ObservableCollection<LinkViewModel> Links { get; } = [];

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    public partial bool HasMembers { get; set; }

    [ObservableProperty]
    public partial bool HasLinks { get; set; }

    [ObservableProperty]
    public partial bool HasServers { get; set; }

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Reiter „Intern“ nur für Member des Squads und Verwalter (<c>squads.manage</c>) mit Token-Scope <c>squads:read</c>.</summary>
    [ObservableProperty]
    public partial bool CanSeeInternal { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboutTab), nameof(IsLineupTab), nameof(IsServersTab), nameof(IsInternalTab))]
    public partial SquadTab Tab { get; set; }

    public bool IsAboutTab
    {
        get => Tab == SquadTab.About;
        set => SetTab(SquadTab.About, value);
    }

    public bool IsLineupTab
    {
        get => Tab == SquadTab.Lineup;
        set => SetTab(SquadTab.Lineup, value);
    }

    public bool IsServersTab
    {
        get => Tab == SquadTab.Servers;
        set => SetTab(SquadTab.Servers, value);
    }

    public bool IsInternalTab
    {
        get => Tab == SquadTab.Internal;
        set => SetTab(SquadTab.Internal, value);
    }

    public SquadInternalViewModel Internal { get; } = new(services);

    /// <summary>Bewerben möglich: angemeldet, der Squad sucht (offen oder Warteliste) und man ist noch nicht drin (wie Windows).</summary>
    [ObservableProperty]
    public partial bool CanApply { get; set; }

    /// <summary>Offenes Bewerbungsformular (ersetzt die Squad-Seite, bis abgeschickt oder abgebrochen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApplying), nameof(ShowSquad))]
    public partial ApplicationFormViewModel? Application { get; set; }

    public bool IsApplying => Application is not null;

    public bool ShowSquad => !IsApplying;

    [RelayCommand]
    private void Apply()
    {
        if (_slug is null || !CanApply)
        {
            return;
        }

        Feedback.Close();
        Application = new ApplicationFormViewModel(Services, _slug, Name, OnApplicationClosed);
        _ = Application.LoadAsync();
    }

    private void OnApplicationClosed(bool submitted)
    {
        Application = null;
        if (submitted)
        {
            CanApply = false;
            Feedback.Show(Services.T("Linux_ApplySent"), MessageTone.Success);
        }
    }

    protected override bool HasData => Name is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Name is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
        _internalLoaded = false;
        Name = null;
        Header = null;
        Logo = null;
        Application = null;
        CanApply = false;
        Tab = SquadTab.About;
        Feedback.Close();
        return LoadAsync();
    }

    private void SetTab(SquadTab tab, bool value)
    {
        if (!value)
        {
            // Der aktive Reiter bleibt aktiv, wenn man ihn erneut anklickt
            OnPropertyChanged(tab switch
            {
                SquadTab.About => nameof(IsAboutTab),
                SquadTab.Lineup => nameof(IsLineupTab),
                SquadTab.Servers => nameof(IsServersTab),
                _ => nameof(IsInternalTab),
            });
            return;
        }

        Tab = tab;
    }

    partial void OnTabChanged(SquadTab value)
    {
        if (value == SquadTab.Internal && !_internalLoaded && _slug is { } slug)
        {
            _internalLoaded = true;
            _ = Internal.ShowAsync(slug);
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var slug = _slug;
        var detailTask = squads.GetAsync(slug, cancellationToken);
        var membersTask = squads.GetMembersAsync(slug, cancellationToken);
        var serversTask = _servers.GetAllAsync(slug, cancellationToken);
        var detail = await detailTask;
        if (slug != _slug)
        {
            return;
        }

        var s = detail.Summary;
        _id = s.Id;
        Subtitle = string.Join(" · ", new[] { s.GameName, s.LeaderName is null ? null : Services.Format(StringKeys.SquadsLeader, s.LeaderName) }.Where(x => !string.IsNullOrEmpty(x)));
        RecruitingText = SquadsViewModel.RecruitingText(s.Recruiting, Services.Localizer);
        IsRecruitingOpen = s.Recruiting == Recruiting.Open;
        IsRecruitingWaitlist = s.Recruiting == Recruiting.Waitlist;
        Description = detail.Description;
        TrainingTimes = detail.TrainingTimes;
        ClanInfo = detail.ClanName is null && s.ClanTagText is null
            ? null
            : string.Join(" · ", new[] { s.ClanTagText, detail.ClanName, detail.ClanLevel is { } level ? Services.Format(StringKeys.SquadsClanLevel, level) : null, detail.ClanRequirements }.Where(x => !string.IsNullOrEmpty(x)));

        Links.Clear();
        foreach (var link in detail.Links)
        {
            Links.Add(new LinkViewModel(link.Label, link.Url, link.Code, Services, Feedback));
        }

        HasLinks = Links.Count > 0;

        Members.Clear();
        try
        {
            foreach (var m in await membersTask)
            {
                var role = m.Substitute ? Services.Format(StringKeys.SquadsSubstitute, m.RoleLabel ?? string.Empty).Trim() : m.RoleLabel;
                Members.Add(new SquadMemberViewModel(m.Name, m.Slug, m.Avatar, role, m.RoleColor, Services));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Line-up ist Beiwerk – die Squad-Seite bleibt sichtbar
        }

        HasMembers = Members.Count > 0;
        CanApply = s.Recruiting is Recruiting.Open or Recruiting.Waitlist
            && await Services.Tokens.GetAsync(cancellationToken) is not null
            && !(Services.CurrentUser is { } me && Members.Any(m => m.Slug == me.Slug));
        MembersHeader = Services.Format(StringKeys.SquadsMembers, Members.Count);

        Servers.Clear();
        try
        {
            foreach (var server in await serversTask)
            {
                Servers.Add(new ServerItemViewModel(server, Services, Feedback));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // ohne Server weiter
        }

        HasServers = Servers.Count > 0;
        CanSeeInternal = await CanSeeInternalAsync(s.Id, cancellationToken);
        Name = s.Name;
        _ = LoadImagesAsync(slug, s.Logo, s.Header);
    }

    private async Task<bool> CanSeeInternalAsync(string squadId, CancellationToken cancellationToken)
    {
        if (await Services.Tokens.GetAsync(cancellationToken) is null || Services.CurrentUser is not { } me || !me.HasScope("squads:read"))
        {
            return false;
        }

        return me.Can(SquadInternalService.ManagePermission, "squad:" + squadId) || Members.Any(m => m.Slug == me.Slug);
    }

    private async Task LoadImagesAsync(string slug, Uri? logo, Uri? header)
    {
        var logoTask = Services.Images.LoadAsync(logo, 128);
        var headerTask = Services.Images.LoadAsync(header, 1280);
        var l = await logoTask;
        var h = await headerTask;
        if (slug == _slug)
        {
            Logo = l;
            Header = h;
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() =>
        _slug is null ? Task.CompletedTask : Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "squads/" + Uri.EscapeDataString(_slug)));
}

/// <summary>Person im Line-up: Avatar, Name, Rolle in Rollenfarbe; Klick öffnet das Profil.</summary>
public sealed partial class SquadMemberViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SquadMemberViewModel(string name, string slug, Uri? avatar, string? role, string? roleColor, AppServices services)
    {
        _services = services;
        Name = name;
        Slug = slug;
        Role = role ?? string.Empty;
        RoleBrush = UiResources.FromHex(roleColor, "RoGMutedBrush");
        _ = LoadAvatarAsync(avatar);
    }

    public string Name { get; }

    public string Slug { get; }

    public string Role { get; }

    public bool HasRole => Role.Length > 0;

    public IBrush RoleBrush { get; }

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    private async Task LoadAvatarAsync(Uri? avatar) => Avatar = await _services.Images.LoadAsync(avatar, 96);

    [RelayCommand]
    private void Open() => _services.Navigate(new AppRoute(AppSection.Members, Slug));
}

/// <summary>Einladungs-, Ref- oder Store-Link: öffnen und Code bzw. Adresse kopieren.</summary>
public sealed partial class LinkViewModel(string label, Uri url, string? code, AppServices services, FeedbackViewModel feedback)
{
    public string Label => label;

    public string Url => url.ToString();

    public string? Code => code;

    public bool HasCode => !string.IsNullOrEmpty(code);

    [RelayCommand]
    private Task OpenAsync() => services.Browser.OpenAsync(url);

    [RelayCommand]
    private void Copy()
    {
        var ok = services.ServerConnector.Copy(code ?? url.ToString());
        feedback.Show(services.T(ok ? "Common_LinkCopied" : "Common_CopyFailed"), ok ? MessageTone.Success : MessageTone.Error);
    }
}

/// <summary>Bereich „Intern“: Forum, Squad-Gruppe, nächste Termine, Downloads, Alben – nur lesen.</summary>
public sealed partial class SquadInternalViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly SquadInternalService _service = new(services.Api.Client);
    private string? _slug;
    private SquadInternal? _data;

    public ObservableCollection<InternalRowViewModel> Threads { get; } = [];

    public ObservableCollection<InternalRowViewModel> Events { get; } = [];

    public ObservableCollection<InternalRowViewModel> Files { get; } = [];

    [ObservableProperty]
    public partial InternalRowViewModel? Conversation { get; set; }

    public bool HasThreads => Threads.Count > 0;

    public bool HasEvents => Events.Count > 0;

    public bool HasFiles => Files.Count > 0;

    public bool HasConversation => Conversation is not null;

    /// <summary>Hinweis, wenn Blöcke fehlen, weil dem Token Scopes fehlen (z. B. <c>forum:read</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScopeHint))]
    public partial string? ScopeHint { get; set; }

    public bool HasScopeHint => !string.IsNullOrEmpty(ScopeHint);

    protected override bool HasData => _data is not null && (HasThreads || HasEvents || HasFiles || HasConversation);

    public Task ShowAsync(string slug)
    {
        _slug = slug;
        _data = null;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var data = await _service.GetAsync(_slug, cancellationToken);
        _data = data;
        var nav = Services;
        Threads.Clear();
        foreach (var t in data.Threads ?? [])
        {
            var meta = string.Join(" · ", new[]
            {
                t.Author,
                Services.Format("Linux_InternalReplies", t.Replies),
                t.LastPostAt?.ToLocalTime().ToString("g", Services.Culture),
            }.Where(s => !string.IsNullOrEmpty(s)));
            Threads.Add(new InternalRowViewModel(t.Title, meta, t.Pinned ? "IconPin" : t.Locked ? "IconLock" : "IconForum", () => Open(t.Url)));
        }

        Events.Clear();
        foreach (var e in data.Events ?? [])
        {
            var when = e.StartsAt is { } start ? EventTimes.Range(start, e.EndsAt, Services.Culture) : string.Empty;
            var rsvp = EventsViewModel.RsvpLabel(e.MyRsvp, Services.Localizer);
            var meta = string.Join(" · ", new[] { when, e.Location, rsvp }.Where(s => !string.IsNullOrEmpty(s)));
            var id = e.Id;
            Events.Add(new InternalRowViewModel(e.Title, meta, "IconCalendar", () => nav.Navigate(new AppRoute(AppSection.Calendar, id))));
        }

        Files.Clear();
        foreach (var d in data.Downloads ?? [])
        {
            Files.Add(new InternalRowViewModel(d.Title, d.Meta ?? string.Empty, "IconDownload", () => Open(d.Url)));
        }

        foreach (var a in data.Albums ?? [])
        {
            Files.Add(new InternalRowViewModel(a.Title, Services.Format("Linux_InternalAlbumItems", a.Meta ?? "0"), "IconMedia", () => Open(a.Url)));
        }

        Conversation = data.Conversation is { } c
            ? new InternalRowViewModel(
                Services.T(c.Unread ? "Linux_InternalChatUnread" : "Linux_InternalChat"),
                string.Join(": ", new[] { c.Author, c.Excerpt }.Where(s => !string.IsNullOrEmpty(s))),
                "IconMessages",
                () => nav.Navigate(new AppRoute(AppSection.Messages, c.Id)))
            { IsUnread = c.Unread }
            : null;

        ScopeHint = data.MissingScopes.Count > 0 ? Services.Format("Linux_InternalMissingScopes", string.Join(", ", data.MissingScopes)) : null;
        OnPropertyChanged(nameof(HasThreads));
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasConversation));
    }

    private bool Open(Uri? url)
    {
        if (url is null)
        {
            return false;
        }

        _ = Services.OpenLinkAsync(url);

        return true;
    }
}

/// <summary>Zeile im Bereich „Intern“ (Symbol, Titel, Zeile darunter); Klick öffnet das Ziel in der App bzw. im Browser.</summary>
public sealed partial class InternalRowViewModel(string title, string meta, string iconKey, Func<bool> open)
{
    public string Title => title;

    public string Meta => meta;

    public bool HasMeta => meta.Length > 0;

    public Geometry? Icon { get; } = Avalonia.Application.Current?.TryGetResource(iconKey, null, out var g) == true ? g as Geometry : null;

    public bool IsUnread { get; init; }

    [RelayCommand]
    private void Open() => open();
}

/// <summary>Spiele des Clans (Umschalter „Spiele“ in Squads): Name, Plattform, Genre, Clan-Tag, Links.</summary>
public sealed partial class GamesViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly GameService _games = new(services.Api.Client);

    public ObservableCollection<GameItemViewModel> Items { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var games = await _games.ListAsync(cancellationToken);
        Items.Clear();
        foreach (var g in games)
        {
            Items.Add(new GameItemViewModel(g, Services, Feedback));
        }
    }
}

public sealed partial class GameItemViewModel : ObservableObject
{
    public GameItemViewModel(GameInfo game, AppServices services, FeedbackViewModel feedback)
    {
        ArgumentNullException.ThrowIfNull(game);
        Name = game.Name;
        ShortName = game.ShortName;
        Meta = string.Join(" · ", new[] { game.Platform.ToUpperInvariant(), game.Genre, game.ClanTag }.Where(s => !string.IsNullOrEmpty(s)));
        Links = game.Links.Select(l => new LinkViewModel(l.Label, l.Url, l.Code, services, feedback)).ToList();
        _ = LoadHeaderAsync(services, game.Header);
    }

    public string Name { get; }

    public string ShortName { get; }

    public string Meta { get; }

    public IReadOnlyList<LinkViewModel> Links { get; }

    public bool HasLinks => Links.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeader))]
    public partial Bitmap? Header { get; set; }

    public bool HasHeader => Header is not null;

    private async Task LoadHeaderAsync(AppServices services, Uri? header) => Header = await services.Images.LoadAsync(header, 640);
}
