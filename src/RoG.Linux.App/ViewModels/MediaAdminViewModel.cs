using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung „Inhalte“ wie <c>MediaAdminViewModel</c> der Windows-App, aufgeteilt in zwei eigenständige Reiter:
/// <see cref="Gallery"/> (Galerie-Freigaben, <c>gallery.manage</c>) und <see cref="Uploads"/> (Upload-Meldungen mit
/// Speicherübersicht, <c>uploads.manage</c>). Die Website-Links der Windows-Klasse gehören zum Reiter „Website“.
/// </summary>
public sealed class MediaAdminViewModel
{
    public MediaAdminViewModel(AppServices services, FeedbackViewModel? feedback = null, InlineConfirmViewModel? confirm = null)
    {
        Gallery = new GalleryAdminViewModel(services, feedback, confirm);
        Uploads = new UploadReportsAdminViewModel(services, feedback, confirm);
    }

    public GalleryAdminViewModel Gallery { get; }

    public UploadReportsAdminViewModel Uploads { get; }

    /// <summary>Reiter „Galerie“ zeigen: <c>gallery.manage</c> (irgendwo) und Scope <c>gallery:manage</c> bzw. <c>gallery:write</c>.</summary>
    public bool CanGallery => Gallery.IsAvailable;

    /// <summary>Reiter „Uploads“ zeigen: <c>uploads.manage</c> und Scope <c>uploads:manage</c>.</summary>
    public bool CanUploads => Uploads.IsAvailable;
}

/// <summary>Gemeinsames der beiden Reiter: Feedback/Rückfrage (eigene oder von der Verwaltungsseite), Laden, Fehler.</summary>
public abstract partial class MediaAdminTabViewModel : ObservableObject, ILazyTab, IOverlayTab
{
    private bool _loaded;

    protected MediaAdminTabViewModel(AppServices services, FeedbackViewModel? feedback, InlineConfirmViewModel? confirm)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        Admin = new MediaAdminService(services.Api.Http);
        HasOwnFeedback = feedback is null;
        HasOwnConfirmation = confirm is null;
        Feedback = feedback ?? new FeedbackViewModel();
        Confirmation = confirm ?? new InlineConfirmViewModel();
    }

    public AppServices Services { get; }

    protected MediaAdminService Admin { get; }

    public FeedbackViewModel Feedback { get; }

    public InlineConfirmViewModel Confirmation { get; }

    /// <summary>Kein gemeinsames Feedback übergeben – der Reiter zeigt seine eigene Leiste.</summary>
    public bool HasOwnFeedback { get; }

    /// <summary>Keine gemeinsame Rückfrage übergeben – der Reiter zeigt seine eigene Leiste.</summary>
    public bool HasOwnConfirmation { get; }

    /// <summary>Recht und Scope vorhanden (Sichtbarkeit des Reiters).</summary>
    public abstract bool IsAvailable { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public abstract bool IsEmpty { get; }

    public bool ShowEmpty => IsLoaded && !IsLoading && ErrorMessage is null && IsEmpty;

    /// <summary>Beim ersten Öffnen des Reiters laden.</summary>
    public Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return Task.CompletedTask;
        }

        _loaded = true;
        return LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        _loaded = true;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await LoadCoreAsync();
            IsLoaded = true;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ErrorMessage = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    protected abstract Task LoadCoreAsync();

    /// <summary>Escape: Rückfrage bzw. Meldung schließen; <c>false</c> = nichts offen.</summary>
    public bool CloseOverlay()
    {
        if (Confirmation.IsOpen)
        {
            Confirmation.Cancel();
            return true;
        }

        if (HasOwnFeedback && Feedback.IsOpen)
        {
            Feedback.Close();
            return true;
        }

        return false;
    }

    protected Task<bool> AskAsync(string titleKey, string text) =>
        Confirmation.AskAsync(Services.T(titleKey), text, Services.T(StringKeys.CommonConfirm), Services.T(StringKeys.CommonCancel));

    protected async Task<bool> RunAsync(Func<Task> action, string? success)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null)
            {
                Feedback.Show(Services.T(success), MessageTone.Success);
            }

            return true;
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}

