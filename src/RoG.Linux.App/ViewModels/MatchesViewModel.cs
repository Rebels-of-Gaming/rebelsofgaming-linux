using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Content;
using RoG.Core.Features.Matches;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>„Match eintragen“/„Bearbeiten“: Editorfenster öffnen; <see cref="Closed"/> bekommt „geändert“.</summary>
public sealed record MatchEditorRequest(AppServices Services, string? MatchId, Action<bool> Closed);

/// <summary>Reiter der Matches-Seite (wie die Windows-App).</summary>
public enum MatchesTab
{
    Matches,
    Hall,
    Stats,
    Tournaments,
}

/// <summary>
/// Matches wie <c>MatchesPage</c> der Windows-App: Reiter <b>Matches</b> (Liste-Detail, neueste zuerst, Detail mit
/// Ergebnis, Maps, Line-up, Links und Bericht), <b>Hall of Fame</b> (Trophäen, Meilensteine, Ehrenmitglieder),
/// <b>Statistiken</b> (<see cref="StatsViewModel"/>) und <b>Turniere</b>. Matches, Hall of Fame und Turniere gehen auch im
/// Gastmodus (nur veröffentlichte Matches); <c>rog://matches/{slug|id}</c> öffnet ein Match. Eintragen/Bearbeiten
/// (<c>matches.manage</c> + Scope <c>matches:write</c>) öffnet den <c>MatchEditorWindow</c>.
/// </summary>
public sealed partial class MatchesViewModel : LoadableViewModel, IDetailPage, IRoutablePage, IRefreshablePage
{
    private readonly MatchService _matches;
    private string? _cursor;
    private string? _pendingKey;

