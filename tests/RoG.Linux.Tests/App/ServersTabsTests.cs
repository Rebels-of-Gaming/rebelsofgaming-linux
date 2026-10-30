using Avalonia.Headless.XUnit;
using RoG.Core.Navigation;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 3: Server-Reiter Minecraft, TeamSpeak und Discord-Voice.</summary>
public class ServersTabsTests
{
    [AvaloniaFact]
    public async Task TeamSpeak_shows_people_under_spacers_and_connects_with_the_rog_name()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var signIn = 0;
        var servers = new ServersViewModel(t.Services, () => signIn++);
        servers.IsTeamSpeakTab = true;
        await ViewModelTests.WaitUntil(() => servers.Voice.ShowContent);
        var voice = servers.Voice;

        Assert.Equal("Rebels of Gaming", voice.TeamSpeakTitle);
        Assert.Equal("5 von 32 online", voice.TeamSpeakStatus);
        Assert.True(voice.IsTeamSpeakOnline);

        // Nur Kanäle mit Personen; Trenner sind Überschriften, ihre Unterkanäle bleiben sichtbar
        var rows = voice.TeamSpeakRows.ToList();
        Assert.Equal(["Lobby", "Wahke", "Nyx | AFK", "RoG ET", "Training", "Kaito", "Gast_42", "RoG CS2", "Lobby", "Sera"], rows.Select(r => r.Text));
        Assert.True(rows[3].IsSection);
        Assert.True(rows[4].IsChannel);
        Assert.True(rows[2].IsAway);
        Assert.Equal("Nyx", rows[2].Detail);
        Assert.Contains("abwesend", rows[2].AutomationName, StringComparison.Ordinal);
        Assert.True(rows[5].IsMuted);
        Assert.Contains("stumm", rows[5].AutomationName, StringComparison.Ordinal);
        Assert.Null(rows[1].Detail); // Spitzname = RoG-Name
        Assert.True(rows[5].Indent.Left > rows[4].Indent.Left);

        Assert.False(voice.ShowTeamSpeakEmpty);

        // Leere Kanäle zeigen
        voice.ShowEmptyChannels = true;
        Assert.Contains(voice.TeamSpeakRows, r => r.Text == "Clanwar");
        Assert.Contains(voice.TeamSpeakRows, r => r.Text == "Taktik");
        Assert.Contains(voice.TeamSpeakRows, r => r.Text == "AFK");

        // Verbinden: ohne TeamSpeak → Adresse kopieren
        await voice.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("ts.example.test", t.Clipboard.Text);
        Assert.True(voice.Feedback.IsInfo);

        // Mit TeamSpeak → ts3server:// mit Spitzname
        t.Launcher.Installed.Add("ts3server");
        await voice.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("ts3server://ts.example.test/?nickname=Wahke", Assert.Single(t.Launcher.Launched).ToString());
        Assert.True(voice.Feedback.IsSuccess);

        // Discord-Voice (angemeldet)
        servers.IsDiscordTab = true;
        Assert.True(servers.IsDiscordTab);
        Assert.False(servers.IsTeamSpeakTab);
        Assert.True(voice.ShowDiscordList);
        Assert.Equal(["Gaming 1", "Wahke", "Kaito", "Squad-Leitung", "Sera"], voice.DiscordRows.Select(r => r.Text));
        Assert.True(voice.DiscordRows[3].IsLocked);
        await ViewModelTests.WaitUntil(() => voice.DiscordRows[1].Avatar is not null);
        Assert.Equal(0, signIn);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Discord_voice_asks_guests_to_sign_in_and_teamspeak_still_loads()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var signIn = 0;
        var servers = new ServersViewModel(t.Services, () => signIn++);
        servers.Open("discord");
        Assert.True(servers.IsDiscordTab);
        await ViewModelTests.WaitUntil(() => servers.Voice.ShowContent);

