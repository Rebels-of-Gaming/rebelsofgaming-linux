using Avalonia.Headless.XUnit;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 7 (Teil E): Steuer-Fenster – Rechte/Scopes, Live-Konsole gegen Fake-WebSocket, Power mit Rückfrage, Moderation.</summary>
public class ServerControlTests
{
    internal static async Task<TestServices> SignedInAsync(bool serverScopes = true)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        if (serverScopes)
        {
            t.Api.Overrides["/v1/me"] = FakeServerControlRoutes.MeWithServerScopes;
        }

        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    internal static ServerControlViewModel Create(TestServices t, FakeConsoleSockets sockets, GameServer? server = null)
    {
        var control = new ServerControlViewModel(t.Services, server ?? FakeServerControlRoutes.Server(), sockets.Create);
        control.Console.Delay = _ => Task.CompletedTask;
        return control;
    }

    /// <summary>Verbinden und „auth success“ von Wings – danach ist die Konsole live.</summary>
    internal static async Task<FakeConsoleSocket> ConnectAsync(ServerControlViewModel control, FakeConsoleSockets sockets)
    {
        await control.StartAsync();
        await ViewModelTests.WaitUntil(() => sockets.Created.Count > 0 && sockets.Last.SentArgs("auth").Count > 0);
        var socket = sockets.Last;
        socket.Send("auth success");
        await ViewModelTests.WaitUntil(() => control.Console.IsConnected);
        return socket;
    }

