using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Moderation;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Moderationsrechte aus <c>/v1/me</c> (Recht irgendwo + Token-Scope, wie <c>ModerationViewModel</c> der Windows-App).
/// Hierarchie (gleich-/niedriger gestellt, nie Owner/man selbst) prüft der Server.
/// </summary>
public sealed record ModerationAccess(bool CanReports, bool CanWarn, bool CanBan, bool CanLog, bool CanManage)
{
    /// <summary>Irgendein Moderations-Reiter sichtbar (Meldungen/Sperren, Mod-Log oder Wortfilter).</summary>
    public bool Any => CanReports || CanLog || CanManage;

    public static ModerationAccess For(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var me = services.CurrentUser;
        bool Allowed(string permission, string scope) => me is not null && me.CanAnywhere(permission) && me.HasScope(scope);
        return new ModerationAccess(
            Allowed("moderation.reports", "moderation:read"),
            Allowed("moderation.warn", "moderation:write"),
            Allowed("moderation.ban", "moderation:write"),
            Allowed("moderation.log", "moderation:read"),
            Allowed("moderation.manage", "moderation:manage"));
    }
}

/// <summary>
/// Gemeinsame Grundlage der Verwaltungs-Reiter dieser Moderation: Laden beim ersten Öffnen (<see cref="ILazyTab"/>),
/// Rückmeldung und Rückfrage der Verwaltungsseite, Escape über <see cref="CloseOverlay"/>.
/// </summary>
public abstract partial class ModerationTabViewModel : LoadableViewModel, ILazyTab
{
    private bool _loaded;

    protected ModerationTabViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(confirm);
        Feedback = feedback;
        Confirmation = confirm;
        Access = ModerationAccess.For(services);
    }

    public ModerationAccess Access { get; }

    /// <summary>Rückmeldung der Seite (Toast-Ersatz).</summary>
    public FeedbackViewModel Feedback { get; }

    /// <summary>Rückfrage der Seite (Leiste im Inhalt).</summary>
    public InlineConfirmViewModel Confirmation { get; }

    /// <summary>Reiter zeigen? (Recht + Scope)</summary>
    public abstract bool IsAvailable { get; }

    internal AppServices App => Services;

    /// <summary>Erstes Öffnen des Reiters lädt; danach nur „Aktualisieren“/F5.</summary>
    public Task EnsureLoadedAsync()
    {
        if (_loaded || !IsAvailable)
        {
            return Task.CompletedTask;
        }

        _loaded = true;
        return LoadAsync();
    }

    protected bool IsLoaded => _loaded;

    /// <summary>Escape: offene Rückfrage bzw. Bereich schließen; <c>true</c>, wenn etwas geschlossen wurde.</summary>
    public virtual bool CloseOverlay()
    {
        if (Confirmation.IsOpen)
        {
            Confirmation.Cancel();
            return true;
        }

        return false;
    }

    protected Task<bool> ConfirmAsync(string title, string text) =>
        Confirmation.AskAsync(title, text, Services.T(StringKeys.CommonConfirm), Services.T(StringKeys.CommonCancel));

    protected void Show(string message, MessageTone tone) => Feedback.Show(message, tone);

    /// <summary>Aktion ausführen; Fehler als Rückmeldung (bei <c>validation_failed</c> der Text des Servers).</summary>
    internal async Task<bool> RunAsync(Func<Task> action)
    {
        if (await ModerationRun.TryAsync(Services, action) is { } error)
        {
            Show(error, MessageTone.Error);
            return false;
        }

        return true;
    }

    internal string L(string key) => Services.T(key);

    internal string Time(DateTimeOffset? time, string format = "g") => time?.ToLocalTime().ToString(format, Services.Culture) ?? string.Empty;
}

/// <summary>Fehlerbehandlung für Moderationsaufrufe (eine Stelle für Reiter und Mitglied-Bereich).</summary>
internal static class ModerationRun
{
    /// <summary>Führt aus; <c>null</c> = ok, sonst der übersetzte Fehlertext.</summary>
    public static async Task<string?> TryAsync(AppServices services, Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (ApiProblemException ex)
        {
            return ex.Problem.Code == "validation_failed" && !string.IsNullOrWhiteSpace(ex.Problem.Detail)
                ? ex.Problem.Detail
                : services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException
            or ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        {
            return services.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown);
        }
    }
}

