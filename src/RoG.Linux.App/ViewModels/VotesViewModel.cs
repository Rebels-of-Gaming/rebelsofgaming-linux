using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Applications;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Abstimmungen (wie <c>VotesViewModel</c> der Windows-App, Kern <see cref="VoteService"/>): offene Votes über
/// Bewerbungen, in denen die Person stimmberechtigt ist – Bewerber → Squad, Alter, Beteiligung/Quorum, Ende, Antworten,
/// Trial-Aufgaben, eigene Stimme; Ja/Nein/Enthaltung (änderbar bis zum Ende). Gäste sehen einen Hinweis zum Anmelden.
/// </summary>
public sealed partial class VotesViewModel(AppServices services, Action requestSignIn) : LoadableViewModel(services)
{
    private readonly VoteService _votes = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<VoteItemViewModel> Items { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyForMember))]
    public partial bool IsGuest { get; set; }

    /// <summary>Leerzustand nur für Angemeldete (Gäste sehen den Hinweis zum Anmelden).</summary>
    public bool IsEmptyForMember => IsEmpty && !IsGuest;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            OnPropertyChanged(nameof(IsEmptyForMember));
            return;
        }

        var votes = await _votes.OpenAsync(cancellationToken);
        Items.Clear();
        foreach (var vote in votes)
        {
            Items.Add(new VoteItemViewModel(vote, this, Services));
        }

        OnPropertyChanged(nameof(IsEmptyForMember));
    }

    internal async Task CastAsync(VoteItemViewModel item, VoteChoice choice)
    {
        if (item.IsBusy)
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            await _votes.CastAsync(item.ApplicationId, choice);
            item.SetMyVote(choice switch
            {
                VoteChoice.Yes => "yes",
                VoteChoice.No => "no",
                _ => "abstain",
            });
            Feedback.Show(Services.T(StringKeys.VotesCast), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private void SignIn() => requestSignIn();
}

/// <summary>Frage und Antwort aus der Bewerbung.</summary>
public sealed record VoteAnswer(string Question, string Answer);

/// <summary>Trial-Aufgabe mit Haken.</summary>
public sealed record VoteTask(string Title, bool Done)
{
    public bool Open => !Done;
}

/// <summary>Karte eines offenen Votes.</summary>
public sealed partial class VoteItemViewModel : ObservableObject
{
    private readonly VotesViewModel _owner;
    private readonly AppServices _services;
    private readonly string? _applicantSlug;

    public VoteItemViewModel(OpenVote vote, VotesViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(vote);
        _owner = owner;
        _services = services;
        _applicantSlug = vote.ApplicantSlug;
        ApplicationId = vote.ApplicationId;
        Applicant = vote.ApplicantName;
        Squad = vote.SquadName;
        Title = $"{vote.ApplicantName} → {vote.SquadName}";
        Meta = string.Join(" · ", new[]
        {
            vote.Age > 0 ? services.Format(StringKeys.VotesAge, vote.Age) : null,
            vote.EndsAt is { } end ? services.Format(StringKeys.VotesEnds, end.ToLocalTime().ToString("g", services.Culture)) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
        Participation = services.Format(StringKeys.VotesParticipation, vote.Participation, vote.QuorumPercent);
        ParticipationValue = Math.Clamp(vote.Participation, 0, 100);
        QuorumMet = vote.QuorumPercent > 0 && vote.Participation >= vote.QuorumPercent;
        // Ja/Nein-Fragen kommen als true/false (Kern: „yes“/„no“) – übersetzt anzeigen
        Answers = vote.Answers.Select(a => new VoteAnswer(a.Question, a.Answer switch
        {
            "yes" => services.T(StringKeys.ApplyYes),
            "no" => services.T(StringKeys.ApplyNo),
            var text => text,
        })).ToList();
        Tasks = vote.Tasks.Select(t => new VoteTask(t.Title, t.Done)).ToList();
        SetMyVote(vote.MyVote);
    }

    public string ApplicationId { get; }

    public string Applicant { get; }

    public string Squad { get; }

    public string Title { get; }

    public string Meta { get; }

    public string Participation { get; }

    public double ParticipationValue { get; }

    public bool QuorumMet { get; }

    public IReadOnlyList<VoteAnswer> Answers { get; }

    public bool HasAnswers => Answers.Count > 0;

    public IReadOnlyList<VoteTask> Tasks { get; }

    public bool HasTasks => Tasks.Count > 0;

    public bool HasProfile => !string.IsNullOrEmpty(_applicantSlug);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMyVote), nameof(IsYes), nameof(IsNo), nameof(IsAbstain))]
    public partial string? MyVoteValue { get; private set; }

    [ObservableProperty]
    public partial string MyVote { get; private set; } = string.Empty;

    public bool HasMyVote => MyVoteValue is not null;

    public bool IsYes => MyVoteValue == "yes";

    public bool IsNo => MyVoteValue == "no";

    public bool IsAbstain => MyVoteValue == "abstain";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    internal void SetMyVote(string? value)
    {
        MyVoteValue = value is "yes" or "no" or "abstain" ? value : null;
        MyVote = MyVoteValue switch
        {
            "yes" => _services.T(StringKeys.VotesMineYes),
            "no" => _services.T(StringKeys.VotesMineNo),
            "abstain" => _services.T(StringKeys.VotesMineAbstain),
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private Task YesAsync() => _owner.CastAsync(this, VoteChoice.Yes);

    [RelayCommand]
    private Task NoAsync() => _owner.CastAsync(this, VoteChoice.No);

    [RelayCommand]
    private Task AbstainAsync() => _owner.CastAsync(this, VoteChoice.Abstain);

    /// <summary>Profil der bewerbenden Person in der App.</summary>
    [RelayCommand]
    private void OpenProfile()
    {
        if (_applicantSlug is { Length: > 0 } slug)
        {
            _services.Navigate(new AppRoute(AppSection.Members, slug));
        }
    }

    public override string ToString() => Title;
}
