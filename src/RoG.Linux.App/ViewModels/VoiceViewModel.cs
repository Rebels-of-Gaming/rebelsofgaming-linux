using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Voice;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Server → TeamSpeak und Discord-Voice (wie <c>VoiceViewModel</c> der Windows-App): Kanäle mit Personen,
/// „TeamSpeak verbinden“ über <c>ts3server://</c> (Spitzname = RoG-Name; ohne TeamSpeak wird die Adresse kopiert).
/// TeamSpeak ist öffentlich, Discord-Voice nur angemeldet. Trenner-Kanäle („[cspacer]“) sind Überschriften – ihre
/// Unterkanäle bleiben sichtbar.
/// </summary>
public sealed partial class VoiceViewModel : LoadableViewModel
{
    private readonly VoiceService _voice;
    private TeamSpeakState? _ts;
    private bool _discordLoaded;

    private readonly Action _requestSignIn;

    public VoiceViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _requestSignIn = requestSignIn;
        _voice = new VoiceService(services.Api.Client);
    }

    public ObservableCollection<VoiceRow> TeamSpeakRows { get; } = [];

    public ObservableCollection<VoiceRow> DiscordRows { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    public partial string TeamSpeakTitle { get; set; } = "TeamSpeak";

    [ObservableProperty]
    public partial string? TeamSpeakStatus { get; set; }

    [ObservableProperty]
    public partial bool IsTeamSpeakOnline { get; set; }

    [ObservableProperty]
    public partial bool CanConnect { get; set; }

    [ObservableProperty]
    public partial bool ShowEmptyChannels { get; set; }

    /// <summary>Discord-Voice braucht ein Konto – im Gastmodus Hinweis statt Liste.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDiscordEmpty), nameof(ShowDiscordList))]
    public partial bool DiscordNeedsLogin { get; set; }

    public bool HasDiscord => DiscordRows.Count > 0;

    public bool ShowDiscordList => HasDiscord && !DiscordNeedsLogin;

    /// <summary>Geladen, angemeldet, aber niemand im Discord-Voice.</summary>
    public bool ShowDiscordEmpty => _discordLoaded && !IsLoading && !DiscordNeedsLogin && !HasDiscord;

    public bool HasTeamSpeakRows => TeamSpeakRows.Count > 0;

    /// <summary>TeamSpeak geladen, aber (ohne leere Kanäle) nichts zu zeigen – niemand online.</summary>
    public bool ShowTeamSpeakEmpty => _ts is not null && !IsLoading && TeamSpeakRows.Count == 0;

    protected override bool HasData => _ts is not null || _discordLoaded;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var tsTask = _voice.GetTeamSpeakAsync(cancellationToken);
        var signedIn = await Services.Tokens.GetAsync(cancellationToken) is not null;
        var discordTask = signedIn ? _voice.GetDiscordAsync(cancellationToken) : null;

        try
        {
            _ts = await tsTask;
            TeamSpeakTitle = _ts.ServerName;
            IsTeamSpeakOnline = _ts.Online;
            TeamSpeakStatus = _ts.Online
                ? Services.Format(StringKeys.VoiceTsOnline, _ts.ClientsOnline, _ts.MaxClients)
                : Services.T(StringKeys.VoiceTsOffline);
            CanConnect = VoiceService.TeamSpeakLink(_ts.Address, null) is not null;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // TeamSpeak nicht eingerichtet oder nicht erreichbar – Discord kann trotzdem da sein
            _ts = null;
            TeamSpeakTitle = "TeamSpeak";
            IsTeamSpeakOnline = false;
            TeamSpeakStatus = Services.T(StringKeys.VoiceTsOffline);
            CanConnect = false;
        }

        BuildTeamSpeakRows();

        DiscordNeedsLogin = !signedIn;
        if (discordTask is not null)
        {
            try
            {
                var rows = new List<VoiceRow>();
                foreach (var channel in await discordTask)
                {
                    rows.Add(VoiceRow.Channel(channel.Name, 0, channel.Private, Services));
                    foreach (var (name, avatar) in channel.Members)
                    {
                        rows.Add(VoiceRow.Person(name, 1, null, false, false, avatar, Services));
                    }
                }

                Replace(DiscordRows, rows);
            }
            catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
            {
                // Discord-Voice ist optional (Bot aus, fehlender Scope) – leer statt Fehler
                DiscordRows.Clear();
            }
        }
        else
        {
            DiscordRows.Clear();
        }

        _discordLoaded = true;
        NotifyLists();
    }

    partial void OnShowEmptyChannelsChanged(bool value) => BuildTeamSpeakRows();

    private void BuildTeamSpeakRows()
    {
        var rows = new List<VoiceRow>();
        if (_ts is not null)
        {
            foreach (var channel in _ts.Channels)
            {
                Add(rows, channel, 0);
            }
        }

        Replace(TeamSpeakRows, rows);
        NotifyLists();
    }

    private void Add(List<VoiceRow> rows, TsChannel channel, int depth)
    {
        if (!ShowEmptyChannels && !channel.HasPeople)
        {
            return;
        }

        if (channel.Spacer)
        {
            // Trenner: Überschrift ohne Einrückung, Unterkanäle eine Ebene tiefer (wie im TeamSpeak-Client)
            if (SpacerTitle(channel.Name) is { } title)
            {
                rows.Add(VoiceRow.Section(title, depth));
            }
        }
        else
        {
            rows.Add(VoiceRow.Channel(channel.Name, depth, false, Services));
            foreach (var client in channel.Clients)
            {
                rows.Add(VoiceRow.Person(client.Nickname, depth + 1, client.MemberName, client.Away, client.Muted, null, Services));
            }
        }

        foreach (var child in channel.Children)
        {
            Add(rows, child, channel.Spacer ? depth : depth + 1);
        }
    }

    /// <summary>
    /// Überschrift eines Trenners: TeamSpeak-Präfix <c>[cspacer]</c>/<c>[*spacer0]</c> weg; reine Linien („---“, „___“)
    /// ergeben keine Überschrift.
    /// </summary>
    internal static string? SpacerTitle(string name)
    {
        var text = name.Trim();
        if (text.StartsWith('[') && text.IndexOf(']', StringComparison.Ordinal) is var end and > 0
            && text[..end].Contains("spacer", StringComparison.OrdinalIgnoreCase))
        {
            text = text[(end + 1)..].Trim();
        }

        return text.Trim('-', '_', '=', '.', '*', '~', ' ').Length > 0 ? text : null;
    }

    private static void Replace(ObservableCollection<VoiceRow> target, List<VoiceRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(HasTeamSpeakRows));
        OnPropertyChanged(nameof(ShowTeamSpeakEmpty));
        OnPropertyChanged(nameof(HasDiscord));
        OnPropertyChanged(nameof(ShowDiscordList));
        OnPropertyChanged(nameof(ShowDiscordEmpty));
        DataChanged();
    }

    /// <summary>TeamSpeak öffnen und direkt verbinden; Spitzname = RoG-Name. Ohne TeamSpeak: Adresse kopieren.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        var nickname = Services.CurrentUser?.Name ?? (await Services.Tokens.GetAsync())?.UserName;
        if (VoiceService.TeamSpeakLink(_ts?.Address, nickname) is not { } link || _ts?.Address is not { } address)
        {
            return;
        }

        if (await Services.Launcher.CanLaunchAsync(link) && await Services.Launcher.LaunchAsync(link))
        {
            Feedback.Show(Services.Format(StringKeys.VoiceTsLaunched, _ts.ServerName), MessageTone.Success);
            return;
        }

        var copied = Services.ServerConnector.Copy(address);
        Feedback.Show(Services.Format(copied ? StringKeys.VoiceTsMissing : StringKeys.ServersCopyFailed, address), copied ? MessageTone.Info : MessageTone.Error);
    }

    [RelayCommand]
    private void SignIn() => _requestSignIn();
}