// --- Meldungen -----------------------------------------------------------------------------------------------------

/// <summary>
/// Reiter „Meldungen“ (Verwaltung, wie Windows): Filter Status/Grund/Herkunft, offene Kinderschutz-Meldungen oben,
/// erledigen/verwerfen mit Notiz, gemeldeten Inhalt öffnen, „Mitglied moderieren“ (Bereich <see cref="Member"/>).
/// Sichtbar mit <c>moderation.reports</c> + <c>moderation:read</c>.
/// </summary>
public sealed partial class ReportsAdminViewModel : ModerationTabViewModel
{
    private readonly ModerationService _moderation;
    private string? _cursor;
    private bool _suppressReload;

    public ReportsAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _moderation = new ModerationService(services.Api.Client);
        Member = new MemberModerationViewModel(services, feedback, Access);
        StatusOptions =
        [
            new("open", L(StringKeys.ModStatusOpen)),
            new("resolved", L(StringKeys.ModStatusResolved)),
            new("dismissed", L(StringKeys.ModStatusDismissed)),
        ];
        ReasonOptions =
        [
            new(string.Empty, L(StringKeys.ModReasonAll)),
            new(ModerationService.ChildSafety, L("Report_Reason_" + ModerationService.ChildSafety)),
        ];
        SourceOptions =
        [
            new(string.Empty, L(StringKeys.ModSourceAll)),
            .. ModerationService.ReportSources.Select(s => new SettingsOption(s, L("Mod_Source_" + s))),
        ];
        _suppressReload = true;
        SelectedStatus = StatusOptions[0];
        SelectedReason = ReasonOptions[0];
        SelectedSource = SourceOptions[0];
        _suppressReload = false;
    }

    public override bool IsAvailable => Access.CanReports;

    /// <summary>„Mitglied moderieren“: Punkte, Verwarnungen, Sperren, neue Maßnahme – Bereich im Reiter.</summary>
    public MemberModerationViewModel Member { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public IReadOnlyList<SettingsOption> ReasonOptions { get; }

    public IReadOnlyList<SettingsOption> SourceOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedReason { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedSource { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value) => Reload();

    partial void OnSelectedReasonChanged(SettingsOption value) => Reload();

    partial void OnSelectedSourceChanged(SettingsOption value) => Reload();

    private void Reload()
    {
        if (!_suppressReload && IsLoaded)
        {
            _ = LoadAsync();
        }
    }

    public ObservableCollection<ReportItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsLoadingMore { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await FetchAsync(null, cancellationToken);
        Items.Clear();
        Append(page);
    }

    private Task<Page<ModReport>> FetchAsync(string? cursor, CancellationToken cancellationToken) =>
        _moderation.ReportsAsync(
            SelectedStatus.Value,
            cursor,
            SelectedReason.Value is { Length: > 0 } reason ? reason : null,
            SelectedSource.Value is { Length: > 0 } source ? source : null,
            cancellationToken);

    private void Append(Page<ModReport> page)
    {
        foreach (var report in page.Items)
        {
            Items.Add(new ReportItemViewModel(report, this));
        }

        _cursor = page.NextCursor;
        HasMore = page.NextCursor is not null;
    }

    private bool CanLoadMore() => !IsLoadingMore;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            Page<ModReport>? page = null;
            if (await RunAsync(async () => page = await FetchAsync(cursor, CancellationToken.None)))
            {
                Append(page!);
            }
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    internal async Task SetStatusAsync(ReportItemViewModel item, string status)
    {
        if (await RunAsync(() => _moderation.UpdateReportAsync(item.Id, status, item.Resolution)))
        {
            Items.Remove(item);
            DataChanged();
            Show(L(StringKeys.ModReportUpdated), MessageTone.Success);
        }
    }

    internal Task OpenMemberAsync(string userId, string? reportId) => Member.OpenAsync(userId, reportId);

    public override bool CloseOverlay() => base.CloseOverlay() || Member.Close();
}

/// <summary>Eine Meldung: Typ · Grund, Wer/Wann/Wen, Spiel-Meldung, Ausschnitt, Kommentar, Aktionen.</summary>
public sealed partial class ReportItemViewModel : ObservableObject
{
    private readonly ModReport _report;
    private readonly ReportsAdminViewModel _owner;

    public ReportItemViewModel(ModReport report, ReportsAdminViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(owner);
        _report = report;
        _owner = owner;
        var s = owner.App;
        Title = $"{Label(s, "Mod_Type_", report.Type)} · {Label(s, "Report_Reason_", report.Reason)}";
        Meta = string.Join(" · ", new[]
        {
            owner.Time(report.CreatedAt),
            report.Reporter is null ? null : s.Format(StringKeys.ModReportedBy, report.Reporter),
            report.Target is null ? null : s.Format(StringKeys.ModAgainst, report.Target.Name),
        }.Where(x => !string.IsNullOrEmpty(x)));
        GameText = report.Game is { } g
            ? string.Join(" · ", new[]
            {
                g.Reported is { } d ? s.Format(StringKeys.ModGameReported, Player(s, d)) : null,
                g.Reporter is { } r ? s.Format(StringKeys.ModReportedBy, Player(s, r)) : null,
                g.ServerName,
                g.NetworkServer,
            }.Where(x => !string.IsNullOrEmpty(x)))
            : string.Empty;
        Resolution = report.Resolution ?? string.Empty;
    }

    private static string Label(AppServices s, string prefix, string value)
    {
        var text = s.T(prefix + value);
        return text.StartsWith(prefix, StringComparison.Ordinal) ? value : text;
    }

    private static string Player(AppServices s, ModGamePlayer p) =>
        p.Edition is { } e ? $"{p.Name} ({MinecraftAccountsViewModel.EditionText(s, e, null)})" : p.Name;

    public string Id => _report.Id;

    public string Title { get; }

    public string Meta { get; }

    public bool IsChildSafety => _report.IsChildSafety;

    public bool IsGame => _report.FromMinecraft;

    /// <summary>Spiel-Meldung: „Notch (Java) gemeldet · gemeldet von jeb_ · RoG Network · lobby“.</summary>
    public string GameText { get; }

    public string Excerpt => _report.Excerpt ?? string.Empty;

    public bool HasExcerpt => !string.IsNullOrEmpty(_report.Excerpt);

    public string Comment => _report.Comment ?? string.Empty;

    public bool HasComment => !string.IsNullOrEmpty(_report.Comment);

    public bool IsOpen => _report.Status == "open";

    public bool HasUrl => _report.Url is not null;

    public bool CanModerateTarget => _report.Target is not null && (_owner.Access.CanWarn || _owner.Access.CanBan);

    [ObservableProperty]
    public partial string Resolution { get; set; }

    [RelayCommand]
    private Task ResolveAsync() => _owner.SetStatusAsync(this, "resolved");

    [RelayCommand]
    private Task DismissAsync() => _owner.SetStatusAsync(this, "dismissed");

    [RelayCommand]
    private Task ModerateAsync() => _report.Target is { } t ? _owner.OpenMemberAsync(t.Id, _report.Id) : Task.CompletedTask;

    /// <summary>Gemeldeten Inhalt öffnen – in der App, wenn es eine Seite dafür gibt, sonst im Browser.</summary>
    [RelayCommand]
    private Task OpenContentAsync() => _report.Url is { } url ? _owner.App.OpenLinkAsync(url) : Task.CompletedTask;
}

// --- Sperren -------------------------------------------------------------------------------------------------------

/// <summary>
/// Reiter „Sperren“ (wie Windows): aktive Website-/Schreib-/Gameserver-Sperren, aufheben mit Rückfrage
/// (<c>moderation.ban</c>), Mitglied öffnen. Sichtbar mit <c>moderation.reports</c> + <c>moderation:read</c>.
/// </summary>
public sealed partial class ModBansAdminViewModel : ModerationTabViewModel
{
    private const int MaxItems = 300;
    private readonly ModerationService _moderation;

    public ModBansAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _moderation = new ModerationService(services.Api.Client);
        Member = new MemberModerationViewModel(services, feedback, Access);
    }

    public override bool IsAvailable => Access.CanReports;

    public MemberModerationViewModel Member { get; }

    public ObservableCollection<ModBanItemViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var all = new List<ModBan>();
        string? cursor = null;
        do
        {
            var page = await _moderation.BansAsync(cursor, cancellationToken);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null && all.Count < MaxItems);

        Items.Clear();
        foreach (var ban in all)
        {
            Items.Add(new ModBanItemViewModel(ban, this));
        }
    }

    internal async Task LiftAsync(ModBanItemViewModel item)
    {
        if (await ConfirmAsync(L(StringKeys.ModLiftTitle), item.Title) && await RunAsync(() => _moderation.LiftBanAsync(item.Id)))
        {
            Items.Remove(item);
            DataChanged();
            Show(L(StringKeys.ModLifted), MessageTone.Success);
        }
    }

    internal Task OpenMemberAsync(string userId) => Member.OpenAsync(userId, null);

    public override bool CloseOverlay() => base.CloseOverlay() || Member.Close();
}

