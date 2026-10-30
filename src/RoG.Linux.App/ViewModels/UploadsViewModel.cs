using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Links;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Datei zum Hochladen – unabhängig von Dateiauswahl, Drag &amp; Drop oder Zwischenablage.</summary>
public sealed record UploadSource(string Name, long Size, Func<Task<Stream>> OpenAsync);

/// <summary>
/// Uploads (wie <c>UploadsViewModel</c> der Windows-App): Bilder/Videos per Drag &amp; Drop, Dateiauswahl (in Flatpak
/// über das FileChooser-Portal) oder Strg+V (Screenshot bzw. kopierte Dateien) hochladen – Datei geht per vorab
/// signierter Adresse direkt in den Speicher der Plattform. Direktlink danach kopieren, eigene Uploads mit Links
/// (Direkt, BBCode, Markdown). Zweiter Reiter: Kurzlinks (<see cref="ShortLinksViewModel"/>).
/// </summary>
public sealed partial class UploadsViewModel : LoadableViewModel
{
    private readonly UploadService _uploads;
    private readonly Action _requestSignIn;
    private string? _cursor;

    public UploadsViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _requestSignIn = requestSignIn;
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        Links = new ShortLinksViewModel(services, Feedback);
        Visibilities =
        [
            new(UploadService.VisibilityValue(UploadVisibility.Public), services.T(StringKeys.UploadsPublic)),
            new(UploadService.VisibilityValue(UploadVisibility.Members), services.T(StringKeys.UploadsMembers)),
            new(UploadService.VisibilityValue(UploadVisibility.Private), services.T(StringKeys.UploadsPrivate)),
        ];
        SelectedVisibility = Visibilities.FirstOrDefault(v => v.Value == services.Settings.UploadVisibility) ?? Visibilities[0];
        CopyLink = services.Settings.CopyLinkAfterUpload;
    }

    public ObservableCollection<UploadItemViewModel> Items { get; } = [];

    public ShortLinksViewModel Links { get; }

    public FeedbackViewModel Feedback { get; } = new();

    public IReadOnlyList<SettingsOption> Visibilities { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedVisibility { get; set; }

    [ObservableProperty]
    public partial bool CopyLink { get; set; }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Reiter Kurzlinks statt Uploads.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUploadsTab))]
    public partial bool IsLinksTab { get; set; }

    public bool IsUploadsTab
    {
        get => !IsLinksTab;
        set
        {
            if (value)
            {
                IsLinksTab = false;
            }
            else
            {
                OnPropertyChanged(nameof(IsUploadsTab));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUploader), nameof(IsEmptyForMember))]
    public partial bool IsGuest { get; set; }

    public bool ShowUploader => !IsGuest;

    /// <summary>Leerzustand nur für Angemeldete (Gäste sehen den Hinweis zum Anmelden).</summary>
    public bool IsEmptyForMember => IsEmpty && !IsGuest;

    protected override bool HasData => Items.Count > 0;

    partial void OnIsLinksTabChanged(bool value)
    {
        Feedback.Close();
        if (value && !Links.ShowContent && !Links.IsLoading)
        {
            _ = Links.LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            OnPropertyChanged(nameof(IsEmptyForMember));
            return;
        }

        var page = await _uploads.ListAsync(null, cancellationToken);
        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(new UploadItemViewModel(item, this, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        OnPropertyChanged(nameof(IsEmptyForMember));
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await _uploads.ListAsync(_cursor);
            foreach (var item in page.Items)
            {
                Items.Add(new UploadItemViewModel(item, this, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // „Mehr laden“ erneut
        }
    }

    partial void OnSelectedVisibilityChanged(SettingsOption value)
    {
        if (value is not null && Services.Settings.UploadVisibility != value.Value)
        {
            _ = Services.UpdateSettingsAsync(s => s with { UploadVisibility = value.Value });
        }
    }

    partial void OnCopyLinkChanged(bool value)
    {
        if (Services.Settings.CopyLinkAfterUpload != value)
        {
            _ = Services.UpdateSettingsAsync(s => s with { CopyLinkAfterUpload = value });
        }
    }

    /// <summary>Dateien nacheinander hochladen; nicht unterstützte Typen werden übersprungen und gemeldet.</summary>
    public async Task UploadFilesAsync(IReadOnlyList<UploadSource> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (IsUploading || IsGuest || files.Count == 0)
        {
            return;
        }

        IsUploading = true;
        Feedback.Close();
        var visibility = SelectedVisibility.Value switch
        {
            "members" => UploadVisibility.Members,
            "private" => UploadVisibility.Private,
            _ => UploadVisibility.Public,
        };
        UploadItem? last = null;
        var failed = 0;
        var errorShown = false;
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (UploadService.MimeFor(file.Name) is null)
                {
                    failed++;
                    continue;
                }

                ProgressText = Services.Format(StringKeys.UploadsUploading, file.Name, i + 1, files.Count);
                Progress = 0;
                try
                {
                    await using var stream = await file.OpenAsync();
                    last = await _uploads.UploadAsync(stream, file.Name, file.Size, visibility, new Progress<double>(p => Progress = p * 100));
                    Items.Insert(0, new UploadItemViewModel(last, this, Services));
                    DataChanged();
                    OnPropertyChanged(nameof(IsEmptyForMember));
                }
                catch (ApiProblemException ex)
                {
                    failed++;
                    errorShown = true;
                    Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
                }
                catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or OverflowException or IOException or UnauthorizedAccessException or TaskCanceledException)
                {
                    failed++;
                    errorShown = true;
                    Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
                }
            }
        }
        finally
        {
            IsUploading = false;
            ProgressText = null;
        }

        if (last?.Links.Direct is { } link)
        {
            var copied = CopyLink && Services.ServerConnector.Copy(link.ToString());
            Feedback.Show(Services.T(copied ? StringKeys.UploadsDoneCopied : StringKeys.UploadsDone), failed == 0 ? MessageTone.Success : MessageTone.Info);
        }
        else if (failed > 0 && !errorShown)
        {
            Feedback.Show(Services.T(StringKeys.UploadsUnsupported), MessageTone.Info);
        }
    }

    /// <summary>
    /// „Bildschirmfoto hochladen“ (F8): Aufnahme über das Screenshot-Portal des Desktops (fragt nach Bereich/Fenster),
    /// danach wie jede andere Datei hochladen. Die Datei legt der Desktop an (z. B. „Bilder/Bildschirmfotos“) – sie
    /// gehört dem Benutzer und bleibt liegen.
    /// </summary>
    [RelayCommand]
    private async Task ScreenshotAsync()
    {
        if (IsUploading || IsGuest)
        {
            return;
        }

        Feedback.Close();
        var result = await Services.Screenshots.TakeAsync(interactive: true);
        switch (result.Status)
        {
            case RoG.Linux.Platform.Desktop.ScreenshotStatus.Taken when result.Path is { } path && File.Exists(path):
                IsUploadsTab = true;
                var name = Path.GetFileName(path);
                if (UploadService.MimeFor(name) is null)
                {
                    name = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
                }

                await UploadFilesAsync([new UploadSource(name, new FileInfo(path).Length, () => Task.FromResult<Stream>(File.OpenRead(path)))]);
                break;
            case RoG.Linux.Platform.Desktop.ScreenshotStatus.Cancelled:
                break;
            default:
                Feedback.Show(Services.T("Linux_ScreenshotUnavailable"), MessageTone.Info);
                break;
        }
    }

    internal void Copied(bool ok) =>
        Feedback.Show(Services.T(ok ? StringKeys.CommonLinkCopied : StringKeys.CommonCopyFailed), ok ? MessageTone.Success : MessageTone.Error);

    [RelayCommand]
    private void SignIn() => _requestSignIn();
}

