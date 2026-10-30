using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 6: Freunde (breit, schmal, Gast, Bestätigung) und Mitgliederkarte mit Fake-Kacheln und Clustern.</summary>
[Trait("Category", "Screenshots")]
public class FriendsMapScreenshotTests
{
    private static async Task<TestServices> WithFriendScopes()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FriendsTests.MeWithFriends;
        return t;
    }

    private static async Task OpenFriends(ShellViewModel shell, bool confirm)
    {
        shell.NavigateTo("Nav_Friends");
        var friends = Assert.IsType<FriendsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => friends.ShowList);
        if (confirm)
        {
            friends.Friends.First(f => f.Name == "Kaito").RemoveCommand.Execute(null);
        }

        await ScreenshotTests.Settle(400);
    }

    [AvaloniaFact]
    public async Task Friends() => await ScreenshotTests.Shoot("friends_1280x800", 1280, 800, await WithFriendScopes(), shell => OpenFriends(shell, false));

    [AvaloniaFact]
    public async Task Friends_narrow() => await ScreenshotTests.Shoot("friends_narrow_480x900", 480, 900, await WithFriendScopes(), shell => OpenFriends(shell, false));

    [AvaloniaFact]
    public async Task Friends_confirm_remove() => await ScreenshotTests.Shoot("friends_confirm_1280x800", 1280, 800, await WithFriendScopes(), shell => OpenFriends(shell, true));

    [AvaloniaFact]
    public async Task Friends_guest() =>
        await ScreenshotTests.Shoot("friends_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), async shell =>
        {
            shell.NavigateTo("Nav_Friends");
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Members_with_map_button_and_befriend() =>
        await ScreenshotTests.Shoot("members_befriend_1280x800", 1280, 800, await WithFriendScopes(), async shell =>
        {
            shell.OpenDeepLink(new Uri("rog://u/nyx"));
            var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => members.ShowContent && members.Detail.ShowContent);
            await members.Detail.BefriendCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public Task Member_map() => ShootMap("member_map_1280x800", 1280, 800, panel: false);

    [AvaloniaFact]
    public Task Member_map_names() => ShootMap("member_map_names_1280x800", 1280, 800, panel: true);

    [AvaloniaFact]
    public Task Member_map_narrow() => ShootMap("member_map_narrow_480x800", 480, 800, panel: true);

    private static async Task ShootMap(string name, int width, int height, bool panel)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme wie in der App
        MemberMapWindow.Current?.Close();
        var map = MemberMapTests.Create(t, new FakeTiles());
        var window = MemberMapWindow.Open(map);
        try
        {
            await ShootWindow(window, map, name, width, height, panel);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    private static async Task ShootWindow(MemberMapWindow window, MemberMapViewModel map, string name, int width, int height, bool panel)
    {
        window.Width = width;
        window.Height = height;
        await ViewModelTests.WaitUntil(() => map.ShowContent);
        await ScreenshotTests.Settle(600);
        if (panel)
        {
            var biggest = window.MapView.Markers.OrderByDescending(m => m.Cluster.Count).First();
            map.ShowCluster(biggest.Cluster);
        }

        await ScreenshotTests.Settle(600);
        var folder = Path.Combine(LocalizationTests.RepoRoot(), "artifacts", "screenshots");
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(width, frame.PixelSize.Width);
        frame.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