/// <summary>
/// Reiter „Galerie“: eingereichte Bilder/Clips aus allen Alben mit Freigabe, die ich verwalten darf (älteste zuerst) –
/// freigeben oder (nach Rückfrage) ablehnen; Klick aufs Bild öffnet die Vollansicht im Browser.
/// </summary>
public sealed partial class GalleryAdminViewModel(AppServices services, FeedbackViewModel? feedback = null, InlineConfirmViewModel? confirm = null)
    : MediaAdminTabViewModel(services, feedback, confirm)
{
    public ObservableCollection<PendingGalleryItemViewModel> Pending { get; } = [];

    public override bool IsAvailable => MediaAdminService.CanModerateGallery(Services.CurrentUser);

    public override bool IsEmpty => Pending.Count == 0;

    protected override async Task LoadCoreAsync()
    {
        var items = await Admin.PendingAsync();
        Pending.Clear();
        foreach (var item in items)
        {
            Pending.Add(new PendingGalleryItemViewModel(item, this));
        }
    }

    internal async Task DecideAsync(PendingGalleryItemViewModel item, bool approve)
    {
        if (!approve && !await AskAsync(StringKeys.MediaAdminRejectTitle, item.Title))
        {
            return;
        }

        if (await RunAsync(() => approve ? Admin.ApproveAsync(item.Id) : Admin.RejectAsync(item.Id), approve ? StringKeys.MediaAdminApproved : StringKeys.MediaAdminRejected))
        {
            Pending.Remove(item);
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }
}

/// <summary>Eingereichter Galerie-Eintrag mit Vorschaubild (asynchron, nur vom CDN der Plattform).</summary>
public sealed partial class PendingGalleryItemViewModel : ObservableObject
{
    private readonly PendingGalleryItem _item;
    private readonly GalleryAdminViewModel _owner;

    public PendingGalleryItemViewModel(PendingGalleryItem item, GalleryAdminViewModel owner)
    {
        _item = item;
        _owner = owner;
        Title = item.Caption ?? item.AlbumTitle;
        Meta = string.Join(" · ", new[]
        {
            item.AlbumTitle,
            item.Author,
            item.CreatedAt?.ToLocalTime().ToString("g", owner.Services.Culture),
            item.Kind switch { "image" => null, "video" => owner.Services.T("Linux_GalleryVideo"), var kind => kind },
        }.Where(s => !string.IsNullOrEmpty(s)));
        IsVideo = item.Kind == "video";
        _ = LoadThumbAsync();
    }

    public string Id => _item.Id;

    public string Title { get; }

    public string Meta { get; }

    public bool IsVideo { get; }

    public string AutomationName => $"{Title}, {Meta}";

    [ObservableProperty]
    public partial Bitmap? Thumb { get; set; }

    private async Task LoadThumbAsync() => Thumb = await _owner.Services.Images.LoadAsync(_item.Thumb, 320);

    [RelayCommand]
    private Task ApproveAsync() => _owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => _owner.DecideAsync(this, approve: false);

    [RelayCommand]
    private Task OpenAsync() => _item.Full is { } url ? _owner.Services.Browser.OpenAsync(url) : Task.CompletedTask;
}

/// <summary>
/// Reiter „Uploads“: offene Meldungen (Kinderschutz zuerst, vom Server sortiert) – Upload behalten oder (nach
/// Rückfrage) löschen – und Speicherverbrauch gesamt sowie der zehn größten Konten.
/// </summary>
public sealed partial class UploadReportsAdminViewModel(AppServices services, FeedbackViewModel? feedback = null, InlineConfirmViewModel? confirm = null)
    : MediaAdminTabViewModel(services, feedback, confirm)
{
    public ObservableCollection<UploadReportItemViewModel> Reports { get; } = [];

    public ObservableCollection<StorageUserRow> StorageUsers { get; } = [];

    public override bool IsAvailable => MediaAdminService.CanManageUploads(Services.CurrentUser);

    public override bool IsEmpty => Reports.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorage))]
    public partial string? StorageText { get; set; }

    public bool HasStorage => !string.IsNullOrEmpty(StorageText);

    protected override async Task LoadCoreAsync()
    {
        var reports = await Admin.ReportsAsync(handled: false);
        Reports.Clear();
        foreach (var r in reports)
        {
            Reports.Add(new UploadReportItemViewModel(r, this));
        }

        OnPropertyChanged(nameof(ShowEmpty));
        try
        {
            var storage = await Admin.StorageAsync();
            StorageText = Services.Format(StringKeys.MediaAdminStorage, storage.Uploads, ServerPaths.FormatSize(storage.Bytes));
            StorageUsers.Clear();
            foreach (var (name, uploads, bytes) in storage.TopUsers)
            {
                StorageUsers.Add(new StorageUserRow(name, Services.Format("Linux_StorageUserUploads", uploads), ServerPaths.FormatSize(bytes)));
            }
        }
        catch (ApiProblemException)
        {
            // Speicherübersicht ist Beiwerk – Meldungen bleiben bearbeitbar
            StorageText = null;
        }
    }

    internal async Task ResolveAsync(UploadReportItemViewModel item, bool delete)
    {
        if (delete && !await AskAsync(StringKeys.MediaAdminDeleteUploadTitle, item.Title))
        {
            return;
        }

        if (await RunAsync(() => Admin.ResolveAsync(item.Id, delete), delete ? StringKeys.MediaAdminUploadDeleted : StringKeys.MediaAdminUploadKept))
        {
            Reports.Remove(item);
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }
}

