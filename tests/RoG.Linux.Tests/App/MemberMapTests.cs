using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Mitgliederkarte – Kacheln nur von der eigenen API, Cluster, Namen, eigenes Fenster.</summary>
public class MemberMapTests
{
    internal static MemberMapViewModel Create(TestServices t, FakeTiles tiles) =>
        new(t.Services) { Tiles = new MapTileCache(new ImageLoader(tiles)) };

    [AvaloniaFact]
    public async Task Loads_places_with_trusted_tiles_and_attribution()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        Assert.True(map.ShowContent);
        Assert.Equal(8, map.Map!.Pins.Count);
        Assert.Equal(FakeMapRoutes.TileTemplate, map.Map.Tiles!.UrlTemplate);
        Assert.False(map.HasInfo);
        Assert.Equal("© OpenStreetMap", map.Attribution);

        await map.OpenAttributionCommand.ExecuteAsync(null);
        Assert.Equal("https://www.openstreetmap.org/copyright", t.Browser.Opened[^1].ToString());
    }

    [AvaloniaFact]
    public async Task Foreign_tile_servers_are_never_used()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/map"] = FakeMapRoutes.Map("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        Assert.Null(map.Map!.Tiles);
        Assert.Equal(t.Services.T(StringKeys.MapNoTiles), map.Info);
        Assert.False(map.HasAttribution);

        // auch nicht die Website einer anderen Umgebung
        t.Api.Overrides["/v1/map"] = FakeMapRoutes.Map("https://api.rebelsofgaming.dev/v1/map/tiles/{z}/{x}/{y}.png?signature=x");
        await map.LoadAsync();
        Assert.Null(map.Map!.Tiles);
    }

    [AvaloniaFact]
    public async Task Empty_map_explains_the_opt_in()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/map"] = FakeMapRoutes.EmptyMap;
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        Assert.True(map.ShowContent);
        Assert.Equal(t.Services.T(StringKeys.MapEmpty), map.Info);
    }

    [AvaloniaFact]
    public async Task Errors_show_the_error_state()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.FailWith = System.Net.HttpStatusCode.Forbidden;
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        Assert.True(map.HasError);
        Assert.False(map.ShowContent);
    }

    [AvaloniaFact]
    public async Task Near_places_are_clustered_until_zoomed_in()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        var pins = map.Map!.Pins;

        var far = MapClustering.Build(pins, 5, 256, 44);
        var trier = Assert.Single(far, c => c.Pins.Any(p => p.City == "Trier"));
        Assert.Contains(trier.Pins, p => p.City == "Luxemburg");
        Assert.Contains(trier.Pins, p => p.City == "Saarbrücken");
        Assert.True(trier.Count >= 6);
        Assert.Equal(pins.Sum(p => p.Members.Count), far.Sum(c => c.Count));
        Assert.StartsWith("Trier, ", trier.Label, StringComparison.Ordinal);

        var near = MapClustering.Build(pins, 10, 256, 44);
        Assert.Equal(8, near.Count);
    }

    [AvaloniaFact]
    public async Task Clicking_a_cluster_lists_names_and_opens_profiles_in_the_app()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var routes = new List<AppRoute>();
        t.Services.Navigator = r =>
        {
            routes.Add(r);
            return true;
        };
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        var cluster = MapClustering.Build(map.Map!.Pins, 5, 256, 44).First(c => c.Pins.Any(p => p.City == "Trier"));
        var opened = new List<string>();
        map.ProfileOpened += (_, slug) => opened.Add(slug);

        map.ShowCluster(cluster);
        Assert.True(map.IsPanelOpen);
        Assert.Equal(t.Services.Format("Linux_MapMembers", cluster.Count), map.PanelTitle);
        Assert.Contains(map.Places, p => p.Title == "TRIER (DE)");
        var trier = map.Places.First(p => p.Title == "TRIER (DE)");
        Assert.Equal(["Mara", "Nyx", "Wahke"], trier.Members.Select(m => m.Name));
        Assert.Equal("Counter-Strike 2", trier.Members[1].Squads);

        trier.Members[1].OpenCommand.Execute(null);
        Assert.Equal(new AppRoute(AppSection.Members, "nyx"), routes[^1]);
        Assert.Equal(["nyx"], opened);

        map.ClosePanel();
        Assert.False(map.IsPanelOpen);
    }

    [AvaloniaFact]
    public async Task Signed_tile_template_is_refreshed_before_it_expires()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var map = Create(t, new FakeTiles());
        await map.LoadAsync();
        int Loads() => t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/map");
        Assert.Equal(1, Loads());
        Assert.False(await map.RefreshIfExpiringAsync(new DateTimeOffset(2098, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(1, Loads());
        Assert.True(await map.RefreshIfExpiringAsync(new DateTimeOffset(2098, 12, 31, 23, 55, 0, TimeSpan.Zero)));
        Assert.Equal(2, Loads());
    }

    [AvaloniaFact]
    public async Task Tile_cache_stays_in_memory_and_bounded()
    {
        var tiles = new FakeTiles();
        var cache = new MapTileCache(new ImageLoader(tiles), capacity: 3);
        var loaded = 0;
        for (var x = 0; x < 5; x++)
        {
            cache.Request(new Uri($"https://api.rebelsofgaming.com/v1/map/tiles/4/{x}/5.png?signature=a"), () => loaded++);
        }

        await ViewModelTests.WaitUntil(() => loaded == 5);
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet(new Uri("https://api.rebelsofgaming.com/v1/map/tiles/4/0/5.png?signature=a"), out _));
        Assert.True(cache.TryGet(new Uri("https://api.rebelsofgaming.com/v1/map/tiles/4/4/5.png?signature=a"), out var bitmap));
        Assert.Equal(256, bitmap!.PixelSize.Width);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [AvaloniaFact]
    public async Task Members_page_opens_the_map_only_when_signed_in()
    {
        using var guest = await TestServices.CreateAsync(guest: true);
        Assert.False(new MembersViewModel(guest.Services).CanShowMap);

        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var members = new MembersViewModel(t.Services);
        var changed = false;
        members.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(MembersViewModel.CanShowMap);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        Assert.True(changed);
        Assert.True(members.CanShowMap);
        MemberMapViewModel? requested = null;
        members.MapRequested += (_, m) => requested = m;
        members.OpenMapCommand.Execute(null);
        Assert.NotNull(requested);
    }

    [AvaloniaFact]
    public async Task Map_window_loads_tiles_only_from_the_own_api_and_supports_keyboard()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen und Theme wie in der App
        MemberMapWindow.Current?.Close();
        var tiles = new FakeTiles();
        var map = Create(t, tiles);
        var window = MemberMapWindow.Open(map);
        Assert.Same(window, MemberMapWindow.Open(Create(t, tiles))); // höchstens ein Fenster
        await ViewModelTests.WaitUntil(() => map.ShowContent);
        for (var i = 0; i < 100 && tiles.Requests.Count < 6; i++)
        {
            await Screenshots.ScreenshotTests.Settle(50); // Kacheln werden beim Zeichnen angefordert
        }

        Assert.True(tiles.Requests.Count >= 6);

        lock (tiles.Requests)
        {
            Assert.All(tiles.Requests, r =>
            {
                Assert.Equal("api.rebelsofgaming.com", r.Url.Host);
                Assert.StartsWith("/v1/map/tiles/", r.Url.AbsolutePath, StringComparison.Ordinal);
                Assert.Contains("signature=", r.Url.Query, StringComparison.Ordinal);
                Assert.False(r.HasAuthorization); // signierte Vorlage, ohne Token
            });
        }

        var view = window.MapView;
        Assert.NotEmpty(view.Markers);
        var zoom = view.Zoom;
        view.Focus();
        window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(zoom + 1, view.Zoom);
        window.KeyPress(Key.Subtract, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(zoom, view.Zoom);
        var center = view.Center;
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(center.X + 80, view.Center.X, 3);

        view.Markers[0].Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(map.IsPanelOpen);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.False(map.IsPanelOpen);
        Assert.Same(window, MemberMapWindow.Current);
        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.None, null);
        Assert.Null(MemberMapWindow.Current);
    }
}
