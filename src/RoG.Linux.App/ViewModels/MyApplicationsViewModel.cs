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
/// Konto → Meine Bewerbungen (wie <c>MyApplicationsViewModel</c> der Windows-App, Guide 07): Status mit Schritt,
/// Verlauf, Trial-Aufgaben, Nachrichten mit dem Squad, Nachricht senden, zurückziehen (mit Rückfrage im Inhalt).
/// Lädt beim Aufklappen.
/// </summary>
public sealed partial class MyApplicationsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ApplicationService _applications = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<MyApplicationViewModel> Items { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage „Bewerbung zurückziehen?“.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    protected override bool HasData => Items.Count > 0;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoading)
        {
            _ = LoadAsync(silent: HasData);
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await _applications.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var a in list)
        {
            Items.Add(new MyApplicationViewModel(a, this, Services));
        }
    }

    internal async Task SendAsync(MyApplicationViewModel item, string text)
    {
        if (await RunAsync(() => _applications.SendMessageAsync(item.Id, text)))
        {
            Feedback.Show(Services.T(StringKeys.ApplyMessageSent), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        else
        {
            item.Draft = text; // Entwurf nicht verlieren
        }
    }

    internal async Task WithdrawAsync(MyApplicationViewModel item)
    {
        var ok = await Confirm.AskAsync(
            Services.T("Linux_ApplicationWithdrawTitle"),
            Services.Format("Linux_ApplicationWithdrawText", item.SquadName),
            Services.T("AccountPage_ApplicationWithdraw.Content"),
            Services.T(StringKeys.CommonCancel));
        if (ok && await RunAsync(() => _applications.WithdrawAsync(item.Id)))
        {
            Feedback.Show(Services.T("Linux_ApplicationWithdrawn"), MessageTone.Success);
            await LoadAsync(silent: true);
        }
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
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Eintrag im Verlauf einer Bewerbung.</summary>
public sealed record ApplicationTimelineRow(string Label, string? When)
{
    public bool HasWhen => !string.IsNullOrEmpty(When);
}

/// <summary>Trial-Aufgabe (erledigt/offen).</summary>
public sealed record ApplicationTaskRow(string Title, bool Done);

/// <summary>Nachricht zwischen Bewerber und Squad.</summary>
public sealed record ApplicationMessageRow(string Author, string Body, string? When, bool FromSquad);

public sealed partial class MyApplicationViewModel : ObservableObject
{
    private readonly MyApplicationsViewModel _owner;

    public MyApplicationViewModel(MyApplication app, MyApplicationsViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(app);
        _owner = owner;
        var culture = services.Culture;
        Id = app.Id;
        SquadName = app.SquadName ?? string.Empty;
        Title = string.IsNullOrEmpty(app.SquadName) ? app.StatusLabel : $"{app.SquadName} – {app.StatusLabel}";
        Step = Math.Clamp(app.Step, 0, 4);
        Progress = Step * 25;
        StepText = services.Format("Linux_ApplicationStep", Step);
        Details = string.Join("\n", new[]
        {
            app.SubmittedAt is { } s ? services.Format(StringKeys.ApplySubmittedAt, s.ToLocalTime().ToString("d", culture)) : null,
            app.TrialEndsAt is { } t ? services.Format(StringKeys.ApplyTrialEnds, t.ToLocalTime().ToString("d", culture)) : null,
            app.VoteParticipation is { } v ? services.Format(StringKeys.ApplyVoteParticipation, v) : null,
            app.DecisionMessage,
        }.Where(x => !string.IsNullOrEmpty(x)));
        Timeline = app.Timeline.Select(e => new ApplicationTimelineRow(e.Label, e.At?.ToLocalTime().ToString("g", culture))).ToList();
        Tasks = app.Tasks.Select(t => new ApplicationTaskRow(t.Title, t.Done)).ToList();
        Messages = app.Messages
            .Select(m => new ApplicationMessageRow(m.FromSquad ? m.Author ?? SquadName : services.T(StringKeys.ApplyYou), m.Body, m.At?.ToLocalTime().ToString("g", culture), m.FromSquad))
            .ToList();
        IsOpen = app.IsOpen;
    }

    public string Id { get; }

    public string SquadName { get; }

    public string Title { get; }

    public int Step { get; }

    /// <summary>Schritt 0–4 als Prozent für den Fortschrittsbalken.</summary>
    public double Progress { get; }

    public string StepText { get; }

    public string Details { get; }

    public bool HasDetails => Details.Length > 0;

    public IReadOnlyList<ApplicationTimelineRow> Timeline { get; }

    public bool HasTimeline => Timeline.Count > 0;

    public IReadOnlyList<ApplicationTaskRow> Tasks { get; }

    public bool HasTasks => Tasks.Count > 0;

    public IReadOnlyList<ApplicationMessageRow> Messages { get; }

    public bool HasMessages => Messages.Count > 0;

    /// <summary>Offen (nicht angenommen/abgelehnt/zurückgezogen/abgelaufen): Nachricht senden und zurückziehen möglich.</summary>
    public bool IsOpen { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft;
        Draft = string.Empty;
        await _owner.SendAsync(this, text);
    }

    [RelayCommand]
    private Task WithdrawAsync() => _owner.WithdrawAsync(this);
}
