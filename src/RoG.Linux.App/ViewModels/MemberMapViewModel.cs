using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Map;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Mitgliederkarte (Guide 36, wie <c>MemberMapWindow</c> der Windows-App): Orte der Mitglieder mit Opt-in, nur
/// Stadt-Genauigkeit (liefert die API so). Kacheln nur über die signierte Vorlage der eigenen Plattform – eine Vorlage,
/// die nicht auf die API/Website der gewählten Umgebung zeigt, wird verworfen (Karte ohne Kacheln statt fremder Server).
/// </summary>
public sealed partial class MemberMapViewModel : LoadableViewModel
{
    private readonly MemberMapService _service;

    public MemberMapViewModel(AppServices services)
        : base(services)
    {
        _service = new MemberMapService(services.Api.Client);
        Tiles = new MapTileCache(services.Images);
    }

    /// <summary>Kachel-Speicher (nur im Speicher); Tests setzen einen mit Fake-Kacheln.</summary>
    public MapTileCache Tiles { get; set; }

    /// <summary>Geladene Karte (Kacheln nur, wenn vertrauenswürdig); <c>null</c> = noch nicht geladen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Attribution), nameof(HasAttribution))]
    public partial MemberMap? Map { get; set; }

    /// <summary>Hinweis über der Karte (niemand freigegeben, Kacheln nicht verfügbar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    public partial string? Info { get; set; }

    public bool HasInfo => !string.IsNullOrEmpty(Info);

    /// <summary>Namensnennung der Kacheln (Pflicht laut API, z. B. „© OpenStreetMap“).</summary>
    public string? Attribution => Map?.Tiles?.Attribution;

    public bool HasAttribution => !string.IsNullOrEmpty(Attribution);

    /// <summary>Orte des angeklickten Punkts bzw. Clusters.</summary>
    public ObservableCollection<MapPlaceViewModel> Places { get; } = [];

    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; }

    [ObservableProperty]
    public partial string? PanelTitle { get; set; }

    /// <summary>Profil geöffnet (das Fenster holt das Hauptfenster nach vorn).</summary>
    public event EventHandler<string>? ProfileOpened;

    protected override bool HasData => Map is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var map = await _service.LoadAsync(null, cancellationToken);
        map = map with { Tiles = Trusted(map.Tiles) };
        Info = map.Pins.Count == 0
            ? Services.T(StringKeys.MapEmpty)
            : map.Tiles is null ? Services.T(StringKeys.MapNoTiles) : null;
        if (Map?.Tiles?.UrlTemplate != map.Tiles?.UrlTemplate)
        {
            Tiles.Clear();
        }

        Map = map;
    }

    /// <summary>Vorlage nur, wenn sie auf die API bzw. Website der gewählten Umgebung zeigt (nie fremde Kachel-Server).</summary>
    internal MapTiles? Trusted(MapTiles? tiles)
    {
        if (tiles is null)
        {
            return null;
        }

        var sample = WebMercator.TileUrl(tiles.UrlTemplate, 0, 0, 0);
        var env = Services.Environment;
        var own = (sample.Host.Equals(env.ApiBaseUrl.Host, StringComparison.OrdinalIgnoreCase) && sample.Scheme == env.ApiBaseUrl.Scheme)
            || (sample.Host.Equals(env.WebBaseUrl.Host, StringComparison.OrdinalIgnoreCase) && sample.Scheme == env.WebBaseUrl.Scheme);
        return own ? tiles : null;
    }

    /// <summary>Die signierte Vorlage gilt 2 Stunden – kurz vor Ablauf still neu holen (Timer des Fensters).</summary>
    public async Task<bool> RefreshIfExpiringAsync(DateTimeOffset now)
    {
        if (Map?.Tiles?.ExpiresAt is { } expires && expires - now < TimeSpan.FromMinutes(10))
        {
            await LoadAsync(silent: true);
            return true;
        }

        return false;
    }

    /// <summary>Punkt/Cluster angeklickt: Orte und Namen daneben zeigen.</summary>
    public void ShowCluster(MapCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        Places.Clear();
        foreach (var pin in cluster.Pins.OrderBy(p => p.City, StringComparer.CurrentCultureIgnoreCase))
        {
            Places.Add(new MapPlaceViewModel(pin, this, Services));
        }

        PanelTitle = cluster.Count == 1 ? Services.T("Linux_MapMemberOne") : Services.Format("Linux_MapMembers", cluster.Count);
        IsPanelOpen = true;
    }

    [RelayCommand]
    public void ClosePanel() => IsPanelOpen = false;

    [RelayCommand]
    private Task OpenAttributionAsync() =>
        Map?.Tiles?.AttributionUrl is { Scheme: "https" or "http" } url ? Services.Browser.OpenAsync(url) : Task.CompletedTask;

    internal void OpenProfile(string slug)
    {
        Services.Navigate(new AppRoute(AppSection.Members, slug));
        ProfileOpened?.Invoke(this, slug);
    }
}

