using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 7 (Teil F): Reiter Configs, Dateien und Backups – gehostet in einem Test-Fenster (das Steuer-Fenster baut
/// Teil E), Steam Deck 1280×800 und schmal 480 px.
/// </summary>
[Trait("Category", "Screenshots")]
public class ServerFilesScreenshotTests
{
    // Aus dem Quellpfad statt LocalizationTests.RepoRoot(): bin/obj können per Symlink außerhalb des Klons liegen
    private static readonly string s_folder = Path.Combine(RepoRoot(), "artifacts", "screenshots");

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".."));

    private static async Task ShootTab(string name, int width, int height, Func<TestServices, ServerDataTabViewModel> create, Func<ServerDataTabViewModel, Task> arrange)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme wie in der App
        var tab = create(t);
        var title = new TextBlock { Text = "RoG ET PUBLIC", Classes = { "title" }, Margin = new Avalonia.Thickness(16, 16, 16, 4) };
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(new ContentControl { Content = tab, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch });
        var window = new Window { Width = width, Height = height, Content = root };
        window.Show();
        try
        {
            await WaitUntil(() => !tab.IsLoading && (tab.ShowContent || tab.IsEmpty || tab.HasError));
            await ScreenshotTests.Settle(200);
            await arrange(tab);
            await ScreenshotTests.Settle(400);

            Directory.CreateDirectory(s_folder);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width);
            frame.Save(Path.Combine(s_folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            AssertNoHorizontalOverflow(window);
        }
        finally
        {
            window.Close();
            main.Close();
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }
    }

    /// <summary>Kein Text ragt rechts aus dem Fenster (schmal: kein horizontales Scrollen der Seite).</summary>
    private static void AssertNoHorizontalOverflow(Window window)
    {
        foreach (var control in window.GetVisualDescendantsOfType<Button>())
        {
            if (control.IsEffectivelyVisible && Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width, 0), window) is { } right)
            {
                Assert.True(right.X <= window.Width + 1, $"Knopf ragt aus dem Fenster: {control.Content}");
            }
        }
    }

    private static ServerConfigsViewModel Configs(TestServices t) => new(t.Services, FakeServerFilesRoutes.Slug, ServerFilesTests.Full);

    private static async Task EditAndReview(ServerDataTabViewModel tab)
    {
        var configs = (ServerConfigsViewModel)tab;
        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/server.cfg");
        await WaitUntil(() => configs.Content == FakeServerFilesRoutes.ServerCfg && configs.Versions.Count == 3 && !configs.IsFileLoading);
        configs.Content = configs.Content
            .Replace("sv_maxclients 24", "sv_maxclients 32", StringComparison.Ordinal)
            .Replace("set g_warmup 15", "set g_warmup 10", StringComparison.Ordinal);
        configs.Note = "Clanwar-Abend";
        _ = configs.SaveCommand.ExecuteAsync(null);
        await WaitUntil(() => configs.Confirm.IsOpen);
    }

    [AvaloniaFact]
    public Task Configs_diff_steam_deck() => ShootTab("server_configs_1280x800", 1280, 800, Configs, EditAndReview);

    [AvaloniaFact]
    public Task Configs_diff_narrow() => ShootTab("server_configs_narrow_480x900", 480, 900, Configs, EditAndReview);

    [AvaloniaFact]
    public Task Configs_template() => ShootTab("server_configs_template_1280x800", 1280, 800, Configs, async tab =>
    {
        var configs = (ServerConfigsViewModel)tab;
        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/server.cfg");
        await WaitUntil(() => configs.Content == FakeServerFilesRoutes.ServerCfg && !configs.IsFileLoading);
        await configs.OpenTemplateCommand.ExecuteAsync(null);
        configs.Template!.Fields[2].Text = "99";
        await configs.Template.ApplyCommand.ExecuteAsync(null);
    });

    [AvaloniaFact]
    public Task Configs_read_only_light() => ShootTab("server_configs_readonly_light_1280x800", 1280, 800,
        t =>
        {
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            return new ServerConfigsViewModel(t.Services, FakeServerFilesRoutes.Slug, ServerFilesTests.ReadOnly);
        },
        async tab =>
        {
            var configs = (ServerConfigsViewModel)tab;
            await WaitUntil(() => configs.Versions.Count == 3 && !configs.IsFileLoading);
            await ScreenshotTests.Settle(100);
        });

    private static ServerFilesViewModel Files(TestServices t) => new(t.Services, FakeServerFilesRoutes.Slug, ServerFilesTests.Full);

    private static async Task EtmainWithPreview(ServerDataTabViewModel tab)
    {
        var files = (ServerFilesViewModel)tab;
        await files.OpenAsync("etmain");
        await files.Items.Single(i => i.Name == "server.cfg").OpenCommand.ExecuteAsync(null);
        _ = files.Items.Single(i => i.Name == "server.cfg").CopyCommand.ExecuteAsync(null);
        await WaitUntil(() => files.Prompt.IsOpen);
    }

    [AvaloniaFact]
    public Task Files_steam_deck() => ShootTab("server_files_1280x800", 1280, 800, Files, EtmainWithPreview);

    [AvaloniaFact]
    public Task Files_narrow() => ShootTab("server_files_narrow_480x800", 480, 800, Files, async tab =>
    {
        var files = (ServerFilesViewModel)tab;
        await files.OpenAsync("etmain");
    });

    [AvaloniaFact]
    public Task Files_narrow_preview() => ShootTab("server_files_preview_narrow_480x800", 480, 800, Files, async tab =>
    {
        var files = (ServerFilesViewModel)tab;
        await files.OpenAsync("etmain");
        await files.Items.Single(i => i.Name == "mapcycle.cfg").OpenCommand.ExecuteAsync(null);
    });

    private static ServerBackupsViewModel Backups(TestServices t) => new(t.Services, FakeServerFilesRoutes.Slug, ServerFilesTests.Full);

    private static async Task AskRestore(ServerDataTabViewModel tab)
    {
        var backups = (ServerBackupsViewModel)tab;
        _ = backups.Items[1].RestoreCommand.ExecuteAsync(null);
        await WaitUntil(() => backups.Confirm.IsOpen);
    }

    [AvaloniaFact]
    public Task Backups_steam_deck() => ShootTab("server_backups_1280x800", 1280, 800, Backups, AskRestore);

    [AvaloniaFact]
    public Task Backups_narrow() => ShootTab("server_backups_narrow_480x900", 480, 900, Backups, _ => Task.CompletedTask);
}

file static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOfType<T>(this Avalonia.Visual root)
        where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
