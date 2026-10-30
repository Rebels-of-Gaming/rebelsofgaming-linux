using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Notifications;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Einstellungen → Benachrichtigungen (wie <c>NotificationSettingsViewModel</c> der Windows-App, Guide 12): je Ereignis
/// Glocke (nur wo abschaltbar), E-Mail, Discord – nur nutzbare Kanäle; dazu Sammel-Mail und Ruhezeit. Nur angemeldet,
/// lädt beim Aufklappen. Desktop-Benachrichtigungen der App selbst stehen weiter in der Karte „Linux-Desktop“.
/// </summary>
public sealed partial class NotificationSettingsViewModel : LoadableViewModel
{
    private static readonly string[] s_digests = ["instant", "daily", "weekly"];
    private readonly NotificationSettingsService _settings;
    private bool _ready;

    public NotificationSettingsViewModel(AppServices services)
        : base(services)
    {
        _settings = new NotificationSettingsService(services.Api.Http);
        DigestOptions = s_digests.Select(d => new SettingsOption(d, services.T("NotifySettings_Digest_" + d))).ToList();
        IsSignedIn = services.CurrentUser is not null;
        services.CurrentUserChanged += (_, _) => IsSignedIn = services.CurrentUser is not null;
    }

    public ObservableCollection<NotificationTypeRowViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> DigestOptions { get; }

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedDigest { get; set; }

    [ObservableProperty]
    public partial string QuietFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string QuietUntil { get; set; } = string.Empty;

    protected override bool HasData => Items.Count > 0;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && Items.Count == 0 && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        var types = await _settings.TypesAsync(cancellationToken);
        var settings = await _settings.SettingsAsync(cancellationToken);
        Items.Clear();
        foreach (var type in types.OrderBy(t => t.Group, StringComparer.Ordinal).ThenBy(t => t.Label, StringComparer.Create(Services.Culture, false)))
        {
            Items.Add(new NotificationTypeRowViewModel(type, settings, this));
        }

        SelectedDigest = DigestOptions.FirstOrDefault(d => d.Value == settings.Digest) ?? DigestOptions[0];
        QuietFrom = settings.QuietFrom ?? string.Empty;
        QuietUntil = settings.QuietUntil ?? string.Empty;
        _ready = true;
    }

    partial void OnSelectedDigestChanged(SettingsOption? value)
    {
        if (_ready && value is not null)
        {
            _ = RunAsync(() => _settings.SetDigestAsync(value.Value));
        }
    }

    [RelayCommand]
    private Task SaveQuietHoursAsync()
    {
        static string? Time(string text) => TimeOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var t) ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
        var from = Time(QuietFrom);
        var until = Time(QuietUntil);
        if ((from is null) != (until is null) || (from is null && (QuietFrom.Length > 0 || QuietUntil.Length > 0)))
        {
            Feedback.Show(Services.T(StringKeys.NotifySettingsQuietInvalid), MessageTone.Error);
            return Task.CompletedTask;
        }

        QuietFrom = from ?? string.Empty;
        QuietUntil = until ?? string.Empty;
        return RunAsync(() => _settings.SetQuietHoursAsync(from, until));
    }

    internal Task SetAsync(string key, string channel, bool on) => _ready ? RunAsync(() => _settings.SetChannelAsync(key, channel, on)) : Task.CompletedTask;

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Feedback.Show(Services.T(StringKeys.NotifySettingsSaved), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }
}

/// <summary>Zeile je Ereignis; <c>bool?</c> wegen <c>CheckBox.IsChecked</c> (TwoWay).</summary>
public sealed partial class NotificationTypeRowViewModel : ObservableObject
{
    private readonly NotificationType _type;
    private readonly NotificationSettingsViewModel _owner;

    public NotificationTypeRowViewModel(NotificationType type, NotificationSettings settings, NotificationSettingsViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(settings);
        _type = type;
        _owner = owner;
        var effective = settings.Effective(type);
        ShowBell = type.InboxOptional && type.Channels.Contains("in_app");
        ShowMail = type.Channels.Contains("mail") && settings.Available.Contains("mail");
        ShowDiscord = type.Channels.Contains("discord") && settings.Available.Contains("discord");
        Bell = effective.Contains("in_app");
        Mail = effective.Contains("mail");
        Discord = effective.Contains("discord");
    }

    public string Key => _type.Key;

    public string Label => _type.Label;

    public bool ShowBell { get; }

    public bool ShowMail { get; }

    public bool ShowDiscord { get; }

    [ObservableProperty]
    public partial bool? Bell { get; set; }

    [ObservableProperty]
    public partial bool? Mail { get; set; }

    [ObservableProperty]
    public partial bool? Discord { get; set; }

    partial void OnBellChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "in_app", value == true);

    partial void OnMailChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "mail", value == true);

    partial void OnDiscordChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "discord", value == true);
}
