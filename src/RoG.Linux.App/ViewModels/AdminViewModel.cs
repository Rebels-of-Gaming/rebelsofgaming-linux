using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Core.Features.Account;
using RoG.Core.Features.Admin;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Minecraft;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Reiter der Verwaltung – Reihenfolge wie die SelectorBar der Windows-App (<c>AdminPage</c>).</summary>
public enum AdminTab
{
    Overview,
    Members,
    Applications,
    Reports,
    Bans,
    Appeals,
    Minecraft,
    Coins,
    Network,
    Log,
    Words,
    Gallery,
    Uploads,
    Website,
}

/// <summary>
/// Welche Reiter der Verwaltung sichtbar sind – Regeln wie <c>AdminPage</c> der Windows-App, nur aus <c>permissions</c>
/// von <c>/v1/me</c> plus Token-Scopes (nie Rollennamen). Der Server prüft jede Aktion ohnehin.
/// </summary>
public sealed record AdminAccess(
    bool Overview,
    bool Members,
    bool Applications,
    bool Reports,
    bool Bans,
    bool Appeals,
    bool Minecraft,
    bool Coins,
    bool Network,
    bool Log,
    bool Words,
    bool Gallery,
    bool Uploads,
    bool Website)
{
    public static AdminAccess None { get; } = new(false, false, false, false, false, false, false, false, false, false, false, false, false, false);

    /// <summary>Gibt es irgendeinen Reiter? Sonst Hinweis „keine Rechte“ (wie <c>AdminPage_NoAccess</c>).</summary>
    public bool Any => Enum.GetValues<AdminTab>().Any(Allows);

    /// <param name="hasAdminSite">Umgebung kennt die Admin-Domain (sonst keine Browser-Links im Reiter Website).</param>
    public static AdminAccess For(UserProfile? me, bool hasAdminSite)
    {
        if (me is null)
        {
            return None;
        }

        bool Allowed(string permission, string scope) => me.CanAnywhere(permission) && me.HasScope(scope);

        // Übersicht: AdminOverviewViewModel.Any (Kennzahlen, Systemstatus, Audit-Log)
        var overview = AdminOverviewViewModel.CanKpisFor(me) || AdminOverviewViewModel.CanSystemFor(me) || AdminOverviewViewModel.CanAuditFor(me);
        var reports = Allowed("moderation.reports", "moderation:read");
        var banScope = me.HasScope(GameBanService.BanScope);
        return new AdminAccess(
            Overview: overview,
            Members: Allowed("members.view", "members:read"),
            Applications: (me.CanAnywhere("applications.view") || me.CanAnywhere("applications.review")) && me.HasScope("applications:read"),
            Reports: reports,
            Bans: reports,
            Appeals: banScope && (me.CanAnywhere(GameBanService.ManagePermission) || me.CanAnywhere("servers.bans")),
            Minecraft: me.HasScope(MinecraftService.ManageScope) && me.Can(MinecraftService.ManagePermission),
            Coins: me.HasScope(MinecraftService.EconomyScope) && me.Can(MinecraftService.EconomyPermission),
            Network: (me.HasScope(MinecraftService.PoolScope) && me.Can(MinecraftService.PoolPermission))
                || (me.HasScope(MinecraftService.DeployScope) && me.Can(MinecraftService.DeployPermission)),
            Log: Allowed("moderation.log", "moderation:read"),
            Words: Allowed("moderation.manage", "moderation:manage"),
            Gallery: MediaAdminService.CanModerateGallery(me),
            Uploads: MediaAdminService.CanManageUploads(me),
            Website: AdminWebsiteViewModel.IsVisibleTo(me, overview, hasAdminSite));
    }

    public bool Allows(AdminTab tab) => tab switch
    {
        AdminTab.Overview => Overview,
        AdminTab.Members => Members,
        AdminTab.Applications => Applications,
        AdminTab.Reports => Reports,
        AdminTab.Bans => Bans,
        AdminTab.Appeals => Appeals,
        AdminTab.Minecraft => Minecraft,
        AdminTab.Coins => Coins,
        AdminTab.Network => Network,
        AdminTab.Log => Log,
        AdminTab.Words => Words,
        AdminTab.Gallery => Gallery,
        AdminTab.Uploads => Uploads,
        _ => Website,
    };

    /// <summary>Eintrag „Verwaltung“ in der Seitenleiste – wie die Windows-App: irgendein Verwaltungsrecht aus <c>/v1/me</c>.</summary>
    public static bool ShowsNavEntry(UserProfile? me) => me is not null && me.Permissions.Values.Any(p => p.Any(IsAdminPermission));

    private static bool IsAdminPermission(string permission) =>
        permission.EndsWith(".manage", StringComparison.Ordinal) || permission.StartsWith("roles.", StringComparison.Ordinal)
        || permission.StartsWith("moderation.", StringComparison.Ordinal) || permission.StartsWith("admin.", StringComparison.Ordinal)
        || permission == "servers.bans" // Einsprüche gegen Gameserver-Bans
        || permission is "minecraft.economy" or "minecraft.pool" or "minecraft.deploy"; // Rebel-Coins, Minecraft-Netzwerk
}

