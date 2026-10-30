using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Minecraft-Bestenlisten (wie <c>LeaderboardsViewModel</c> der Windows-App, Website v0.41.0): Spielzeit, je Minispiel,
/// Parkour; Zeitraum gesamt/30 Tage, Wertung und Kurs je Liste. Es erscheinen nur Mitglieder, die ihre Statistiken
/// freigeben – welche, entscheidet der Server. Geht auch im Gastmodus; ein Eintrag öffnet das Profil.
/// </summary>
public sealed partial class LeaderboardsViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;
    private bool _suppress;

    public LeaderboardsViewModel(AppServices services)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        BoardOptions = MinecraftService.Boards.Select(b => new SettingsOption(b, Label("Leaderboard_Board_", b))).ToList();
        PeriodOptions = MinecraftService.Periods.Select(p => new SettingsOption(p, Label("Leaderboard_Period_", p))).ToList();
        _suppress = true;
        SelectedBoard = BoardOptions[0];
        SelectedPeriod = PeriodOptions[0];
        _suppress = false;
    }

    public IReadOnlyList<SettingsOption> BoardOptions { get; }

    public IReadOnlyList<SettingsOption> PeriodOptions { get; }

    public ObservableCollection<SettingsOption> SortOptions { get; } = [];

    public ObservableCollection<SettingsOption> CourseOptions { get; } = [];

    public ObservableCollection<LeaderboardEntryViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial SettingsOption SelectedBoard { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedSort { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedCourse { get; set; }

    [ObservableProperty]
    public partial bool HasSorts { get; set; }

    [ObservableProperty]
    public partial bool HasCourses { get; set; }

    /// <summary>„Siegquote ab 10 Spielen“, „seit 23.09.2026“ – was der Server gewertet hat.</summary>
    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial bool HasHint { get; set; }

    protected override bool HasData => Items.Count > 0;

    partial void OnSelectedBoardChanged(SettingsOption value)
    {
        if (_suppress || value is null)
        {
            return;
        }

        _suppress = true;
        SortOptions.Clear();
        foreach (var sort in MinecraftService.SortsFor(value.Value))
        {
            SortOptions.Add(new SettingsOption(sort, Label("Leaderboard_Sort_", sort.Replace(MinecraftService.ObjectivePrefix, "objective_", StringComparison.Ordinal))));
        }

        HasSorts = SortOptions.Count > 0;
        SelectedSort = SortOptions.FirstOrDefault();
        CourseOptions.Clear();
        SelectedCourse = null;
        HasCourses = false;
        _suppress = false;
        _ = LoadAsync();
    }

    partial void OnSelectedPeriodChanged(SettingsOption value) => Reload();

    partial void OnSelectedSortChanged(SettingsOption? value) => Reload();

    partial void OnSelectedCourseChanged(SettingsOption? value) => Reload();

    private void Reload()
    {
        if (!_suppress)
        {
            _ = LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var board = await _minecraft.LeaderboardAsync(SelectedBoard.Value, SelectedPeriod.Value, SelectedSort?.Value, SelectedCourse?.Value, cancellationToken);
        var kind = MinecraftService.ValueKind(board.Board, board.Sort);

        _suppress = true;
        if (board.Board == MinecraftService.BoardParkour && board.Courses.Count > 0 && CourseOptions.Count == 0)
        {
            foreach (var (id, label) in board.Courses)
            {
                CourseOptions.Add(new SettingsOption(id, label));
            }

            SelectedCourse = CourseOptions.FirstOrDefault(c => c.Value == board.Course) ?? CourseOptions[0];
        }

        HasCourses = CourseOptions.Count > 0;
        _suppress = false;

        Items.Clear();
        foreach (var entry in board.Entries)
        {
            Items.Add(new LeaderboardEntryViewModel(Services, entry, ValueText(kind, entry.Value)));
        }

        var hints = new List<string>();
        if (board.Sort == MinecraftService.SortWinRate && board.MinMatches is { } min)
        {
            hints.Add(Services.Format(StringKeys.LeaderboardMinMatches, min));
        }

        if (board.From is { } from)
        {
            hints.Add(Services.Format(StringKeys.LeaderboardFrom, from.ToLocalTime().ToString("d", Services.Culture)));
        }

        Hint = string.Join(" · ", hints);
        HasHint = hints.Count > 0;
    }

    private string ValueText(LeaderboardValueKind kind, double value) => kind switch
    {
        LeaderboardValueKind.Seconds => Playtime(Services, (long)value),
        LeaderboardValueKind.Milliseconds => MinecraftService.FormatDuration((long)value),
        LeaderboardValueKind.Percent => value.ToString("0.#", Services.Culture) + " %",
        _ => ((long)value).ToString("N0", Services.Culture),
    };

    /// <summary>„112 Std. 5 Min.“ bzw. „5 Min.“.</summary>
    internal static string Playtime(AppServices services, long seconds)
    {
        var (hours, minutes) = MinecraftService.SplitPlaytime(seconds);
        return hours > 0
            ? services.Format(StringKeys.StatsPlaytimeHours, hours.ToString("N0", services.Culture), minutes)
            : services.Format(StringKeys.StatsPlaytimeMinutes, minutes);
    }

    /// <summary>Übersetzter Name, sonst der Rohwert (unbekannte Spiele/Wertungen der Registry).</summary>
    private string Label(string prefix, string value)
    {
        var key = prefix + value;
        var text = Services.T(key);
        return text == key ? value : text;
    }
}

public sealed partial class LeaderboardEntryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly LeaderboardEntry _entry;

    public LeaderboardEntryViewModel(AppServices services, LeaderboardEntry entry, string valueText)
    {
        _services = services;
        _entry = entry;
        ValueText = valueText;
        RankText = "#" + entry.Rank.ToString(services.Culture);
        RankBrush = UiResources.Brush(entry.Rank <= 3 ? "RoGGoldBrush" : "RoGMutedBrush");
        Meta = entry.Played is { } played
            ? services.Format(StringKeys.LeaderboardMatches, played, entry.Won ?? 0, entry.WinRate is { } rate ? rate.ToString("0.#", services.Culture) : "–")
            : null;
        _ = LoadAvatarAsync(entry.Avatar);
    }

    public string RankText { get; }

    public string Name => _entry.Name;

    public string ValueText { get; }

    public string? Meta { get; }

    public bool HasMeta => Meta is not null;

    /// <summary>Gold für die ersten drei Plätze (Rang/Auszeichnung), sonst gedämpft.</summary>
    public IBrush RankBrush { get; }

    public string AutomationName => $"{RankText} {Name}, {ValueText}";

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    private async Task LoadAvatarAsync(Uri? avatar) => Avatar = await _services.Images.LoadAsync(avatar, 64);

    [RelayCommand]
    private void Open() => _services.Navigate(new AppRoute(AppSection.Members, _entry.Slug));
}
