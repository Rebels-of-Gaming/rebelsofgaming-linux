using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RoG.Core.Features.Admin;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Stufe 9 „J“: Verwaltung (Übersicht, Website, Mod-Log, ohne Rechte) im Hauptfenster und das Editor-Fenster.</summary>
[Trait("Category", "Screenshots")]
public class AdminScreenshotTests
{
    private static async Task<TestServices> As(string me)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = me;
        return t;
    }

    private static async Task<AdminViewModel> OpenAdmin(ShellViewModel shell, AdminTab tab)
    {
        Assert.True(shell.NavigateTo("Nav_Admin"));
        var admin = Assert.IsType<AdminViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => admin.HasAccess || admin.NoAccess);
        if (admin.HasAccess)
        {
            admin.SelectedTab = tab;
            switch (admin.SelectedContent)
            {
                case LoadableViewModel loadable:
                    await ViewModelTests.WaitUntil(() => loadable.ShowContent || loadable.HasError);
                    break;
            }
        }

        await ScreenshotTests.Settle(400);
        return admin;
    }

    [AvaloniaFact]
    public async Task Overview_1280() => await ScreenshotTests.Shoot("admin_overview_1280x800", 1280, 800, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Overview));

    [AvaloniaFact]
    public async Task Overview_narrow() => await ScreenshotTests.Shoot("admin_overview_narrow_480x900", 480, 900, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Overview));

    [AvaloniaFact]
    public async Task Overview_audit_1280() => await ScreenshotTests.Shoot("admin_overview_audit_1280x1300", 1280, 1300, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Overview));

    [AvaloniaFact]
    public async Task Website_1280() => await ScreenshotTests.Shoot("admin_website_1280x800", 1280, 800, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Website));

    [AvaloniaFact]
    public async Task Website_narrow() => await ScreenshotTests.Shoot("admin_website_narrow_480x900", 480, 900, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Website));

    [AvaloniaFact]
    public async Task Pending_tab_1280() => await ScreenshotTests.Shoot("admin_pending_1280x800", 1280, 800, await As(FakeAdminRoutes.MeAdmin),
        shell => OpenAdmin(shell, AdminTab.Members));

    [AvaloniaFact]
    public async Task Mod_log_1280() => await ScreenshotTests.Shoot("admin_modlog_1280x800", 1280, 800, await As(FakeAdminRoutes.MeModerator),
        shell => OpenAdmin(shell, AdminTab.Log));

    [AvaloniaFact]
    public async Task No_access_1280() => await ScreenshotTests.Shoot("admin_noaccess_1280x800", 1280, 800, await As(FakeAdminRoutes.MeAdminWithoutScopes),
        shell => OpenAdmin(shell, AdminTab.Overview));

    /// <summary>Editor-Fenster: Liste + Formular (bzw. schmal nur eins davon).</summary>
    private static async Task ShootEditor(string name, int width, int height, string areaKey, Func<AdminEditorViewModel, Task>? arrange = null)
    {
        using var t = await AdminTests.SignedInAsync(FakeAdminRoutes.MeAdmin);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var window = AdminEditorWindow.OpenFor(t.Services, AdminAreas.Find(areaKey)!);
        window.Width = width;
        window.Height = height;
        var vm = (AdminEditorViewModel)window.DataContext!;
        try
        {
            await ViewModelTests.WaitUntil(() => vm.ShowRecords || vm.HasForm);
            if (arrange is not null)
            {
                await arrange(vm);
            }

            await ScreenshotTests.Settle(500);
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

        Assert.False(AdminEditorWindow.OpenWindows.ContainsKey(areaKey));
    }

    private static async Task OpenFirst(AdminEditorViewModel vm)
    {
        vm.Selected = vm.Records[0];
        await ViewModelTests.WaitUntil(() => vm.HasForm);
    }

    [AvaloniaFact]
    public Task Editor_1040() => ShootEditor("admin_editor_1040x720", 1040, 720, "games", OpenFirst);

    [AvaloniaFact]
    public Task Editor_list_narrow() => ShootEditor("admin_editor_list_480x800", 480, 800, "games");

    [AvaloniaFact]
    public Task Editor_form_narrow() => ShootEditor("admin_editor_form_480x900", 480, 900, "games", OpenFirst);

    [AvaloniaFact]
    public Task Editor_delete_prompt() => ShootEditor("admin_editor_delete_1040x720", 1040, 720, "games", async vm =>
    {
        await OpenFirst(vm);
        _ = vm.DeleteCommand.ExecuteAsync(null);
    });

    [AvaloniaFact]
    public Task Editor_settings() => ShootEditor("admin_editor_settings_1040x720", 1040, 720, "settings-clan");
}