/// <summary>Ein Reiter in der Leiste (nur erlaubte Reiter stehen in <see cref="AdminViewModel.Tabs"/>).</summary>
public sealed partial class AdminTabItem(AdminTab tab, string label, AdminViewModel owner) : ObservableObject
{
    public AdminTab Tab => tab;

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

/// <summary>Reiter mit Rückfrage/Formular im Inhalt: Escape schließt zuerst das (<c>true</c> = etwas geschlossen).</summary>
public interface IOverlayTab
{
    bool CloseOverlay();
}

/// <summary>
/// Seite „Verwaltung“ (wie <c>AdminPage</c> der Windows-App): Reiterleiste Übersicht · Mitglieder · Bewerbungen ·
/// Meldungen · Sperren · Einsprüche · Minecraft · Rebel-Coins · Minecraft-Netzwerk · Mod-Log · Wortfilter · Galerie ·
/// Uploads · Website – je nach Recht + Scope (<see cref="AdminAccess"/>). Jeder Reiter ist eine eigene Eigenschaft
/// <c>&lt;Name&gt;Tab</c> (ViewModel, Ansicht per DataTemplate aus <c>App.axaml</c>); <c>null</c> zeigt „kommt“. Reiter
/// laden beim ersten Öffnen (<see cref="ILazyTab"/>). Rückmeldung und Rückfrage teilen sich alle Reiter.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject, IDetailPage, IRefreshablePage
{
    private readonly AppServices _services;

    public AdminViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Build();

        // Profil kommt nach dem Start (bzw. nach Anmeldung) – Reiter dann neu bestimmen
        services.CurrentUserChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Build);
    }

    /// <summary>Rückmeldung für alle Reiter (wie die InfoBar der Windows-Seite).</summary>
    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage im Inhalt für alle Reiter; Escape bricht ab.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccess), nameof(NoAccess))]
    public partial AdminAccess Access { get; private set; } = AdminAccess.None;

    public bool HasAccess => Access.Any;

    public bool NoAccess => !Access.Any;

    public ObservableCollection<AdminTabItem> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedContent), nameof(SelectedPending))]
    public partial AdminTab SelectedTab { get; set; }

    // ---- Reiter-Hooks: je Reiter ein ViewModel (Ansicht per DataTemplate), null = „kommt“ ----

    /// <summary>Übersicht: Kennzahlen, Systemstatus, Audit-Log (<see cref="AdminOverviewViewModel"/>, Teil J).</summary>
    [ObservableProperty]
    public partial object? OverviewTab { get; set; }

    /// <summary>Mitglieder (Teil K).</summary>
    [ObservableProperty]
    public partial object? MembersTab { get; set; }

    /// <summary>Bewerbungen (Teil K).</summary>
    [ObservableProperty]
    public partial object? ApplicationsTab { get; set; }

    /// <summary>Meldungen (Teil L).</summary>
    [ObservableProperty]
    public partial object? ReportsTab { get; set; }

    /// <summary>Sperren (Teil L).</summary>
    [ObservableProperty]
    public partial object? BansTab { get; set; }

    /// <summary>Einsprüche gegen Gameserver-Bans (Teil L).</summary>
    [ObservableProperty]
    public partial object? AppealsTab { get; set; }

    /// <summary>Minecraft-Konten und -Gruppen (Teil M).</summary>
    [ObservableProperty]
    public partial object? MinecraftTab { get; set; }

    /// <summary>Rebel-Coins (Teil M).</summary>
    [ObservableProperty]
    public partial object? CoinsTab { get; set; }

    /// <summary>Minecraft-Netzwerk (Teil M).</summary>
    [ObservableProperty]
    public partial object? NetworkTab { get; set; }

    /// <summary>Mod-Log (<see cref="ModerationLogViewModel"/>, Teil J).</summary>
    [ObservableProperty]
    public partial object? LogTab { get; set; }

    /// <summary>Wortfilter (Teil L).</summary>
    [ObservableProperty]
    public partial object? WordsTab { get; set; }

    /// <summary>Galerie-Freigaben (Teil N).</summary>
    [ObservableProperty]
    public partial object? GalleryTab { get; set; }

    /// <summary>Upload-Meldungen (Teil N).</summary>
    [ObservableProperty]
    public partial object? UploadsTab { get; set; }

    /// <summary>Website-Verwaltung: Bereiche im eigenen Editor-Fenster + Browser-Links (<see cref="AdminWebsiteViewModel"/>, Teil J).</summary>
    [ObservableProperty]
    public partial object? WebsiteTab { get; set; }

    /// <summary>Inhalt des offenen Reiters.</summary>
    public object? SelectedContent => ContentFor(SelectedTab);

    /// <summary>Offener Reiter ist noch nicht gebaut („kommt“).</summary>
    public bool SelectedPending => HasAccess && SelectedContent is null;

    public object? ContentFor(AdminTab tab) => tab switch
    {
        AdminTab.Overview => OverviewTab,
        AdminTab.Members => MembersTab,
        AdminTab.Applications => ApplicationsTab,
        AdminTab.Reports => ReportsTab,
        AdminTab.Bans => BansTab,
        AdminTab.Appeals => AppealsTab,
        AdminTab.Minecraft => MinecraftTab,
        AdminTab.Coins => CoinsTab,
        AdminTab.Network => NetworkTab,
        AdminTab.Log => LogTab,
        AdminTab.Words => WordsTab,
        AdminTab.Gallery => GalleryTab,
        AdminTab.Uploads => UploadsTab,
        _ => WebsiteTab,
    };

    /// <summary>Reiter-ViewModels anlegen – nur für erlaubte Reiter. Weitere Teile hängen sich hier ein.</summary>
    private void CreateTabs(AdminAccess a)
    {
        OverviewTab = a.Overview ? new AdminOverviewViewModel(_services, Feedback) : null;
        LogTab = a.Log ? new ModerationLogViewModel(_services, Feedback) : null;
        WebsiteTab = a.Website ? new AdminWebsiteViewModel(_services, Feedback) : null;

        // Teile K/L/M/N, z. B.: MembersTab = a.Members ? new MembersAdminViewModel(_services, Feedback, Confirm) : null;
    }

    private void Build()
    {
        Confirm.Cancel();
        var access = AdminAccess.For(_services.CurrentUser, _services.Environment.AdminBaseUrl is not null);
        if (access == Access && Tabs.Count > 0)
        {
            return; // gleiche Rechte (z. B. Profil nur aktualisiert): Reiter und Zustand behalten
        }

        Access = access;
        Tabs.Clear();
        foreach (var tab in Enum.GetValues<AdminTab>().Where(access.Allows))
        {
            Tabs.Add(new AdminTabItem(tab, _services.T(TabKey(tab)).ToUpper(_services.Culture), this));
        }

        CreateTabs(access);
        var first = Tabs.Count > 0 ? Tabs[0].Tab : AdminTab.Overview;
        if (SelectedTab == first)
        {
            OnSelectedTabChanged(first);
            OnPropertyChanged(nameof(SelectedContent));
            OnPropertyChanged(nameof(SelectedPending));
        }
        else
        {
            SelectedTab = first;
        }
    }

    partial void OnSelectedTabChanged(AdminTab value)
    {
        foreach (var item in Tabs)
        {
            item.Refresh();
        }

        Confirm.Cancel();
        _ = (ContentFor(value) as ILazyTab)?.EnsureLoadedAsync();
    }

    partial void OnOverviewTabChanged(object? value) => TabChanged(AdminTab.Overview);

    partial void OnMembersTabChanged(object? value) => TabChanged(AdminTab.Members);

    partial void OnApplicationsTabChanged(object? value) => TabChanged(AdminTab.Applications);

    partial void OnReportsTabChanged(object? value) => TabChanged(AdminTab.Reports);

    partial void OnBansTabChanged(object? value) => TabChanged(AdminTab.Bans);

    partial void OnAppealsTabChanged(object? value) => TabChanged(AdminTab.Appeals);

    partial void OnMinecraftTabChanged(object? value) => TabChanged(AdminTab.Minecraft);

    partial void OnCoinsTabChanged(object? value) => TabChanged(AdminTab.Coins);

    partial void OnNetworkTabChanged(object? value) => TabChanged(AdminTab.Network);

    partial void OnLogTabChanged(object? value) => TabChanged(AdminTab.Log);

    partial void OnWordsTabChanged(object? value) => TabChanged(AdminTab.Words);

    partial void OnGalleryTabChanged(object? value) => TabChanged(AdminTab.Gallery);

    partial void OnUploadsTabChanged(object? value) => TabChanged(AdminTab.Uploads);

    partial void OnWebsiteTabChanged(object? value) => TabChanged(AdminTab.Website);

    /// <summary>Ein Reiter wurde (später) eingehängt: ist er offen, Ansicht tauschen und laden.</summary>
    private void TabChanged(AdminTab tab)
    {
        if (tab != SelectedTab)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedContent));
        OnPropertyChanged(nameof(SelectedPending));
        _ = (ContentFor(tab) as ILazyTab)?.EnsureLoadedAsync();
    }

    /// <summary>Escape (Hauptfenster): Rückfrage bzw. Formular des offenen Reiters schließen.</summary>
    public bool IsDetailOpen => Confirm.IsOpen || SelectedContent is IOverlayTab;

    public void CloseDetail() => CloseOverlay();

    /// <summary>Escape: offene Rückfrage, sonst Formular/Rückfrage des Reiters. <c>true</c> = etwas geschlossen.</summary>
    public bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return SelectedContent is IOverlayTab overlay && overlay.CloseOverlay();
    }

    /// <summary>F5: offenen Reiter neu laden.</summary>
    public Task RefreshAsync() => SelectedContent switch
    {
        IRefreshablePage page => page.RefreshAsync(),
        LoadableViewModel loadable => loadable.LoadAsync(),
        _ => Task.CompletedTask,
    };

    /// <summary>Knopf „Aktualisieren“ im Kopf.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task RefreshTab() => RefreshAsync();

    private static string TabKey(AdminTab tab) => tab switch
    {
        AdminTab.Overview => "AdminPage_TabOverview.Text",
        AdminTab.Members => "AdminPage_TabMembers.Text",
        AdminTab.Applications => "AdminPage_TabApplications.Text",
        AdminTab.Reports => "AdminPage_TabReports.Text",
        AdminTab.Bans => "AdminPage_TabBans.Text",
        AdminTab.Appeals => "AdminPage_TabAppeals.Text",
        AdminTab.Minecraft => "AdminPage_TabMinecraft.Text",
        AdminTab.Coins => "AdminPage_TabCoins.Text",
        AdminTab.Network => "AdminPage_TabNetwork.Text",
        AdminTab.Log => "AdminPage_TabLog.Text",
        AdminTab.Words => "AdminPage_TabWords.Text",
        AdminTab.Gallery => "AdminPage_TabGallery.Text",
        AdminTab.Uploads => "AdminPage_TabUploads.Text",
        _ => "AdminPage_TabWebsite.Text",
    };
}