public sealed partial class ModBanItemViewModel
{
    private readonly ModBan _ban;
    private readonly ModBansAdminViewModel _owner;

    public ModBanItemViewModel(ModBan ban, ModBansAdminViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(ban);
        ArgumentNullException.ThrowIfNull(owner);
        _ban = ban;
        _owner = owner;
        var s = owner.App;
        Title = $"{ban.User?.Name ?? "—"} · {s.T("Mod_Scope_" + ban.Scope)}";
        Meta = string.Join(" · ", new[]
        {
            ban.Reason,
            ban.EndsAt is { } end ? s.Format(StringKeys.BanUntil, owner.Time(end)) : s.T(StringKeys.BanPermanent),
        }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public string Id => _ban.Id;

    public string Title { get; }

    public string Meta { get; }

    public bool CanLift => _owner.Access.CanBan;

    public bool CanOpen => _ban.User is not null && (_owner.Access.CanWarn || _owner.Access.CanBan || _owner.Access.CanReports);

    [RelayCommand]
    private Task LiftAsync() => _owner.LiftAsync(this);

    [RelayCommand]
    private Task OpenAsync() => _ban.User is { } u ? _owner.OpenMemberAsync(u.Id) : Task.CompletedTask;
}

// --- Wortfilter ----------------------------------------------------------------------------------------------------

/// <summary>
/// Reiter „Wortfilter“ (gilt für Forum, News-Kommentare, Direktnachrichten, Profiltexte): Muster mit Aktion ersetzen/
/// blockieren/prüfen lassen anlegen, löschen mit Rückfrage. Sichtbar mit <c>moderation.manage</c> + <c>moderation:manage</c>.
/// </summary>
public sealed partial class WordFilterViewModel : ModerationTabViewModel
{
    private readonly ModerationService _moderation;

    public WordFilterViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _moderation = new ModerationService(services.Api.Client);
        Actions =
        [
            new("replace", L(StringKeys.ModWordReplace)),
            new("block", L(StringKeys.ModWordBlock)),
            new("review", L(StringKeys.ModWordReview)),
        ];
        NewAction = Actions[0];
    }

    public override bool IsAvailable => Access.CanManage;

    public IReadOnlyList<SettingsOption> Actions { get; }

    public ObservableCollection<WordItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewPattern { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplace))]
    public partial SettingsOption NewAction { get; set; }

    /// <summary>Ersatz nur bei „ersetzen“.</summary>
    public bool IsReplace => NewAction.Value == "replace";

    [ObservableProperty]
    public partial string NewReplacement { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsSaving { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var words = await _moderation.WordsAsync(cancellationToken);
        Items.Clear();
        foreach (var word in words)
        {
            Items.Add(new WordItemViewModel(word, this));
        }
    }

    private bool CanAdd() => !IsSaving && !string.IsNullOrWhiteSpace(NewPattern);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        IsSaving = true;
        try
        {
            if (await RunAsync(() => _moderation.AddWordAsync(NewPattern, NewAction.Value, NewReplacement)))
            {
                NewPattern = NewReplacement = string.Empty;
                await LoadAsync(silent: true);
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    internal async Task DeleteAsync(WordItemViewModel item)
    {
        if (await ConfirmAsync(L(StringKeys.ModWordDeleteTitle), item.Pattern) && await RunAsync(() => _moderation.DeleteWordAsync(item.Id)))
        {
            Items.Remove(item);
            DataChanged();
        }
    }
}

public sealed partial class WordItemViewModel(WordFilter word, WordFilterViewModel owner)
{
    public string Id => word.Id;

    public string Pattern => word.Pattern;

    public string Meta { get; } = owner.L("Mod_Word_" + word.Action) + (word.Replacement is { } r ? $" → {r}" : string.Empty);

    public string DeleteName { get; } = $"{owner.L("Backup_Delete.Content")}: {word.Pattern}";

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}

// --- Mod-Log -------------------------------------------------------------------------------------------------------

/// <summary>Reiter „Mod-Log“ (wie Windows): Zeit · wer · Aktion · wen. Sichtbar mit <c>moderation.log</c> + <c>moderation:read</c>.</summary>
public sealed partial class ModLogViewModel : ModerationTabViewModel
{
    private readonly ModerationService _moderation;
    private string? _cursor;

    public ModLogViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _moderation = new ModerationService(services.Api.Client);
    }

    public override bool IsAvailable => Access.CanLog;

    public ObservableCollection<string> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _moderation.LogAsync(null, cancellationToken);
        Items.Clear();
        Append(page);
    }

    private void Append(Page<ModLogEntry> page)
    {
        foreach (var e in page.Items)
        {
            Items.Add(string.Join(" · ", new[] { Time(e.CreatedAt), e.Actor, e.Action, e.Target }.Where(s => !string.IsNullOrEmpty(s))));
        }

        _cursor = page.NextCursor;
        HasMore = page.NextCursor is not null;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is { } cursor)
        {
            Page<ModLogEntry>? page = null;
            if (await RunAsync(async () => page = await _moderation.LogAsync(cursor)))
            {
                Append(page!);
            }
        }
    }
}

