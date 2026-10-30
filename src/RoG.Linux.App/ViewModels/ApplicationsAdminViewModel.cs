using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Applications;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung – Reiter „Bewerbungen“ für die Squad-Leitung (wie <c>ApplicationsAdminViewModel</c> der Windows-App):
/// Liste nach Status mit Suche, Detail mit Vote-Stand, Antworten, Sperr-Check, Aktivität, Trial-Aufgaben, Ablauf und
/// Nachrichten/Notizen. Entscheiden (Rückfrage im Inhalt mit Nachricht und – bei „verlängern“ – Tagen), erinnern,
/// Aufgaben und Nachrichten nur mit <c>applications.review</c> + Scope <c>applications:write</c>. Welche Squads man sieht
/// und ob ein Schritt gerade erlaubt ist, entscheidet der Server. Logik aus <see cref="ApplicationAdminService"/>.
/// </summary>
public sealed partial class ApplicationsAdminViewModel : LoadableViewModel, ILazyTab, IOverlayTab, IDetailPage
{
    private static readonly string[] s_statuses = ["open", "submitted", "trial", "voting", "accepted", "rejected"];
    private readonly ApplicationAdminService _admin;
    private string? _cursor;
    private int _generation;
    private int _detailGeneration;
    private bool _loaded;
    private bool _restoring;

