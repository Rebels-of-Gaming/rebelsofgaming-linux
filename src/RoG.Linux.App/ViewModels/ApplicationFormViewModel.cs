using System.Collections.ObjectModel;
using System.Globalization;
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
/// Bewerbung bei einem Squad (wie <c>ApplicationDialog</c> der Windows-App, Guide 07): Formular aus
/// <see cref="ApplicationService.GetFormAsync"/> – Warteliste, Geburtsdatum mit Mindestalter, Fragen je Typ, Regelwerke
/// bestätigen („lesen“ öffnet die Info-Seite in der App), Trial-Zustimmung, Werber. Fehler der API (Discord fehlt,
/// Sperre, Alter …) erscheinen im Formular. Avalonia hat keinen ContentDialog → Bereich auf der Squad-Seite.
/// </summary>
public sealed partial class ApplicationFormViewModel : LoadableViewModel
{
    private readonly ApplicationService _applications;
    private readonly string _squadSlug;
    private readonly Action<bool> _closed;
    private ApplicationForm? _form;

    public ApplicationFormViewModel(AppServices services, string squadSlug, string? squadName, Action<bool> closed)
        : base(services)
    {
        _applications = new ApplicationService(services.Api.Client, services.Api.Http);
        _squadSlug = squadSlug;
        _closed = closed;
        Title = services.Format(StringKeys.ApplyTitleFor, squadName ?? squadSlug);
        BirthdateLabel = services.Format(StringKeys.ApplyBirthdate, 16);
    }

    public string Title { get; private set; }

    public string BirthdateLabel { get; private set; }

    [ObservableProperty]
    public partial bool IsWaitlist { get; set; }

    [ObservableProperty]
    public partial DateTime? Birthdate { get; set; }

    /// <summary>Beispiel im Datumsformat der App-Sprache (Platzhalter im Datumsfeld).</summary>
    public string BirthdateExample => new DateTime(2004, 5, 17).ToString("d", Services.Culture);

    /// <summary>Spätestes wählbares Geburtsdatum (heute).</summary>
    public DateTime Today { get; } = DateTime.Today;

    public ObservableCollection<ApplicationQuestionViewModel> Questions { get; } = [];

    public ObservableCollection<ApplicationRuleViewModel> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    [ObservableProperty]
    public partial bool TrialConsent { get; set; }

    [ObservableProperty]
    public partial string? Referral { get; set; }

    [ObservableProperty]
    public partial bool IsSubmitting { get; set; }

    /// <summary>Fehler (Pflichtfelder, API) im Formular.</summary>
    public FeedbackViewModel Feedback { get; } = new();

