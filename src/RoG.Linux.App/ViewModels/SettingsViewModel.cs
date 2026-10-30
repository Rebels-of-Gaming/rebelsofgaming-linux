using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Localization;
using RoG.Core.Settings;
using RoG.Linux.App.Services;
using RoG.Linux.Platform;

namespace RoG.Linux.App.ViewModels;

/// <summary>Eine Auswahl in einer ComboBox (Wert + Anzeigetext).</summary>
public sealed record SettingsOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Einstellungen wie in der Windows-App: Sprache (wirkt nach Neustart), Design (sofort), Desktop-Benachrichtigungen,
/// Über die App (Version, Plattform, Umgebung, Paket, Anzeige, Links, Copyright) und Lizenzen. Die Umgebung wird nur
/// angezeigt, nicht gewählt: Ein Token der Produktion darf nie an Staging gehen (Wahl nur per Startargument).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Gründungsjahr des Clans – Copyright-Zeile wie im Website-Footer.</summary>
    private const int ClanFounded = 2020;

    private readonly AppServices _services;
    private readonly string _startLanguage;
    private bool _loaded;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _startLanguage = services.Settings.Language;
        Languages =
        [
            new(LanguageResolver.System, services.T(StringKeys.SettingsLanguageSystem)),
            new("de", "Deutsch"),
            new("en", "English"),
            new("fr", "Français"),
        ];
        Themes =
        [
            new(LinuxSettings.ThemeDark, services.T("Linux_ThemeDark")),
            new(LinuxSettings.ThemeSystem, services.T("Linux_ThemeSystem")),
            new(LinuxSettings.ThemeLight, services.T("Linux_ThemeLight")),
        ];
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == _startLanguage) ?? Languages[0];
        SelectedTheme = Themes.First(t => t.Value == services.LinuxSettings.EffectiveTheme);
        Notifications = services.Settings.ToastNotifications;
        AppVersionText = services.Format(StringKeys.SettingsAppVersion, AppServices.AppVersion);
        PlatformText = services.Format(StringKeys.SettingsPlatformVersion, "…");
        EnvironmentText = services.Format("Linux_Environment", services.Environment.Name);
        PackageText = services.T(services.IsFlatpak ? "Linux_PackageFlatpak" : "Linux_PackageNative")
            + (LinuxDeviceName.IsSteamDeck() ? " · " + services.T("Linux_SteamDeck") : string.Empty);
        DisplayText = services.Format("Linux_DisplayBackend", WindowPlacementService.ActualBackend);
        CopyrightText = services.Format(StringKeys.SettingsPageClanCopyright, ClanFounded, DateTime.Now.Year);
        _loaded = true;
        _ = LoadPlatformAsync();
    }

    public IReadOnlyList<SettingsOption> Languages { get; }

    public IReadOnlyList<SettingsOption> Themes { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool RestartRequired { get; set; }

    [ObservableProperty]
    public partial bool Notifications { get; set; }

    /// <summary>Rückmeldung der Test-Benachrichtigung.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotificationFeedback))]
    public partial string? NotificationFeedback { get; set; }

    public bool HasNotificationFeedback => !string.IsNullOrEmpty(NotificationFeedback);

    public string AppVersionText { get; }

    [ObservableProperty]
    public partial string PlatformText { get; set; }

    public string EnvironmentText { get; }

    public string PackageText { get; }

    public string DisplayText { get; }

    public string CopyrightText { get; }

    /// <summary>Plattform-Version (<c>GET /v1</c>) – Fehler nur als Hinweis.</summary>
    public async Task LoadPlatformAsync()
    {
        try
        {
            var platform = await _services.Account.GetPlatformAsync();
            PlatformText = _services.Format(StringKeys.SettingsPlatformVersion, platform.Version);
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            PlatformText = _services.T(StringKeys.SettingsPlatformUnavailable);
        }
    }

    partial void OnSelectedLanguageChanged(SettingsOption value)
    {
        if (!_loaded || value is null)
        {
            return;
        }

        if (_services.Settings.Language != value.Value)
        {
            _ = _services.UpdateSettingsAsync(s => s with { Language = value.Value });
        }

        // Texte werden beim Start geladen (wie unter Windows) → Hinweis auf Neustart
        RestartRequired = value.Value != _startLanguage;
    }

    partial void OnSelectedThemeChanged(SettingsOption value)
    {
        if (_loaded && value is not null && _services.LinuxSettings.EffectiveTheme != value.Value)
        {
            _ = _services.UpdateLinuxSettingsAsync(s => s with { Theme = value.Value });
        }
    }

    partial void OnNotificationsChanged(bool value)
    {
        if (_loaded && _services.Settings.ToastNotifications != value)
        {
            _ = _services.UpdateSettingsAsync(s => s with { ToastNotifications = value });
        }
    }

    /// <summary>Zeigt eine Desktop-Benachrichtigung – prüft Dienst und Darstellung (Symbol, App-Name).</summary>
    [RelayCommand]
    private async Task TestNotificationAsync()
    {
        var shown = await _services.Notifier.ShowAsync(_services.T("Linux_TestNotificationTitle"), _services.T("Linux_TestNotificationBody"));
        NotificationFeedback = _services.T(shown ? "Linux_TestNotificationSent" : "Linux_TestNotificationFailed");
    }

    [RelayCommand]
    private Task OpenWebsiteAsync() => _services.Browser.OpenAsync(_services.Environment.WebBaseUrl);

    [RelayCommand]
    private Task OpenStatusAsync() => _services.Browser.OpenAsync(_services.Environment.StatusUrl);

    [RelayCommand]
    private Task OpenPrivacyAsync() => OpenLegalAsync("privacy");

    [RelayCommand]
    private Task OpenTermsAsync() => OpenLegalAsync("terms");

    [RelayCommand]
    private Task OpenImprintAsync() => OpenLegalAsync("imprint");

    [RelayCommand]
    private Task OpenChildSafetyAsync() => OpenLegalAsync("child-safety");

    [RelayCommand]
    private Task OpenCreditAsync() => _services.Browser.OpenAsync(new Uri(_services.T("SettingsPage_CreditLink.NavigateUri")));

    private Task OpenLegalAsync(string page) => _services.Browser.OpenAsync(_services.Environment.WebPage(_services.Language, page));
}
