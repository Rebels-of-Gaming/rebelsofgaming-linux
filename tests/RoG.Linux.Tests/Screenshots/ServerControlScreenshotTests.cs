using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 7 (Teil E): Steuer-Fenster je Reiter breit (1280×800) und schmal (600 px), Rückfrage, Lesemodus, Server-Karte.</summary>
[Trait("Category", "Screenshots")]
public class ServerControlScreenshotTests
{
    private static readonly string[] s_log =
    [
        "\u001b[1;32mETL 2.83.2 linux-x86_64\u001b[0m",
        "------- Server Initialization -------",
        "Loading map supply …",
        "Server: supply  (sv_maxclients 20, sv_privateclients 2)",
        "ClientConnect: 0 Nyx (GUID 3F2A…91C0)",
        "ClientConnect: 1 Kaito (GUID 8B11…02DE)",
        "say: Nyx: gg, nächste Runde Radar?",
        "Objective: The Allies have breached the Forward Bunker!",
        "ClientBegin: 2 Gast_42",
        "Warmup ended – match started (timelimit 30)",
        "Kill: 0 2 7: Nyx killed Gast_42 by MOD_MP40",
        "say: Kaito: Truck ist unterwegs",
        "Objective: The Axis have destroyed the Crane Controls!",
    ];

    [AvaloniaFact]
    public Task Console_wide() => Shoot("server_control_console_1280x800", 1280, 800, ServerControlTab.Console);

    [AvaloniaFact]
    public Task Console_narrow() => Shoot("server_control_console_narrow_600x800", 600, 800, ServerControlTab.Console);

    [AvaloniaFact]
    public Task Console_kill_confirm() => Shoot("server_control_kill_confirm_1280x800", 1280, 800, ServerControlTab.Console, async control =>
    {
        _ = control.Console.KillServerCommand.ExecuteAsync(null);
        await ScreenshotTests.Settle(200);
    });

    [AvaloniaFact]
    public Task Console_scrolled_up() => Shoot("server_control_console_paused_1280x800", 1280, 800, ServerControlTab.Console, async control =>
    {
        control.Console.IsFollowing = false;
        await ScreenshotTests.Settle(200);
    });

    [AvaloniaFact]
    public Task Console_read_only() => Shoot("server_control_console_readonly_1280x800", 1280, 800, ServerControlTab.Console,
        server: FakeServerControlRoutes.Server("cs2-retake", new RoG.Core.Features.Servers.ServerAbilities(Power: false, Console: true, ConsoleCommand: false, CanCommand: false), "RoG CS2: Retake"));

    [AvaloniaFact]
    public Task Console_demo_narrow() => Shoot("server_control_demo_narrow_600x800", 600, 800, ServerControlTab.Console,
        server: FakeServerControlRoutes.Server("demo-et", name: "RoG Demo (ET)"));

    [AvaloniaFact]
    public Task Rcon_wide() => Shoot("server_control_rcon_1280x800", 1280, 800, ServerControlTab.Rcon, async control =>
    {
        control.Rcon.Command = "status";
        await control.Rcon.SendCommand.ExecuteAsync(null);
        await ScreenshotTests.Settle(200);
    });

    [AvaloniaFact]
    public Task Rcon_narrow() => Shoot("server_control_rcon_narrow_600x800", 600, 800, ServerControlTab.Rcon, async control =>
    {
        control.Rcon.Command = "status";
        await control.Rcon.SendCommand.ExecuteAsync(null);
        await ScreenshotTests.Settle(200);
    });

    [AvaloniaFact]
    public Task Bans_wide() => Shoot("server_control_bans_1280x800", 1280, 800, ServerControlTab.Bans, c => WaitFor(() => c.Bans.ShowContent));

    [AvaloniaFact]
    public Task Bans_narrow() => Shoot("server_control_bans_narrow_600x800", 600, 800, ServerControlTab.Bans, c => WaitFor(() => c.Bans.ShowContent));

    [AvaloniaFact]
    public Task Whitelist_wide() => Shoot("server_control_whitelist_1280x800", 1280, 800, ServerControlTab.Whitelist, c => WaitFor(() => c.Whitelist.ShowContent));