    protected override bool HasData => _form is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var form = await _applications.GetFormAsync(_squadSlug, cancellationToken);
        Title = Services.Format(StringKeys.ApplyTitleFor, form.SquadName);
        BirthdateLabel = Services.Format(StringKeys.ApplyBirthdate, form.MinAge);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BirthdateLabel));
        IsWaitlist = form.Waitlist;

        Questions.Clear();
        foreach (var question in form.Questions)
        {
            Questions.Add(new ApplicationQuestionViewModel(question, Services));
        }

        Rules.Clear();
        foreach (var rule in form.Rules)
        {
            Rules.Add(new ApplicationRuleViewModel(rule, Services));
        }

        OnPropertyChanged(nameof(HasRules));
        _form = form;
    }

    /// <summary>Fehlende Pflichtangaben (Bezeichnungen der Fragen); leer = vollständig, sofern Datum/Regeln/Trial passen.</summary>
    internal IReadOnlyList<string> MissingQuestions() =>
        Questions.Where(q => q.Required && string.IsNullOrWhiteSpace(q.Answer)).Select(q => q.Label).ToList();

    public bool IsComplete => Birthdate is not null && MissingQuestions().Count == 0 && Rules.All(r => r.Accepted) && TrialConsent;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_form is not { } form || IsSubmitting)
        {
            return;
        }

        var missing = MissingQuestions();
        if (!IsComplete)
        {
            Feedback.Show(Services.T(StringKeys.ApplyIncomplete) + (missing.Count > 0 ? " " + string.Join(", ", missing) : string.Empty), MessageTone.Error);
            return;
        }

        IsSubmitting = true;
        try
        {
            await _applications.SubmitAsync(
                form.SquadSlug,
                DateOnly.FromDateTime(Birthdate!.Value.Date),
                Questions.ToDictionary(q => q.Id, q => q.Answer ?? string.Empty),
                Rules.Select(r => r.Id),
                TrialConsent,
                Referral);
            Feedback.Close();
            _closed(true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code is "application_blocked" or "validation_failed" && ex.Problem.Detail is { Length: > 0 } detail
                ? detail
                : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    public void Cancel() => _closed(false);
}

/// <summary>Frage im Formular: Auswahl, Ja/Nein, Zahl, Text oder langer Text; Pflichtfragen mit „*“.</summary>
public sealed partial class ApplicationQuestionViewModel : ObservableObject
{
    public ApplicationQuestionViewModel(FormQuestion question, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(services);
        Id = question.Id;
        Label = question.Label;
        Header = question.Required ? question.Label + " *" : question.Label;
        Help = question.Help;
        Required = question.Required;
        Kind = question.Type switch
        {
            "select" when question.Options.Count > 0 => QuestionKind.Select,
            "yesno" or "boolean" or "checkbox" => QuestionKind.YesNo,
            "number" => QuestionKind.Number,
            "textarea" or "longtext" => QuestionKind.LongText,
            _ => QuestionKind.Text,
        };
        Options = Kind switch
        {
            QuestionKind.Select => question.Options.Select(o => new SettingsOption(o.Value, o.Label)).ToList(),
            QuestionKind.YesNo => [new("yes", services.T(StringKeys.ApplyYes)), new("no", services.T(StringKeys.ApplyNo))],
            _ => [],
        };
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>Bezeichnung mit „*“ bei Pflichtfragen.</summary>
    public string Header { get; }

    public string? Help { get; }

    public bool HasHelp => !string.IsNullOrEmpty(Help);

    public bool Required { get; }

    public QuestionKind Kind { get; }

    public bool IsChoice => Kind is QuestionKind.Select or QuestionKind.YesNo;

    public bool IsNumber => Kind == QuestionKind.Number;

    public bool IsText => Kind == QuestionKind.Text;

    public bool IsLongText => Kind == QuestionKind.LongText;

    public IReadOnlyList<SettingsOption> Options { get; }

    [ObservableProperty]
    public partial SettingsOption? SelectedOption { get; set; }

    [ObservableProperty]
    public partial decimal? Number { get; set; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    /// <summary>Antwort als Text (Auswahlwert, <c>yes</c>/<c>no</c>, Zahl invariant, Text) – <c>null</c> = leer.</summary>
    public string? Answer => Kind switch
    {
        QuestionKind.Select or QuestionKind.YesNo => SelectedOption?.Value,
        QuestionKind.Number => Number?.ToString(CultureInfo.InvariantCulture),
        _ => string.IsNullOrWhiteSpace(Text) ? null : Text,
    };
}

public enum QuestionKind
{
    Text,
    LongText,
    Number,
    Select,
    YesNo,
}

/// <summary>Regelwerk, das mit der Bewerbung bestätigt wird; „lesen“ öffnet es auf der Info-Seite.</summary>
public sealed partial class ApplicationRuleViewModel(FormRule rule, AppServices services) : ObservableObject
{
    public string Id => rule.Id;

    public string Slug => rule.Slug;

    public string Text { get; } = services.Format(StringKeys.ApplyAcceptRules, rule.Title);

    [ObservableProperty]
    public partial bool Accepted { get; set; }

    [RelayCommand]
    private void Read() => services.Navigate(new AppRoute(AppSection.Info, rule.Slug));
}