    public ApplicationsAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services)
    {
        _admin = new ApplicationAdminService(services.Api.Client, services.Api.Http);
        Feedback = feedback;
        Confirm = confirm;
        StatusOptions = s_statuses
            .Select(s => new SettingsOption(s, services.T("AppAdmin_Status_" + s))).ToList();
        DecisionOptions = ApplicationAdminService.Decisions
            .Select(d => new SettingsOption(d, services.T("AppAdmin_Decision_" + d))).ToList();
        SelectedStatus = StatusOptions[0];
    }

    public FeedbackViewModel Feedback { get; }

    public InlineConfirmViewModel Confirm { get; }

    /// <summary>Rückmeldung und Rückfrage im Reiter zeigen (Standard). <c>false</c>, wenn die Seite die gemeinsamen selbst zeigt.</summary>
    public bool ShowBars { get; set; } = true;

    /// <summary>Reiter sichtbar: <c>applications.view</c> oder <c>applications.review</c> (global oder im Squad) + Scope <c>applications:read</c>.</summary>
    public bool CanView => Services.CurrentUser is { } me
        && (me.CanAnywhere("applications.view") || me.CanAnywhere("applications.review")) && me.HasScope("applications:read");

    /// <summary>Entscheiden, Aufgaben, Nachrichten: <c>applications.review</c> + Scope <c>applications:write</c>.</summary>
    public bool CanReview => Services.CurrentUser is { } me && me.CanAnywhere("applications.review") && me.HasScope("applications:write");

    /// <summary>Erinnern: wie der Knopf der Website schon mit <c>applications.view</c> (+ Schreib-Scope).</summary>
    public bool CanRemind => Services.CurrentUser is { } me
        && (me.CanAnywhere("applications.view") || me.CanAnywhere("applications.review")) && me.HasScope("applications:write");

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public IReadOnlyList<SettingsOption> DecisionOptions { get; }

    public ObservableCollection<ApplicationAdminRowViewModel> Items { get; } = [];

    public ObservableCollection<ApplicationAnswerViewModel> Answers { get; } = [];

    public ObservableCollection<ApplicationCheckViewModel> Checks { get; } = [];

    public ObservableCollection<ApplicationTaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<ApplicationEntryViewModel> Timeline { get; } = [];

    public ObservableCollection<ApplicationEntryViewModel> Messages { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial ApplicationAdminRowViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    public partial bool IsDetailLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    public partial ApplicationDetail? Detail { get; set; }

    public bool ShowDetail => Detail is not null && !IsDetailLoading;

    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVote))]
    public partial string VoteText { get; set; } = string.Empty;

    public bool HasVote => VoteText.Length > 0;

    [ObservableProperty]
    public partial string ActivityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTasks { get; set; }

    [ObservableProperty]
    public partial bool HasMessages { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTask { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string NewMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MessageInternal { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DecideCommand))]
    public partial SettingsOption? SelectedDecision { get; set; }

    /// <summary>Rückfrage zur Entscheidung im Inhalt: Titel = Entscheidung, Nachricht an die bewerbende Person, Tage.</summary>
    [ObservableProperty]
    public partial bool IsDecisionOpen { get; set; }

    [ObservableProperty]
    public partial string DecisionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DecisionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DecisionNeedsDays { get; set; }

    [ObservableProperty]
    public partial decimal? DecisionDays { get; set; } = 7;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DecideCommand), nameof(ConfirmDecisionCommand), nameof(RemindCommand), nameof(SendMessageCommand), nameof(AddTaskCommand))]
    public partial bool IsBusy { get; set; }

    protected override bool HasData => Items.Count > 0;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    partial void OnSelectedStatusChanged(SettingsOption? value)
    {
        if (_loaded)
        {
            Selected = null;
            _ = LoadAsync();
        }
    }

    partial void OnSelectedChanged(ApplicationAdminRowViewModel? value)
    {
        if (_restoring)
        {
            return;
        }

        IsDecisionOpen = false;
        if (value is not null)
        {
            _ = OpenAsync(value.Id);
        }
        else
        {
            Detail = null;
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanView)
        {
            Items.Clear();
            HasMore = false;
            throw new ApiProblemException(new ApiProblem { Code = "forbidden", Status = 403 });
        }

        var generation = ++_generation;
        var page = await _admin.ListAsync(SelectedStatus?.Value, Search, null, cancellationToken);
        if (generation != _generation)
        {
            return;
        }

        // Auswahl beim Neuladen behalten (nach einer Entscheidung), ohne das Detail erneut zu holen
        var previous = Selected;
        _restoring = true;
        try
        {
            Items.Clear();
            foreach (var a in page.Items)
            {
                Items.Add(new ApplicationAdminRowViewModel(a, Services));
            }

            if (previous is not null)
            {
                Selected = Items.FirstOrDefault(i => i.Id == previous.Id) ?? previous;
            }
        }
        finally
        {
            _restoring = false;
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private Task SearchNowAsync()
    {
        _loaded = true;
        Selected = null;
        return LoadAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is not { } cursor || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        var generation = _generation;
        try
        {
            await RunAsync(async () =>
            {
                var page = await _admin.ListAsync(SelectedStatus?.Value, Search, cursor);
                if (generation != _generation)
                {
                    return;
                }

                foreach (var a in page.Items)
                {
                    Items.Add(new ApplicationAdminRowViewModel(a, Services));
                }

                _cursor = page.NextCursor;
                HasMore = page.HasMore;
            });
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task OpenAsync(string id, bool quiet = false)
    {
        var generation = ++_detailGeneration;
        IsDetailLoading = !quiet;
        try
        {
            ApplicationDetail? loaded = null;
            if (await RunAsync(async () => loaded = await _admin.DetailAsync(id)) && generation == _detailGeneration && loaded is { } d)
            {
                Show(d);
            }
        }
        finally
        {
            if (generation == _detailGeneration)
            {
                IsDetailLoading = false;
            }
        }
    }

    private void Show(ApplicationDetail d)
    {
        Header = $"{d.Applicant} → {d.Squad}";
        Subtitle = string.Join(" · ", new[]
        {
            Services.T("AppAdmin_Status_" + d.Status),
            d.Age > 0 ? Services.Format(StringKeys.VotesAge, d.Age) : null,
            d.Referral is { } r ? Services.Format("Linux_AppAdminReferral", r) : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        VoteText = d.Vote is { } v
            ? Services.Format(StringKeys.AppAdminVote, v.Yes, v.No, v.Abstain, v.Participation, v.QuorumPercent, Services.T("AppAdmin_Result_" + v.Result))
                + (v.Missing.Count > 0 ? "\n" + Services.Format(StringKeys.AppAdminMissing, string.Join(", ", v.Missing)) : string.Empty)
            : string.Empty;
        ActivityText = d.ActivityMinutes.Count == 0
            ? Services.T(StringKeys.AppAdminNoActivity)
            : string.Join(" · ", d.ActivityMinutes.Select(a => $"{Services.T("AppAdmin_Source_" + a.Key)} {(a.Value / 60.0).ToString("0.0", Services.Culture)} h"));

        Answers.Clear();
        foreach (var (question, answer) in d.Answers)
        {
            Answers.Add(new ApplicationAnswerViewModel(question, answer));
        }

        Checks.Clear();
        foreach (var (_, status, message) in d.BanCheck)
        {
            Checks.Add(new ApplicationCheckViewModel(message, status == "ok"));
        }

        Tasks.Clear();
        foreach (var t in d.Tasks)
        {
            Tasks.Add(new ApplicationTaskItemViewModel(t, this));
        }

        HasTasks = Tasks.Count > 0;
        Timeline.Clear();
        foreach (var t in d.Timeline)
        {
            var hidden = t.Label.StartsWith("🔒 ", StringComparison.Ordinal);
            Timeline.Add(new ApplicationEntryViewModel(
                hidden ? t.Label[3..] : t.Label,
                t.At?.ToLocalTime().ToString("g", Services.Culture) ?? string.Empty,
                null,
                hidden));
        }

        Messages.Clear();
        foreach (var m in d.Messages)
        {
            Messages.Add(new ApplicationEntryViewModel(
                m.Author,
                m.At?.ToLocalTime().ToString("g", Services.Culture) ?? string.Empty,
                m.Body,
                m.Internal));
        }

        HasMessages = Messages.Count > 0;
        Detail = d;
    }

    private bool CanDecide() => CanReview && SelectedDecision is not null && !IsBusy;

    /// <summary>„Ausführen“: öffnet die Rückfrage im Inhalt (Nachricht, ggf. Tage).</summary>
    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Decide()
    {
        if (Detail is null || SelectedDecision is not { } decision)
        {
            return;
        }

        Confirm.Cancel();
        DecisionTitle = decision.Label;
        DecisionMessage = string.Empty;
        DecisionNeedsDays = decision.Value == "extend";
        DecisionDays = 7;
        IsDecisionOpen = true;
    }

    private bool CanConfirmDecision() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConfirmDecision))]
    private async Task ConfirmDecisionAsync()
    {
        if (Detail is not { } d || SelectedDecision is not { } decision)
        {
            IsDecisionOpen = false;
            return;
        }

        int? days = DecisionNeedsDays && DecisionDays is { } value ? (int)Math.Clamp(value, 1, 90) : null;
        IsBusy = true;
        try
        {
            if (await RunAsync(() => _admin.DecideAsync(d.Id, decision.Value, DecisionMessage, days)))
            {
                IsDecisionOpen = false;
                Feedback.Show(Services.T(StringKeys.AppAdminDecided), MessageTone.Success);
                await OpenAsync(d.Id, quiet: true);
                await LoadAsync(silent: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDecision() => IsDecisionOpen = false;

    private bool CanRemindNow() => CanRemind && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRemindNow))]
    private async Task RemindAsync()
    {
        if (Detail is { } d && await RunAsync(() => _admin.RemindAsync(d.Id)))
        {
            Feedback.Show(Services.T(StringKeys.AppAdminReminded), MessageTone.Success);
        }
    }

    private bool CanAddTask() => CanReview && !IsBusy && !string.IsNullOrWhiteSpace(NewTask);

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        if (Detail is { } d && await RunAsync(() => _admin.AddTaskAsync(d.Id, NewTask)))
        {
            NewTask = string.Empty;
            await OpenAsync(d.Id, quiet: true);
        }
    }

    internal async Task<bool> ToggleTaskAsync(ApplicationTaskItemViewModel task, bool done) =>
        Detail is { } d && await RunAsync(() => _admin.SetTaskDoneAsync(d.Id, task.Id, done));

    internal async Task DeleteTaskAsync(ApplicationTaskItemViewModel task)
    {
        if (Detail is { } d
            && await Confirm.AskAsync(Services.T("Linux_AppAdminDeleteTaskTitle"), task.Title, Services.T("Backup_Delete.Content"), Services.T(StringKeys.CommonCancel))
            && await RunAsync(() => _admin.DeleteTaskAsync(d.Id, task.Id)))
        {
            Tasks.Remove(task);
            HasTasks = Tasks.Count > 0;
        }
    }

    private bool CanSendMessage() => CanReview && !IsBusy && !string.IsNullOrWhiteSpace(NewMessage);

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (Detail is { } d && await RunAsync(() => _admin.MessageAsync(d.Id, NewMessage, MessageInternal)))
        {
            NewMessage = string.Empty;
            Feedback.Show(Services.T(MessageInternal ? "Linux_AppAdminNoteSaved" : "Linux_AppAdminMessageSent"), MessageTone.Success);
            await OpenAsync(d.Id, quiet: true);
        }
    }

    [RelayCommand]
    public void CloseDetail() => Selected = null;

    /// <summary>Escape: Entscheidung bzw. Rückfrage schließen, sonst Detail. <c>true</c> = etwas geschlossen.</summary>
    public bool CloseOverlay()
    {
        if (IsDecisionOpen)
        {
            IsDecisionOpen = false;
            return true;
        }

        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        if (Selected is not null)
        {
            Selected = null;
            return true;
        }

        return false;
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(AdminErrors.Text(Services, ex.Problem), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException
            or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Zeile der Bewerbungsliste: „Name → Squad“, Status · eingereicht · Vote-Beteiligung.</summary>
public sealed class ApplicationAdminRowViewModel(ApplicationSummary summary, AppServices services)
{
    public string Id => summary.Id;

    public string Title { get; } = $"{summary.Applicant} → {summary.Squad}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        services.T("AppAdmin_Status_" + summary.Status),
        summary.SubmittedAt?.ToLocalTime().ToString("d", services.Culture),
        summary.Participation is { } p ? $"{p} %" : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public bool IsVoting => summary.Status == "voting";
}

public sealed record ApplicationAnswerViewModel(string Question, string Answer);

/// <summary>Ergebnis des Sperr-Checks (ok = grün, sonst Warnung in Gold).</summary>
public sealed record ApplicationCheckViewModel(string Message, bool Ok)
{
    public bool Warn => !Ok;
}

/// <summary>Eintrag im Ablauf bzw. Nachricht/Notiz; <c>Internal</c> = nur für die Leitung sichtbar (Schloss).</summary>
public sealed record ApplicationEntryViewModel(string Title, string When, string? Body, bool Internal)
{
    public bool HasBody => !string.IsNullOrEmpty(Body);
}

/// <summary>Trial-Aufgabe: abhaken (sofort gespeichert, bei Fehler zurück), löschen mit Rückfrage.</summary>
public sealed partial class ApplicationTaskItemViewModel : ObservableObject
{
    private readonly ApplicationsAdminViewModel _owner;
    private bool _reverting;

    public ApplicationTaskItemViewModel(ApplicationTask task, ApplicationsAdminViewModel owner)
    {
        _owner = owner;
        Id = task.Id;
        Title = task.Title;
        _reverting = true;
        Done = task.Done;
        _reverting = false;
    }

    public string Id { get; }

    public string Title { get; }

    public bool CanEdit => _owner.CanReview;

    [ObservableProperty]
    public partial bool Done { get; set; }

    partial void OnDoneChanged(bool value)
    {
        if (!_reverting)
        {
            _ = SaveAsync(value);
        }
    }

    private async Task SaveAsync(bool value)
    {
        if (!await _owner.ToggleTaskAsync(this, value))
        {
            _reverting = true;
            Done = !value;
            _reverting = false;
        }
    }

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteTaskAsync(this);
}
