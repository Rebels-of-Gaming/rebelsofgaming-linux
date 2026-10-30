using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Media;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Medien (wie <c>MediaViewModel</c> der Windows-App): Reiter <b>Galerie</b> (Alben, Bilder/Clips, Großansicht mit
/// Pfeiltasten, Einreichen mit Bildunterschrift und Rechte-Bestätigung) und <b>Downloads</b> (signierter Link,
/// Prüfsumme, Speichern im Downloads-Ordner, „Im Ordner zeigen“). Bilder nur vom CDN der Plattform, nur im Speicher.
/// </summary>
public sealed partial class MediaViewModel : LoadableViewModel, IDetailPage
{
    private readonly MediaService _media;

    public MediaViewModel(AppServices services)
        : base(services)
    {
        _media = new MediaService(services.Api.Client, services.StorageHttp);
        Downloads = new DownloadsViewModel(services, _media);
        Report = new ReportFormViewModel(services);
        Social = new SocialStatusViewModel(services);
    }

    /// <summary>Karte „Social Media“ am geöffneten Album (Galerie-Posts entstehen je Album, nur mit <c>social.publish</c>).</summary>
    public SocialStatusViewModel Social { get; }

    public ObservableCollection<AlbumItemViewModel> Albums { get; } = [];

    public ObservableCollection<GalleryItemViewModel> Items { get; } = [];

    public DownloadsViewModel Downloads { get; }

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGalleryTab))]
    public partial bool IsDownloadsTab { get; set; }

    public bool IsGalleryTab
    {
        get => !IsDownloadsTab;
        set
        {
            if (value)
            {
                IsDownloadsTab = false;
            }
            else
            {
                OnPropertyChanged(nameof(IsGalleryTab));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAlbums), nameof(ShowItems), nameof(AlbumTitle))]
    public partial AlbumItemViewModel? Album { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowItemsEmpty))]
    public partial bool IsLoadingItems { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowItemsEmpty))]
    public partial string? ItemsError { get; set; }

    /// <summary>Einreichen erlaubt (Album erlaubt Uploads von Mitgliedern bzw. Recht <c>gallery.upload</c>).</summary>
    [ObservableProperty]
    public partial bool CanUpload { get; set; }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    /// <summary>Formular „Einreichen“ (Bildunterschrift, Rechte) offen.</summary>
    [ObservableProperty]
    public partial bool IsSubmitOpen { get; set; }

    [ObservableProperty]
    public partial string? Caption { get; set; }

    [ObservableProperty]
    public partial bool RightsConfirmed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerOpen))]
    public partial GalleryItemViewModel? Viewed { get; set; }

    public bool ShowAlbums => Album is null;

    public bool ShowItems => Album is not null;

    public bool ShowItemsEmpty => ShowItems && !IsLoadingItems && ItemsError is null && Items.Count == 0;

    public string AlbumTitle => Album?.Title ?? string.Empty;

    public bool IsViewerOpen => Viewed is not null;

    partial void OnViewedChanged(GalleryItemViewModel? value) => Report.Close();

    /// <summary>„Melden“ in der Großansicht wie Windows (angemeldet, Scope <c>reports:write</c>).</summary>
    public bool CanReport => ReportFormViewModel.IsAllowed(Services);

    /// <summary>Galeriebild melden – Formular in der Großansicht.</summary>
    public ReportFormViewModel Report { get; }

    [RelayCommand]
    private void ReportViewed()
    {
        if (Viewed is { } item)
        {
            Report.Open("gallery_item", item.Id, string.Join(" · ", new[] { Services.T(StringKeys.ModTypeGalleryItem), item.Author }.Where(s => !string.IsNullOrEmpty(s))));
        }
    }

    bool IDetailPage.IsDetailOpen => IsViewerOpen || Album is not null;

    protected override bool HasData => Albums.Count > 0;

    partial void OnIsDownloadsTabChanged(bool value)
    {
        Feedback.Close();
        if (value && !Downloads.ShowContent && !Downloads.IsLoading)
        {
            _ = Downloads.LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var albums = await _media.GetAlbumsAsync(cancellationToken);
        Albums.Clear();
        foreach (var a in albums)
        {
            Albums.Add(new AlbumItemViewModel(a, Services));
        }
    }

    [RelayCommand]
    private void OpenAlbum(AlbumItemViewModel? album) => Album = album;

    partial void OnAlbumChanged(AlbumItemViewModel? value)
    {
        Items.Clear();
        IsSubmitOpen = false;
        ItemsError = null;
        _ = Social.ShowAsync(RoG.Core.Features.Social.SocialSources.Gallery, value?.Id);
        if (value is not null)
        {
            _ = LoadItemsAsync(value.Slug);
        }
    }

    private async Task LoadItemsAsync(string slug)
    {
        IsLoadingItems = true;
        CanUpload = false;
        try
        {
            if (Services.CurrentUser is not null)
            {
                try
                {
                    CanUpload = await _media.CanUploadAsync(slug);
                }
                catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
                {
                    CanUpload = false;
                }
            }

            foreach (var item in await _media.GetItemsAsync(slug))
            {
                Items.Add(new GalleryItemViewModel(item, Services));
            }
        }
        catch (ApiProblemException ex)
        {
            ItemsError = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            ItemsError = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingItems = false;
            OnPropertyChanged(nameof(ShowItemsEmpty));
        }
    }

    [RelayCommand]
    private void BackToAlbums()
    {
        Viewed = null;
        Album = null;
    }

    [RelayCommand]
    private void ToggleSubmit()
    {
        IsSubmitOpen = !IsSubmitOpen;
        if (IsSubmitOpen)
        {
            Caption = null;
            RightsConfirmed = false;
        }
    }

    /// <summary>Dateien ins geöffnete Album einreichen (Rechte müssen bestätigt sein).</summary>
    public async Task SubmitAsync(IReadOnlyList<UploadSource> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (Album is not { } album || files.Count == 0 || IsUploading)
        {
            return;
        }

        if (!RightsConfirmed)
        {
            Feedback.Show(Services.T(StringKeys.GalleryRights), MessageTone.Info);
            return;
        }

        IsUploading = true;
        var uploads = new UploadService(Services.Api.Client, Services.StorageHttp);
        var pending = 0;
        var done = 0;
        try
        {
            foreach (var file in files.Where(f => UploadService.MimeFor(f.Name) is not null))
            {
                await using var stream = await file.OpenAsync();
                var status = await uploads.SubmitToGalleryAsync(album.Slug, stream, file.Name, file.Size, Caption, rightsConfirmed: true);
                done++;
                if (status != "approved")
                {
                    pending++;
                }
            }

            if (done == 0)
            {
                Feedback.Show(Services.T(StringKeys.UploadsUnsupported), MessageTone.Info);
                return;
            }

            Feedback.Show(Services.Format(pending > 0 ? StringKeys.GallerySubmittedPending : StringKeys.GallerySubmitted, done), MessageTone.Success);
            IsSubmitOpen = false;
            Items.Clear();
            await LoadItemsAsync(album.Slug);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or NotSupportedException or IOException or OverflowException or HttpRequestException)
        {
            Feedback.Show(Services.Localizer.GetError("upload_failed"), MessageTone.Error);
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private void View(GalleryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsVideo)
        {
            _ = item.OpenInBrowserCommand.ExecuteAsync(null);
            return;
        }

        Viewed = item;
        _ = item.LoadFullAsync();
    }

    [RelayCommand]
    private void CloseViewer() => Viewed = null;

    [RelayCommand]
    private void Next() => Step(1);

    [RelayCommand]
    private void Previous() => Step(-1);

    /// <summary>In der Großansicht blättern (Pfeiltasten), Videos werden übersprungen.</summary>
    public void Step(int delta)
    {
        var images = Items.Where(i => !i.IsVideo).ToList();
        if (Viewed is null || images.Count == 0)
        {
            return;
        }

        var index = (images.IndexOf(Viewed) + delta + images.Count) % images.Count;
        Viewed = images[index];
        _ = Viewed.LoadFullAsync();
    }

    /// <summary>Escape: Großansicht, dann Album schließen.</summary>
    public void CloseDetail()
    {
        if (Report.Close())
        {
            return; // Escape schließt zuerst „Melden“
        }

        if (Viewed is not null)
        {
            Viewed = null;
        }
        else
        {
            Album = null;
        }
    }
}

public sealed partial class AlbumItemViewModel : ObservableObject
{
    public AlbumItemViewModel(GalleryAlbum album, AppServices services)
    {
        Id = album.Id;
        Slug = album.Slug;
        Title = album.Title;
        Meta = string.Join(" · ", new[] { services.Format(StringKeys.MediaItems, album.Items), album.SquadName }.Where(s => !string.IsNullOrEmpty(s)));
        _ = LoadCoverAsync(services, album.Cover);
    }

    public string Id { get; }

    public string Slug { get; }

    public string Title { get; }

    public string Meta { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasNoCover => Cover is null;

    private async Task LoadCoverAsync(AppServices services, Uri? cover) => Cover = await services.Images.LoadAsync(cover, 480);
}

public sealed partial class GalleryItemViewModel : ObservableObject
{
    private readonly GalleryItem _item;
    private readonly AppServices _services;

    public GalleryItemViewModel(GalleryItem item, AppServices services)
    {
        _item = item;
        _services = services;
        Meta = string.Join(" · ", new[] { item.Author, item.CreatedAt?.ToLocalTime().ToString("d", services.Culture) }.Where(s => !string.IsNullOrEmpty(s)));
        AutomationName = string.Join(", ", new[] { item.Caption ?? item.Author ?? item.Id, item.IsVideo ? services.T("Linux_Video") : null }.Where(s => s is not null));
        _ = LoadThumbAsync();
    }

    public string Id => _item.Id;

    public string? Author => _item.Author;

    public string Caption => _item.Caption ?? string.Empty;

    public bool HasCaption => !string.IsNullOrEmpty(_item.Caption);

    public string Meta { get; }

    public string AutomationName { get; }

    public bool IsVideo => _item.IsVideo;

    [ObservableProperty]
    public partial Bitmap? Thumb { get; set; }

    /// <summary>Großansicht erst beim Öffnen laden.</summary>
    [ObservableProperty]
    public partial Bitmap? Full { get; set; }

    private async Task LoadThumbAsync() => Thumb = await _services.Images.LoadAsync(_item.Thumb ?? _item.Medium, 320);

    public async Task LoadFullAsync() => Full ??= await _services.Images.LoadAsync(_item.Full ?? _item.Medium, 1920);

    [RelayCommand]
    private Task OpenInBrowserAsync() => (_item.VideoUrl ?? _item.WebUrl ?? _item.Full) is { } url ? _services.Browser.OpenAsync(url) : Task.CompletedTask;
}

/// <summary>Downloads: Liste mit Version, Spiel, Größe, Zähler, Änderungen; Laden in den Downloads-Ordner mit Prüfsumme.</summary>
public sealed partial class DownloadsViewModel(AppServices services, MediaService media) : LoadableViewModel(services)
{
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var entries = await media.GetDownloadsAsync(cancellationToken);
        Items.Clear();
        foreach (var e in entries)
        {
            Items.Add(new DownloadItemViewModel(e, this, Services));
        }
    }

    internal async Task DownloadAsync(DownloadItemViewModel item)
    {
        if (item.Entry.VersionId is not { } version)
        {
            return;
        }

        item.IsBusy = true;
        Feedback.Close();
        try
        {
            item.SavedPath = await media.DownloadAsync(version, Services.DownloadDirectory, new Progress<double>(p => item.Progress = p * 100));
            Feedback.Show(Services.Format(StringKeys.MediaDownloaded, Path.GetFileName(item.SavedPath)), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    internal Task ShowInFolderAsync(string path) => Services.FileManager.ShowAsync(path);
}

public sealed partial class DownloadItemViewModel(DownloadEntry entry, DownloadsViewModel owner, AppServices services) : ObservableObject
{
    public DownloadEntry Entry => entry;

    public string Title => entry.Title;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        entry.Version is null ? null : "v" + entry.Version,
        entry.Category,
        entry.Game,
        entry.Size > 0 ? (entry.Size / 1048576.0).ToString("0.0 'MB'", services.Culture) : null,
        services.Format(StringKeys.MediaDownloadCount, entry.Downloads),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Changelog => entry.Changelog ?? string.Empty;

    public bool HasChangelog => !string.IsNullOrEmpty(entry.Changelog);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    public partial string? SavedPath { get; set; }

    public bool CanDownload => entry.Available && entry.VersionId is not null && !IsBusy;

    public bool IsSaved => SavedPath is not null;

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);

    [RelayCommand]
    private Task ShowInFolderAsync() => SavedPath is { } path && File.Exists(path) ? owner.ShowInFolderAsync(path) : Task.CompletedTask;
}
