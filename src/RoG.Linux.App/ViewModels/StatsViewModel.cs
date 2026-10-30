using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Stats;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Statistiken (Reiter der Matches-Seite, wie <c>StatsViewModel</c> der Windows-App): Spiel, Zeitraum und Kennzahl
/// wählen → Rangliste (eigener Platz in Gold) und Wochen-Awards. Mitgliederbereich – im Gastmodus Hinweis + „Anmelden“.
/// </summary>
public sealed partial class StatsViewModel(AppServices services, Action requestSignIn) : LoadableViewModel(services)
{
    private readonly StatsService _stats = new(services.Api.Client);
    private bool _gamesLoaded;
    private bool _suppress;

    public ObservableCollection<SettingsOption> Games { get; } = [];

    public IReadOnlyList<SettingsOption> Periods { get; } = StatsService.Periods
        .Select(p => new SettingsOption(p, services.T("Stats_Period_" + p))).ToList();

    public IReadOnlyList<SettingsOption> Metrics { get; } = StatsService.Metrics
        .Select(m => new SettingsOption(m, services.T("Stats_Metric_" + m))).ToList();

    public ObservableCollection<StatsRowViewModel> Rows { get; } = [];

    public ObservableCollection<AwardRowViewModel> Awards { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedGame { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedMetric { get; set; }

    [ObservableProperty]
    public partial string MyRankText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMyRank { get; set; }

    [ObservableProperty]
    public partial string AwardsTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAwards { get; set; }

    /// <summary>Gastmodus: Stats sind Mitgliederbereich (die API antwortet mit 401) – Hinweis statt Fehler.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilters))]
    public partial bool NeedsLogin { get; set; }

    public bool ShowFilters => !NeedsLogin && Games.Count > 0;

    protected override bool HasData => Rows.Count > 0 || Awards.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        NeedsLogin = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (NeedsLogin)
        {
            Rows.Clear();
            Awards.Clear();
            HasMyRank = HasAwards = false;
            return;
        }

        if (!_gamesLoaded)
        {
            var games = await _stats.GamesAsync(cancellationToken);
            _suppress = true;
            Games.Clear();
            foreach (var g in games)
            {
                Games.Add(new SettingsOption(g.Slug, g.Name));
            }

            SelectedGame = Games.FirstOrDefault();
            SelectedPeriod = Periods[1];
            SelectedMetric = Metrics[0];
            _suppress = false;
            _gamesLoaded = true;
            OnPropertyChanged(nameof(ShowFilters));
        }

        Rows.Clear();
        Awards.Clear();
        HasMyRank = HasAwards = false;
        if (SelectedGame is not { } game)
        {
            return;
        }

        var metric = SelectedMetric?.Value ?? "kills";
        var board = await _stats.LeaderboardAsync(game.Value, SelectedPeriod?.Value ?? "30d", metric, cancellationToken);
        foreach (var e in board.Entries)
        {
            Rows.Add(new StatsRowViewModel(e, metric, e.Rank == board.MyRank, Services));
        }

        if (board.MyRank is { } me)
        {
            MyRankText = Services.Format(StringKeys.StatsMyRank, me);
            HasMyRank = true;
        }

        var awards = await _stats.AwardsAsync(game.Value, cancellationToken);
        foreach (var a in awards.Items)
        {
            var label = Services.T("Stats_Award_" + a.Award);
            Awards.Add(new AwardRowViewModel(
                label.StartsWith("Stats_Award_", StringComparison.Ordinal) ? a.Award : label,
                StatsService.FormatAwardValue(a.Award, a.Value, Services.Culture),
                string.Join(", ", a.Members)));
        }

        AwardsTitle = Services.Format(StringKeys.StatsAwardsTitle, awards.Week);
        HasAwards = Awards.Count > 0;
    }

    partial void OnSelectedGameChanged(SettingsOption? value) => Reload();

    partial void OnSelectedPeriodChanged(SettingsOption? value) => Reload();

    partial void OnSelectedMetricChanged(SettingsOption? value) => Reload();

    private void Reload()
    {
        if (!_suppress && _gamesLoaded)
        {
            _ = LoadAsync(silent: true);
        }
    }

    [RelayCommand]
    private void SignIn() => requestSignIn();
}

/// <summary>Platz in der Rangliste: Rang, Name, Kills/Tode/Runden, Wert der gewählten Kennzahl.</summary>
public sealed class StatsRowViewModel(LeaderboardEntry entry, string metric, bool isMe, AppServices services)
{
    public string Rank { get; } = entry.Rank.ToString(services.Culture) + ".";

    public string Name => entry.Name;

    public bool IsMe => isMe;

    /// <summary>Eigener Platz hervorgehoben.</summary>
    public Avalonia.Media.IBrush? RowBackground { get; } = isMe ? UiResources.Brush("RoGBg3Brush") : null;

    public string Value { get; } = metric switch
    {
        "kd" => entry.Kd?.ToString("0.00", services.Culture) ?? "–",
        "playtime" => entry.Playtime.TotalHours.ToString("0.0", services.Culture) + " h",
        "rounds" => entry.Rounds.ToString(services.Culture),
        "headshots" => entry.Headshots.ToString(services.Culture),
        _ => entry.Kills.ToString(services.Culture),
    };

    public string Details { get; } = string.Format(CultureInfo.InvariantCulture, "{0} K · {1} D · {2} R",
        entry.Kills.ToString(services.Culture), entry.Deaths.ToString(services.Culture), entry.Rounds.ToString(services.Culture));

    public string AutomationName => $"{Rank} {Name}, {Value}";
}

public sealed record AwardRowViewModel(string Label, string Value, string Members)
{
    public string Text => $"{Members} · {Value}";
}
