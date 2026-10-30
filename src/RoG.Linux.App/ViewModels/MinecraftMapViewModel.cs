using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Eintrag der Weltauswahl („Server · Welt“).</summary>
public sealed record MapWorldOption(MapServer Server, MapWorld World, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Warp oder Live-Spieler in der Seitenliste; Klick zentriert die Karte.</summary>
public sealed record MapPointItem(string Label, string Detail, double X, double Z);

/// <summary>
/// Minecraft-Weltkarte (wie <c>MinecraftMapViewModel</c> der Windows-App, Website v0.44.0, Guide 48): Welt wählen,
/// Kacheln nur von der Plattform über den Kachel-Index je Region, Warps, Spawn und Live-Spieler (alle 5 s, solange die
/// Seite offen ist und nur, wenn die Website sie diesem Betrachter zeigt). Live-Positionen werden nicht gespeichert.
/// Verwaltende (<c>minecraft:manage</c> + <c>minecraft.manage</c>) stellen Live-Spieler um, blenden Welten/Server aus,
/// benennen Welten je Sprache und löschen Kacheln. Links <c>rog://map?server=…&amp;world=…&amp;x=…&amp;z=…</c>.
/// </summary>
public sealed partial class MinecraftMapViewModel : LoadableViewModel, IRoutablePage, IRefreshablePage, IDetailPage
{
    /// <summary>Kacheln im Speicher (512 × 512 px, zuletzt benutzte zuerst behalten) – etwa 64 MB.</summary>
    public const int TileCacheCapacity = 64;

    private readonly MinecraftService _minecraft;
    private MapLink _link = new(null, null, null, null);
    private bool _liveBusy;
    private bool _applyingMode;

    public MinecraftMapViewModel(AppServices services)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        Tiles = new MapTileCache(services.Images, TileCacheCapacity);
        LiveModeOptions = MinecraftService.LiveModes.Select(m => new SettingsOption(m, services.T("MinecraftMap_Live_" + m))).ToList();
    }

    /// <summary>Kachel- und Kopfbilder (nur im Speicher; Tests setzen eigene Fake-Kacheln).</summary>
    public MapTileCache Tiles { get; set; }

    public FeedbackViewModel Feedback { get; } = new();

    public InlineConfirmViewModel Confirm { get; } = new();

    public ObservableCollection<MapWorldOption> Worlds { get; } = [];

    public ObservableCollection<MapPointItem> Players { get; } = [];

    public ObservableCollection<MapPointItem> Warps { get; } = [];

    public IReadOnlyList<SettingsOption> LiveModeOptions { get; }

    /// <summary>Welt gewechselt: Karte neu aufbauen, optional auf eine Stelle aus dem Link zentrieren.</summary>
    public event EventHandler<MapWorldChange>? WorldChanged;

    /// <summary>Live-Spieler neu (für die Karte).</summary>
    public event EventHandler<IReadOnlyList<MapLivePlayer>>? PlayersChanged;

    /// <summary>Kachel-Index nach Änderungen (Kacheln gelöscht, aktualisiert) neu laden.</summary>
    public event EventHandler? TilesReset;

    /// <summary>Karte auf einen Punkt zentrieren (Klick auf Warp/Spieler in der Liste).</summary>
    public event EventHandler<MapPointItem>? CenterRequested;

    /// <summary>Zuletzt gemeldete Live-Spieler (alle Welten des Servers).</summary>
    public IReadOnlyList<MapLivePlayer> LivePlayers { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorld), nameof(WorldInfo), nameof(HasNoTiles), nameof(CanEditWorld))]
    public partial MapWorldOption? SelectedWorld { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveAvailable))]
    public partial MapOverview? Overview { get; set; }

    /// <summary>Live-Spieler anzeigen (Schalter der Seite; nur wirksam, wenn die Website sie zeigt).</summary>
    [ObservableProperty]
    public partial bool ShowLive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveInfo))]
    public partial string? LiveInfo { get; set; }

    public bool HasLiveInfo => !string.IsNullOrEmpty(LiveInfo);

    public bool HasWorld => SelectedWorld is not null;

    public bool LiveAvailable => Overview?.LivePlayers == true;

    public bool HasPlayers => Players.Count > 0;

    public bool HasWarps => Warps.Count > 0;

    protected override bool HasData => Worlds.Count > 0;

    /// <summary>Kachelzahl und Stand der gewählten Welt.</summary>
    public string WorldInfo => SelectedWorld?.World is not { } w
        ? string.Empty
        : Services.Format(
            w.TilesUpdatedAt is null ? StringKeys.MinecraftMapWorldInfoNoDate : StringKeys.MinecraftMapWorldInfo,
            w.TileCount,
            Services.T("MinecraftMap_Env_" + (w.Environment is "nether" or "the_end" ? w.Environment : "normal")),
            w.TilesUpdatedAt?.ToLocalTime().ToString("g", Services.Culture) ?? string.Empty);

    public bool HasNoTiles => SelectedWorld?.World.TileCount == 0;

    // --- Verwaltung -------------------------------------------------------------------------------------------------

    public bool CanManage => Services.CurrentUser is { } me && me.Can(MinecraftService.ManagePermission) && me.HasScope(MinecraftService.ManageScope);

    public bool CanEditWorld => CanManage && SelectedWorld is not null;

    [ObservableProperty]
    public partial SettingsOption? SelectedLiveMode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWorldCommand), nameof(ClearTilesCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool EditWorldHidden { get; set; }

    [ObservableProperty]
    public partial bool EditServerHidden { get; set; }

    [ObservableProperty]
    public partial string EditNameDe { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameFr { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuota))]
    public partial string? QuotaText { get; set; }

    [ObservableProperty]
    public partial string? WorldAdminInfo { get; set; }

    public bool HasQuota => !string.IsNullOrEmpty(QuotaText);

    /// <summary>Kachel-Index einer Region für die Karte (Fehler fängt die Karte ab und zeigt die Region leer).</summary>
    public Task<IReadOnlyList<MapTileRef>> LoadTilesAsync(string world, int zoom, int regionX, int regionZ) =>
        _minecraft.MapTilesAsync(world, zoom, regionX, regionZ);

    /// <summary>Link aus dem Spiel/von der Website (<c>server</c>, <c>world</c>, <c>x</c>, <c>z</c>) für den nächsten Ladevorgang.</summary>
    public void UseLink(string? key) => _link = MapLink.Parse(key);

    /// <summary>Deep Link <c>rog://map?…</c>: Server/Welt wählen und zentrieren.</summary>
    public void Open(string key)
    {
        UseLink(key);
        if (!IsLoading && HasData)
        {
            _ = LoadAsync(silent: true);
        }
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(silent: HasData);
        TilesReset?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private Task ReloadMapAsync() => RefreshAsync();

    bool IDetailPage.IsDetailOpen => Confirm.IsOpen;

    /// <summary>Escape: offene Rückfrage abbrechen.</summary>
    void IDetailPage.CloseDetail() => Confirm.Cancel();

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        MapOverview map;
        try
        {
            map = await _minecraft.MapAsync(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new RoG.Core.Api.ApiProblemException();
        }

        var previous = SelectedWorld?.World.Id;
        Overview = map;
        var worlds = new List<MapWorldOption>();
        foreach (var server in map.Servers)
        {
            foreach (var world in server.Worlds)
            {
                var label = server.Worlds.Count > 1 || map.Servers.Count > 1 ? $"{server.Name} · {world.Name}" : world.Name;
                if (world.Hidden || server.Hidden)
                {
                    label += " · " + Services.T(StringKeys.MinecraftMapHidden);
                }

                worlds.Add(new MapWorldOption(server, world, label));
            }
        }

        var linked = Linked(worlds);
        var selected = linked ?? worlds.FirstOrDefault(w => w.World.Id == previous) ?? worlds.FirstOrDefault();
        _suppressWorldChange = true;
        try
        {
            Worlds.Clear();
            foreach (var w in worlds)
            {
                Worlds.Add(w);
            }

            SelectedWorld = selected;
        }
        finally
        {
            _suppressWorldChange = false;
        }

        ApplyAdmin(map);
        FillWorld(selected);
        WorldChanged?.Invoke(this, new MapWorldChange(selected?.World, linked is null ? null : _link.X, linked is null ? null : _link.Z));
        _link = new MapLink(null, null, null, null);
        UpdateLiveInfo();
        await RefreshLiveAsync();
    }

    private bool _suppressWorldChange;

    /// <summary>Live-Spieler des gewählten Servers (Ansicht ruft alle 5 s); ohne Freigabe oder Schalter aus: leer.</summary>
    public async Task RefreshLiveAsync()
    {
        if (_liveBusy)
        {
            return;
        }

        if (SelectedWorld is not { } option || !LiveAvailable || !ShowLive)
        {
            SetPlayers([]);
            return;
        }

        _liveBusy = true;
        try
        {
            var server = option.Server.NetworkId is { } n && MinecraftMapGeometry.IsValidName(n) ? n : option.Server.Id;
            var live = await _minecraft.MapLiveAsync(server);
            if (SelectedWorld == option)
            {
                SetPlayers(live.Players);
            }
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            SetPlayers([]); // Live-Stand ist Beiwerk – Karte bleibt nutzbar
        }
        finally
        {
            _liveBusy = false;
        }
    }

    partial void OnSelectedWorldChanged(MapWorldOption? value)
    {
        if (_suppressWorldChange)
        {
            return;
        }

        FillWorld(value);
        WorldChanged?.Invoke(this, new MapWorldChange(value?.World, null, null));
        SetPlayers([]);
        _ = RefreshLiveAsync();
    }

    partial void OnShowLiveChanged(bool value) => _ = RefreshLiveAsync();

    async partial void OnSelectedLiveModeChanged(SettingsOption? value)
    {
        if (_applyingMode || value is null || !CanManage || value.Value == Overview?.LiveMode)
        {
            return;
        }

        try
        {
            var mode = await _minecraft.SetMapLiveModeAsync(value.Value);
            Feedback.Show(Services.Format(StringKeys.MinecraftMapLiveSaved, Services.T("MinecraftMap_Live_" + mode)), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            Feedback.Show(MinecraftAdminTabBase.ErrorText(Services, ex), MessageTone.Error);
            ApplyAdmin(Overview);
        }
    }

    private bool CanChangeWorld() => CanManage && SelectedWorld is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanChangeWorld))]
    private async Task SaveWorldAsync()
    {
        if (SelectedWorld is not { } option)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var names = new Dictionary<string, string?> { ["de"] = EditNameDe, ["en"] = EditNameEn, ["fr"] = EditNameFr };
            await _minecraft.UpdateMapWorldAsync(option.World.Id, EditWorldHidden, option.World.Names is null ? null : names);
            if (EditServerHidden != option.Server.Hidden)
            {
                await _minecraft.SetMapServerHiddenAsync(option.Server.Id, EditServerHidden);
            }

            Feedback.Show(Services.T(StringKeys.MinecraftMapSaved), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            Feedback.Show(MinecraftAdminTabBase.ErrorText(Services, ex), MessageTone.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeWorld))]
    private async Task ClearTilesAsync()
    {
        if (SelectedWorld is not { } option)
        {
            return;
        }

        if (!await Confirm.AskAsync(Services.T(StringKeys.MinecraftMapClearTitle), Services.Format(StringKeys.MinecraftMapClearText, option.Label),
                Services.T(StringKeys.CommonConfirm), Services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        IsSaving = true;
        try
        {
            await _minecraft.ClearMapTilesAsync(option.World.Id);
            Feedback.Show(Services.T(StringKeys.MinecraftMapCleared), MessageTone.Success);
            await LoadAsync(silent: true);
            TilesReset?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            Feedback.Show(MinecraftAdminTabBase.ErrorText(Services, ex), MessageTone.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Warp oder Spieler aus der Liste: Karte dorthin.</summary>
    [RelayCommand]
    private void CenterOn(MapPointItem? point)
    {
        if (point is not null)
        {
            CenterRequested?.Invoke(this, point);
        }
    }

    private MapWorldOption? Linked(IReadOnlyList<MapWorldOption> worlds)
    {
        if (_link.Server is not { } server)
        {
            return null;
        }

        var candidates = worlds.Where(w => w.Server.Id == server || w.Server.NetworkId == server).ToList();
        return candidates.FirstOrDefault(w => _link.World is { } world && w.World.World == world) ?? candidates.FirstOrDefault();
    }

    private void FillWorld(MapWorldOption? value)
    {
        Warps.Clear();
        foreach (var marker in value?.World.Markers.OrderBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase) ?? Enumerable.Empty<MapMarker>())
        {
            Warps.Add(new MapPointItem(marker.Label, Coordinates(marker.X, marker.Z), marker.X + 0.5, marker.Z + 0.5));
        }

        OnPropertyChanged(nameof(HasWarps));
        OnPropertyChanged(nameof(HasNoTiles));
        OnPropertyChanged(nameof(WorldInfo));
        FillWorldAdmin(value);
    }

    private void SetPlayers(IReadOnlyList<MapLivePlayer> players)
    {
        var world = SelectedWorld?.World;
        LivePlayers = players;
        Players.Clear();
        foreach (var p in players.Where(p => world is not null && (p.WorldId == world.Id || (p.WorldId is null && p.World == world.World))))
        {
            Players.Add(new MapPointItem(p.Bedrock ? p.Name + " · Bedrock" : p.Name, Coordinates(p.X, p.Z), p.X, p.Z));
        }

        OnPropertyChanged(nameof(HasPlayers));
        PlayersChanged?.Invoke(this, players);
        UpdateLiveInfo();
    }

    private void UpdateLiveInfo()
    {
        LiveInfo = Overview switch
        {
            null => null,
            { LivePlayers: false, LiveMode: MinecraftService.LiveAuth } => Services.T(StringKeys.MinecraftMapLiveSignIn),
            { LivePlayers: false } => Services.T(StringKeys.MinecraftMapLiveOff),
            _ when !ShowLive => null,
            _ when Players.Count == 0 => Services.T(StringKeys.MinecraftMapNoPlayers),
            _ => null,
        };
    }

    private void ApplyAdmin(MapOverview? map)
    {
        _applyingMode = true;
        SelectedLiveMode = LiveModeOptions.FirstOrDefault(o => o.Value == map?.LiveMode);
        _applyingMode = false;
        QuotaText = CanManage && map?.Quota is { Total: > 0 } q
            ? Services.Format(StringKeys.MinecraftMapQuota, MinecraftMapGeometry.FormatBytes(q.Used), MinecraftMapGeometry.FormatBytes(q.Total), MinecraftMapGeometry.FormatBytes(q.PerServer))
            : null;
        OnPropertyChanged(nameof(CanManage));
    }

    private void FillWorldAdmin(MapWorldOption? option)
    {
        EditWorldHidden = option?.World.Hidden ?? false;
        EditServerHidden = option?.Server.Hidden ?? false;
        EditNameDe = option?.World.Names?.GetValueOrDefault("de") ?? string.Empty;
        EditNameEn = option?.World.Names?.GetValueOrDefault("en") ?? string.Empty;
        EditNameFr = option?.World.Names?.GetValueOrDefault("fr") ?? string.Empty;
        WorldAdminInfo = option is null || !CanManage
            ? null
            : Services.Format(
                option.Server.IsPublic == false ? StringKeys.MinecraftMapWorldAdminNotPublic : StringKeys.MinecraftMapWorldAdmin,
                option.World.World,
                MinecraftMapGeometry.FormatBytes(option.World.Bytes ?? 0));
        SaveWorldCommand.NotifyCanExecuteChanged();
        ClearTilesCommand.NotifyCanExecuteChanged();
    }

    private string Coordinates(double x, double z) =>
        string.Format(Services.Culture, "x {0:0} · z {1:0}", Math.Floor(x), Math.Floor(z));
}

/// <summary>Neue Welt für die Karte, optional mit Stelle aus einem Link.</summary>
public sealed record MapWorldChange(MapWorld? World, int? X, int? Z);
