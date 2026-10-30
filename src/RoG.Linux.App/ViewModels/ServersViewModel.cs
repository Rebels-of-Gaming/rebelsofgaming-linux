using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Realtime;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Gameserver mit Live-Stand (auch im Gastmodus), „Verbinden“ über <c>xdg-open</c> (<c>steam://</c>, <c>et://</c> …,
/// sonst Adresse kopieren) und „Adresse kopieren“. Lädt jede Minute still nach (der Query-Worker der Website fragt
/// minütlich ab); mit Echtzeit kommen Statusänderungen sofort (<see cref="Apply"/>).
/// </summary>
public sealed partial class ServersViewModel : LoadableViewModel, IRoutablePage
{
    private readonly ServerService _service;
    private readonly DispatcherTimer _timer;
    private string? _highlight;

    public ServersViewModel(AppServices services)
        : base(services)
    {
        _service = new ServerService(services.Api.Client);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = LoadAsync(silent: true);
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    protected override bool HasData => Servers.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var servers = await _service.GetAllAsync(cancellationToken);
        Servers.Clear();
        foreach (var server in servers)
        {
            Servers.Add(new ServerItemViewModel(server, Services, Feedback) { IsHighlighted = server.Slug == _highlight });
        }

        _timer.Start();
    }

    /// <summary><c>rog://servers/{slug}</c>: Server hervorheben (Liste zeigt alle).</summary>
    public void Open(string key)
    {
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
                Servers[i] = new ServerItemViewModel(changed, Services, Feedback) { IsHighlighted = Servers[i].IsHighlighted };
            }
        }
    }

    /// <summary>Seite verlassen: kein Nachladen im Hintergrund.</summary>
    public void Pause() => _timer.Stop();
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
    }

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