/// <summary>Konto in der Speicherübersicht.</summary>
public sealed record StorageUserRow(string Name, string Uploads, string Size);

/// <summary>Gemeldeter Upload mit Grund, Kinderschutz-Kennzeichen und Vorschau.</summary>
public sealed partial class UploadReportItemViewModel : ObservableObject
{
    private readonly UploadReport _report;
    private readonly UploadReportsAdminViewModel _owner;

    public UploadReportItemViewModel(UploadReport report, UploadReportsAdminViewModel owner)
    {
        _report = report;
        _owner = owner;
        Meta = string.Join(" · ", new[]
        {
            report.Owner,
            report.Reporter is { } r ? owner.Services.Format("Linux_ReportedBy", r) : null,
            report.CreatedAt?.ToLocalTime().ToString("g", owner.Services.Culture),
        }.Where(s => !string.IsNullOrEmpty(s)));
        _ = LoadThumbAsync();
    }

    public string Id => _report.Id;

    public string Title => _report.UploadTitle ?? "—";

    public string Reason => _report.Reason;

    public bool HasReason => !string.IsNullOrWhiteSpace(_report.Reason);

    public bool IsChildSafety => _report.IsChildSafety;

    public string Meta { get; }

    public bool HasDirect => _report.Direct is not null;

    public string AutomationName => $"{Title}: {Reason}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoThumb))]
    public partial Bitmap? Thumb { get; set; }

    public bool HasNoThumb => Thumb is null;

    private async Task LoadThumbAsync() => Thumb = await _owner.Services.Images.LoadAsync(_report.Preview, 320);

    [RelayCommand]
    private Task KeepAsync() => _owner.ResolveAsync(this, delete: false);

    [RelayCommand]
    private Task DeleteAsync() => _owner.ResolveAsync(this, delete: true);

    [RelayCommand]
    private Task OpenAsync() => _report.Direct is { } url ? _owner.Services.Browser.OpenAsync(url) : Task.CompletedTask;
}
