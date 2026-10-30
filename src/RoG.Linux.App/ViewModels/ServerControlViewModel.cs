using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Core.Features.Account;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Realtime;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Reiter im Steuer-Fenster – Reihenfolge wie die SelectorBar der Windows-App.</summary>
public enum ServerControlTab
{
    Console,
    Configs,
    Files,
    Backups,
    Rcon,
    Bans,
    Whitelist,
    GameBans,
    Approvals,
}

/// <summary>
/// Was das Steuer-Fenster an einem Server zeigt: wirksame Rechte aus <c>control.abilities</c> (<see cref="ServerAbilities"/>)
/// und zusätzlich die Token-Scopes aus <c>/v1/me</c> (Guide 18: Scope <b>und</b> Recht müssen passen). Die Reiter folgen
/// der Windows-App (<c>ServerConsoleWindow</c>); der Server prüft ohnehin jede Aktion.
/// </summary>
public sealed record ServerControlAccess(
    bool Power,
    bool Console,
    bool Configs,
    bool Files,
    bool Backups,
    bool Rcon,
    bool Bans,
    bool Whitelist,
    bool GameBans,
    bool Approvals)
{
    public const string PowerScope = "servers:power";
    public const string ConsoleScope = "servers:console";
    public const string FilesScope = "servers:files";
    public const string BackupsScope = "servers:backups";
    public const string RconScope = "servers:rcon";

    public static ServerControlAccess None { get; } = new(false, false, false, false, false, false, false, false, false, false);

    /// <summary>Gibt es irgendetwas zu steuern? Dann zeigt die Server-Karte „Steuern“.</summary>
    public bool Any => Power || Console || Configs || Files || Backups || Rcon || Bans || Whitelist || GameBans || Approvals;

    public static ServerControlAccess For(ServerAbilities? abilities, UserProfile? me)
    {
        if (abilities is not { } a || me is null)
        {
            return None;
        }

        bool Scope(string scope) => me.HasScope(scope);
        var files = Scope(FilesScope);
        var rcon = Scope(RconScope);
        // „Bans & Plugin“ wie ServerGameBansViewModel.IsAvailable der Windows-App
        var banManager = me.Can(GameBanService.ManagePermission) && Scope(GameBanService.BanScope);
        var gameBans = (Scope(GameBanService.BanScope) && (a.Bans || banManager)) || Scope(GameBanService.KeyScope);
        return new ServerControlAccess(
            Power: a.Power && Scope(PowerScope),
            Console: a.Console && Scope(ConsoleScope),
            Configs: (a.FilesRead || a.FilesWrite) && files,
            Files: a.FilesRead && files,
            Backups: a.Backups && Scope(BackupsScope),
            Rcon: a.Rcon && rcon,
            Bans: a.Bans && rcon,
            Whitelist: a.Bans && rcon,
            GameBans: gameBans,
            Approvals: a.FilesApprove && files);
    }

    public bool Allows(ServerControlTab tab) => tab switch
    {
        ServerControlTab.Console => Console,
        ServerControlTab.Configs => Configs,
        ServerControlTab.Files => Files,
        ServerControlTab.Backups => Backups,
        ServerControlTab.Rcon => Rcon,
        ServerControlTab.Bans => Bans,
        ServerControlTab.Whitelist => Whitelist,
        ServerControlTab.GameBans => GameBans,
        _ => Approvals,
    };
}

/// <summary>Ein Reiter in der Leiste (nur sichtbare Reiter stehen in <see cref="ServerControlViewModel.Tabs"/>).</summary>
public sealed partial class ServerControlTabItem(ServerControlTab tab, string label, ServerControlViewModel owner) : ObservableObject
{
    public ServerControlTab Tab => tab;

    public string Label => label;

