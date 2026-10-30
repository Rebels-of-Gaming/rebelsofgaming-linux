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
/// Stufe 9 „K“: Reiter Mitglieder und Bewerbungen (im Test-Fenster, wie die Verwaltungsseite sie einhängt),
/// Squad-Verwaltungsfenster und der Einstieg „Verwalten“ auf der Squad-Seite.
/// </summary>
[Trait("Category", "Screenshots")]
public class PeopleAdminScreenshotTests
{
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

    /// <summary>Reiter im Test-Fenster über das DataTemplate aus <c>App.axaml</c> (wie der ContentControl der Verwaltungsseite).</summary>
    private static async Task ShootTab<T>(string name, int width, int height, Func<TestServices, T> create, Func<T, Task> arrange, bool scrollDown = false)
        where T : class
    {
        using var t = await PeopleAdminTests.SignedInAsync(FakePeopleAdminRoutes.MeLeader);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var vm = create(t);
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
            await ScreenshotTests.Settle(200);
            await arrange(vm);
            if (scrollDown)
            {
                await ScreenshotTests.Settle(300);
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible).MaxBy(s => s.Extent.Height - s.Viewport.Height)!;
                scroller.Offset = new Avalonia.Vector(0, 520);
            }

            await Save(window, name, width);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    private static async Task OpenMember(MembersAdminViewModel vm)
    {
        await ViewModelTests.WaitUntil(() => vm.Items.Count > 0);
        vm.Selected = vm.Items[0];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
        vm.SelectedRole = vm.RoleOptions.FirstOrDefault(o => o.Value == "rk-eventteam");
    }

    private static async Task OpenApplication(ApplicationsAdminViewModel vm)
    {
        await ViewModelTests.WaitUntil(() => vm.Items.Count > 0);
        vm.Selected = vm.Items[0];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
    }

    [AvaloniaFact]
    public Task Members_tab() => ShootTab("admin_members_1280x800", 1280, 800, PeopleAdminTests.Members, OpenMember);

    [AvaloniaFact]
    public Task Members_tab_list_narrow() => ShootTab("admin_members_list_480x800", 480, 800, PeopleAdminTests.Members,
        vm => ViewModelTests.WaitUntil(() => vm.Items.Count > 0));

    [AvaloniaFact]
    public Task Members_tab_detail_narrow() => ShootTab("admin_members_detail_480x800", 480, 800, PeopleAdminTests.Members, async vm =>
    {
        await OpenMember(vm);
        _ = vm.Roles[1].RevokeCommand.ExecuteAsync(null); // Rückfrage offen
    });

    [AvaloniaFact]
    public Task Members_tab_refused() => ShootTab("admin_members_refused_1280x800", 1280, 800, PeopleAdminTests.Members, async vm =>
    {
        await ViewModelTests.WaitUntil(() => vm.Items.Count > 0);
        vm.Selected = vm.Items.First(i => i.Slug == "blizzard");
        await ViewModelTests.WaitUntil(() => vm.ShowDetail && vm.Header.StartsWith("Blizzard", StringComparison.Ordinal));
        vm.SelectedRole = vm.RoleOptions.First(o => o.Value == "rk-admin");
        await vm.AssignCommand.ExecuteAsync(null);
    });

    [AvaloniaFact]
    public Task Applications_tab() => ShootTab("admin_applications_1280x800", 1280, 800, PeopleAdminTests.Applications, OpenApplication);

    [AvaloniaFact]
    public Task Applications_tab_lower() => ShootTab("admin_applications_lower_1280x800", 1280, 800, PeopleAdminTests.Applications, OpenApplication, scrollDown: true);

    [AvaloniaFact]
    public Task Applications_tab_decision() => ShootTab("admin_applications_decision_1280x800", 1280, 800, PeopleAdminTests.Applications, async vm =>
    {
        await OpenApplication(vm);
        vm.SelectedDecision = vm.DecisionOptions.First(o => o.Value == "extend");
        vm.DecideCommand.Execute(null);
        vm.DecisionMessage = "Noch ein Training, dann entscheiden wir.";
    });