/// <summary>Ein eigener Upload: Vorschaubild (nur öffentliche, vom CDN der Plattform), Zeit/Größe/Status, Links kopieren.</summary>
public sealed partial class UploadItemViewModel : ObservableObject
{
    private readonly UploadItem _item;
    private readonly UploadsViewModel _owner;
    private readonly AppServices _services;

    public UploadItemViewModel(UploadItem item, UploadsViewModel owner, AppServices services)
    {
        _item = item;
        _owner = owner;
        _services = services;
        Meta = string.Join(" · ", new[]
        {
            item.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
            item.Size > 0 ? (item.Size / 1024.0).ToString("0 'KB'", services.Culture) : null,
            item.Failed ? item.ErrorMessage : item.IsReady ? null : services.T(StringKeys.UploadsProcessing),
        }.Where(s => !string.IsNullOrEmpty(s)));
        if (item.Links.Thumb is { } thumb && item.Visibility == UploadVisibility.Public)
        {
            _ = LoadThumbAsync(thumb);
        }
    }

    public string Title => _item.Title ?? _item.OriginalName;

    public string Meta { get; }

    public string Direct => _item.Links.Direct?.ToString() ?? string.Empty;

    public bool HasLinks => _item.Links.Direct is not null && !_item.Failed;

    public bool IsFailed => _item.Failed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumb))]
    public partial Bitmap? Thumb { get; set; }

    public bool HasThumb => Thumb is not null;

    private async Task LoadThumbAsync(Uri thumb) => Thumb = await _services.Images.LoadAsync(thumb, 160);

    [RelayCommand]
    private void CopyDirect() => _owner.Copied(HasLinks && _services.ServerConnector.Copy(Direct));

    [RelayCommand]
    private void CopyBbCode() => _owner.Copied(_item.Links.BbCode is { } b && _services.ServerConnector.Copy(b));

    [RelayCommand]
    private void CopyMarkdown() => _owner.Copied(_item.Links.Markdown is { } m && _services.ServerConnector.Copy(m));

    [RelayCommand]
    private Task OpenAsync() => _item.Links.Preview is { } p ? _services.Browser.OpenAsync(p) : Task.CompletedTask;
}

