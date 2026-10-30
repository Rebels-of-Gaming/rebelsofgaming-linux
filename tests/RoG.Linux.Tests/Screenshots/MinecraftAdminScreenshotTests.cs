using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using RoG.Linux.App.Controls;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 9 (Teil M): Verwaltungsreiter Minecraft, Rebel-Coins und Minecraft-Netzwerk – gehostet in einem Test-Fenster
/// mit Rückmeldung und Rückfrage oben (die Verwaltungsseite baut Teil J) –, die Weltkarte (1280×800 und schmal) und der
/// Server-Reiter Minecraft mit Ressourcenpaket und „Beitrittsdaten bearbeiten“.
/// </summary>
[Trait("Category", "Screenshots")]
public class MinecraftAdminScreenshotTests
{
    // Aus dem Quellpfad statt LocalizationTests.RepoRoot(): bin/obj können per Symlink außerhalb des Klons liegen
    private static readonly string s_folder = Path.Combine(RepoRoot(), "artifacts", "screenshots");

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".."));

    private static async Task ShootTab(string name, int width, int height, Func<TestServices, FeedbackViewModel, InlineConfirmViewModel, MinecraftAdminTabBase> create, Func<MinecraftAdminTabBase, Window, Task> arrange)
    {
        using var t = await MinecraftAdminTests.SignedInAsync();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme wie in der App
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var tab = create(t, feedback, confirm);
        var top = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(24, 16, 24, 8) };
        top.Children.Add(new TextBlock { Text = "VERWALTUNG", Classes = { "display" } });
        top.Children.Add(new FeedbackBar { DataContext = feedback, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left });
        top.Children.Add(new ConfirmBar { DataContext = confirm, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ContentControl { Content = tab, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch });
        var window = new Window { Width = width, Height = height, Content = root };
        window.Show();
        try
        {
            await tab.EnsureLoadedAsync();
            await ScreenshotTests.Settle(300);
            await arrange(tab, window);
            await ScreenshotTests.Settle(400);
            Save(window, name, width);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    /// <summary>Wie <c>ScreenshotTests.Shoot</c>, aber mit dem Fenster für <paramref name="arrange"/> (Scrollen) und Ablage im Klon.</summary>
    private static async Task ShootShell(string name, int width, int height, TestServices t, Func<ShellViewModel, Window, Task> arrange)
    {
        using (t)
        {
            var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
            window.Width = width;
            window.Height = height;
            window.Show();
            try
            {
                var shell = (ShellViewModel)window.DataContext!;
                await shell.InitializeAsync();
                await ScreenshotTests.Settle(200);
                await arrange(shell, window);
                Save(window, name, width);
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            }
        }
    }

    private static void Save(Window window, string name, int width)
    {
        Directory.CreateDirectory(s_folder);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(width, frame.PixelSize.Width);
        frame.Save(Path.Combine(s_folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        foreach (var button in window.GetVisualDescendants().OfType<Button>())
        {
            if (button.IsEffectivelyVisible && button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, 0), window) is { } right)
            {
                Assert.True(right.X <= window.Width + 1, $"Knopf ragt aus dem Fenster: {button.Content}");
            }
        }
    }

    private static MinecraftAdminViewModel Minecraft(TestServices t, FeedbackViewModel f, InlineConfirmViewModel c) => new(t.Services, f, c);

    private static CoinsAdminViewModel Coins(TestServices t, FeedbackViewModel f, InlineConfirmViewModel c) => new(t.Services, f, c);

    private static MinecraftOpsViewModel Network(TestServices t, FeedbackViewModel f, InlineConfirmViewModel c) =>
        new(t.Services, f, c) { ReleasePoll = TimeSpan.Zero, DeployPoll = TimeSpan.Zero };

    [AvaloniaFact]
    public Task Minecraft_tab_steam_deck() => ShootTab("admin_minecraft_1280x800", 1280, 800, Minecraft, async (tab, w) =>
    {
        var mc = (MinecraftAdminViewModel)tab;
        await WaitUntil(() => mc.Groups.RoleOptions.Count > 0 && !mc.IsBusy);
        mc.Groups.Mappings[0].EditCommand.Execute(null);
    });

    [AvaloniaFact]
    public Task Minecraft_tab_unlink_narrow() => ShootTab("admin_minecraft_narrow_480x900", 480, 900, Minecraft, async (tab, w) =>
    {
        var mc = (MinecraftAdminViewModel)tab;
        await WaitUntil(() => mc.Items.Count > 0);
        _ = mc.Items[1].UnlinkCommand.ExecuteAsync(null);
        await WaitUntil(() => mc.Confirm.IsOpen);
    });

    [AvaloniaFact]
    public Task Coins_tab_steam_deck() => ShootTab("admin_coins_1280x800", 1280, 800, Coins, async (tab, w) =>
    {
        var coins = (CoinsAdminViewModel)tab;
        await WaitUntil(() => coins.Items.Count > 0);
        coins.Selected = coins.Items[0];
        await WaitUntil(() => coins.Transactions.Count > 0);
        coins.Amount = 250;
        coins.Reason = "Gewinn Kürbis-Event";
    });

    [AvaloniaFact]
    public Task Coins_tab_confirm_narrow() => ShootTab("admin_coins_narrow_480x900", 480, 900, Coins, async (tab, w) =>
    {
        var coins = (CoinsAdminViewModel)tab;
        await WaitUntil(() => coins.Items.Count > 0);
        coins.Selected = coins.Items[0];
        await WaitUntil(() => coins.Transactions.Count > 0);
        coins.Amount = 250;
        coins.Reason = "Gewinn Kürbis-Event";
        _ = coins.AdjustCommand.ExecuteAsync(null);
        await WaitUntil(() => coins.Confirm.IsOpen);
    });

    [AvaloniaFact]
    public Task Coins_tab_list_narrow() => ShootTab("admin_coins_list_narrow_480x800", 480, 800, Coins, async (tab, w) =>
    {
        var coins = (CoinsAdminViewModel)tab;
        await WaitUntil(() => coins.Items.Count > 0);
    });

    [AvaloniaFact]
    public Task Network_tab_steam_deck() => ShootTab("admin_network_1280x800", 1280, 800, Network, async (tab, w) =>
    {
        var ops = (MinecraftOpsViewModel)tab;
        await WaitUntil(() => ops.Servers.Count > 0 && !ops.IsBusy);
        await ops.CheckReleaseCommand.ExecuteAsync(null);
        ops.Servers.Single(s => s.Name == "RoG Lobby").IsSelected = true;
        await ScreenshotTests.Settle(200);
        ScrollTo(w.GetVisualDescendants().OfType<MinecraftOpsView>().Single(), "Plugins verteilen");
    });

    [AvaloniaFact]
    public Task Network_tab_pool_narrow() => ShootTab("admin_network_narrow_480x900", 480, 900, Network, async (tab, window) =>
    {
        var ops = (MinecraftOpsViewModel)tab;
        await WaitUntil(() => ops.Groups.Count > 0 && !ops.IsBusy);
        await ScreenshotTests.Settle(200);
        ScrollTo(window.GetVisualDescendants().OfType<MinecraftOpsView>().Single(), "Minigame-Pool");
    });

    /// <summary>Scrollt die erste ScrollViewer so, dass der Text <paramref name="text"/> oben steht.</summary>
    private static void ScrollTo(Control view, string text)
    {
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        var target = scroll.GetVisualDescendants().OfType<TextBlock>().First(b => string.Equals(b.Text, text, StringComparison.OrdinalIgnoreCase));
        var y = target.TranslatePoint(new Avalonia.Point(0, 0), (Visual)scroll.Content!)!.Value.Y;
        scroll.Offset = new Avalonia.Vector(0, Math.Max(0, y - 12));
    }

    private static async Task<TestServices> AdminShellAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeMinecraftAdminRoutes.MeAdmin;
        return t;
    }

    private static async Task OpenMap(ShellViewModel shell, bool admin)
    {
        Assert.True(shell.OpenDeepLink(new Uri("rog://map?server=freebuild-1&x=40&z=40")));
        var map = Assert.IsType<MinecraftMapViewModel>(shell.CurrentPage);
        map.Tiles = new MapTileCache(new ImageLoader(new FakeMinecraftTiles()), MinecraftMapViewModel.TileCacheCapacity);
        await WaitUntil(() => map.ShowContent && map.Players.Count > 0);
        await ScreenshotTests.Settle(900);
        if (admin)
        {
            _ = map.ClearTilesCommand.ExecuteAsync(null);
            await WaitUntil(() => map.Confirm.IsOpen);
        }

        await ScreenshotTests.Settle(600);
    }

    [AvaloniaFact]
    public async Task Map_steam_deck() =>
        await ShootShell("minecraft_map_1280x800", 1280, 800, await AdminShellAsync(), async (shell, window) =>
        {
            await OpenMap(shell, false);
            var view = window.GetVisualDescendants().OfType<MinecraftMapView>().Single();
            Assert.True(view.MapControl.DrawnTiles >= 4);
        });

    [AvaloniaFact]
    public async Task Map_narrow() =>
        await ShootShell("minecraft_map_narrow_480x800", 480, 800, await AdminShellAsync(), (shell, _) => OpenMap(shell, false));

    [AvaloniaFact]
    public async Task Map_admin_clear_tiles() =>
        await ShootShell("minecraft_map_admin_1280x800", 1280, 800, await AdminShellAsync(), async (shell, window) =>
        {
            await OpenMap(shell, true);
            var expander = window.GetVisualDescendants().OfType<MinecraftMapView>().Single().GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
            await ScreenshotTests.Settle(300);
            var side = window.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Name == "Side");
            side.Offset = new Vector(0, side.Extent.Height);
            await ScreenshotTests.Settle(200);
        });

    [AvaloniaFact]
    public async Task Map_guest() =>
        await ShootShell("minecraft_map_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async (shell, _) =>
        {
            shell.NavigateTo("Nav_MinecraftMap");
            var map = Assert.IsType<MinecraftMapViewModel>(shell.CurrentPage);
            map.Tiles = new MapTileCache(new ImageLoader(new FakeMinecraftTiles()), MinecraftMapViewModel.TileCacheCapacity);
            await WaitUntil(() => map.ShowContent);
            await ScreenshotTests.Settle(900);
        });

    private static async Task OpenMinecraftServers(ShellViewModel shell, Window window, TestServices t, Func<MinecraftJoinViewModel, MinecraftJoinView, Task> arrange)
    {
        t.Api.Overrides["/v1/minecraft/join"] = FakeMinecraftAdminRoutes.JoinForManagers;
        Assert.True(shell.OpenDeepLink(new Uri("rog://servers/minecraft")));
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        await WaitUntil(() => servers.Join.ShowContent && servers.Join.CanEdit);
        await ScreenshotTests.Settle(300);
        var view = window.GetVisualDescendants().OfType<MinecraftJoinView>().Single();
        await arrange(servers.Join, view);
        await ScreenshotTests.Settle(500);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Servers_minecraft_pack_admin()
    {
        var t = await AdminShellAsync();
        await ShootShell("servers_minecraft_pack_1280x800", 1280, 800, t, (shell, window) => OpenMinecraftServers(shell, window, t, async (join, view) =>
        {
            _ = join.DeletePackCommand.ExecuteAsync(null);
            await WaitUntil(() => join.Confirm.IsOpen);
            await ScreenshotTests.Settle(200);
            ScrollTo(view, t.Services.T("McJoin_PackTitle.Text"));
        }));
    }

    [AvaloniaFact]
    public async Task Servers_minecraft_edit()
    {
        var t = await AdminShellAsync();
        await ShootShell("servers_minecraft_edit_1280x800", 1280, 800, t, (shell, window) => OpenMinecraftServers(shell, window, t, async (join, view) =>
        {
            join.IsEditExpanded = true;
            await WaitUntil(() => join.CanSave);
            join.EditJavaPort = 25570;
            await ScreenshotTests.Settle(300);
            ScrollTo(view, t.Services.T("McJoin_Edit.Header"));
        }));
    }

    [AvaloniaFact]
    public async Task Servers_minecraft_edit_narrow()
    {
        var t = await AdminShellAsync();
        await ShootShell("servers_minecraft_edit_narrow_480x900", 480, 900, t, (shell, window) => OpenMinecraftServers(shell, window, t, async (join, view) =>
        {
            join.IsEditExpanded = true;
            await WaitUntil(() => join.CanSave);
            await ScreenshotTests.Settle(300);
            ScrollTo(view, t.Services.T("McJoin_EditLead.Text"));
        }));
    }
}
