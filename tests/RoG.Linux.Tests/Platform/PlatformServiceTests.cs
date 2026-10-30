using RoG.Linux.App;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

public class PlatformServiceTests
{
    [Theory]
    [InlineData("wahke", false, "Linux-App – PC von wahke")]
    [InlineData("deck", true, "Linux-App – Steam Deck von deck")]
    [InlineData("", false, "Linux-App")]
    [InlineData("a\u0007b", false, "Linux-App – PC von ab")]
    public void Device_name(string owner, bool deck, string expected) => Assert.Equal(expected, LinuxDeviceName.For(owner, deck));

    [Fact]
    public void Device_name_is_capped() => Assert.Equal(80, LinuxDeviceName.For(new string('x', 200)).Length);

    [Theory]
    [InlineData("1", null, true)]
    [InlineData(null, "Jupiter\n", true)]
    [InlineData(null, "Galileo", true)]
    [InlineData(null, "B550 AORUS", false)]
    [InlineData(null, null, false)]
    public void Steam_deck_detection(string? env, string? board, bool expected) =>
        Assert.Equal(expected, LinuxDeviceName.IsSteamDeck(n => n == "SteamDeck" ? env : null, _ => board));

    [Fact]
    public void Xdg_paths_follow_the_specification()
    {
        var env = new Dictionary<string, string?> { ["HOME"] = "/home/rebel", ["XDG_CONFIG_HOME"] = "/cfg", ["XDG_CACHE_HOME"] = "relativ/ungueltig" };
        var paths = new LinuxPaths(n => env.GetValueOrDefault(n));
        Assert.Equal("/cfg/rebelsofgaming", paths.ConfigDirectory);
        Assert.Equal("/home/rebel/.local/share/rebelsofgaming", paths.DataDirectory);
        Assert.Equal("/home/rebel/.cache/rebelsofgaming", paths.CacheDirectory);
        Assert.Equal("/cfg/rebelsofgaming/settings.json", paths.SettingsFile);
    }

    [Fact]
    public async Task Browser_opens_only_web_addresses_without_shell()
    {
        var processes = new RecordingProcessLauncher();
        var launcher = new XdgLauncher(processes);

        Assert.True(await launcher.OpenAsync(new Uri("https://rebelsofgaming.com/de/news?a=1&b=2")));
        Assert.False(await launcher.OpenAsync(new Uri("file:///etc/passwd")));
        Assert.False(await launcher.OpenAsync(new Uri("javascript:alert(1)")));

        var (file, args) = Assert.Single(processes.Calls);
        Assert.Equal("xdg-open", file);
        Assert.Equal(["https://rebelsofgaming.com/de/news?a=1&b=2"], args);
    }

    [Fact]
    public async Task Game_links_are_launched_but_unknown_schemes_are_not()
    {
        var processes = new RecordingProcessLauncher();
        var launcher = new XdgLauncher(processes);
        Assert.True(await launcher.CanLaunchAsync(new Uri("steam://connect/1.2.3.4:27015")));
        Assert.True(await launcher.LaunchAsync(new Uri("et://1.2.3.4:27960")));
        Assert.False(await launcher.LaunchAsync(new Uri("ssh://host")));
        Assert.Single(processes.Calls);
    }

    [Fact]
    public void Launch_options_accept_known_environments_and_deep_links()
    {
        var options = LaunchOptions.Parse(["--environment=staging", "rog://news/abc"], _ => null);
        Assert.Equal("staging", options.Environment);
        Assert.Equal("rog://news/abc", options.DeepLink?.ToString());

        Assert.Null(LaunchOptions.Parse(["--environment=evil"], _ => null).Environment);
        Assert.Equal("local", LaunchOptions.Parse([], n => n == "ROG_ENVIRONMENT" ? "local" : null).Environment);
    }
}
