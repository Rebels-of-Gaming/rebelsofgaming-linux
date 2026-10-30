using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 7 „G“: Reiter Spiel-Bans (in einem Test-Fenster, wie ihn das Steuer-Fenster einhängt), Provisioning-Fenster
/// (anlegen, verwalten), Server-Karte mit Provisioning und öffentliche Ban-Liste.
/// </summary>
[Trait("Category", "Screenshots")]
public class GameBansProvisionScreenshotTests
{
    private static async Task<TestServices> Manager()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeGameBansRoutes.MeManager;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static async Task Save(Window window, string name, int width)
    {
        await ScreenshotTests.Settle(500);
        var folder = Path.Combine(LocalizationTests.RepoRoot(), "artifacts", "screenshots");
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(width, frame.PixelSize.Width);
        frame.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Reiter im Test-Fenster über das DataTemplate aus <c>App.axaml</c> (wie der ContentControl im Steuer-Fenster).</summary>
    private static async Task ShootGameBans(string name, int width, int height, Func<ServerGameBansViewModel, Task>? arrange = null, bool scrollDown = false)
    {
        using var t = await Manager();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var vm = new ServerGameBansViewModel(t.Services, FakeGameBansRoutes.Slug, FakeGameBansRoutes.ServerId, GameBansProvisionTests.BanAbilities);
        var window = new Window
        {
            Width = width,
            Height = height,
            Background = RoG.Linux.App.Services.UiResources.Brush("RoGBgBrush"),
            Content = new ContentControl { Content = vm },
        };
        window.Show();
        try
        {
            await ViewModelTests.WaitUntil(() => vm.Items.Count > 0 && vm.HasKeySection);
            if (arrange is not null)
            {
                await arrange(vm);
            }

            if (scrollDown)
            {
                await ScreenshotTests.Settle(300);
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroller.Offset = new Avalonia.Vector(0, 900);
            }

            await Save(window, name, width);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    [AvaloniaFact]
    public Task Game_bans_tab() => ShootGameBans("gamebans_1280x800", 1280, 800);

    [AvaloniaFact]
    public Task Game_bans_tab_list() => ShootGameBans("gamebans_list_1280x800", 1280, 800, async vm =>
    {
        await vm.Items[0].DetailsCommand.ExecuteAsync(null);
    }, scrollDown: true);

    [AvaloniaFact]
    public Task Game_bans_tab_narrow() => ShootGameBans("gamebans_narrow_480x900", 480, 900);

    [AvaloniaFact]
    public Task Game_bans_tab_lift_prompt() => ShootGameBans("gamebans_lift_480x900", 480, 900, vm =>
    {
        _ = vm.Items[0].LiftCommand.ExecuteAsync(null);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task Game_bans_tab_token() => ShootGameBans("gamebans_token_1280x800", 1280, 800, async vm =>
    {
        _ = vm.CreateKeyCommand.ExecuteAsync(null);
        vm.Confirmation.AcceptCommand.Execute(null);
        await ViewModelTests.WaitUntil(() => vm.Token.IsOpen);
    });

    private static async Task ShootProvision(string name, int width, int height, bool manage, Func<ServerProvisionViewModel, Task>? arrange = null)
    {
        using var t = await Manager();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        t.Api.Overrides["/v1/servers"] = FakeGameBansRoutes.ServersWithProvisioning;
        var servers = new ServersViewModel(t.Services, () => { });
        await servers.LoadAsync();
        servers.Pause();
        ServerProvisionRequest? request = null;
        servers.ProvisionRequested += (_, r) => request = r;
        if (manage)
        {
            servers.Servers[1].ManageCommand.Execute(null);
        }
        else
        {
            servers.ProvisionCommand.Execute(null);
        }

        var window = ServerProvisionWindow.OpenFor(request!);
        window.Width = width;
        window.Height = height;
        var vm = (ServerProvisionViewModel)window.DataContext!;
        try
        {
            if (!manage)
            {
                await ViewModelTests.WaitUntil(() => vm.ShowCreate);
                vm.SelectedEgg = vm.Eggs[0];
                await ViewModelTests.WaitUntil(() => vm.Fields.Any(f => f.Field.Key == "environment.SRCDS_MAP"));
            }

            if (arrange is not null)
            {
                await arrange(vm);
            }

            await Save(window, name, width);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    [AvaloniaFact]
    public Task Provision_create() => ShootProvision("provision_create_1280x800", 1280, 800, manage: false);

    [AvaloniaFact]
    public Task Provision_create_narrow() => ShootProvision("provision_create_narrow_480x900", 480, 900, manage: false);

    [AvaloniaFact]
    public Task Provision_create_errors() => ShootProvision("provision_errors_1280x800", 1280, 800, manage: false, vm => vm.CreateCommand.ExecuteAsync(null));

    [AvaloniaFact]
    public Task Provision_manage() => ShootProvision("provision_manage_1280x800", 1280, 800, manage: true, vm =>
    {
        vm.ConfirmName = "RoG CS2: Scrim";
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task Provision_manage_narrow() => ShootProvision("provision_manage_narrow_480x900", 480, 900, manage: true, vm =>
    {
        vm.ConfirmName = "RoG CS2: Scrim";
        _ = vm.DeleteCommand.ExecuteAsync(null);
        return Task.CompletedTask;
    });

    private static async Task OpenServers(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Servers");
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => servers.ShowContent);
        await ScreenshotTests.Settle(400);
    }

    private static async Task<TestServices> ManagerWithServers()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeGameBansRoutes.MeManager;
        t.Api.Overrides["/v1/servers"] = FakeGameBansRoutes.ServersWithProvisioning;
        return t;
    }

    [AvaloniaFact]
    public async Task Servers_with_provisioning() =>
        await ScreenshotTests.Shoot("servers_provisioning_1280x800", 1280, 800, await ManagerWithServers(), OpenServers);

    [AvaloniaFact]
    public async Task Servers_with_provisioning_narrow() =>
        await ScreenshotTests.Shoot("servers_provisioning_narrow_480x900", 480, 900, await ManagerWithServers(), OpenServers);

    private static async Task OpenBans(ShellViewModel shell, bool detail)
    {
        if (detail)
        {
            shell.OpenDeepLink(new Uri("rog://bans/" + FakeGameBansRoutes.PublicActive));
        }
        else
        {
            shell.OpenDeepLink(new Uri("rog://bans"));
        }

        var bans = Assert.IsType<BansViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => bans.ShowContent && (!detail || bans.Detail.ShowContent));
        await ScreenshotTests.Settle(400);
    }

    private static async Task<TestServices> Member()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeGameBansRoutes.MeMember;
        return t;
    }

    [AvaloniaFact]
    public async Task Ban_list() => await ScreenshotTests.Shoot("bans_1280x800", 1280, 800, await Member(), shell => OpenBans(shell, detail: true));

    [AvaloniaFact]
    public async Task Ban_list_narrow() => await ScreenshotTests.Shoot("bans_narrow_480x900", 480, 900, await Member(), shell => OpenBans(shell, detail: false));

    [AvaloniaFact]
    public async Task Ban_list_guest() =>
        await ScreenshotTests.Shoot("bans_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => OpenBans(shell, detail: true));
}
