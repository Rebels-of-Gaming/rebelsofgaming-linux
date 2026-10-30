using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Realtime;
using RoG.Linux.App.Services;
using RoG.Linux.Platform;

namespace RoG.Linux.App.ViewModels;

/// <summary>Zustand der Live-Konsole (Text plus Punkt-Farbe in der Ansicht).</summary>
public enum ConsoleConnection
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Lost,
    Demo,
    NoAccess,
}

/// <summary>
/// Reiter „Konsole“ (wie <c>ServerConsoleViewModel</c> der Windows-App): Zustand und Ressourcen (live aus Wings-<c>stats</c>),
/// Power mit Rückfrage im Inhalt, Live-Konsole über den Wings-WebSocket mit kurzlebigem Ticket aus der API
/// (<see cref="ConsoleClient"/> des Kerns – er erneuert das Ticket bei <c>token expiring</c>), ANSI entfernt, Befehle über
/// die API (Audit-Log) mit ↑/↓-Verlauf. Das Ticket bleibt nur im Konsolen-Client im Speicher – nie in Logs oder Dateien.
/// Was gezeigt wird, folgt <see cref="ServerControlAccess"/> und dem <see cref="ConsoleLevel"/> des Tickets.
/// </summary>
public sealed partial class ServerConsoleViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Höchstens so viele Zeilen im Verlauf (ältere fallen vorne weg).</summary>
    public const int MaxLines = 5000;

    private const int MaxRetries = 3;

    private readonly AppServices _services;
    private readonly GameServer _server;
    private readonly ServerControlAccess _access;
    private readonly FeedbackViewModel _feedback;
    private readonly InlineConfirmViewModel _confirm;
    private readonly ServerControlService _control;
    private readonly Func<string?, IWebSocketConnection> _sockets;
    private ConsoleClient? _console;
    private int _retries;
    private bool _closing;

    public ServerConsoleViewModel(
        AppServices services,
        GameServer server,
        ServerControlAccess access,
        FeedbackViewModel feedback,
        InlineConfirmViewModel confirm,
        Func<string?, IWebSocketConnection>? sockets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _server = server;
        _access = access;
        _feedback = feedback;
        _confirm = confirm;
        _control = new ServerControlService(services.Api.Client);
        var userAgent = $"{AppIdentity.UserAgentProduct}/{AppServices.AppVersion}";
        // Wings prüft den Origin gegen allowed_origins – die API nennt ihn im Ticket (Admin-Domain), keine feste Domain
        _sockets = sockets ?? (origin => new ClientWebSocketConnection(userAgent, origin));
        Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoLines));
    }

    /// <summary>Im UI-Thread ausführen (Ereignisse des Konsolen-Clients kommen aus dem Empfangs-Thread); Tests setzen es.</summary>
    internal Action<Action> OnUi { get; set; } = action => Dispatcher.UIThread.Post(action);

    /// <summary>Wartezeit vor dem automatischen Neuverbinden (Tests verkürzen sie).</summary>
    internal Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

    public ObservableCollection<string> Lines { get; } = [];

    public bool HasNoLines => Lines.Count == 0;

    /// <summary>Befehls-Verlauf für ↑/↓ (Kern).</summary>
    public CommandHistory History { get; } = new();

    public bool CanPower => _access.Power;

    public bool HasConsole => _access.Console;

    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResources))]
    public partial string ResourcesText { get; set; } = string.Empty;

    public bool HasResources => ResourcesText.Length > 0;

    /// <summary>Speicher in Prozent des Limits (Balken), <c>null</c> = ohne Limit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryLimit))]
    public partial double? MemoryPercent { get; set; }

    public bool HasMemoryLimit => MemoryPercent is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    public partial bool IsRunning { get; set; }

    public bool IsStopped => !IsRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsConnecting), nameof(IsLost), nameof(IsDemo), nameof(CanCommand), nameof(CommandHint), nameof(HasCommandHint))]
    public partial ConsoleConnection Connection { get; set; }

    public bool IsConnected => Connection == ConsoleConnection.Connected;

    public bool IsConnecting => Connection is ConsoleConnection.Connecting or ConsoleConnection.Reconnecting;

    public bool IsLost => Connection == ConsoleConnection.Lost;

    /// <summary>Demo-Server: keine echte Konsole, Befehle und Power simuliert die API.</summary>
    public bool IsDemo => Connection == ConsoleConnection.Demo;

    /// <summary>„Konsole: verbunden“, „verbinde …“, „getrennt …“, „Demo-Server …“, „Keine Berechtigung …“.</summary>
    [ObservableProperty]
    public partial string ConnectionText { get; set; } = string.Empty;

    /// <summary>Was das Ticket erlaubt: lesen, Befehle, Power.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommand), nameof(CommandHint), nameof(HasCommandHint))]
    public partial ConsoleLevel Level { get; set; }

    /// <summary>Befehle nur mit Recht, ohne Vier-Augen-Sperre, mit passendem Ticket-Level und verbundener (oder Demo-)Konsole.</summary>
    public bool CanCommand => (IsConnected || IsDemo) && MayCommand && Level >= ConsoleLevel.Command;

    private bool MayCommand => _access.Console && _server.Control is { ConsoleCommand: true, CanCommand: true };

    /// <summary>Warum die Eingabe gesperrt ist (Lesemodus bzw. Vier-Augen-Freigabe); leer = keine Sperre oder nicht verbunden.</summary>
    public string CommandHint
    {
        get
        {
            if (!_access.Console || !(IsConnected || IsDemo))
            {
                return string.Empty;
            }

            if (_server.Control is { ConsoleCommand: true, CanCommand: false })
            {
                return _services.T("Linux_ConsoleApprovalNeeded");
            }

            return MayCommand && Level >= ConsoleLevel.Command ? string.Empty : _services.T("Linux_ConsoleReadOnly");
        }
    }

    public bool HasCommandHint => CommandHint.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Command { get; set; } = string.Empty;

    /// <summary>Ansicht folgt dem Ende (man ist unten); sonst bietet sie „Zum Ende“ an. Setzt die Ansicht.</summary>
    [ObservableProperty]
    public partial bool IsFollowing { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServerCommand), nameof(StopServerCommand), nameof(RestartServerCommand), nameof(KillServerCommand))]
    public partial bool IsPowerBusy { get; set; }

    /// <summary>Neue Zeilen – die Ansicht scrollt ans Ende, solange <see cref="IsFollowing"/>.</summary>
    public event EventHandler? LinesAdded;

    public async Task StartAsync()
    {
        await RefreshResourcesAsync();
        if (_access.Console)
        {
            await ConnectAsync();
        }
        else
        {
            Connection = ConsoleConnection.NoAccess;
            ConnectionText = _services.T(StringKeys.ConsoleNoAccess);
        }
    }

    /// <summary>„Neu verbinden“: Zähler zurück, neues Ticket, neue Verbindung.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!_access.Console)
        {
            return;
        }

        _retries = 0;
        await OpenAsync(ConsoleConnection.Connecting);
    }

    private async Task OpenAsync(ConsoleConnection state)
    {
        if (_console is { } old)
        {
            _console = null;
            await old.DisposeAsync();
        }

        if (_closing)
        {
            return;
        }

        Connection = state;
        ConnectionText = _services.T(state == ConsoleConnection.Reconnecting ? StringKeys.ConsoleReconnecting : StringKeys.ConsoleConnecting);
        var console = new ConsoleClient(ct => _control.GetConsoleTicketAsync(_server.Slug, ct), _sockets);
        console.Output += (_, line) => OnUi(() => AddLines(line));
        console.Error += (_, text) => OnUi(() => AddLines("[Wings] " + text));
        console.StatusChanged += (_, s) => OnUi(() => SetState(s));
        console.Stats += (_, json) =>
        {
            if (ServerResources.FromWingsStats(json) is { } r)
            {
                OnUi(() => ShowResources(r));
            }
        };
        console.Authenticated += (_, _) => OnUi(() =>
        {
            if (ReferenceEquals(_console, console))
            {
                _retries = 0;
                Level = console.Level; // nach erneuertem Ticket kann sich der Level ändern
                Connection = ConsoleConnection.Connected;
                ConnectionText = _services.T(StringKeys.ConsoleConnected);
            }
        });
        console.Disconnected += (_, error) => OnUi(() => OnDisconnected(console, error));
        _console = console;

        try
        {
            await console.ConnectAsync();
            if (!ReferenceEquals(_console, console))
            {
                return; // inzwischen neu verbunden oder Fenster zu
            }

            Level = console.Level;
            if (console.IsDemo)
            {
                Connection = ConsoleConnection.Demo;
                ConnectionText = _services.T(StringKeys.ConsoleDemo);
                AddLines("[RoG] " + ConnectionText);
                return;
            }

            // Verbunden heißt erst „auth success“ – bis dahin bleibt „verbinde …“ stehen
        }
        catch (ApiProblemException ex)
        {
            await FailAsync(console, _services.Localizer.GetError(ex.Problem.Code));
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            await FailAsync(console, _services.Localizer.GetError(ApiProblem.Codes.Unknown));
        }
#pragma warning disable CA1031 // jeder Verbindungsfehler (WebSocket, DNS, TLS) wird angezeigt, statt die Konsole leer zu lassen
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await FailAsync(console, ex.Message);
        }
    }

    private async Task FailAsync(ConsoleClient console, string message)
    {
        if (ReferenceEquals(_console, console))
        {
            _console = null;
        }

        await console.DisposeAsync();
        Connection = ConsoleConnection.Lost;
        ConnectionText = _services.T(StringKeys.ConsoleLost);
        AddLines("[RoG] " + message);
    }

    private void OnDisconnected(ConsoleClient console, Exception? error)
    {
        if (!ReferenceEquals(_console, console) || _closing)
        {
            return; // alte Verbindung nach „Neu verbinden“ oder Fenster zu
        }

        Connection = ConsoleConnection.Lost;
        ConnectionText = _services.T(StringKeys.ConsoleLost);
        if (error is not null)
        {
            AddLines("[RoG] " + error.Message);
        }

        // wie Website und Windows: bis zu dreimal mit wachsendem Abstand neu verbinden
        if (_retries < MaxRetries)
        {
            _retries++;
            Connection = ConsoleConnection.Reconnecting;
            ConnectionText = _services.T(StringKeys.ConsoleReconnecting);
            _ = ReconnectLaterAsync(console, TimeSpan.FromSeconds(1.5 * _retries));
        }
    }

    private async Task ReconnectLaterAsync(ConsoleClient lost, TimeSpan wait)
    {
        await Delay(wait);
        OnUi(() =>
        {
            // nur, wenn inzwischen niemand „Neu verbinden“ gedrückt hat
            if (!_closing && ReferenceEquals(_console, lost))
            {
                _ = OpenAsync(ConsoleConnection.Reconnecting);
            }
        });
    }

    private void SetState(string state)
    {
        StateText = StateLabel(state);
        IsRunning = state is "running" or "starting";
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Command);

    /// <summary>Befehl über die API (Audit-Log, Rechte serverseitig) statt direkt über den Socket – wie Windows.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var command = Command.Trim();
        if (!CanCommand || command.Length is 0 or > 500)
        {
            return;
        }

        Command = string.Empty;
        History.Add(command);
        AddLines("> " + command);
        await RunAsync(() => _control.SendCommandAsync(_server.Slug, command));
    }

    /// <summary>↑ in der Eingabe: vorheriger Befehl (<c>null</c> = Verlauf leer).</summary>
    public string? PreviousCommand() => History.Previous() is { } text ? Command = text : null;

    /// <summary>↓ in der Eingabe: nächster Befehl, hinter dem neuesten die leere Eingabe.</summary>
    public string NextCommand() => Command = History.Next();

    private bool CanPowerNow() => CanPower && !IsPowerBusy;

    [RelayCommand(CanExecute = nameof(CanPowerNow))]
    private Task StartServerAsync() => PowerAsync(PowerSignal.Start);

    [RelayCommand(CanExecute = nameof(CanPowerNow))]
    private async Task StopServerAsync()
    {
        if (await AskAsync("Linux_ConsoleStopTitle", "Linux_ConsoleStopText", "Console_Stop.Content"))
        {
            await PowerAsync(PowerSignal.Stop);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPowerNow))]
    private async Task RestartServerAsync()
    {
        if (await AskAsync("Linux_ConsoleRestartTitle", "Linux_ConsoleRestartText", "Console_Restart.Content"))
        {
            await PowerAsync(PowerSignal.Restart);
        }
    }

    /// <summary>Kill fragt immer (Datenverlust möglich) – Texte der Windows-App.</summary>
    [RelayCommand(CanExecute = nameof(CanPowerNow))]
    private async Task KillServerAsync()
    {
        if (await AskAsync(StringKeys.ConsoleKillTitle, StringKeys.ConsoleKillText, StringKeys.ConsoleKillConfirm))
        {
            await PowerAsync(PowerSignal.Kill);
        }
    }

    private Task<bool> AskAsync(string title, string text, string confirm) =>
        _confirm.AskAsync(_services.T(title), _services.T(text), _services.T(confirm).ToUpper(_services.Culture), _services.T(StringKeys.CommonCancel).ToUpper(_services.Culture));

    private async Task PowerAsync(PowerSignal signal)
    {
        IsPowerBusy = true;
        try
        {
            if (await RunAsync(() => _control.PowerAsync(_server.Slug, signal)))
            {
                _feedback.Show(_services.T(StringKeys.ConsolePowerSent), MessageTone.Success);
                await RefreshResourcesAsync();
            }
        }
        finally
        {
            IsPowerBusy = false;
        }
    }

    [RelayCommand]
    private void ClearLines() => Lines.Clear();

    /// <summary>„Kopieren“: markierte Zeilen, ohne Auswahl die ganze Ausgabe – in die Zwischenablage.</summary>
    public void CopyLines(IReadOnlyList<string>? selected)
    {
        var lines = selected is { Count: > 0 } ? selected : Lines.ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var ok = _services.ServerConnector.Copy(string.Join('\n', lines));
        _feedback.Show(ok ? _services.Format("Linux_ConsoleCopied", lines.Count) : _services.T("Linux_CopyFailed"), ok ? MessageTone.Success : MessageTone.Error);
    }

    [RelayCommand]
    private void CopyAll() => CopyLines(null);

    private async Task RefreshResourcesAsync()
    {
        if (!_access.Console)
        {
            return;
        }

        try
        {
            ShowResources(await _control.GetResourcesAsync(_server.Slug));
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // Ressourcen sind Beiwerk – die Live-Konsole liefert sie laufend
        }
    }

    private void ShowResources(ServerResources r)
    {
        SetState(r.State);
        var memory = r.MemoryLimit is { } limit ? $"{Mb(r.Memory)} / {Mb(limit)}" : Mb(r.Memory);
        MemoryPercent = r.MemoryLimit is { } max && max > 0 ? Math.Clamp(100.0 * r.Memory / max, 0, 100) : null;
        ResourcesText = _services.Format(StringKeys.ConsoleResources,
            r.CpuPercent.ToString("0.0", _services.Culture), memory, Mb(r.Disk), Uptime(r.Uptime));
    }

    private string StateLabel(string state) => _services.T(state switch
    {
        "running" => StringKeys.ConsoleStateRunning,
        "starting" => StringKeys.ConsoleStateStarting,
        "stopping" => StringKeys.ConsoleStateStopping,
        "offline" => StringKeys.ConsoleStateOffline,
        _ => StringKeys.ServersUnknown,
    });

    private static string Mb(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0} MB");

    private static string Uptime(TimeSpan t) => t.TotalDays >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalDays} d {t.Hours} h")
        : string.Create(CultureInfo.InvariantCulture, $"{t.Hours} h {t.Minutes} min");

    /// <summary>Zeilen anhängen (mehrzeilige Ausgabe wird geteilt), Verlauf auf <see cref="MaxLines"/> begrenzen.</summary>
    internal void AddLines(string text)
    {
        foreach (var part in text.Split('\n'))
        {
            Lines.Add(part.TrimEnd('\r'));
        }

        var overflow = Lines.Count - MaxLines;
        for (var i = 0; i < overflow; i++)
        {
            Lines.RemoveAt(0);
        }

        LinesAdded?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            _feedback.Show(_services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or ArgumentException)
        {
            _feedback.Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        if (_console is { } console)
        {
            _console = null;
            await console.DisposeAsync();
        }
    }
}