    [AvaloniaFact]
    public Task Whitelist_narrow() => Shoot("server_control_whitelist_narrow_600x800", 600, 800, ServerControlTab.Whitelist, c => WaitFor(() => c.Whitelist.ShowContent));

    [AvaloniaFact]
    public Task Approvals_wide() => Shoot("server_control_approvals_1280x800", 1280, 800, ServerControlTab.Approvals, c => WaitFor(() => c.Approvals.ShowContent));

    [AvaloniaFact]
    public Task Approvals_narrow() => Shoot("server_control_approvals_narrow_600x800", 600, 800, ServerControlTab.Approvals, c => WaitFor(() => c.Approvals.ShowContent));

    [AvaloniaFact]
    public Task Configs_wide() => Shoot("server_control_configs_1280x800", 1280, 800, ServerControlTab.Configs);

    [AvaloniaFact]
    public Task Configs_narrow() => Shoot("server_control_configs_narrow_600x800", 600, 800, ServerControlTab.Configs);

    [AvaloniaFact]
    public Task Files_wide() => Shoot("server_control_files_1280x800", 1280, 800, ServerControlTab.Files);

    [AvaloniaFact]
    public Task Files_narrow() => Shoot("server_control_files_narrow_600x800", 600, 800, ServerControlTab.Files);

    [AvaloniaFact]
    public Task Backups_wide() => Shoot("server_control_backups_1280x800", 1280, 800, ServerControlTab.Backups);

    [AvaloniaFact]
    public Task Backups_narrow() => Shoot("server_control_backups_narrow_600x800", 600, 800, ServerControlTab.Backups);

    [AvaloniaFact]
    public Task GameBans_wide() => Shoot("server_control_gamebans_1280x800", 1280, 800, ServerControlTab.GameBans);

    [AvaloniaFact]
    public Task GameBans_narrow() => Shoot("server_control_gamebans_narrow_600x800", 600, 800, ServerControlTab.GameBans);

    [AvaloniaFact]
    public async Task Server_card_with_control_button()
    {
        var t = await ServerControlTests.SignedInAsync();
        t.Api.Overrides["/v1/servers"] = FakeServerControlRoutes.ServersWithControl;
        await ScreenshotTests.Shoot("servers_control_button_1280x800", 1280, 800, t, async shell =>
        {
            shell.NavigateTo("Nav_Servers");
            var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => servers.ShowContent);
            Assert.True(servers.Servers[0].CanControl);
            await ScreenshotTests.Settle(400);
        });
    }

    private static Task WaitFor(Func<bool> condition) => ViewModelTests.WaitUntil(condition);

    private static async Task Shoot(string name, int width, int height, ServerControlTab tab, Func<ServerControlViewModel, Task>? arrange = null,
        RoG.Core.Features.Servers.GameServer? server = null)
    {
        using var t = await ServerControlTests.SignedInAsync();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme wie in der App
        var sockets = new FakeConsoleSockets();
        var control = ServerControlTests.Create(t, sockets, server);
        var window = new ServerControlWindow(control) { Width = width, Height = height };
        try
        {
            window.Show();
            if (control.Access.Console)
            {
                if (server?.Slug == "demo-et")
                {
                    await ViewModelTests.WaitUntil(() => control.Console.IsDemo);
                }
                else
                {
                    await ViewModelTests.WaitUntil(() => sockets.Created.Count > 0 && sockets.Last.SentArgs("auth").Count > 0);
                    var socket = sockets.Last;
                    socket.Send("auth success");
                    await ViewModelTests.WaitUntil(() => control.Console.IsConnected);
                    foreach (var line in s_log)
                    {
                        socket.Send("console output", line);
                    }

                    await ViewModelTests.WaitUntil(() => control.Console.Lines.Count >= s_log.Length);
                }
            }

            control.Console.History.Add("say Scrim um 20 Uhr");
            control.SelectedTab = tab;
            await ScreenshotTests.Settle(300);
            if (arrange is not null)
            {
                await arrange(control);
            }

            await ScreenshotTests.Settle(400);
            var folder = Path.Combine(LocalizationTests.RepoRoot(), "artifacts", "screenshots");
            Directory.CreateDirectory(folder);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width);
            frame.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }
}
