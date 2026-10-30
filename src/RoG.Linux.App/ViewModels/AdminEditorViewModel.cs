using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Allgemeiner Editor für einen Bereich der Website-Verwaltung (<see cref="AdminArea"/>, wie <c>AdminEditorViewModel</c>
/// der Windows-App): Liste mit Suche, Formular aus der Felddefinition (<c>Controls/AdminFieldsView</c>),
/// Anlegen/Speichern/Löschen. Lesen, Prüfen und Body baut <see cref="AdminForm"/> im Kern; Rückfrage vor dem Löschen im
/// Inhalt (<see cref="Confirm"/>).
/// </summary>
public sealed partial class AdminEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AdminResourceService _resources;
    private readonly List<AdminRecordViewModel> _all = [];
    private string? _cursor;
    private AdminFormValues? _original;
    private AdminRecord? _current;
    private bool _suppressOpen;

    public AdminEditorViewModel(AppServices services, AdminArea area)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(area);
        _services = services;
        Area = area;
        _resources = new AdminResourceService(services.Api.Http);
        CanWrite = area.IsWritableBy(services.CurrentUser);
        Title = services.T(area.TitleKey);
        Help = area.HelpKey is null ? null : services.T(area.HelpKey);
    }

    public AdminArea Area { get; }

    public string Title { get; }

    public string? Help { get; }

    public bool HasHelp => !string.IsNullOrEmpty(Help);

    public bool CanWrite { get; }

    public bool CanCreate => CanWrite && Area.CanCreate && !Area.IsSingleton;

    /// <summary>Liste links – nicht bei Einstellungen (ein Objekt).</summary>
    public bool ShowList => !Area.IsSingleton;

    public bool IsSingleton => Area.IsSingleton;

    public bool HasWebPath => Area.WebPath is not null && _services.Environment.AdminBaseUrl is not null;

    public ObservableCollection<AdminRecordViewModel> Records { get; } = [];

    public ObservableCollection<AdminFieldViewModel> Fields { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage vor dem Löschen; Escape bricht ab.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    /// <summary>Etwas wurde gespeichert oder gelöscht.</summary>
    public bool Changed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowRecords), nameof(ShowSingletonLoading))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowRecords), nameof(HasListError))]
    public partial string? ListError { get; set; }

    public bool HasListError => ListError is not null;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    public bool IsEmpty => !IsLoading && ListError is null && Records.Count == 0;

    public bool ShowRecords => !IsLoading && ListError is null && Records.Count > 0;

    public bool ShowSingletonLoading => IsSingleton && IsLoading;

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AdminRecordViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanDelete), nameof(HasNoForm))]
    public partial bool HasForm { get; set; }

    public bool HasNoForm => !HasForm && !Area.IsSingleton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanDelete))]
    public partial bool IsNew { get; set; }

    [ObservableProperty]
    public partial string FormTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DeleteCommand))]
    public partial bool IsSaving { get; set; }

    public bool CanSave => HasForm && CanWrite && (IsNew ? Area.CanCreate : Area.CanUpdate);

    public bool CanDelete => HasForm && CanWrite && !IsNew && Area.CanDelete;

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(AdminRecordViewModel? value)
    {
        if (value is not null && !_suppressOpen)
        {
            _ = OpenAsync(value.Record);
        }
    }

    public async Task LoadAsync()
    {
        if (Area.IsSingleton)
        {
            IsLoading = true;
            await RunAsync(async () => await ShowFormAsync(await _resources.LoadSingletonAsync(Area, _services.Language)));
            IsLoading = false;
            return;
        }

        _cursor = null;
        _all.Clear();
        await LoadPageAsync();
    }

    /// <summary>Was der Editor nicht kann (Reihenfolge, Bilder, verschachtelte Listen), in der Website-Verwaltung öffnen.</summary>
    [RelayCommand]
    private async Task OpenWebAsync()
    {
        if (Area.WebPath is { } path && _services.Environment.AdminBaseUrl is { } admin)
        {
            await _services.Browser.OpenAsync(new Uri(admin, path));
        }
    }

    [RelayCommand]
    private Task MoreAsync() => LoadPageAsync();

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    private async Task LoadPageAsync()
    {
        IsLoading = true;
        try
        {
            var page = await _resources.ListAsync(Area, _services.Language, _cursor);
            _all.AddRange(page.Items.Select(r => new AdminRecordViewModel(r)));
            _cursor = page.NextCursor;
            HasMore = Area.Paged && page.NextCursor is not null;
            ListError = null;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            ListError = ErrorText(ex);
        }
        finally
        {
            IsLoading = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var q = Search.Trim();
        var selected = Selected;
        _suppressOpen = true;
        Records.Clear();
        foreach (var r in _all.Where(r => q.Length == 0 || r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                     || (r.Subtitle?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)))
        {
            Records.Add(r);
        }

        Selected = selected is not null && Records.Contains(selected) ? selected : null;
        _suppressOpen = false;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowRecords));
    }

    private Task OpenAsync(AdminRecord record) => RunAsync(async () =>
    {
        var fresh = await _resources.GetAsync(Area, record, _services.Language);
        await ShowFormAsync(fresh);
    });

    [RelayCommand]
    private async Task NewAsync()
    {
        Selected = null;
        await ShowFormAsync(null);
    }

    /// <summary>Formular schließen (schmale Ansicht: zurück zur Liste).</summary>
    [RelayCommand]
    public void CloseForm()
    {
        if (Area.IsSingleton)
        {
            return;
        }

        Confirm.Cancel();
        HasForm = false;
        Fields.Clear();
        _suppressOpen = true;
        Selected = null;
        _suppressOpen = false;
    }

    private async Task ShowFormAsync(AdminRecord? record)
    {
        Confirm.Cancel();
        _current = record;
        _original = AdminForm.Read(Area, record?.Raw, _services.Language);
        IsNew = record is null;
        FormTitle = Area.IsSingleton ? Title : record?.Title ?? _services.T(StringKeys.AdminFormNew);
        Fields.Clear();
        foreach (var f in Area.Fields)
        {
            var field = new AdminFieldViewModel(f, _original.GetValueOrDefault(f.Key), _services.Localizer, enabled: CanWrite && (IsNew || !f.CreateOnly), _services.Language);
            if (f.Type == AdminFieldType.Reference && f.Reference is { } refKey && AdminAreas.Find(refKey) is { } refArea)
            {
                await field.LoadReferenceAsync(_resources, refArea, _services.Language);
            }

            Fields.Add(field);
        }

        HasForm = true;
    }

    private bool CanRunWrite() => !IsSaving;

    [RelayCommand(CanExecute = nameof(CanRunWrite))]
    private async Task SaveAsync()
    {
        var values = new AdminFormValues();
        foreach (var f in Fields)
        {
            values[f.Field.Key] = f.Value;
        }

        var errors = AdminForm.Validate(Area, values, IsNew);
        foreach (var f in Fields)
        {
            f.Error = errors.TryGetValue(f.Field.Key, out var e) ? _services.T(e) : null;
        }

        if (errors.Count > 0)
        {
            Feedback.Show(_services.T(StringKeys.AdminFormCheckFields), MessageTone.Error);
            return;
        }

        var body = AdminForm.BuildBody(Area, IsNew ? null : _original, values);
        if (!IsNew && body.Count == 0)
        {
            Feedback.Show(_services.T(StringKeys.AdminFormNoChanges), MessageTone.Info);
            return;
        }

        IsSaving = true;
        try
        {
            await RunAsync(async () =>
            {
                var saved = await _resources.SaveAsync(Area, IsNew ? null : _current, body, _services.Language);
                Changed = true;
                Feedback.Show(_services.T(StringKeys.AdminFormSaved), MessageTone.Success);
                await LoadAsync();
                if (Area.IsSingleton)
                {
                    return;
                }

                var again = saved is null ? null : _all.FirstOrDefault(r => r.Record.Id == saved.Id);
                if (again is not null && Records.Contains(again))
                {
                    _suppressOpen = true;
                    Selected = again;
                    _suppressOpen = false;
                    await ShowFormAsync(saved);
                }
                else if (saved is not null)
                {
                    await ShowFormAsync(saved);
                }
            });
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunWrite))]
    private async Task DeleteAsync()
    {
        if (_current is not { } record
            || !await Confirm.AskAsync(
                _services.T(StringKeys.AdminFormDeleteTitle),
                string.Format(CultureInfo.CurrentCulture, _services.T(StringKeys.AdminFormDeleteText), record.Title),
                _services.T(StringKeys.AdminFormDeleteConfirm),
                _services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        IsSaving = true;
        try
        {
            await RunAsync(async () =>
            {
                await _resources.DeleteAsync(Area, record);
                Changed = true;
                CloseForm();
                Feedback.Show(_services.T(StringKeys.AdminFormDeleted), MessageTone.Success);
                await LoadAsync();
            });
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Escape: Rückfrage, sonst (schmal) Formular schließen. <c>true</c> = etwas geschlossen.</summary>
    public bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return false;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            Feedback.Show(ErrorText(ex), MessageTone.Error);
        }
    }

    private string ErrorText(Exception ex) => ex switch
    {
        ApiProblemException p => _services.Localizer.GetError(p.Problem.Code),
        HttpRequestException => _services.Localizer.GetError("network_error"),
        TaskCanceledException => _services.Localizer.GetError("timeout"),
        _ => _services.Localizer.GetError(ApiProblem.Codes.Unknown),
    };

    private static bool IsApiFailure(Exception ex) =>
        ex is ApiProblemException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or RoG.Core.Api.Generated.GeneratedApiException or InvalidOperationException;
}

/// <summary>Zeile der Liste im Editor.</summary>
public sealed class AdminRecordViewModel(AdminRecord record)
{
    public AdminRecord Record => record;

    public string Title => record.Title;

    public string? Subtitle => record.Subtitle;

    public bool HasSubtitle => !string.IsNullOrEmpty(record.Subtitle);
}