// --- Mitglied moderieren -------------------------------------------------------------------------------------------

/// <summary>
/// Mitglied moderieren (wie <c>MemberModerationDialog</c> der Windows-App) als Bereich im Reiter: aktive Punkte,
/// Verwarnungen (zurücknehmen), Sperren (aufheben), neue Verwarnung (<c>moderation.warn</c>) oder Sperre
/// (<c>moderation.ban</c>). Was erlaubt ist (Hierarchie, nie Owner/man selbst), entscheidet der Server.
/// </summary>
public sealed partial class MemberModerationViewModel : ObservableObject
{
    private static readonly string[] s_scopes = ["forum", "site", "gameserver"];
    private readonly AppServices _services;
    private readonly FeedbackViewModel _feedback;
    private readonly ModerationService _moderation;
    private string? _userId;
    private string? _reportId;

    public MemberModerationViewModel(AppServices services, FeedbackViewModel feedback, ModerationAccess access)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(access);
        _services = services;
        _feedback = feedback;
        _moderation = new ModerationService(services.Api.Client);
        CanWarn = access.CanWarn;
        CanBan = access.CanBan;
        ScopeOptions = s_scopes.Select(s => new SettingsOption(s, services.T("Mod_Scope_" + s))).ToList();
        SelectedScope = ScopeOptions[0];
    }

    public bool CanWarn { get; }

    public bool CanBan { get; }

    public bool CanAct => CanWarn || CanBan;

    public IReadOnlyList<SettingsOption> ScopeOptions { get; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool IsLoaded { get; set; }

    public bool ShowContent => IsLoaded && !IsLoading;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PointsText { get; set; } = string.Empty;

    public ObservableCollection<ModRecordViewModel> Warnings { get; } = [];

    public ObservableCollection<ModRecordViewModel> Bans { get; } = [];

    [ObservableProperty]
    public partial bool HasNoWarnings { get; set; }

    [ObservableProperty]
    public partial bool HasNoBans { get; set; }

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? Points { get; set; } = 1;

    [ObservableProperty]
    public partial decimal? Days { get; set; } = 0;

    [ObservableProperty]
    public partial SettingsOption SelectedScope { get; set; }

    [ObservableProperty]
    public partial bool IsPublic { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WarnCommand), nameof(BanCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Bereich für ein Mitglied öffnen (aus einer Meldung mit <paramref name="reportId"/>: Verwarnung verweist darauf).</summary>
    public async Task OpenAsync(string userId, string? reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _userId = userId;
        _reportId = reportId;
        Reason = string.Empty;
        Points = 1;
        Days = 0;
        IsPublic = false;
        SelectedScope = ScopeOptions[0];
        Error = null;
        IsLoaded = false;
        IsOpen = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_userId is not { } userId)
        {
            return;
        }

        IsLoading = true;
        ModMember? member = null;
        var error = await ModerationRun.TryAsync(_services, async () => member = await _moderation.MemberAsync(userId));
        IsLoading = false;
        if (userId != _userId)
        {
            return;
        }

        if (member is null)
        {
            Error = error;
            return;
        }

        Name = member.User.Name;
        PointsText = _services.Format(StringKeys.ModActivePoints, member.ActivePoints);
        Warnings.Clear();
        foreach (var w in member.Warnings)
        {
            var text = string.Join(" · ", new[]
            {
                $"{w.Points} P",
                w.Reason,
                w.CreatedAt?.ToLocalTime().ToString("d", _services.Culture),
                w.Active ? null : _services.T(StringKeys.ModInactive),
            }.Where(s => !string.IsNullOrEmpty(s)));
            Warnings.Add(new ModRecordViewModel(text, w.Active && CanWarn, _services.T(StringKeys.ModRevoke), () => RevokeAsync(w.Id)));
        }

        Bans.Clear();
        foreach (var b in member.Bans)
        {
            var text = string.Join(" · ", new[]
            {
                _services.T("Mod_Scope_" + b.Scope),
                b.Reason,
                b.Active ? null : _services.T(StringKeys.ModInactive),
            }.Where(s => !string.IsNullOrEmpty(s)));
            Bans.Add(new ModRecordViewModel(text, b.Active && CanBan, _services.T(StringKeys.BanLiftAction), () => LiftAsync(b.Id)));
        }

        HasNoWarnings = Warnings.Count == 0;
        HasNoBans = Bans.Count == 0;
        IsLoaded = true;
    }

    private async Task<bool> RevokeAsync(string warningId)
    {
        if (await ModerationRun.TryAsync(_services, () => _moderation.RevokeWarningAsync(warningId)) is { } error)
        {
            Error = error;
            return false;
        }

        _feedback.Show(_services.T(StringKeys.ModRevoked), MessageTone.Success);
        return true;
    }

    private async Task<bool> LiftAsync(string banId)
    {
        if (await ModerationRun.TryAsync(_services, () => _moderation.LiftBanAsync(banId)) is { } error)
        {
            Error = error;
            return false;
        }

        _feedback.Show(_services.T(StringKeys.ModLifted), MessageTone.Success);
        return true;
    }

    private bool CanSubmit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task WarnAsync() => ActAsync(ban: false);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task BanAsync() => ActAsync(ban: true);

    private async Task ActAsync(bool ban)
    {
        if (_userId is not { } userId || (ban ? !CanBan : !CanWarn))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            Error = _services.T(StringKeys.ModReasonMissing);
            return;
        }

        IsBusy = true;
        Error = null;
        var days = (int)Math.Clamp(Days ?? 0, 0, 3650);
        var error = await ModerationRun.TryAsync(_services, () => ban
            ? _moderation.BanAsync(userId, SelectedScope.Value, Reason, days, IsPublic)
            : _moderation.WarnAsync(userId, Reason, (int)Math.Clamp(Points ?? 1, 0, 100), days, _reportId));
        IsBusy = false;
        if (error is not null)
        {
            Error = error;
            return;
        }

        _feedback.Show(_services.T(ban ? StringKeys.ModBanned : StringKeys.ModWarned), MessageTone.Success);
        IsOpen = false;
    }

    /// <summary>Schließen (auch Escape); <c>true</c>, wenn offen war.</summary>
    public bool Close()
    {
        if (!IsOpen)
        {
            return false;
        }

        IsOpen = false;
        _userId = null;
        return true;
    }

    [RelayCommand]
    private void Cancel() => Close();
}

/// <summary>Verwarnung bzw. Sperre im Mitglied-Bereich mit „Zurücknehmen“/„Aufheben“ (einmalig).</summary>
public sealed partial class ModRecordViewModel : ObservableObject
{
    private readonly Func<Task<bool>> _act;

    public ModRecordViewModel(string text, bool canAct, string actionLabel, Func<Task<bool>> act)
    {
        Text = text;
        ActionLabel = actionLabel;
        CanAct = canAct;
        _act = act;
    }

    public string Text { get; }

    public string ActionLabel { get; }

    [ObservableProperty]
    public partial bool CanAct { get; set; }

    [RelayCommand]
    private async Task ActAsync()
    {
        if (CanAct && await _act())
        {
            CanAct = false;
        }
    }
}