        Assert.True(servers.Voice.DiscordNeedsLogin);
        Assert.False(servers.Voice.ShowDiscordEmpty);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/discord/voice", StringComparison.Ordinal));
        Assert.True(servers.Voice.HasTeamSpeakRows);
        servers.Voice.SignInCommand.Execute(null);
        Assert.Equal(1, signIn);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task TeamSpeak_offline_is_not_an_error()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.FailWith = System.Net.HttpStatusCode.ServiceUnavailable;
        var servers = new ServersViewModel(t.Services, () => { });
        servers.IsTeamSpeakTab = true;
        await ViewModelTests.WaitUntil(() => !servers.Voice.IsLoading && servers.Voice.ShowContent);
        Assert.False(servers.Voice.IsTeamSpeakOnline);
        Assert.False(servers.Voice.CanConnect);
        Assert.False(servers.Voice.ShowTeamSpeakEmpty); // kein TeamSpeak-Stand → kein „niemand da“
        Assert.Equal(t.Services.T("Voice_TsOffline"), servers.Voice.TeamSpeakStatus);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Empty_teamspeak_shows_the_empty_state_until_empty_channels_are_shown()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        t.Api.Overrides["/v1/teamspeak/viewer"] = """
            {"data":{"online":true,"server":{"name":"RoG","clients_online":0,"max_clients":32,"address":"ts.example.test"},"channels":[
              {"id":1,"name":"Lobby","spacer":false,"default":true,"clients":[],"children":[]}]}}
            """;
        var servers = new ServersViewModel(t.Services, () => { });
        servers.IsTeamSpeakTab = true;
        await ViewModelTests.WaitUntil(() => servers.Voice.ShowContent);
        Assert.True(servers.Voice.ShowTeamSpeakEmpty);
        servers.Voice.ShowEmptyChannels = true;
        Assert.False(servers.Voice.ShowTeamSpeakEmpty);
        Assert.Equal("Lobby", Assert.Single(servers.Voice.TeamSpeakRows).Text);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Minecraft_tab_shows_join_data_copies_and_opens_pack_and_links()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var servers = new ServersViewModel(t.Services, () => { });
        servers.Open(AppRoute.MinecraftKey);
        Assert.True(servers.IsMinecraftTab);
        var join = servers.Join;
        await ViewModelTests.WaitUntil(() => join.ShowContent && join.HasPack);

        Assert.Equal("mc.example.test", join.JavaAddress);
        Assert.True(join.ShowDefaultPortNote);
        Assert.Equal("19132", join.BedrockPort);
        Assert.True(join.ShowConsole);
        Assert.True(join.HasHint);
        Assert.Equal("RoG6328", join.Gamertag);
        Assert.False(join.IsSignedIn);
        Assert.StartsWith("Paket: 7", join.PackInfo, StringComparison.Ordinal);

        join.CopyBedrockPortCommand.Execute(null);
        Assert.Equal("19132", t.Clipboard.Text);
        Assert.True(servers.Feedback.IsSuccess);
        join.CopyGamertagCommand.Execute(null);
        Assert.Equal("RoG6328", t.Clipboard.Text);

        await join.DownloadPackCommand.ExecuteAsync(null);
        Assert.Equal("https://cdn.example.test/minecraft/pack-3f2a.zip", t.Browser.Opened[^1].ToString());

        // Weltkarte und Bestenlisten: Website-Seite, solange die App sie nicht nativ hat
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        join.OpenMapCommand.Execute(null);
        Assert.EndsWith("/de/minecraft/karte", t.Browser.Opened[^1].ToString(), StringComparison.Ordinal);
        servers.Pause();
    }

    [AvaloniaFact]
    public async Task Deep_links_open_the_server_tabs()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://servers/minecraft")));
        var servers = Assert.IsType<ServersViewModel>(shell.CurrentPage);
        Assert.True(servers.IsMinecraftTab);
        Assert.True(shell.OpenDeepLink(new Uri("rog://servers/teamspeak")));
        Assert.True(servers.IsTeamSpeakTab);
        Assert.True(shell.OpenDeepLink(new Uri("rog://servers/et-sniper")));
        Assert.True(servers.IsGameTab);

        // Website-Adresse /minecraft → Reiter Minecraft
        Assert.NotNull(t.Services.RouteFor(new Uri("https://rebelsofgaming.com/de/minecraft")));
        servers.Pause();
    }
}