    public MatchesViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _matches = new MatchService(services.Api.Client);
        Detail = new MatchDetailViewModel(services);
        Hall = new HallOfFameViewModel(services, _matches);
        Stats = new StatsViewModel(services, requestSignIn);
        Tournaments = new TournamentsViewModel(services);
    }

    public ObservableCollection<MatchRowViewModel> Items { get; } = [];

    public MatchDetailViewModel Detail { get; }

    public HallOfFameViewModel Hall { get; }

    public StatsViewModel Stats { get; }

    public TournamentsViewModel Tournaments { get; }

    /// <summary>„Match eintragen“/„Bearbeiten“: <c>matches.manage</c> (global oder in einem Squad) + Scope <c>matches:write</c>.</summary>
    public bool CanManage => MatchAdminService.CanManageAny(Services.CurrentUser);

    /// <summary>Editor öffnen (Match-ID oder <c>null</c> = neu) – die Ansicht öffnet das Fenster.</summary>
    public event EventHandler<MatchEditorRequest>? EditorRequested;

    [RelayCommand]
    private void AddMatch() => RequestEditor(null);

    [RelayCommand]
    private void EditMatch()
    {
        if ((Selected?.Id ?? Detail.Match?.Id) is { } id)
        {
            RequestEditor(id);
        }
    }

    private void RequestEditor(string? matchId) => EditorRequested?.Invoke(this, new MatchEditorRequest(Services, matchId, changed =>
    {
        if (changed)
        {
            Selected = null;
            IsLinkedDetailOpen = false;
            _ = LoadAsync(silent: true);
        }
    }));

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial MatchRowViewModel? Selected { get; set; }

    /// <summary>Match aus einem Link, das nicht in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool IsLinkedDetailOpen { get; set; }

    public bool IsDetailOpen => Selected is not null || IsLinkedDetailOpen;

    public bool ShowSelectHint => !IsDetailOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMatchesTab), nameof(IsHallTab), nameof(IsStatsTab), nameof(IsTournamentsTab))]
    public partial MatchesTab Tab { get; set; }

    public bool IsMatchesTab
    {
        get => Tab == MatchesTab.Matches;
        set => SelectTab(value, MatchesTab.Matches, nameof(IsMatchesTab));
    }

    public bool IsHallTab
    {
        get => Tab == MatchesTab.Hall;
        set => SelectTab(value, MatchesTab.Hall, nameof(IsHallTab));
    }

    public bool IsStatsTab
    {
        get => Tab == MatchesTab.Stats;
        set => SelectTab(value, MatchesTab.Stats, nameof(IsStatsTab));
    }

    public bool IsTournamentsTab
    {
        get => Tab == MatchesTab.Tournaments;
        set => SelectTab(value, MatchesTab.Tournaments, nameof(IsTournamentsTab));
    }

    protected override bool HasData => Items.Count > 0;

    /// <summary>ToggleButtons: nur das Einschalten wählt einen Reiter (Ausschalten des aktiven bleibt wirkungslos).</summary>
    private void SelectTab(bool value, MatchesTab tab, string property)
    {
        if (value)
        {
            Tab = tab;
        }
        else if (Tab == tab)
        {
            OnPropertyChanged(property);
        }
    }

    partial void OnTabChanged(MatchesTab value)
    {
        switch (value)
        {
            case MatchesTab.Hall:
                _ = Hall.LoadAsync(silent: Hall.ShowContent);
                break;
            case MatchesTab.Stats when !Stats.ShowContent && !Stats.IsLoading:
                _ = Stats.LoadAsync();
                break;
            case MatchesTab.Tournaments when !Tournaments.ShowContent && !Tournaments.IsLoading:
                _ = Tournaments.LoadAsync();
                break;
        }
    }

    /// <summary>F5: den offenen Reiter neu laden.</summary>
    public Task RefreshAsync() => Tab switch
    {
        MatchesTab.Hall => Hall.LoadAsync(),
        MatchesTab.Stats => Stats.LoadAsync(),
        MatchesTab.Tournaments => Tournaments.LoadAsync(),
        _ => LoadAsync(),
    };

    [RelayCommand]
    private Task RefreshCurrentAsync() => RefreshAsync();

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _matches.ListAsync(null, cancellationToken);
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var m in page.Items)
        {
            Items.Add(new MatchRowViewModel(m, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        if (selectedId is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Id == selectedId);
        }

        if (_pendingKey is { } pending)
        {
            _pendingKey = null;
            Open(pending);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            var page = await _matches.ListAsync(_cursor);
            foreach (var m in page.Items)
            {
                Items.Add(new MatchRowViewModel(m, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            // erneut versuchen über „Mehr laden“
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>Match per Slug oder ID öffnen (Deep Link, Suche, Benachrichtigung).</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Tab = MatchesTab.Matches;
        if (Items.FirstOrDefault(i => i.Id == key || i.Slug == key) is { } item)
        {
            IsLinkedDetailOpen = false;
            Selected = item;
        }
        else if (Items.Count == 0 && (IsLoading || !ShowContent) && !HasError)
        {
            _pendingKey = key;
        }
        else
        {
            Selected = null;
            IsLinkedDetailOpen = true;
            _ = Detail.ShowAsync(key, null);
        }
    }

    partial void OnSelectedChanged(MatchRowViewModel? value)
    {
        if (value is not null)
        {
            IsLinkedDetailOpen = false;
            _ = Detail.ShowAsync(value.Slug, value.Match);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        Selected = null;
        IsLinkedDetailOpen = false;
    }
}

/// <summary>Ein Match in der Liste: Ergebnisfarbe, Titel „RoG ET vs. Gegner“, Datum · Spiel · Turnier, Ergebnis.</summary>
public sealed class MatchRowViewModel(MatchItem match, AppServices services)
{
    public MatchItem Match => match;

    public string Id => match.Id;

    public string Slug => match.Slug;

    public string Title { get; } = MatchText.Title(match);

    public string Subtitle { get; } = MatchText.Subtitle(match, services);

    public string Score { get; } = MatchText.Score(match.Own, match.OpponentScore);

    public IBrush ResultBrush { get; } = MatchText.BrushOf(match.Result);

    public string ResultText { get; } = MatchText.ResultText(match.Result, services);

    public string AutomationName => $"{Title}, {Score}, {ResultText}";
}

public sealed record MatchMapRow(string Map, string Score, IBrush ResultBrush);

public sealed record MatchPlayerRow(string Name, string? Detail)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>
/// Detail eines Matches: sofort mit den Werten der Liste, dann vollständig aus <c>GET /v1/matches/{match}</c> (Line-up,
/// Links, Bericht). Ein Link auf ein Match, das nicht in der Liste steht, lädt nur von dort.
/// </summary>
public sealed partial class MatchDetailViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly MatchDetailService _service = new(services.Api.Client, services.Environment.WebBaseUrl);
    private string? _key;
    private Uri? _webUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Score), nameof(ResultBrush), nameof(ResultText), nameof(AutomationName))]
    public partial MatchItem? Match { get; set; }

    public string Title => Match is { } m ? MatchText.Title(m) : string.Empty;

    public string Subtitle => Match is { } m ? MatchText.Subtitle(m, Services, withType: true) : string.Empty;

    public string Score => Match is { } m ? MatchText.Score(m.Own, m.OpponentScore) : string.Empty;

    public IBrush ResultBrush => MatchText.BrushOf(Match?.Result ?? MatchOutcome.None);

    public string ResultText => MatchText.ResultText(Match?.Result ?? MatchOutcome.None, Services);

    public string AutomationName => $"{Title}, {Score}, {ResultText}";

    public ObservableCollection<MatchMapRow> Maps { get; } = [];

    public ObservableCollection<MatchPlayerRow> Lineup { get; } = [];

    public ObservableCollection<LinkViewModel> Links { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    public partial RichDocument? Report { get; set; }

    public bool HasReport => Report is { Blocks.Count: > 0 };

    public bool HasMaps => Maps.Count > 0;

    public bool HasLineup => Lineup.Count > 0;

    public bool HasLinks => Links.Count > 0;

    protected override bool HasData => Match is not null;

    /// <summary>Match zeigen; <paramref name="preview"/> (aus der Liste) erscheint sofort, Fehler beim Nachladen bleiben dann still.</summary>
    public async Task ShowAsync(string key, MatchItem? preview)
    {
        _key = key;
        _webUrl = null;
        Report = null;
        Lineup.Clear();
        Links.Clear();
        Apply(preview);
        await LoadAsync(silent: preview is not null);
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_key is not { } key)
        {
            return;
        }

        var details = await _service.GetAsync(key, cancellationToken);
        if (key != _key)
        {
            return; // inzwischen anderes Match gewählt
        }

        Apply(details.Match);
        _webUrl = details.WebUrl;
        Lineup.Clear();
        foreach (var p in details.Lineup)
        {
            Lineup.Add(new MatchPlayerRow(p.Name, string.Join(" · ", new[]
            {
                p.Position,
                p.Substitute ? Services.T("Linux_MatchSubstitute") : null,
            }.Where(s => !string.IsNullOrEmpty(s)))));
        }

        Links.Clear();
        foreach (var (label, url) in details.Match.Links)
        {
            Links.Add(new LinkViewModel(label, url, null, Services, Feedback));
        }

        Report = details.Report;
        OnPropertyChanged(nameof(HasLineup));
        OnPropertyChanged(nameof(HasLinks));
    }

    private void Apply(MatchItem? match)
    {
        Match = match;
        Maps.Clear();
        foreach (var m in match?.Maps ?? [])
        {
            Maps.Add(new MatchMapRow(m.Map, MatchText.Score(m.Own, m.Opponent), MatchText.BrushOf(m.Result)));
        }

        if (match is not null && Lineup.Count == 0)
        {
            foreach (var name in match.Lineup)
            {
                Lineup.Add(new MatchPlayerRow(name, null));
            }
        }

        OnPropertyChanged(nameof(HasMaps));
        OnPropertyChanged(nameof(HasLineup));
        DataChanged();
    }

    [RelayCommand]
    private Task OpenInBrowserAsync()
    {
        var url = _webUrl ?? (Match is { } m ? Services.Environment.WebPage(Services.Language, "matches/" + Uri.EscapeDataString(m.Slug)) : null);
        return url is null ? Task.CompletedTask : Services.Browser.OpenAsync(url);
    }
}

/// <summary>Spieler im Line-up (Ersatz und Position optional).</summary>
public sealed record MatchPlayer(string Name, string? Slug, bool Substitute, string? Position);

/// <summary>Ein Match mit allem, was <c>GET /v1/matches/{match}</c> zusätzlich liefert.</summary>
public sealed record MatchDetails(MatchItem Match, IReadOnlyList<MatchPlayer> Lineup, RichDocument Report, Uri? WebUrl);

/// <summary>
/// Einzelnes Match laden (der Kern hat nur die Liste – Wunsch an den Kern: <c>MatchService.GetAsync</c>). Abbildung wie
/// <c>MatchService.Map</c> des Kerns, Bericht über den HTML-Parser des Kerns.
/// </summary>
public sealed class MatchDetailService(IRoGApiClient client, Uri webBaseUrl)
{
    private readonly HtmlContentParser _html = new(ContentPolicy.Platform, webBaseUrl);

    public async Task<MatchDetails> GetAsync(string match, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(match);
        var m = (await client.V1_matches_showAsync(match, cancellationToken).ConfigureAwait(false)).Data;
        return new MatchDetails(Map(m), Lineup(m), _html.Parse(m.Report_html), ApiValues.WebUri(m.Web_url));
    }

    public static MatchItem Map(MatchResource m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new MatchItem(
            m.Id,
            m.Slug,
            m.Type,
            m.Squad?.Name,
            ApiValues.Text(m.Squad?.Game),
            m.Opponent.Name,
            ApiValues.Text(m.Opponent.Tag),
            ApiValues.Time(m.Played_at),
            m.Score.Own,
            m.Score.Opponent,
            MatchService.ParseOutcome(m.Result),
            m.Tournament?.Name,
            m.Tournament?.Placement,
            m.Maps?.OrderBy(x => x.Position).Select(x => new MatchMap(x.Map, x.Score.Own, x.Score.Opponent, MatchService.ParseOutcome(x.Result))).ToList() ?? [],
            m.Lineup?.Select(l => l.Member.Name).ToList() ?? [],
            m.Links?.Select(l => (Label: l.Label ?? l.Type, Url: ApiValues.WebUri(l.Url))).Where(l => l.Url is not null).Select(l => (l.Label, l.Url!)).ToList() ?? []);
    }

    /// <summary>Stammspieler zuerst, dann Ersatz – jeweils in der Reihenfolge der API.</summary>
    private static List<MatchPlayer> Lineup(MatchResource m) =>
        m.Lineup?.Select(l => new MatchPlayer(l.Member.Name, ApiValues.Text(l.Member.Slug), l.Slot == "substitute", ApiValues.Text(l.Position)))
            .OrderBy(p => p.Substitute)
            .ToList() ?? [];
}

/// <summary>Gemeinsame Texte und Farben für Liste und Detail.</summary>
internal static class MatchText
{
    public static string Title(MatchItem match) => $"{match.SquadName ?? "RoG"} vs. {match.Opponent}";

    public static string Subtitle(MatchItem match, AppServices services, bool withType = false)
    {
        var type = withType ? services.T("MatchEdit_Type_" + match.Type) : null;
        return string.Join(" · ", new[]
        {
            match.PlayedAt?.ToLocalTime().ToString(withType ? "g" : "d", services.Culture),
            match.Game,
            type is { Length: > 0 } t && !t.StartsWith("MatchEdit_", StringComparison.Ordinal) ? t : null,
            match.Tournament is null ? null : match.Placement is { } p ? $"{match.Tournament} (#{p})" : match.Tournament,
        }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public static string Score(int? own, int? opponent) =>
        own is null && opponent is null ? "–" : $"{own?.ToString(CultureInfo.CurrentCulture) ?? "–"} : {opponent?.ToString(CultureInfo.CurrentCulture) ?? "–"}";

    public static string ResultText(MatchOutcome outcome, AppServices services) => services.T(outcome switch
    {
        MatchOutcome.Win => StringKeys.MatchesWin,
        MatchOutcome.Loss => StringKeys.MatchesLoss,
        MatchOutcome.Draw => StringKeys.MatchesDraw,
        _ => StringKeys.MatchesOpen,
    });

    /// <summary>Sieg grün, Niederlage rot, Unentschieden gold, offen gedämpft (wie die Windows-App).</summary>
    public static IBrush BrushOf(MatchOutcome outcome) => UiResources.Brush(outcome switch
    {
        MatchOutcome.Win => "RoGGreenBrush",
        MatchOutcome.Loss => "RoGRedTextBrush",
        MatchOutcome.Draw => "RoGGoldBrush",
        _ => "RoGMutedBrush",
    });
}

/// <summary>Hall of Fame: Trophäen, Meilensteine, Ehrenmitglieder (wie die Windows-App).</summary>
public sealed partial class HallOfFameViewModel(AppServices services, MatchService matches) : LoadableViewModel(services)
{
    public ObservableCollection<HallEntryViewModel> Trophies { get; } = [];

    public ObservableCollection<HallEntryViewModel> Milestones { get; } = [];

    public ObservableCollection<HonoraryViewModel> Honorary { get; } = [];

    [ObservableProperty]
    public partial bool HasTrophies { get; set; }

    [ObservableProperty]
    public partial bool HasMilestones { get; set; }

    [ObservableProperty]
    public partial bool HasHonorary { get; set; }

    protected override bool HasData => HasTrophies || HasMilestones || HasHonorary;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var hall = await matches.GetHallOfFameAsync(cancellationToken);
        Fill(Trophies, hall.Trophies);
        Fill(Milestones, hall.Milestones);
        Honorary.Clear();
        foreach (var h in hall.Honorary)
        {
            Honorary.Add(new HonoraryViewModel(h, Services));
        }

        HasTrophies = Trophies.Count > 0;
        HasMilestones = Milestones.Count > 0;
        HasHonorary = Honorary.Count > 0;
    }

    private void Fill(ObservableCollection<HallEntryViewModel> target, IEnumerable<HallEntry> entries)
    {
        target.Clear();
        foreach (var e in entries)
        {
            target.Add(new HallEntryViewModel(e, Services));
        }
    }
}

public sealed class HallEntryViewModel(HallEntry entry, AppServices services)
{
    public string Title => entry.Title;

    public string Description => entry.Description ?? string.Empty;

    public bool HasDescription => entry.Description is not null;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        entry.AchievedOn?.ToLocalTime().ToString("Y", services.Culture),
        entry.Placement is { } p ? $"#{p}" : null,
        entry.Squad,
        entry.Game,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Medaille: Gold/Silber/Bronze als Farbe (wie die Windows-App), sonst Gold (Auszeichnung).</summary>
    public IBrush MedalBrush { get; } = entry.Medal switch
    {
        "silver" => UiResources.FromHex("#C0C4CC", "RoGMutedBrush"),
        "bronze" => UiResources.FromHex("#CD7F32", "RoGGoldBrush"),
        _ => UiResources.Brush("RoGGoldBrush"),
    };
}

public sealed partial class HonoraryViewModel : ObservableObject
{
    public HonoraryViewModel(HonoraryMember member, AppServices services)
    {
        Name = member.Name;
        Period = member.Period ?? string.Empty;
        Tribute = member.Tribute;
        _ = LoadAvatarAsync(services, member.Avatar);
    }

    public string Name { get; }

    public string Period { get; }

    public bool HasPeriod => Period.Length > 0;

    public string Tribute { get; }

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    private async Task LoadAvatarAsync(AppServices services, Uri? avatar) => Avatar = await services.Images.LoadAsync(avatar, 112);
}

/// <summary>Turniere (Matches-Seite): Name, Veranstalter, Zeitraum, Platzierung; Link öffnet die Turnierseite.</summary>
public sealed partial class TournamentsViewModel(AppServices services) : LoadableViewModel(services)
{
    public ObservableCollection<TournamentRowViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await new SquadExtrasService(Services.Api.Client).TournamentsAsync(cancellationToken: cancellationToken);
        Items.Clear();
        foreach (var t in list)
        {
            Items.Add(new TournamentRowViewModel(t, Services));
        }
    }
}

public sealed partial class TournamentRowViewModel(RoG.Core.Features.Squads.Tournament tournament, AppServices services)
{
    public string Title { get; } = tournament.GameShort is { } g ? $"{tournament.Name} · {g}" : tournament.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        tournament.StartsOn?.ToString("d", services.Culture) + (tournament.EndsOn is { } e && e != tournament.StartsOn ? "–" + e.ToString("d", services.Culture) : string.Empty),
        tournament.Organizer,
        tournament.SquadName,
        tournament.Participants is { } n ? services.Format(StringKeys.TournamentParticipants, n) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Placement { get; } = tournament.Placement is { } p ? p.ToString(services.Culture) + "." : string.Empty;

    public bool HasPlacement => tournament.Placement is not null;

    public bool HasUrl => tournament.Url is not null;

    [RelayCommand]
    private Task OpenAsync() => tournament.Url is { } url ? services.Browser.OpenAsync(url) : Task.CompletedTask;
}