    /// <summary>ToggleButton: nur Einschalten wählt (Ausschalten des aktiven bleibt wirkungslos).</summary>
    public bool IsSelected
    {
        get => owner.SelectedTab == tab;
        set
        {
            if (value)
            {
                owner.SelectedTab = tab;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>
/// Server-Steuerung im eigenen Fenster (wie <c>ServerConsoleWindow</c> der Windows-App): Kopf mit Zustand, Ressourcen und
/// Power, Rückmeldung und Rückfrage im Inhalt, Reiter Konsole · Configs · Dateien · Backups · RCON · Sperren · Whitelist ·
/// Bans &amp; Plugin · Freigaben – je nach Rechten. Configs, Dateien, Backups und „Bans &amp; Plugin“ hängen eigene
/// ViewModels über <see cref="ConfigsTab"/>, <see cref="FilesTab"/>, <see cref="BackupsTab"/> und <see cref="GameBansTab"/>
/// ein (Ansicht per DataTemplate); solange keins gesetzt ist, zeigt der Reiter „kommt“.
/// </summary>
public sealed partial class ServerControlViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppServices _services;

    public ServerControlViewModel(AppServices services, GameServer server, Func<string?, IWebSocketConnection>? sockets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(server);
        _services = services;
        Server = server;
        Access = ServerControlAccess.For(server.Control, services.CurrentUser);
        Console = new ServerConsoleViewModel(services, server, Access, Feedback, Confirm, sockets);
        Rcon = new ServerRconViewModel(services, server.Slug, Feedback);
        Bans = new ServerBansViewModel(services, server.Slug, Feedback, Confirm);
        Whitelist = new ServerWhitelistViewModel(services, server.Slug, Feedback, Confirm);
        Approvals = new ServerApprovalsViewModel(services, server.Slug, Feedback, Confirm);

        foreach (var tab in Enum.GetValues<ServerControlTab>().Where(Access.Allows))
        {
            Tabs.Add(new ServerControlTabItem(tab, services.T(TabKey(tab)).ToUpper(services.Culture), this));
        }

        SelectedTab = Tabs.Count > 0 ? Tabs[0].Tab : ServerControlTab.Console;

        // Hier hängen die Teile „Configs/Dateien/Backups“ und „Bans & Plugin“ ihre ViewModels ein, z. B.
        // ConfigsTab = new ServerConfigsViewModel(services, server) – Rückmeldung/Rückfrage über Feedback und Confirm.
    }

    public GameServer Server { get; }

    public string Slug => Server.Slug;

    public string Name => Server.Name;

    /// <summary>Wirksame Rechte des Servers (für die eingehängten Reiter).</summary>
    public ServerAbilities? Abilities => Server.Control;

    public ServerControlAccess Access { get; }

    /// <summary>Rückmeldung für alle Reiter (wie die InfoBar des Windows-Fensters).</summary>
    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage im Inhalt (Kill, Sperre aufheben, Freigabe …); Escape bricht ab.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    public ServerConsoleViewModel Console { get; }

    public ServerRconViewModel Rcon { get; }

    public ServerBansViewModel Bans { get; }

    public ServerWhitelistViewModel Whitelist { get; }

    public ServerApprovalsViewModel Approvals { get; }

    /// <summary>Reiter „Configs“ (Teil F): ViewModel, dessen Ansicht ein DataTemplate liefert; <c>null</c> = kommt noch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigsPending))]
    public partial object? ConfigsTab { get; set; }

    /// <summary>Reiter „Dateien“ (Teil F); <c>null</c> = kommt noch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesPending))]
    public partial object? FilesTab { get; set; }

    /// <summary>Reiter „Backups“ (Teil F); <c>null</c> = kommt noch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupsPending))]
    public partial object? BackupsTab { get; set; }

    /// <summary>Reiter „Bans &amp; Plugin“ (Teil G); <c>null</c> = kommt noch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameBansPending))]
    public partial object? GameBansTab { get; set; }

    public bool ConfigsPending => ConfigsTab is null;

    public bool FilesPending => FilesTab is null;

    public bool BackupsPending => BackupsTab is null;

    public bool GameBansPending => GameBansTab is null;

    public ObservableCollection<ServerControlTabItem> Tabs { get; } = [];

    /// <summary>Leiste nur bei mehr als einem Reiter (wie Windows).</summary>
    public bool ShowTabs => Tabs.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConsoleTab), nameof(IsConfigsTab), nameof(IsFilesTab), nameof(IsBackupsTab), nameof(IsRconTab),
        nameof(IsBansTab), nameof(IsWhitelistTab), nameof(IsGameBansTab), nameof(IsApprovalsTab))]
    public partial ServerControlTab SelectedTab { get; set; }

