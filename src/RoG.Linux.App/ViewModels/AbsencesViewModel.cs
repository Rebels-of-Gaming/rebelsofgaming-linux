using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Absences;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Konto → Abwesenheit (wie <c>AbsencesViewModel</c> der Windows-App, Guide 16): melden, Liste, löschen.</summary>
public sealed partial class AbsencesViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly AbsenceService _absences = new(services.Api.Client);

    public ObservableCollection<AbsenceItemViewModel> Items { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReportCommand))]
    public partial DateTime? From { get; set; } = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReportCommand))]
    public partial DateTime? To { get; set; } = DateTime.Today.AddDays(7);

    [ObservableProperty]
    public partial string? Reason { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReportCommand))]
    public partial bool IsSaving { get; set; }

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
        var list = await _absences.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var a in list)
        {
            Items.Add(new AbsenceItemViewModel(a, this, Services));
        }
    }

    private bool CanReport() => From is not null && To is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanReport))]
    private async Task ReportAsync()
    {
        if (From is not { } from || To is not { } to)
        {
            return;
        }

        if (to.Date < from.Date)
        {
            Feedback.Show(Services.T("Linux_AbsenceRange"), MessageTone.Error);
            return;
        }

        IsSaving = true;
        try
        {
            await _absences.CreateAsync(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), Reason);
            Reason = null;
            Feedback.Show(Services.T(StringKeys.AbsenceReported), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Detail ?? Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    internal async Task DeleteAsync(AbsenceItemViewModel item)
    {
        try
        {
            await _absences.DeleteAsync(item.Id);
            Items.Remove(item);
            DataChanged();
            Feedback.Show(Services.T("Linux_AbsenceDeleted"), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }
}

public sealed partial class AbsenceItemViewModel(Absence absence, AbsencesViewModel owner, AppServices services)
{
    public string Id => absence.Id;

    public bool IsCurrent => absence.IsCurrent;

    public string Period { get; } = $"{absence.StartsOn.ToString("d", services.Culture)} – {absence.EndsOn.ToString("d", services.Culture)}";

    public string Detail { get; } = string.Join(" · ", new[]
    {
        services.T(absence.IsCurrent ? StringKeys.AbsenceActive : StringKeys.AbsenceUpcoming),
        absence.Reason,
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}
