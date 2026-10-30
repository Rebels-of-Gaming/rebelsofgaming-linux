using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung → Reiter „Minecraft-Netzwerk“ (wie <c>MinecraftOpsViewModel</c> der Windows-App, Website v0.41.0,
/// Guide 47): Heartbeat-Stand und Rolle je Server, Minigame-Pool und Plugin-Deploy in zwei Stufen (Release prüfen, dann
/// verteilen). Pool nur mit <c>minecraft:pool</c> + Recht <c>minecraft.pool</c>, Rollen und Deploy nur mit
/// <c>minecraft:deploy</c> + Recht <c>minecraft.deploy</c>; der Reiter ist sichtbar, sobald eins von beiden geht.
/// </summary>
public sealed partial class MinecraftOpsViewModel : MinecraftAdminTabBase
{
    public MinecraftOpsViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        RoleOptions =
        [
            new SettingsOption(string.Empty, L(StringKeys.NetworkRoleNone)),
            .. MinecraftService.ServerRoles.Select(r => new SettingsOption(r, Label("Network_Role_", r))),
        ];
    }

    public bool CanPool => Allowed(MinecraftService.PoolPermission, MinecraftService.PoolScope);

    public bool CanDeploy => Allowed(MinecraftService.DeployPermission, MinecraftService.DeployScope);

    public override bool CanView => CanPool || CanDeploy;

    /// <summary>Wartezeit zwischen zwei Abfragen der Release-Prüfung bzw. des Verteilens (Tests: 0).</summary>
    internal TimeSpan ReleasePoll { get; set; } = TimeSpan.FromSeconds(2);

    internal TimeSpan DeployPoll { get; set; } = TimeSpan.FromSeconds(3);

    public IReadOnlyList<SettingsOption> RoleOptions { get; }

    public ObservableCollection<NetworkServerItemViewModel> Servers { get; } = [];

    public ObservableCollection<PoolGroupItemViewModel> Groups { get; } = [];

    public ObservableCollection<string> PoolLog { get; } = [];

    public ObservableCollection<DeployItemViewModel> Deploys { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool NoServers { get; set; }

    [ObservableProperty]
    public partial bool NoGroups { get; set; }

    [ObservableProperty]
    public partial bool HasPoolLog { get; set; }

    [ObservableProperty]
    public partial string? PoolInfo { get; set; }

    // --- Plugin-Deploy -----------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelease), nameof(CanStartDeploy))]
    [NotifyCanExecuteChangedFor(nameof(StartDeployCommand))]
    public partial PluginRelease? Release { get; set; }

    public bool HasRelease => Release is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDeploy))]
    [NotifyCanExecuteChangedFor(nameof(StartDeployCommand), nameof(CheckReleaseCommand))]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDeploy))]
    [NotifyCanExecuteChangedFor(nameof(StartDeployCommand))]
    public partial bool IsDeploying { get; set; }

    public bool CanStartDeploy => Release is { IsReady: true } && !IsChecking && !IsDeploying;

    [ObservableProperty]
    public partial string? ReleaseText { get; set; }

    [ObservableProperty]
    public partial string? ReleaseFiles { get; set; }

    [ObservableProperty]
    public partial bool Restart { get; set; } = true;

    [ObservableProperty]
    public partial bool HasDeploys { get; set; }

    protected override Task LoadFirstAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsBusy || !CanView)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadServersAsync();
            if (CanPool)
            {
                await LoadPoolAsync();
            }

            if (CanDeploy)
            {
                await LoadDeploysAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadServersAsync()
    {
        try
        {
            var servers = await Minecraft.NetworkServersAsync();
            var selected = Servers.Where(s => s.IsSelected).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            Servers.Clear();
            foreach (var server in servers)
            {
                Servers.Add(new NetworkServerItemViewModel(server, this) { IsSelected = selected.Contains(server.Id) });
            }

            NoServers = Servers.Count == 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
    }

    internal async Task LoadPoolAsync()
    {
        try
        {
            var pool = await Minecraft.PoolAsync();
            Groups.Clear();
            foreach (var group in pool.Groups)
            {
                Groups.Add(new PoolGroupItemViewModel(group, this));
            }

            NoGroups = Groups.Count == 0;
            PoolLog.Clear();
            foreach (var action in pool.Actions.Take(30))
            {
                PoolLog.Add(ActionText(action));
            }

            HasPoolLog = PoolLog.Count > 0;
            PoolInfo = Format(StringKeys.PoolInfo, pool.CooldownMinutes);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
    }

    private async Task LoadDeploysAsync()
    {
        try
        {
            var deploys = await Minecraft.DeploysAsync();
            Deploys.Clear();
            foreach (var deploy in deploys)
            {
                Deploys.Add(new DeployItemViewModel(deploy, this));
            }

            HasDeploys = Deploys.Count > 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
    }

    internal async Task SaveRoleAsync(NetworkServerItemViewModel item)
    {
        var role = string.IsNullOrEmpty(item.SelectedRole?.Value) ? null : item.SelectedRole!.Value;
        if (role == MinecraftService.RoleMinigame && !MinecraftService.IsValidName(item.Folder.Trim()))
        {
            Show(L(StringKeys.NetworkFolderFormat), MessageTone.Info);
            return;
        }

        await RunAsync(async () =>
        {
            if (await Minecraft.SetServerRoleAsync(item.Id, role, item.Folder) is { } updated)
            {
                var index = Servers.IndexOf(item);
                if (index >= 0)
                {
                    Servers[index] = new NetworkServerItemViewModel(updated, this) { IsSelected = item.IsSelected };
                }
            }

            Show(Format(StringKeys.NetworkRoleSaved, item.Name), MessageTone.Success);
        });
    }

    internal async Task SavePoolAsync(PoolGroupItemViewModel group, IReadOnlyList<string> servers)
    {
        if (await RunAsync(() => Minecraft.SavePoolAsync(group.Game, group.Enabled, ToInt(group.MinRunning), ToInt(group.StartThreshold), ToInt(group.IdleMinutes), servers)))
        {
            Show(Format(StringKeys.PoolSaved, group.Game), MessageTone.Success);
            await LoadPoolAsync();
        }
    }

    internal async Task DeletePoolAsync(PoolGroupItemViewModel group)
    {
        if (await AskAsync(L(StringKeys.PoolDeleteTitle), Format(StringKeys.PoolDeleteText, group.Game))
            && await RunAsync(() => Minecraft.DeletePoolAsync(group.Game)))
        {
            Show(Format(StringKeys.PoolDeleted, group.Game), MessageTone.Success);
            await LoadPoolAsync();
        }
    }

    internal async Task UpdateInstanceAsync(string game, string serverId, bool? excluded, bool resetFailures)
    {
        await RunAsync(() => Minecraft.UpdatePoolServerAsync(game, serverId, excluded, resetFailures));
        await LoadPoolAsync();
    }

    private bool CanCheckRelease() => CanDeploy && !IsChecking;

    /// <summary>Stufe 1: Release prüfen und warten, bis die Prüfung fertig ist (höchstens 2 Minuten).</summary>
    [RelayCommand(CanExecute = nameof(CanCheckRelease))]
    private async Task CheckReleaseAsync()
    {
        IsChecking = true;
        try
        {
            var release = await Minecraft.CheckReleaseAsync();
            for (var i = 0; release is { IsChecking: true } && i < 60; i++)
            {
                ShowRelease(release);
                await Task.Delay(ReleasePoll);
                release = await Minecraft.ReleaseAsync(release.Id);
            }

            ShowRelease(release);
            if (release is { IsReady: false, IsChecking: false })
            {
                Show(Format(StringKeys.ReleaseFailed, Label("Release_Error_", release.Error ?? "job_failed")), MessageTone.Error);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ShowRelease(PluginRelease? release)
    {
        Release = release;
        if (release is null)
        {
            ReleaseText = null;
            ReleaseFiles = null;
            return;
        }

        var parts = new List<string> { release.Tag ?? "?" };
        if (release.PublishedAt is { } published)
        {
            parts.Add(Format(StringKeys.ReleasePublished, published.ToLocalTime().ToString("g", Services.Culture)));
        }

        parts.Add(Label("Release_Status_", release.Status));
        parts.Add(L(release.IsSigned ? StringKeys.ReleaseSigned : StringKeys.ReleaseUnsigned));
        if (release.IsReady && release.ExpiresAt is { } expires)
        {
            parts.Add(Format(StringKeys.ReleaseValidUntil, expires.ToLocalTime().ToString("t", Services.Culture)));
        }

        ReleaseText = string.Join(" · ", parts);
        var lines = release.Folders.Select(f => f.Folder + ": " + string.Join(", ", f.Files.Select(x => x.Name))).ToList();
        if (release.Sha256 is { } sha)
        {
            lines.Insert(0, "SHA-256 " + sha);
        }

        if (release.Ignored.Count > 0)
        {
            lines.Add(Format(StringKeys.ReleaseIgnored, string.Join(", ", release.Ignored)));
        }

        ReleaseFiles = string.Join("\n", lines);
    }

    /// <summary>Stufe 2: verteilen – ausgewählte Server, ohne Auswahl alle passenden (Proxy nur, wenn ausgewählt).</summary>
    [RelayCommand(CanExecute = nameof(CanStartDeploy))]
    private async Task StartDeployAsync()
    {
        if (Release is not { } release)
        {
            return;
        }

        var selected = Servers.Where(s => s.IsSelected).ToList();
        var targets = selected.Count > 0 ? string.Join(", ", selected.Select(s => s.Name)) : L(StringKeys.DeployAllTargets);
        var text = Format(StringKeys.DeployConfirmText, release.Tag ?? "?", targets, L(Restart ? StringKeys.DeployWithRestart : StringKeys.DeployWithoutRestart));
        if (!await AskAsync(L(StringKeys.DeployConfirmTitle), text))
        {
            return;
        }

        IsDeploying = true;
        try
        {
            var deploy = await Minecraft.StartDeployAsync(release.Id, selected.Select(s => s.Id).ToList(), Restart);
            for (var i = 0; deploy is { IsRunning: true } && i < 200; i++)
            {
                ShowDeploy(deploy);
                await Task.Delay(DeployPoll);
                deploy = await Minecraft.DeployAsync(deploy.Id);
            }

            if (deploy is not null)
            {
                ShowDeploy(deploy);
                Show(Format(StringKeys.DeployFinished, Label("Deploy_Status_", deploy.Status)), deploy.Status == "done" ? MessageTone.Success : MessageTone.Error);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private void ShowDeploy(PluginDeploy deploy)
    {
        var item = new DeployItemViewModel(deploy, this);
        var index = Deploys.ToList().FindIndex(d => d.Id == deploy.Id);
        if (index >= 0)
        {
            Deploys[index] = item;
        }
        else
        {
            Deploys.Insert(0, item);
        }

        HasDeploys = true;
    }

    internal string ActionText(PoolAction a)
    {
        var parts = new List<string>();
        if (a.CreatedAt is { } at)
        {
            parts.Add(at.ToLocalTime().ToString("g", Services.Culture));
        }

        parts.Add(a.Game);
        parts.Add(Label("Pool_Action_", a.Action) + (a.ServerName is { } s ? " " + s : string.Empty));
        if (a.Reason is { } reason)
        {
            parts.Add(Label("Pool_Reason_", reason));
        }

        if (!a.Ok)
        {
            parts.Add(L(StringKeys.PoolActionFailed) + (a.Error is { } e ? ": " + e : string.Empty));
        }

        return string.Join(" · ", parts);
    }

    internal string Text(string key) => L(key);

    internal string Text(string key, params object?[] args) => Format(key, args);
}

/// <summary>Ein Minecraft-Server: Heartbeat-Stand, festgelegte Rolle (bearbeitbar mit Deploy-Recht), Auswahl für den Deploy.</summary>
public sealed partial class NetworkServerItemViewModel : ObservableObject
{
    private readonly MinecraftOpsViewModel _owner;

    public NetworkServerItemViewModel(NetworkServer server, MinecraftOpsViewModel owner)
    {
        _owner = owner;
        Id = server.Id;
        Name = server.Name;
        var state = server.State;
        if (state is null)
        {
            StateText = owner.Text(StringKeys.NetworkNoHeartbeat);
        }
        else
        {
            var parts = new List<string> { owner.Text(state.Fresh ? StringKeys.NetworkFresh : StringKeys.NetworkStale) };
            if (state.Role is { } role)
            {
                parts.Add(owner.Label("Network_Role_", role));
            }

            var culture = owner.Culture;
            parts.Add(state.PlayersMax is { } max ? $"{state.PlayersOnline}/{max}" : state.PlayersOnline.ToString(culture));
            if (state.Tps is { } tps)
            {
                parts.Add("TPS " + tps.ToString("0.0", culture));
            }

            if (state.MinigameGame is { } game)
            {
                parts.Add(string.Join(" ", new[] { game, state.MinigameState, state.Arena }.Where(x => !string.IsNullOrEmpty(x))));
            }

            if (state.PluginVersion is { } version)
            {
                parts.Add("rog " + version);
            }

            if (state.ReportedAt is { } at)
            {
                parts.Add(at.ToLocalTime().ToString("g", culture));
            }

            StateText = string.Join(" · ", parts);
            IsFresh = state.Fresh;
        }

        var d = server.Deploy;
        DeployText = d.Configured
            ? owner.Text(StringKeys.NetworkDeployConfigured, owner.Label("Network_Role_", d.Role ?? "?"), d.Folder ?? "?")
            : owner.Text(StringKeys.NetworkDeployMissing);
        HasWarning = d.Mismatch || !server.Linked;
        WarningText = !server.Linked ? owner.Text(StringKeys.NetworkNotLinked) : d.Mismatch ? owner.Text(StringKeys.NetworkMismatch) : null;
        CanEditRole = owner.CanDeploy;
        CanSelect = owner.CanDeploy && server.Linked;

        // Vorbelegung: festgelegte Rolle, sonst der Vorschlag aus dem Heartbeat
        var suggested = d.Role ?? state?.Role;
        SelectedRole = owner.RoleOptions.FirstOrDefault(o => o.Value == suggested) ?? owner.RoleOptions[0];
        Folder = d.Folder ?? state?.DeployFolder ?? string.Empty;
    }

    public string Id { get; }

    public string Name { get; }

    public string StateText { get; }

    /// <summary>Heartbeat jünger als 90 s (grüner Punkt), sonst grau.</summary>
    public bool IsFresh { get; }

    public string DeployText { get; }

    public bool HasWarning { get; }

    public string? WarningText { get; }

    public bool CanEditRole { get; }

    public bool CanSelect { get; }

    public bool NotSelectable => !CanSelect;

    public IReadOnlyList<SettingsOption> RoleOptions => _owner.RoleOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFolder))]
    public partial SettingsOption? SelectedRole { get; set; }

    public bool ShowFolder => SelectedRole?.Value == MinecraftService.RoleMinigame;

    [ObservableProperty]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private Task SaveRoleAsync() => _owner.SaveRoleAsync(this);
}

/// <summary>Gruppe des Minigame-Pools mit Einstellung, zugeordneten Instanzen und Vorschlägen aus dem Heartbeat.</summary>
public sealed partial class PoolGroupItemViewModel : ObservableObject
{
    private readonly MinecraftOpsViewModel _owner;

    public PoolGroupItemViewModel(PoolGroup group, MinecraftOpsViewModel owner)
    {
        _owner = owner;
        Game = group.Game;
        Configured = group.Configured;
        Enabled = group.Enabled;
        MinRunning = group.MinRunning;
        StartThreshold = group.StartThreshold;
        IdleMinutes = group.IdleMinutes;
        Summary = owner.Text(StringKeys.PoolSummary, group.Running, group.Joinable, group.Players)
            + (group.Configured ? string.Empty : " · " + owner.Text(StringKeys.PoolNotConfigured));
        LastAction = group.LastAction is { } a ? owner.Text(StringKeys.PoolLastAction, owner.ActionText(a)) : null;
        foreach (var instance in group.Instances)
        {
            Instances.Add(new PoolInstanceItemViewModel(instance, this, owner));
        }

        foreach (var (id, name) in group.Suggestions)
        {
            Suggestions.Add(new PoolSuggestionItemViewModel(id, name, this));
        }
    }

    public string Game { get; }

    public bool Configured { get; }

    public string Summary { get; }

    public string? LastAction { get; }

    public bool HasLastAction => LastAction is not null;

    public ObservableCollection<PoolInstanceItemViewModel> Instances { get; } = [];

    public ObservableCollection<PoolSuggestionItemViewModel> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial decimal? MinRunning { get; set; }

    [ObservableProperty]
    public partial decimal? StartThreshold { get; set; }

    [ObservableProperty]
    public partial decimal? IdleMinutes { get; set; }

    internal IReadOnlyList<string> AssignedIds => Instances.Select(i => i.Id).ToList();

    [RelayCommand]
    private Task SaveAsync() => _owner.SavePoolAsync(this, AssignedIds);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeletePoolAsync(this);

    internal Task AssignAsync(string serverId) => _owner.SavePoolAsync(this, [.. AssignedIds, serverId]);

    internal Task UnassignAsync(string serverId) => _owner.SavePoolAsync(this, AssignedIds.Where(i => i != serverId).ToList());
}

public sealed partial class PoolInstanceItemViewModel : ObservableObject
{
    private readonly PoolGroupItemViewModel _group;
    private readonly MinecraftOpsViewModel _owner;
    private readonly bool _ready;

    public PoolInstanceItemViewModel(PoolInstance instance, PoolGroupItemViewModel group, MinecraftOpsViewModel owner)
    {
        _group = group;
        _owner = owner;
        Id = instance.Id;
        Name = instance.Name;
        var culture = owner.Culture;
        var parts = new List<string> { owner.Label("Pool_Status_", instance.Status) };
        if (instance.MinigameState is { } state)
        {
            parts.Add(state + (instance.Arena is { } arena ? " " + arena : string.Empty));
        }

        parts.Add(instance.Capacity is { } cap ? $"{instance.PlayersOnline}/{cap}" : instance.PlayersOnline.ToString(culture));
        if (instance.Load is { } load)
        {
            parts.Add(load.ToString("0", culture) + " %");
        }

        if (instance.Quarantined)
        {
            parts.Add(owner.Text(StringKeys.PoolQuarantined));
        }
        else if (instance.Backoff)
        {
            parts.Add(owner.Text(StringKeys.PoolBackoff));
        }

        if (instance.Failures > 0)
        {
            parts.Add(owner.Text(StringKeys.PoolFailures, instance.Failures));
        }

        if (!instance.Linked)
        {
            parts.Add(owner.Text(StringKeys.NetworkNotLinked));
        }

        StatusText = string.Join(" · ", parts);
        HasFailures = instance.Failures > 0 || instance.Quarantined;
        Excluded = instance.Excluded;
        _ready = true;
    }

    public string Id { get; }

    public string Name { get; }

    public string StatusText { get; }

    public bool HasFailures { get; }

    /// <summary>Wartungsschalter „vom Pool ausnehmen“ – wird sofort gespeichert.</summary>
    [ObservableProperty]
    public partial bool Excluded { get; set; }

    partial void OnExcludedChanged(bool value)
    {
        if (_ready)
        {
            _ = _owner.UpdateInstanceAsync(_group.Game, Id, value, resetFailures: false);
        }
    }

    [RelayCommand]
    private Task ResetFailuresAsync() => _owner.UpdateInstanceAsync(_group.Game, Id, null, resetFailures: true);

    [RelayCommand]
    private Task UnassignAsync() => _group.UnassignAsync(Id);
}

public sealed partial class PoolSuggestionItemViewModel(string id, string name, PoolGroupItemViewModel group)
{
    public string Name { get; } = name;

    [RelayCommand]
    private Task AssignAsync() => group.AssignAsync(id);
}

/// <summary>Ein Verteil-Lauf mit Fortschritt je Server.</summary>
public sealed class DeployItemViewModel
{
    public DeployItemViewModel(PluginDeploy deploy, MinecraftOpsViewModel owner)
    {
        Id = deploy.Id;
        var parts = new List<string> { deploy.Tag ?? "?", owner.Label("Deploy_Status_", deploy.Status) };
        if (deploy.CreatedAt is { } at)
        {
            parts.Add(at.ToLocalTime().ToString("g", owner.Culture));
        }

        if (deploy.StartedBy is { } by)
        {
            parts.Add(by);
        }

        if (deploy.Error is { } error)
        {
            parts.Add(owner.Label("Deploy_Message_", error));
        }

        Title = string.Join(" · ", parts);
        Targets = deploy.Targets.Select(t =>
        {
            var line = $"{t.ServerName}{(t.Folder is { } f ? " (" + f + ")" : string.Empty)}: {owner.Label("Deploy_Status_", t.Status)}";
            if (t.Message is { } message)
            {
                line += " – " + Message(owner, message);
            }

            if (t.Uploaded.Count > 0)
            {
                line += " · " + string.Join(", ", t.Uploaded);
            }

            return t.Restarted ? line + " · " + owner.Text(StringKeys.DeployRestarted) : line;
        }).ToList();
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<string> Targets { get; }

    /// <summary>„rolled_back – Grund“: bekannter Teil übersetzt, der Rest bleibt.</summary>
    private static string Message(MinecraftOpsViewModel owner, string message)
    {
        var i = message.IndexOf(" – ", StringComparison.Ordinal);
        return i < 0 ? owner.Label("Deploy_Message_", message) : owner.Label("Deploy_Message_", message[..i]) + message[i..];
    }
}