    public bool IsConsoleTab => SelectedTab == ServerControlTab.Console && Access.Console;

    public bool IsConfigsTab => SelectedTab == ServerControlTab.Configs;

    public bool IsFilesTab => SelectedTab == ServerControlTab.Files;

    public bool IsBackupsTab => SelectedTab == ServerControlTab.Backups;

    public bool IsRconTab => SelectedTab == ServerControlTab.Rcon;

    public bool IsBansTab => SelectedTab == ServerControlTab.Bans;

    public bool IsWhitelistTab => SelectedTab == ServerControlTab.Whitelist;

    public bool IsGameBansTab => SelectedTab == ServerControlTab.GameBans;

    public bool IsApprovalsTab => SelectedTab == ServerControlTab.Approvals;

    /// <summary>Wird ein Reiter gewechselt, laden Listen beim ersten Öffnen (wie Windows); eingehängte Reiter ebenso,
    /// wenn ihr ViewModel <c>EnsureLoadedAsync()</c> anbietet (<see cref="ILazyTab"/>).</summary>
    partial void OnSelectedTabChanged(ServerControlTab value)
    {
        foreach (var item in Tabs)
        {
            item.Refresh();
        }

        Confirm.Cancel();
        _ = value switch
        {
            ServerControlTab.Bans => Bans.EnsureLoadedAsync(),
            ServerControlTab.Whitelist => Whitelist.EnsureLoadedAsync(),
            ServerControlTab.Approvals => Approvals.EnsureLoadedAsync(),
            ServerControlTab.Configs => (ConfigsTab as ILazyTab)?.EnsureLoadedAsync() ?? Task.CompletedTask,
            ServerControlTab.Files => (FilesTab as ILazyTab)?.EnsureLoadedAsync() ?? Task.CompletedTask,
            ServerControlTab.Backups => (BackupsTab as ILazyTab)?.EnsureLoadedAsync() ?? Task.CompletedTask,
            ServerControlTab.GameBans => (GameBansTab as ILazyTab)?.EnsureLoadedAsync() ?? Task.CompletedTask,
            _ => Task.CompletedTask,
        };
    }

    /// <summary>Fenster geöffnet: Ressourcen holen, Live-Konsole verbinden, ersten Reiter laden.</summary>
    public async Task StartAsync()
    {
        OnSelectedTabChanged(SelectedTab);
        await Console.StartAsync();
    }

    /// <summary>Escape: offene Rückfrage abbrechen bzw. Meldung schließen; <c>false</c> = nichts offen (Fenster schließt).</summary>
    public bool HandleEscape()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        Confirm.Cancel();
        return Console.DisposeAsync();
    }

    private static string TabKey(ServerControlTab tab) => tab switch
    {
        ServerControlTab.Console => "Console_TabConsole.Text",
        ServerControlTab.Configs => "Console_TabConfigs.Text",
        ServerControlTab.Files => "Console_TabFiles.Text",
        ServerControlTab.Backups => "Console_TabBackups.Text",
        ServerControlTab.Rcon => "Console_TabRcon.Text",
        ServerControlTab.Bans => "Console_TabBans.Text",
        ServerControlTab.Whitelist => "Console_TabWhitelist.Text",
        ServerControlTab.GameBans => "Console_TabGameBans.Content",
        _ => "Console_TabApprovals.Text",
    };
}

/// <summary>Reiter, der beim ersten Öffnen lädt (eingehängte Reiter können das anbieten).</summary>
public interface ILazyTab
{
    Task EnsureLoadedAsync();
}