    [AvaloniaFact]
    public Task Applications_tab_vote_narrow() => ShootTab("admin_applications_vote_480x900", 480, 900, PeopleAdminTests.Applications, async vm =>
    {
        await ViewModelTests.WaitUntil(() => vm.Items.Count > 1);
        vm.Selected = vm.Items[1];
        await ViewModelTests.WaitUntil(() => vm.ShowDetail);
    });

    [AvaloniaFact]
    public Task Applications_tab_empty() => ShootTab("admin_applications_empty_1280x800", 1280, 800, PeopleAdminTests.Applications, async vm =>
    {
        await ViewModelTests.WaitUntil(() => vm.Items.Count > 0);
        vm.SelectedStatus = vm.StatusOptions.First(o => o.Value == "rejected");
        await ViewModelTests.WaitUntil(() => vm.IsEmpty);
    });

    private static async Task ShootSquadAdmin(string name, int width, int height, string me, Func<SquadAdminViewModel, Task>? arrange = null)
    {
        using var t = await PeopleAdminTests.SignedInAsync(me);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var admin = await PeopleAdminTests.OpenSquadAdminAsync(t);
        var window = new SquadAdminWindow(admin) { Width = width, Height = height };
        window.Show();
        try
        {
            await ScreenshotTests.Settle(200);
            if (arrange is not null)
            {
                await arrange(admin);
            }

            await Save(window, name, width);
        }
        finally
        {
            window.Close();
            main.Close();
        }
    }

    private static async Task Lineup(SquadAdminViewModel admin)
    {
        admin.IsMembersTab = true;
        await ViewModelTests.WaitUntil(() => admin.Lineup.Count > 0);
        admin.Lineup.First(m => m.Slug == "nyx").SelectedRole = admin.Roles.First(r => r.Value == "r3");
        admin.MemberSearch = "sa";
        await ViewModelTests.WaitUntil(() => admin.HasSuggestions);
    }

    [AvaloniaFact]
    public Task Squad_admin_profile() => ShootSquadAdmin("squad_admin_profile_860x680", 860, 680, FakePeopleAdminRoutes.MeLeader);

    [AvaloniaFact]
    public Task Squad_admin_lineup() => ShootSquadAdmin("squad_admin_lineup_860x680", 860, 680, FakePeopleAdminRoutes.MeLeader, Lineup);

    [AvaloniaFact]
    public Task Squad_admin_lineup_narrow() => ShootSquadAdmin("squad_admin_lineup_480x800", 480, 800, FakePeopleAdminRoutes.MeLeader, Lineup);

    [AvaloniaFact]
    public Task Squad_admin_squad_leader() => ShootSquadAdmin("squad_admin_leader_860x680", 860, 680, FakePeopleAdminRoutes.MeSquadLeader,
        admin => ViewModelTests.WaitUntil(() => admin.Lineup.Count > 0));

    [AvaloniaFact]
    public Task Squad_admin_archive() => ShootSquadAdmin("squad_admin_archive_860x680", 860, 680, FakePeopleAdminRoutes.MeLeader, admin =>
    {
        admin.IsArchiveTab = true;
        _ = admin.ToggleArchiveCommand.ExecuteAsync(null);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public async Task Squad_page_with_manage() => await ScreenshotTests.Shoot("squads_manage_1280x800", 1280, 800, await PeopleAdminTests.SignedInAsync(FakePeopleAdminRoutes.MeLeader), async shell =>
    {
        shell.NavigateTo("Nav_Squads");
        var squads = Assert.IsType<SquadsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => squads.ShowContent);
        squads.Selected = squads.Items.First(i => i.Slug == FakePeopleAdminRoutes.SquadSlug);
        await ViewModelTests.WaitUntil(() => squads.Detail.ShowContent && squads.Detail.CanManage);
        await ScreenshotTests.Settle(400);
    });
}
