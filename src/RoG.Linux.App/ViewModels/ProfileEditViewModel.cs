using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Profile;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Profil bearbeiten (Konto-Seite, wie <c>ProfileEditViewModel</c> der Windows-App): Motto, Über mich, Ort (nur Stadt,
/// für die Mitgliederkarte nur mit Opt-in), Einwilligungen (Karte, Online-Status, Hall of Fame), Avatar und Banner
/// (der Server entfernt Metadaten). Lädt erst beim Aufklappen.
/// </summary>
public sealed partial class ProfileEditViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ProfileService _profile = new(services.Api.Client);
    private ProfileData? _loaded;

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    public partial string Motto { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Bio { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string City { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Country { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; }

    [ObservableProperty]
    public partial bool ShowOnlineStatus { get; set; }

    [ObservableProperty]
    public partial bool HallOfFameConsent { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    protected override bool HasData => _loaded is not null;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && _loaded is null && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var p = await _profile.GetAsync(cancellationToken);
        Motto = p.Motto;
        Bio = p.Bio;
        City = p.City;
        Country = p.Country;
        ShowOnMap = p.ShowOnMap;
        ShowOnlineStatus = p.ShowOnlineStatus;
        HallOfFameConsent = p.HallOfFameConsent;
        _loaded = p;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => _loaded is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loaded is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var saved = _loaded with
            {
                Motto = Motto,
                Bio = Bio,
                City = City,
                Country = Country,
                ShowOnMap = ShowOnMap,
                ShowOnlineStatus = ShowOnlineStatus,
                HallOfFameConsent = HallOfFameConsent,
            };
            await _profile.SaveAsync(saved);
            _loaded = saved;
            Feedback.Show(Services.T(StringKeys.ProfileSaved), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
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

    /// <summary>Avatar oder Banner ersetzen (Bild aus der Dateiauswahl); Größe gegen die Grenze der Plattform geprüft.</summary>
    public async Task SetImageAsync(UploadSource file, bool banner)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (UploadService.MimeFor(file.Name) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Feedback.Show(Services.T(StringKeys.UploadsUnsupported), MessageTone.Info);
            return;
        }

        var limit = banner ? _loaded?.BannerMaxBytes : _loaded?.AvatarMaxBytes;
        if (limit is > 0 && file.Size > limit)
        {
            Feedback.Show(Services.Format("Linux_ImageTooLarge", Math.Ceiling(limit.Value / 1048576.0)), MessageTone.Error);
            return;
        }

        try
        {
            await using var stream = await file.OpenAsync();
            if (banner)
            {
                await _profile.SetBannerAsync(stream, file.Name, mime);
            }
            else
            {
                await _profile.SetAvatarAsync(stream, file.Name, mime);
            }

            // Verarbeitung läuft im Hintergrund – Profil danach neu laden
            Services.CurrentUser = await Services.Account.GetMeAsync();
            Feedback.Show(Services.T(StringKeys.ProfileImageSaved), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }
}