/// <summary>Ort im Seitenbereich: „Trier (DE)“ und die Mitglieder dort.</summary>
public sealed class MapPlaceViewModel
{
    public MapPlaceViewModel(MapPin pin, MemberMapViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(pin);
        Title = (pin.Country is null ? pin.City : $"{pin.City} ({pin.Country})").ToUpper(services.Culture);
        Members = pin.Members
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new MapMemberItemViewModel(m, owner, services))
            .ToList();
    }

    public string Title { get; }

    public IReadOnlyList<MapMemberItemViewModel> Members { get; }
}

/// <summary>Mitglied auf der Karte: Avatar, Name, Squads; Klick öffnet das Profil in der App.</summary>
public sealed partial class MapMemberItemViewModel : ObservableObject
{
    private readonly MapMember _member;
    private readonly MemberMapViewModel _owner;

    public MapMemberItemViewModel(MapMember member, MemberMapViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(services);
        _member = member;
        _owner = owner;
        Squads = string.Join(" · ", member.Squads);
        AutomationName = services.Format("Linux_MapOpenProfile", member.Name);
        _ = LoadAvatarAsync(services);
    }

    public string Name => _member.Name;

    public string Slug => _member.Slug;

    public string Squads { get; }

    public bool HasSquads => Squads.Length > 0;

    public string AutomationName { get; }

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    [RelayCommand]
    private void Open() => _owner.OpenProfile(_member.Slug);

    private async Task LoadAvatarAsync(AppServices services) =>
        Avatar = Uri.TryCreate(_member.AvatarUrl, UriKind.Absolute, out var url) ? await services.Images.LoadAsync(url, 64) : null;
}

/// <summary>Zusammengefasste Orte, die bei einer Zoomstufe dicht beieinander liegen; Position in Weltpixeln.</summary>
public sealed record MapCluster(double X, double Y, IReadOnlyList<MapPin> Pins)
{
    public int Count => Pins.Sum(p => p.Members.Count);

    /// <summary>„Trier“ bzw. „Trier, Luxemburg +2“ (Screenreader, Tooltip).</summary>
    public string Label => Pins.Count switch
    {
        1 => Pins[0].City,
        2 => $"{Pins[0].City}, {Pins[1].City}",
        _ => $"{Pins[0].City}, {Pins[1].City} +{Pins.Count - 2}",
    };
}

/// <summary>Einfaches Clustern: größte Orte zuerst, alles im Umkreis (Bildschirmpixel) kommt dazu.</summary>
public static class MapClustering
{
    public static IReadOnlyList<MapCluster> Build(IEnumerable<MapPin> pins, int zoom, int tileSize, double radius)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var clusters = new List<(double X, double Y, double Weight, List<MapPin> Pins)>();
        foreach (var pin in pins.OrderByDescending(p => p.Members.Count).ThenBy(p => p.City, StringComparer.Ordinal))
        {
            var (x, y) = WebMercator.ToPixel(pin.Lat, pin.Lng, zoom, tileSize);
            var weight = Math.Max(1, pin.Members.Count);
            var index = clusters.FindIndex(c => Math.Sqrt(((c.X - x) * (c.X - x)) + ((c.Y - y) * (c.Y - y))) <= radius);
            if (index < 0)
            {
                clusters.Add((x, y, weight, [pin]));
                continue;
            }

            var c = clusters[index];
            var total = c.Weight + weight;
            c.Pins.Add(pin);
            clusters[index] = (((c.X * c.Weight) + (x * weight)) / total, ((c.Y * c.Weight) + (y * weight)) / total, total, c.Pins);
        }

        return clusters.Select(c => new MapCluster(c.X, c.Y, c.Pins)).ToList();
    }
}