    [AvaloniaFact]
    public async Task Tabs_follow_abilities_in_windows_order_and_need_token_scopes()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());

        Assert.Equal(
            [ServerControlTab.Console, ServerControlTab.Configs, ServerControlTab.Files, ServerControlTab.Backups, ServerControlTab.Rcon,
             ServerControlTab.Bans, ServerControlTab.Whitelist, ServerControlTab.GameBans, ServerControlTab.Approvals],
            control.Tabs.Select(x => x.Tab));
        Assert.Equal(["KONSOLE", "CONFIGS", "DATEIEN", "BACKUPS", "RCON", "SPERREN", "WHITELIST", "BANS & PLUGIN", "FREIGABEN"], control.Tabs.Select(x => x.Label));
        Assert.True(control.ShowTabs && control.IsConsoleTab && control.Console.CanPower);

        // Nur Konsole lesen (kein Power, keine Dateien …) → ein Reiter, keine Leiste
        var reader = FakeServerControlRoutes.Server(abilities: new ServerAbilities(Power: false, Console: true, ConsoleCommand: false, CanCommand: false));
        await using var readOnly = Create(t, new FakeConsoleSockets(), reader);
        Assert.Equal([ServerControlTab.Console], readOnly.Tabs.Select(x => x.Tab));
        Assert.False(readOnly.ShowTabs);
        Assert.False(readOnly.Console.CanPower);

        // Ohne Steuer-Scopes im Token gibt es nichts – auch wenn die Rechte da sind
        using var plain = await SignedInAsync(serverScopes: false);
        Assert.False(ServerControlAccess.For(FakeServerControlRoutes.Server().Control, plain.Services.CurrentUser).Any);
        Assert.False(ServerControlAccess.For(null, t.Services.CurrentUser).Any);
        Assert.False(ServerControlAccess.For(FakeServerControlRoutes.Server().Control, null).Any);
    }

    [AvaloniaFact]
    public async Task Server_card_offers_control_only_with_rights_and_opens_the_window()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/servers"] = FakeServerControlRoutes.ServersWithControl;
        var opened = new List<GameServer>();
        t.Services.ServerControlOpener = opened.Add;
        var servers = new ServersViewModel(t.Services, () => { });
        await servers.LoadAsync();

        Assert.Equal([true, false, false, false], servers.Servers.Select(s => s.CanControl));
        Assert.Equal("RoG ET: Sniper steuern", servers.Servers[0].ControlAutomationName);
        servers.Servers[0].ControlCommand.Execute(null);
        Assert.Equal("et-sniper", Assert.Single(opened).Slug);
        Assert.True(Assert.Single(opened).Control!.Rcon);

        // Gleicher Server ohne passende Scopes: kein „Steuern“
        using var plain = await SignedInAsync(serverScopes: false);
        plain.Api.Overrides["/v1/servers"] = FakeServerControlRoutes.ServersWithControl;
        var other = new ServersViewModel(plain.Services, () => { });
        await other.LoadAsync();
        Assert.False(other.Servers[0].CanControl);
    }

    [AvaloniaFact]
    public async Task Console_connects_with_short_lived_ticket_streams_output_without_ansi_and_live_stats()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets);

        await control.StartAsync();
        Assert.Equal(t.Services.T(StringKeys.ConsoleStateRunning), control.Console.StateText); // Ressourcen aus der API
        Assert.Contains("37,4", control.Console.ResourcesText, StringComparison.Ordinal);
        Assert.Contains("700 MB / 1024 MB", control.Console.ResourcesText, StringComparison.Ordinal);
        Assert.Equal(68.4, control.Console.MemoryPercent!.Value, 1);
        Assert.True(control.Console.IsRunning);
        await ViewModelTests.WaitUntil(() => sockets.Created.Count == 1 && sockets.Last.SentArgs("auth").Count == 1);

        var socket = sockets.Last;
        Assert.Equal(new Uri("wss://node1.example.test:8080/api/servers/5b1c/ws"), socket.ConnectedTo);
        Assert.Equal("https://admin.example.test", socket.Origin);
        Assert.StartsWith("ticket-", socket.SentArgs("auth")[0], StringComparison.Ordinal);
        Assert.True(control.Console.IsConnecting);
        Assert.False(control.Console.CanCommand);

        socket.Send("auth success");
        await ViewModelTests.WaitUntil(() => control.Console.IsConnected);
        Assert.Equal(t.Services.T(StringKeys.ConsoleConnected), control.Console.ConnectionText);
        await ViewModelTests.WaitUntil(() => socket.SentArgs("send logs").Count == 1 && socket.SentArgs("send stats").Count == 1);
        Assert.True(control.Console.CanCommand);

        socket.Send("console output", "\u001b[33mServer gestartet\u001b[0m\r\n[ETL] Map supply geladen");
        socket.Send("stats", """{"state":"stopping","cpu_absolute":3.5,"memory_bytes":104857600,"memory_limit_bytes":1073741824,"disk_bytes":943718400,"uptime":60000}""");
        await ViewModelTests.WaitUntil(() => control.Console.Lines.Count >= 2 && control.Console.StateText == t.Services.T(StringKeys.ConsoleStateStopping));
        Assert.Equal(["Server gestartet", "[ETL] Map supply geladen"], control.Console.Lines.Take(2));
        Assert.Contains("100 MB / 1024 MB", control.Console.ResourcesText, StringComparison.Ordinal);
        socket.Send("status", "offline");
        await ViewModelTests.WaitUntil(() => control.Console.IsStopped);

        // Ticket nie auf der Platte (Einstellungen, Caches)
        foreach (var file in Directory.EnumerateFiles(t.Folder, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("ticket-", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Ticket_is_renewed_when_wings_says_token_expiring()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets);
        var socket = await ConnectAsync(control, sockets);

        socket.Send("token expiring");
        await ViewModelTests.WaitUntil(() => socket.SentArgs("auth").Count == 2);

        var tokens = socket.SentArgs("auth");
        Assert.NotEqual(tokens[0], tokens[1]);
        Assert.Single(sockets.Created); // gleiche Verbindung, nur neu angemeldet
        Assert.Equal(2, t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/servers/et-sniper/console-token"));
        Assert.True(control.Console.IsConnected);
    }

    [AvaloniaFact]
    public async Task Lost_connection_reconnects_with_a_new_ticket_and_manual_reconnect_works()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets);
        var socket = await ConnectAsync(control, sockets);

        socket.Drop();
        await ViewModelTests.WaitUntil(() => sockets.Created.Count == 2);
        Assert.True(sockets.Created[0].Disposed);
        sockets.Last.Send("auth success");
        await ViewModelTests.WaitUntil(() => control.Console.IsConnected);

        await control.Console.ConnectCommand.ExecuteAsync(null);
        await ViewModelTests.WaitUntil(() => sockets.Created.Count == 3 && sockets.Last.SentArgs("auth").Count == 1);
        Assert.True(sockets.Created[1].Disposed);
        Assert.Equal(3, t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/servers/et-sniper/console-token"));
    }

    [AvaloniaFact]
    public async Task Ticket_error_is_shown_translated_in_the_console()
    {
        using var t = await SignedInAsync();
        t.Api.Failures["/v1/servers/et-sniper/console-token"] = (System.Net.HttpStatusCode.Conflict, FakeAccountRoutes.Problem(409, "pterodactyl_write_blocked"));
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets);

        await control.StartAsync();

        Assert.Empty(sockets.Created);
        Assert.True(control.Console.IsLost);
        Assert.Equal("[RoG] " + t.Services.Localizer.GetError("pterodactyl_write_blocked"), control.Console.Lines[^1]);
        Assert.False(control.Console.CanCommand);
    }

    [AvaloniaFact]
    public async Task Commands_go_through_the_api_and_arrow_keys_browse_the_history()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets);
        await ConnectAsync(control, sockets);
        var console = control.Console;

        foreach (var command in new[] { "say Scrim startet in 5 Minuten", "map radar", "map radar", "  status  " })
        {
            console.Command = command;
            await console.SendCommand.ExecuteAsync(null);
        }

        Assert.Equal(string.Empty, console.Command);
        var bodies = t.Api.Writes.Where(w => w.Path == "/v1/servers/et-sniper/commands").Select(w => w.Body).ToList();
        Assert.Equal(4, bodies.Count);
        Assert.Contains("\"command\":\"status\"", bodies[^1], StringComparison.Ordinal);
        Assert.Equal("> status", console.Lines[^1]);
        Assert.Equal(["say Scrim startet in 5 Minuten", "map radar", "status"], console.History.Items); // doppelte direkt hintereinander nur einmal

        Assert.Equal("status", console.PreviousCommand());
        Assert.Equal("map radar", console.PreviousCommand());
        Assert.Equal("say Scrim startet in 5 Minuten", console.PreviousCommand());
        Assert.Equal("say Scrim startet in 5 Minuten", console.PreviousCommand()); // am Anfang bleibt es beim ältesten
        Assert.Equal("map radar", console.NextCommand());
        Assert.Equal("status", console.NextCommand());
        Assert.Equal(string.Empty, console.NextCommand());
        Assert.Equal(string.Empty, console.Command);
        Assert.False(console.SendCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Read_level_and_four_eyes_lock_the_input_with_a_reason()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var reader = Create(t, sockets, FakeServerControlRoutes.Server("cs2-retake"));
        await ConnectAsync(reader, sockets);

        Assert.Equal(ConsoleLevel.Read, reader.Console.Level);
        Assert.False(reader.Console.CanCommand);
        Assert.Equal(t.Services.T("Linux_ConsoleReadOnly"), reader.Console.CommandHint);
        reader.Console.Command = "kick all";
        await reader.Console.SendCommand.ExecuteAsync(null);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/commands", StringComparison.Ordinal));

        var approval = FakeServerControlRoutes.Server(abilities: new ServerAbilities(Power: true, Console: true, ConsoleCommand: true, CanCommand: false));
        var more = new FakeConsoleSockets();
        await using var locked = Create(t, more, approval);
        await ConnectAsync(locked, more);
        Assert.False(locked.Console.CanCommand);
        Assert.Equal(t.Services.T("Linux_ConsoleApprovalNeeded"), locked.Console.CommandHint);
    }

    [AvaloniaFact]
    public async Task Demo_server_has_no_live_socket_but_simulated_commands()
    {
        using var t = await SignedInAsync();
        var sockets = new FakeConsoleSockets();
        await using var control = Create(t, sockets, FakeServerControlRoutes.Server("demo-et", name: "RoG Demo (ET)"));

        await control.StartAsync();

        Assert.Empty(sockets.Created);
        Assert.True(control.Console.IsDemo);
        Assert.Equal(t.Services.T(StringKeys.ConsoleDemo), control.Console.ConnectionText);
        Assert.True(control.Console.CanCommand);
    }

    [AvaloniaFact]
    public async Task Power_asks_in_the_content_first_and_escape_cancels()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var console = control.Console;
        bool PowerSent(string signal) => t.Api.Writes.Any(w => w.Path == "/v1/servers/et-sniper/power" && w.Body.Contains($"\"signal\":\"{signal}\"", StringComparison.Ordinal));

        // Kill: Rückfrage mit den Windows-Texten, Escape bricht ab
        var kill = console.KillServerCommand.ExecuteAsync(null);
        Assert.True(control.Confirm.IsOpen);
        Assert.Equal(t.Services.T(StringKeys.ConsoleKillTitle), control.Confirm.Title);
        Assert.Equal(t.Services.T(StringKeys.ConsoleKillConfirm).ToUpperInvariant(), control.Confirm.ConfirmLabel);
        Assert.True(control.HandleEscape());
        await kill;
        Assert.False(PowerSent("kill"));
        Assert.False(control.HandleEscape()); // nichts mehr offen → Fenster schließt

        kill = console.KillServerCommand.ExecuteAsync(null);
        control.Confirm.AcceptCommand.Execute(null);
        await kill;
        Assert.True(PowerSent("kill"));
        Assert.Equal(t.Services.T(StringKeys.ConsolePowerSent), control.Feedback.Message);

        // Neustart und Stopp fragen ebenfalls, Start nicht
        var restart = console.RestartServerCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_ConsoleRestartTitle"), control.Confirm.Title);
        control.Confirm.AcceptCommand.Execute(null);
        await restart;
        Assert.True(PowerSent("restart"));

        var stop = console.StopServerCommand.ExecuteAsync(null);
        control.Confirm.CancelCommand.Execute(null);
        await stop;
        Assert.False(PowerSent("stop"));

        await console.StartServerCommand.ExecuteAsync(null);
        Assert.False(control.Confirm.IsOpen);
        Assert.True(PowerSent("start"));
    }

    [AvaloniaFact]
    public async Task Power_error_is_shown_and_buttons_need_the_power_right()
    {
        using var t = await SignedInAsync();
        t.Api.Failures["/v1/servers/et-sniper/power"] = (System.Net.HttpStatusCode.TooManyRequests, FakeAccountRoutes.Problem(429, "too_many_requests"));
        await using var control = Create(t, new FakeConsoleSockets());

        await control.Console.StartServerCommand.ExecuteAsync(null);
        Assert.True(control.Feedback.IsError);
        Assert.Equal(t.Services.Localizer.GetError("too_many_requests"), control.Feedback.Message);

        var noPower = FakeServerControlRoutes.Server(abilities: new ServerAbilities(Power: false, Console: true, ConsoleCommand: true, CanCommand: true));
        await using var viewer = Create(t, new FakeConsoleSockets(), noPower);
        Assert.False(viewer.Console.KillServerCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task History_is_capped_and_lines_can_be_copied()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var console = control.Console;
        var added = 0;
        console.LinesAdded += (_, _) => added++;

        for (var i = 0; i < ServerConsoleViewModel.MaxLines + 100; i++)
        {
            console.AddLines($"Zeile {i}");
        }

        Assert.Equal(ServerConsoleViewModel.MaxLines, console.Lines.Count);
        Assert.Equal("Zeile 100", console.Lines[0]);
        Assert.Equal(ServerConsoleViewModel.MaxLines + 100, added);

        console.CopyLines(["Zeile 200", "Zeile 201"]);
        Assert.Equal("Zeile 200\nZeile 201", t.Clipboard.Text);
        Assert.Equal(t.Services.Format("Linux_ConsoleCopied", 2), control.Feedback.Message);

        console.CopyAllCommand.Execute(null);
        Assert.StartsWith("Zeile 100\nZeile 101\n", t.Clipboard.Text, StringComparison.Ordinal);

        t.Clipboard.Locked = true;
        console.CopyAllCommand.Execute(null);
        Assert.True(control.Feedback.IsError);

        console.ClearLinesCommand.Execute(null);
        Assert.True(console.HasNoLines);
    }

    [AvaloniaFact]
    public async Task Rcon_shows_newest_output_first_and_keeps_its_own_history()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var rcon = control.Rcon;

        rcon.Command = "status";
        await rcon.SendCommand.ExecuteAsync(null);
        rcon.Command = "listbans";
        await rcon.SendCommand.ExecuteAsync(null);

        Assert.StartsWith("> listbans\nUnknown command\n\n> status\nmap: supply", rcon.Output, StringComparison.Ordinal);
        Assert.Equal("listbans", rcon.PreviousCommand());
        Assert.Equal("status", rcon.PreviousCommand());

        t.Api.Failures["/v1/servers/et-sniper/rcon"] = (System.Net.HttpStatusCode.Conflict, FakeAccountRoutes.Problem(409, "rcon_not_configured"));
        rcon.Command = "status";
        await rcon.SendCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.Localizer.GetError("rcon_not_configured"), control.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Bans_load_on_first_open_ban_a_guest_and_lift_with_confirmation()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var bans = control.Bans;

        control.SelectedTab = ServerControlTab.Bans;
        await ViewModelTests.WaitUntil(() => bans.ShowContent);
        Assert.True(control.IsBansTab && !control.IsConsoleTab);
        Assert.True(control.Tabs.Single(x => x.Tab == ServerControlTab.Bans).IsSelected);
        Assert.Equal(["Gr1efer", "aimbot_andi"], bans.Items.Select(b => b.Player));
        Assert.Contains("Teamkills auf Supply", bans.Items[0].Meta, StringComparison.Ordinal);
        Assert.Contains(t.Services.T(StringKeys.BanPermanent), bans.Items[1].Meta, StringComparison.Ordinal);

        await bans.BanCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.BanPlayerMissing), control.Feedback.Message);

        bans.PlayerName = "Camper_Carl";
        bans.Minutes = 0;
        bans.Reason = "Spawnkill";
        await bans.BanCommand.ExecuteAsync(null);
        var write = Assert.Single(t.Api.Writes, w => w is { Method: "POST", Path: "/v1/servers/et-sniper/bans" });
        Assert.Contains("\"player_name\":\"Camper_Carl\"", write.Body, StringComparison.Ordinal);
        Assert.Contains("\"minutes\":0", write.Body, StringComparison.Ordinal);
        Assert.Equal(t.Services.T(StringKeys.BanDone), control.Feedback.Message);
        Assert.Equal(string.Empty, bans.PlayerName);

        var lift = bans.Items[0].LiftCommand.ExecuteAsync(null);
        Assert.Equal("Gr1efer", control.Confirm.Text);
        control.Confirm.AcceptCommand.Execute(null);
        await lift;
        Assert.Contains(t.Api.Writes, w => w is { Method: "DELETE", Path: "/v1/servers/et-sniper/bans/01BAN1" });
        Assert.Equal(["aimbot_andi"], bans.Items.Select(b => b.Player));

        // Reiterwechsel bricht eine offene Rückfrage ab
        var pending = bans.Items[0].LiftCommand.ExecuteAsync(null);
        control.SelectedTab = ServerControlTab.Console;
        await pending;
        Assert.Single(bans.Items);
    }

    [AvaloniaFact]
    public async Task Whitelist_adds_players_and_removes_members_with_confirmation()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var whitelist = control.Whitelist;

        control.Tabs.Single(x => x.Tab == ServerControlTab.Whitelist).IsSelected = true;
        await ViewModelTests.WaitUntil(() => whitelist.ShowContent);
        Assert.Equal(["Kaito (Kaito2020)", "Nyx (Nyx_MC)", "Steve_vom_Nachbarn"], whitelist.Items.Select(w => w.Display));
        Assert.True(whitelist.Items[0].IsMember);

        whitelist.PlayerName = " Notch_Fan ";
        await whitelist.AddCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/servers/et-sniper/whitelist" && w.Body.Contains("\"action\":\"add\"", StringComparison.Ordinal)
            && w.Body.Contains("\"player_name\":\"Notch_Fan\"", StringComparison.Ordinal));
        Assert.Equal(t.Services.T(StringKeys.WhitelistAdded), control.Feedback.Message);

        var remove = whitelist.Items[1].RemoveCommand.ExecuteAsync(null);
        control.Confirm.AcceptCommand.Execute(null);
        await remove;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/servers/et-sniper/whitelist" && w.Body.Contains("\"action\":\"remove\"", StringComparison.Ordinal)
            && w.Body.Contains("\"user_id\":\"01U2\"", StringComparison.Ordinal));
        Assert.Equal(2, whitelist.Items.Count);
    }

    [AvaloniaFact]
    public async Task Approvals_show_the_diff_and_approve_with_note_after_confirmation()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        var approvals = control.Approvals;

        control.SelectedTab = ServerControlTab.Approvals;
        await ViewModelTests.WaitUntil(() => approvals.ShowContent);
        var change = Assert.Single(approvals.Items);
        Assert.Equal("etmain/server.cfg", change.Title);
        Assert.Contains("Kaito", change.Meta, StringComparison.Ordinal);
        Assert.Contains("\u2212 set g_maxlives 0", change.Diff, StringComparison.Ordinal);
        Assert.Contains("+ set g_maxlives 1", change.Diff, StringComparison.Ordinal);

        change.Note = "Passt für Freitag";
        var approve = change.ApproveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T(StringKeys.ApprovalApproveTitle), control.Confirm.Title);
        control.Confirm.AcceptCommand.Execute(null);
        await approve;

        var write = Assert.Single(t.Api.Writes, w => w.Path == "/v1/servers/et-sniper/config-changes/01CHG1/approve");
        Assert.Equal("Passt für Freitag", System.Text.Json.JsonDocument.Parse(write.Body).RootElement.GetProperty("note").GetString());
        Assert.Equal(t.Services.T(StringKeys.ApprovalApproved), control.Feedback.Message);
        Assert.True(approvals.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Moderation_list_errors_show_the_error_state()
    {
        using var t = await SignedInAsync();
        t.Api.Failures["/v1/servers/et-sniper/bans"] = (System.Net.HttpStatusCode.Conflict, FakeAccountRoutes.Problem(409, "ban_not_supported"));
        await using var control = Create(t, new FakeConsoleSockets());

        control.SelectedTab = ServerControlTab.Bans;
        await ViewModelTests.WaitUntil(() => control.Bans.HasError);
        Assert.Equal(t.Services.Localizer.GetError("ban_not_supported"), control.Bans.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Hooked_tabs_are_wired_by_rights_and_load_lazily()
    {
        using var t = await SignedInAsync();
        await using var control = Create(t, new FakeConsoleSockets());
        Assert.IsType<ServerConfigsViewModel>(control.ConfigsTab);
        Assert.IsType<ServerFilesViewModel>(control.FilesTab);
        Assert.IsType<ServerBackupsViewModel>(control.BackupsTab);
        Assert.False(control.ConfigsPending);

        var tab = new LazyTab();
        control.ConfigsTab = tab;
        Assert.False(control.ConfigsPending);
        control.SelectedTab = ServerControlTab.Configs;
        Assert.Equal(1, tab.Loads);
        Assert.Equal("et-sniper", control.Slug);
        Assert.True(control.Abilities!.FilesWrite);
    }

    private sealed class LazyTab : ILazyTab
    {
        public int Loads { get; private set; }

        public Task EnsureLoadedAsync()
        {
            Loads++;
            return Task.CompletedTask;
        }
    }
}
