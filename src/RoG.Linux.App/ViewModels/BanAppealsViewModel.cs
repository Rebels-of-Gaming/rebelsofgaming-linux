using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Reiter „Einsprüche“ (wie <c>BanAppealsViewModel</c> der Windows-App, Website v0.34.0, Guide 43): Einsprüche gegen
/// Gameserver-Bans annehmen (hebt den Ban auf) oder ablehnen (optional sofort neuer Einspruch erlaubt) mit öffentlicher
/// Begründung (<see cref="Decision"/> im Inhalt); mit <c>gamebans.manage</c> zusätzlich Forum-Bereich für Einsprüche und
/// Mindest-Plugin-Versionen. Sichtbar mit Scope <c>gamebans:manage</c> und <c>gamebans.manage</c> bzw.
/// <c>servers.bans</c> – was jemand entscheiden darf, sagt der Server je Einspruch (<c>can_decide</c>).
/// </summary>
public sealed partial class BanAppealsViewModel : ModerationTabViewModel
{
    private readonly PublicBanService _appeals;
    private string? _cursor;

    public BanAppealsViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _appeals = new PublicBanService(services.Api.Http);
        var me = services.CurrentUser;
        var scope = me is not null && me.HasScope(GameBanService.BanScope);
        CanView = scope && (me!.CanAnywhere(GameBanService.ManagePermission) || me.CanAnywhere("servers.bans"));
        IsManager = scope && me!.Can(GameBanService.ManagePermission);
        StatusOptions = PublicBanService.AppealStatuses.Select(s => new SettingsOption(s, L("BanAppeals_Status_" + s))).ToList();
        _selectedStatus = StatusOptions[0];
        AskDecision = (title, accept) => Decision.AskAsync(title, L(accept ? StringKeys.BanAppealsAcceptText : StringKeys.BanAppealsRejectText), accept);
    }

    public override bool IsAvailable => CanView;

    public bool CanView { get; }

    /// <summary>Forum-Bereich und Mindest-Plugin-Versionen verwalten (<c>gamebans.manage</c>).</summary>
    public bool IsManager { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    private SettingsOption _selectedStatus;

    public SettingsOption SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (value is not null && SetProperty(ref _selectedStatus, value) && IsLoaded)
            {
                _ = LoadAsync();
            }
        }
    }

    public ObservableCollection<BanAppealItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Annehmen/Ablehnen: Begründung (+ „neuer Einspruch sofort erlaubt“) – Bereich im Inhalt.</summary>
    public AppealDecisionViewModel Decision { get; } = new();

    /// <summary>Entscheidung abfragen (Titel, annehmen?) → Begründung + Neu-Einspruch, <c>null</c> = abgebrochen. Standard: <see cref="Decision"/>.</summary>
    public Func<string, bool, Task<(string? Note, bool Reappeal)?>> AskDecision { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _appeals.AppealsAsync(SelectedStatus.Value, null, cancellationToken);
        Items.Clear();
        Append(page);
        if (IsManager && !HasSettings)
        {
            ApplySettings(await _appeals.SettingsAsync(cancellationToken));
        }
    }

    private void Append(RoG.Core.Api.Page<BanAppeal> page)
    {
        foreach (var appeal in page.Items)
        {
            Items.Add(new BanAppealItemViewModel(appeal, this));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        RoG.Core.Api.Page<BanAppeal>? page = null;
        if (await RunAsync(async () => page = await _appeals.AppealsAsync(SelectedStatus.Value, cursor)))
        {
            Append(page!);
        }
    }

    internal async Task DecideAsync(BanAppealItemViewModel item, bool accept)
    {
        var title = Services.Format(accept ? StringKeys.BanAppealsAcceptTitle : StringKeys.BanAppealsRejectTitle, item.Title);
        if (await AskDecision(title, accept) is not { } answer)
        {
            return;
        }

        BanAppeal? decided = null;
        if (await RunAsync(async () => decided = await _appeals.DecideAsync(item.Id, accept, answer.Note, answer.Reappeal)))
        {
            var index = Items.IndexOf(item);
            if (index >= 0)
            {
                if (SelectedStatus.Value == PublicBanService.AppealOpen)
                {
                    Items.RemoveAt(index);
                }
                else
                {
                    Items[index] = new BanAppealItemViewModel(decided!, this);
                }
            }

            DataChanged();
            Show(L(accept ? StringKeys.BanAppealsAccepted : StringKeys.BanAppealsRejected), MessageTone.Success);
        }
    }

    internal void Open(Uri url) => _ = Services.OpenLinkAsync(url);

    public override bool CloseOverlay()
    {
        if (Decision.IsOpen)
        {
            Decision.Cancel();
            return true;
        }

        return base.CloseOverlay();
    }

    // --- Forum-Bereich und Plugin-Versionen (gamebans.manage) -----------------------------------------------------

    public ObservableCollection<SettingsOption> BoardOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedBoard { get; set; }

    [ObservableProperty]
    public partial string BoardText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSettings { get; set; }

    /// <summary>Admin-Einträge der Mindest-Plugin-Versionen, eine Zeile je Plugin („rog-proxy 0.6.0“).</summary>
    [ObservableProperty]
    public partial string PluginVersions { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PluginVersionsInfo { get; set; } = string.Empty;

    private void ApplySettings(AppealSettings settings)
    {
        BoardOptions.Clear();
        BoardOptions.Add(new SettingsOption(string.Empty, L(StringKeys.BanAppealsNoBoard)));
        foreach (var (id, name) in settings.BoardOptions)
        {
            BoardOptions.Add(new SettingsOption(id, name));
        }

        SelectedBoard = BoardOptions.FirstOrDefault(o => o.Value == (settings.BoardId ?? string.Empty)) ?? BoardOptions[0];
        BoardText = settings.BoardId is null
            ? L(StringKeys.BanAppealsBoardMissing)
            : Services.Format(settings.BoardPublic ? StringKeys.BanAppealsBoardActive : StringKeys.BanAppealsBoardNotPublic, settings.BoardName ?? settings.BoardId);
        PluginVersions = PublicBanService.FormatPluginVersions(settings.PluginVersions);
        var env = settings.EnvPluginVersions.Count > 0 ? string.Join(", ", settings.EnvPluginVersions.Select(v => v.Plugin + " " + v.Version)) : "—";
        PluginVersionsInfo = Services.Format(StringKeys.BanAppealsPluginVersionsInfo, settings.MinPluginVersion ?? "1.0.0", env);
        HasSettings = true;
    }

    [RelayCommand]
    private async Task SaveBoardAsync()
    {
        AppealSettings? settings = null;
        if (IsManager && await RunAsync(async () => settings = await _appeals.SetBoardAsync(SelectedBoard?.Value)))
        {
            ApplySettings(settings!);
            Show(L(StringKeys.BanAppealsBoardSaved), MessageTone.Success);
        }
    }

    [RelayCommand]
    private async Task CreateBoardAsync()
    {
        AppealSettings? settings = null;
        if (IsManager && await RunAsync(async () => settings = await _appeals.CreateBoardAsync()))
        {
            ApplySettings(settings!);
            Show(L(StringKeys.BanAppealsBoardSaved), MessageTone.Success);
        }
    }

    [RelayCommand]
    private async Task SavePluginVersionsAsync()
    {
        if (!IsManager)
        {
            return;
        }

        if (PublicBanService.ParsePluginVersions(PluginVersions) is not { } versions)
        {
            Show(L(StringKeys.BanAppealsPluginVersionsFormat), MessageTone.Info);
            return;
        }

        AppealSettings? settings = null;
        if (await RunAsync(async () => settings = await _appeals.SetPluginVersionsAsync(versions)))
        {
            ApplySettings(settings!);
            Show(L(StringKeys.BanAppealsPluginVersionsSaved), MessageTone.Success);
        }
    }
}

/// <summary>Ein Einspruch: Ban, Status (Punkt), von wem, Nachweis, Zeit, Ban-Status, Begründung; Aktionen.</summary>
public sealed partial class BanAppealItemViewModel
{
    private readonly BanAppeal _appeal;
    private readonly BanAppealsViewModel _owner;

    public BanAppealItemViewModel(BanAppeal appeal, BanAppealsViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(appeal);
        ArgumentNullException.ThrowIfNull(owner);
        _appeal = appeal;
        _owner = owner;
        var s = owner.App;
        StatusText = s.T("BanAppeals_Status_" + appeal.Status);
        Meta = string.Join(" · ", new[]
        {
            appeal.UserName is { } user ? s.Format(StringKeys.BanAppealsBy, user) : null,
            appeal.Proof is { } proof ? s.T("BanAppeals_Proof_" + proof) : null,
            owner.Time(appeal.CreatedAt),
            appeal.BanStatus is { } status ? s.T("GameBans_Status_" + status) : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public string Id => _appeal.Id;

    public string Title => _appeal.BanName ?? _appeal.UserName ?? _appeal.Id;

    public string StatusText { get; }

    public IBrush StatusBrush => UiResources.Brush(_appeal.Status switch
    {
        PublicBanService.AppealOpen => "RoGGoldBrush",
        PublicBanService.AppealAccepted => "RoGGreenBrush",
        _ => "RoGMutedBrush",
    });

    public string AutomationName => $"{Title}, {StatusText}";

    public string Meta { get; }

    public string Note => _appeal.DecisionNote ?? string.Empty;

    public bool HasNote => !string.IsNullOrEmpty(_appeal.DecisionNote);

    /// <summary>Offen und laut Server entscheidbar (Geltung, Rang).</summary>
    public bool CanDecide => _appeal.IsOpen && _appeal.CanDecide;

    public bool HasThread => _appeal.ThreadUrl is not null;

    public bool HasBan => _appeal.BanUrl is not null;

    [RelayCommand]
    private Task AcceptAsync() => _owner.DecideAsync(this, accept: true);

    [RelayCommand]
    private Task RejectAsync() => _owner.DecideAsync(this, accept: false);

    [RelayCommand]
    private void OpenThread()
    {
        if (_appeal.ThreadUrl is { } url)
        {
            _owner.Open(url);
        }
    }

    [RelayCommand]
    private void OpenBan()
    {
        if (_appeal.BanUrl is { } url)
        {
            _owner.Open(url);
        }
    }
}

/// <summary>Entscheidung über einen Einspruch im Inhalt (statt ContentDialog): Titel, Erklärung, Begründung, ggf. Neu-Einspruch.</summary>
public sealed partial class AppealDecisionViewModel : ObservableObject
{
    private TaskCompletionSource<(string? Note, bool Reappeal)?>? _pending;

    public static int MaxNote => PublicBanService.MaxNote;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Reappeal { get; set; }

    /// <summary>Nur beim Ablehnen: „Neuer Einspruch sofort erlaubt“.</summary>
    [ObservableProperty]
    public partial bool IsReject { get; set; }

    public Task<(string? Note, bool Reappeal)?> AskAsync(string title, string text, bool accept)
    {
        _pending?.TrySetResult(null);
        var pending = new TaskCompletionSource<(string? Note, bool Reappeal)?>();
        _pending = pending;
        Title = title;
        Text = text;
        Note = string.Empty;
        Reappeal = false;
        IsReject = !accept;
        IsOpen = true;
        return pending.Task;
    }

    [RelayCommand]
    private void Accept() => Finish((Note, IsReject && Reappeal));

    [RelayCommand]
    public void Cancel() => Finish(null);

    private void Finish((string? Note, bool Reappeal)? result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}
