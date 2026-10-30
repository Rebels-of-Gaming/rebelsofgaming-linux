using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Realtime;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Reiter der Server-Seite (wie die Windows-App).</summary>
public enum ServersTab
{
    Game,
    Minecraft,
    TeamSpeak,
    Discord,
}

/// <summary>
/// Server-Seite mit Reitern wie in der Windows-App: <b>Gameserver</b> mit Live-Stand (auch im Gastmodus), „Verbinden“
/// über <c>xdg-open</c> (<c>steam://</c>, <c>et://</c> …, sonst Adresse kopieren) und „Adresse kopieren“;
/// <b>Minecraft</b> (Beitritts-Anleitung, <see cref="MinecraftJoinViewModel"/>); <b>TeamSpeak</b> und
/// <b>Discord-Voice</b> (wer ist wo, <see cref="VoiceViewModel"/>). Gameserver laden jede Minute still nach, Voice alle
/// 30 Sekunden und bei <c>.live.updated</c> – jeweils nur, solange der Reiter offen ist.
/// </summary>
public sealed partial class ServersViewModel : LoadableViewModel, IRoutablePage, IRefreshablePage
{
    private readonly ServerService _service;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _voiceTimer;
    private string? _highlight;
    private bool _active = true;

    public ServersViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _service = new ServerService(services.Api.Client);
        Join = new MinecraftJoinViewModel(services, Feedback);
        Voice = new VoiceViewModel(services, requestSignIn);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = LoadAsync(silent: true);
        _voiceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _voiceTimer.Tick += (_, _) => _ = Voice.LoadAsync(silent: true);
    }

    public MinecraftJoinViewModel Join { get; }

    public VoiceViewModel Voice { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameTab), nameof(IsMinecraftTab), nameof(IsTeamSpeakTab), nameof(IsDiscordTab))]
    public partial ServersTab Tab { get; set; }

    public bool IsGameTab
    {
        get => Tab == ServersTab.Game;
        set => SelectTab(value, ServersTab.Game);
    }

    public bool IsMinecraftTab
    {
        get => Tab == ServersTab.Minecraft;
        set => SelectTab(value, ServersTab.Minecraft);
    }

    public bool IsTeamSpeakTab
    {
        get => Tab == ServersTab.TeamSpeak;
        set => SelectTab(value, ServersTab.TeamSpeak);
    }

    public bool IsDiscordTab
    {
        get => Tab == ServersTab.Discord;
        set => SelectTab(value, ServersTab.Discord);
    }

    /// <summary>ToggleButtons: nur das Einschalten wählt einen Reiter (Ausschalten des aktiven bleibt wirkungslos).</summary>
    private void SelectTab(bool value, ServersTab tab)
    {
        if (value)
        {
            Tab = tab;
        }
        else if (Tab == tab)
        {
            OnPropertyChanged(tab switch
            {
                ServersTab.Game => nameof(IsGameTab),
                ServersTab.Minecraft => nameof(IsMinecraftTab),
                ServersTab.TeamSpeak => nameof(IsTeamSpeakTab),
                _ => nameof(IsDiscordTab),
            });
        }
    }

    partial void OnTabChanged(ServersTab value)
    {
        Feedback.Close();
        Voice.Feedback.Close();
        UpdateTimers();
        switch (value)
        {
            case ServersTab.Minecraft when !Join.ShowContent && !Join.IsLoading:
                _ = Join.LoadAsync();
                break;
            case ServersTab.TeamSpeak or ServersTab.Discord:
                _ = Voice.LoadAsync(silent: Voice.ShowContent);
                break;
        }
    }

    private void UpdateTimers()
    {
        if (_active && Tab == ServersTab.Game && Servers.Count > 0)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }

        if (_active && Tab is ServersTab.TeamSpeak or ServersTab.Discord)
        {
            _voiceTimer.Start();
        }
        else
        {
            _voiceTimer.Stop();
        }
    }

    /// <summary>F5 bzw. Aktualisieren-Knopf: den offenen Reiter neu laden.</summary>
    public Task RefreshAsync() => Tab switch
    {
        ServersTab.Minecraft => Join.LoadAsync(),
        ServersTab.TeamSpeak or ServersTab.Discord => Voice.LoadAsync(),
        _ => LoadAsync(),
    };

    [RelayCommand]
    private Task RefreshCurrentAsync() => RefreshAsync();

    /// <summary><c>.live.updated</c> (Voice-Zahlen ändern sich): offenen Voice-Reiter still nachladen.</summary>
    public void OnLiveChanged()
    {
        if (_active && Tab is ServersTab.TeamSpeak or ServersTab.Discord && Voice.ShowContent)
        {
            _ = Voice.LoadAsync(silent: true);
        }
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>„Neuer Gameserver“: Recht <c>servers.create</c> (nur mit 2FA vergeben) und Scope <c>servers:manage</c>.</summary>
    public bool CanProvision => Services.CurrentUser is { } me && me.Can(ServerProvisioningService.Permission) && me.HasScope(ServerProvisioningService.Scope);

    /// <summary>Provisioning-Fenster öffnen (neuer Server bzw. Verwaltung) – die Ansicht öffnet das Fenster.</summary>
    public event EventHandler<ServerProvisionRequest>? ProvisionRequested;

    [RelayCommand]
    private void Provision() => RequestProvision(null);

    private void RequestProvision(GameServer? server)
    {
        var request = new ServerProvisionRequest(server?.Slug ?? string.Empty, () =>
        {
            var vm = new ServerProvisionViewModel(Services, server);
            vm.Changed += (_, _) => _ = LoadAsync(silent: true);
            return vm;
        });
        ProvisionRequested?.Invoke(this, request);
    }

    /// <summary>Öffentliche Ban-Liste (wie „Ban-Liste“ der Windows-App).</summary>
    [RelayCommand]
    private void OpenBanList() => Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Bans));

    private ServerItemViewModel Card(GameServer server, bool highlighted) => new(server, Services, Feedback)
    {
        IsHighlighted = highlighted,
        CanManageProvisioning = server.Provisioning is not null && CanProvision,
        ManageProvisioning = () => RequestProvision(server),
    };

    protected override bool HasData => Servers.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var servers = await _service.GetAllAsync(cancellationToken);
        Servers.Clear();
        foreach (var server in servers)
        {
            Servers.Add(Card(server, server.Slug == _highlight));
        }

        UpdateTimers();
    }

    /// <summary>
    /// <c>rog://servers/{slug}</c>: Server hervorheben (Liste zeigt alle); <c>minecraft</c>, <c>teamspeak</c> und
    /// <c>discord</c> öffnen den Reiter.
    /// </summary>
    public void Open(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        switch (key.ToLowerInvariant())
        {
            case RoG.Core.Navigation.AppRoute.MinecraftKey:
                Tab = ServersTab.Minecraft;
                return;
            case "teamspeak" or "ts":
                Tab = ServersTab.TeamSpeak;
                return;
            case "discord" or "voice":
                Tab = ServersTab.Discord;
                return;
        }

        Tab = ServersTab.Game;
        _highlight = key;
        foreach (var s in Servers)
        {
            s.IsHighlighted = s.Server.Slug == key;
        }
    }

    /// <summary>Statusänderung aus der Echtzeit (Kanal <c>servers</c>) – nur Zahlen, ohne Spielernamen.</summary>
    public void Apply(ServerStatusUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        for (var i = 0; i < Servers.Count; i++)
        {
            var s = Servers[i].Server;
            if (s.Id == update.Id || (update.Slug.Length > 0 && s.Slug == update.Slug))
            {
                var changed = s with { Online = update.Online, Map = update.Map, Players = update.Players, MaxPlayers = update.MaxPlayers };
                Servers[i] = Card(changed, Servers[i].IsHighlighted);
            }
        }
    }

    /// <summary>Seite verlassen: kein Nachladen im Hintergrund.</summary>
    public void Pause()
    {
        _active = false;
        UpdateTimers();
    }

    /// <summary>Seite wieder geöffnet: Nachladen des offenen Reiters weiter.</summary>
    public void Resume()
    {
        _active = true;
        UpdateTimers();
    }
}