/// <summary>Zeile im Voice-Baum: Überschrift (Trenner), Kanal oder Person – eingerückt nach Tiefe.</summary>
public sealed partial class VoiceRow : ObservableObject
{
    private VoiceRow()
    {
    }

    public required string Text { get; init; }

    /// <summary>RoG-Name hinter dem TeamSpeak-Spitznamen (wenn verschieden).</summary>
    public string? Detail { get; init; }

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public bool IsSection { get; private init; }

    public bool IsChannel { get; private init; }

    public bool IsPerson { get; private init; }

    public bool IsLocked { get; private init; }

    public bool IsAway { get; private init; }

    public bool IsMuted { get; private init; }

    /// <summary>Symbol: stumm geht vor abwesend, sonst „aktiv“.</summary>
    public bool ShowAwayIcon => IsAway && !IsMuted;

    public bool ShowActiveIcon => !IsAway && !IsMuted;

    public Thickness Indent { get; private init; }

    /// <summary>Screenreader: „Name, stumm“, „Kanal Lobby“ …</summary>
    public string AutomationName { get; private init; } = string.Empty;

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    public static VoiceRow Section(string title, int depth) => new()
    {
        Text = title,
        IsSection = true,
        Indent = new Thickness(depth * 20, depth == 0 ? 14 : 8, 0, 2),
        AutomationName = title,
    };

    public static VoiceRow Channel(string name, int depth, bool locked, AppServices services) => new()
    {
        Text = name,
        IsChannel = true,
        IsLocked = locked,
        Indent = new Thickness(depth * 20, 6, 0, 2),
        AutomationName = services.Format("Linux_VoiceChannelAutomation", name),
    };

    public static VoiceRow Person(string name, int depth, string? member, bool away, bool muted, Uri? avatar, AppServices services)
    {
        var detail = member is not null && member != name ? member : null;
        var states = new List<string>();
        if (detail is not null)
        {
            states.Add(detail);
        }

        if (muted)
        {
            states.Add(services.T("Linux_VoiceMuted"));
        }

        if (away)
        {
            states.Add(services.T("Linux_VoiceAway"));
        }

        var row = new VoiceRow
        {
            Text = name,
            Detail = detail,
            IsPerson = true,
            IsAway = away,
            IsMuted = muted,
            Indent = new Thickness(depth * 20, 2, 0, 2),
            AutomationName = states.Count > 0 ? name + ", " + string.Join(", ", states) : name,
        };
        if (avatar is not null)
        {
            _ = row.LoadAvatarAsync(services, avatar);
        }

        return row;
    }

    private async Task LoadAvatarAsync(AppServices services, Uri avatar) => Avatar = await services.Images.LoadAsync(avatar, 48);
}