/// <summary>Kurzlinks (Reiter auf der Upload-Seite): anlegen (Ziel, Wunsch-Kürzel, Titel), Liste mit Klicks, kopieren.</summary>
public sealed partial class ShortLinksViewModel(AppServices services, FeedbackViewModel feedback) : LoadableViewModel(services)
{
    private readonly ShortLinkService _links = new(services.Api.Client);

    public ObservableCollection<ShortLinkItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Code { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsCreating { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (await Services.Tokens.GetAsync(cancellationToken) is null)
        {
            Items.Clear();
            return;
        }

        var page = await _links.ListAsync(null, cancellationToken);
        Items.Clear();
        foreach (var link in page.Items)
        {
            Items.Add(new ShortLinkItemViewModel(link, this, Services));
        }
    }

    private bool CanCreate() => !IsCreating && Uri.TryCreate(Target.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsCreating = true;
        try
        {
            var link = await _links.CreateAsync(new Uri(Target.Trim()), string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(), string.IsNullOrWhiteSpace(Title) ? null : Title.Trim());
            Items.Insert(0, new ShortLinkItemViewModel(link, this, Services));
            DataChanged();
            Target = string.Empty;
            Code = null;
            Title = null;
            var copied = link.ShortUrl is { } url && Services.ServerConnector.Copy(url.ToString());
            feedback.Show(Services.T(copied ? StringKeys.LinksCreatedCopied : StringKeys.LinksCreated), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsCreating = false;
        }
    }

    internal void Copied(bool ok) =>
        feedback.Show(Services.T(ok ? StringKeys.CommonLinkCopied : StringKeys.CommonCopyFailed), ok ? MessageTone.Success : MessageTone.Error);
}

public sealed partial class ShortLinkItemViewModel(ShortLink link, ShortLinksViewModel owner, AppServices services)
{
    public string ShortUrl => link.ShortUrl?.ToString() ?? link.Code;

    public string Target => link.Title is null ? link.Target?.ToString() ?? string.Empty : $"{link.Title} – {link.Target}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        services.Format(StringKeys.LinksClicks, link.Clicks),
        link.Blocked ? services.T(StringKeys.LinksBlocked) : link.Active ? null : services.T(StringKeys.LinksInactive),
        link.CreatedAt?.ToLocalTime().ToString("d", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsBlocked => link.Blocked;

    [RelayCommand]
    private void Copy() => owner.Copied(services.ServerConnector.Copy(ShortUrl));
}