/// <summary>Eine Server-Karte (Server-Seite, Squad-Detail, Termin): Status, Spieler, Map, Verbinden, Kopieren.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly FeedbackViewModel _feedback;

    public ServerItemViewModel(GameServer server, AppServices services, FeedbackViewModel feedback)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(services);
        Server = server;
        _services = services;
        _feedback = feedback;
        var plan = ServerConnector.Plan(server);
        CanConnect = plan.Kind != ConnectKind.None;
        IsOnline = server.Online == true;
        IsOffline = server.Online == false;
        StatusText = services.T(server.Online switch
        {
            true => StringKeys.ServersOnline,
            false => StringKeys.ServersOffline,
            null => StringKeys.ServersUnknown,
        });
        PlayersText = services.Format(StringKeys.ServersPlayers, server.Players, server.MaxPlayers);
        Players = server.PlayerList?.Select(p => p.MemberName is null || p.MemberName == p.Name ? p.Name : $"{p.Name} · {p.MemberName}").ToList() ?? [];
        PlayersHeader = services.Format(StringKeys.ServersPlayerListHeader, Players.Count);
        ConnectAutomationName = services.Format(StringKeys.ServersConnectAutomation, server.Name);
        CopyAutomationName = services.Format(StringKeys.ServersCopyAutomation, server.Name);
        Meta = string.Join(" · ", new[] { server.GameName, server.Map }.Where(s => !string.IsNullOrEmpty(s)));
        FillPercent = server.MaxPlayers > 0 ? Math.Clamp(100.0 * server.Players / server.MaxPlayers, 0, 100) : 0;
        CanControl = ServerControlAccess.For(server.Control, services.CurrentUser).Any;
        ControlAutomationName = services.Format("Linux_ServerControlAutomation", server.Name);
    }

    /// <summary>Steuerungsrechte (<c>control</c>) plus passender Token-Scope → „Steuern“ (eigenes Fenster).</summary>
    public bool CanControl { get; }

    public string ControlAutomationName { get; }

    [RelayCommand]
    private void Control() => _services.ServerControlOpener?.Invoke(Server);

    public GameServer Server { get; }

    public string Name => Server.Name;

    public string Address => Server.Address;

    /// <summary>„Spiel · Map“.</summary>
    public string Meta { get; }

    public bool HasMeta => Meta.Length > 0;

    public bool IsOnline { get; }

    public bool IsOffline { get; }

    public bool IsUnknown => !IsOnline && !IsOffline;

    public string StatusText { get; }

    public string PlayersText { get; }

    public double FillPercent { get; }

    public bool ShowPlayers => IsOnline;

    public bool Passworded => Server.Passworded;

    public IReadOnlyList<string> Players { get; }

    public bool HasPlayerList => Players.Count > 0;

    public string PlayersHeader { get; }

    public bool CanConnect { get; }

    public string ConnectAutomationName { get; }

    public string CopyAutomationName { get; }

    /// <summary>Über die Website angelegt: Installationsstand und Ablauf als Hinweis auf der Karte.</summary>
    public bool HasProvisioning => Server.Provisioning is not null;

    public string ProvisioningText => Server.Provisioning is not { } p ? string.Empty : string.Join(" · ", new[]
    {
        _services.T("Provision_Status_" + p.Status),
        p.ExpiresAt is { } at ? _services.Format(StringKeys.ProvisionExpiresAt, at.ToLocalTime().ToString("g", _services.Culture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Rot: fehlgeschlagen oder läuft in 24 h ab; Gold: wird installiert; sonst gedämpft.</summary>
    public Avalonia.Media.IBrush ProvisioningBrush => UiResources.Brush(
        Server.Provisioning is { } p && (p.IsFailed || p.ExpiresSoon(DateTimeOffset.Now)) ? "RoGRedTextBrush" : Server.Provisioning?.IsInstalling == true ? "RoGGoldBrush" : "RoGMutedBrush");

    /// <summary>Alle zugewiesenen Ports (über die Website angelegt), Hauptport zuerst und gekennzeichnet.</summary>
    public IReadOnlyList<string> Ports => Server.Provisioning is { } p
        ? p.Ports.Select(a => a.Primary ? $"{a.Address} · {_services.T(StringKeys.ServersPrimaryPort)}" : a.Address).ToList()
        : [];

    public bool HasPorts => Server.Provisioning?.Ports.Count > 0;

    public string PortsHeader => _services.Format(StringKeys.ServersPortsHeader, Server.Provisioning?.Ports.Count ?? 0);

    /// <summary>„Verwalten“ (Provisioning-Fenster) – nur mit <c>servers.create</c> + <c>servers:manage</c>.</summary>
    public bool CanManageProvisioning { get; init; }

    public Action? ManageProvisioning { get; init; }

    [RelayCommand]
    private void Manage() => ManageProvisioning?.Invoke();

    /// <summary>Per Link geöffnet (<c>rog://servers/slug</c>) – Karte mit roter Kante.</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var outcome = await _services.ServerConnector.ConnectAsync(Server);
        var message = ConnectMessage.Describe(outcome, Name, _services.Localizer);
        _feedback.Show(message.Text, message.Tone);
    }

    [RelayCommand]
    private void Copy()
    {
        var ok = _services.ServerConnector.Copy(Address);
        _feedback.Show(_services.Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, Address), ok ? MessageTone.Success : MessageTone.Error);
    }
}

/// <summary>Provisioning-Fenster: <see cref="Key"/> = Slug (leer = neuer Server), <see cref="Create"/> baut das ViewModel.</summary>
public sealed record ServerProvisionRequest(string Key, Func<ServerProvisionViewModel> Create);
