using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 10 – sichtbare Änderungen aus Barrierefreiheit/Kontrast/Bewegung: Fokusrahmen nach Tab, rote Schrift (RoGRedText)
/// und Primär-Knöpfe in beiden Designs, Galerie-Großansicht im hellen Design, ruhende Ladeleiste bei „Bewegung reduzieren“.
/// </summary>
[Trait("Category", "Screenshots")]
public class AccessibilityScreenshotTests
{
    private static readonly string s_folder = Path.Combine(LocalizationTests.RepoRoot(), "artifacts", "screenshots");

    private static async Task Shoot(string name, int width, int height, TestServices t, Func<ShellViewModel, Window, Task> arrange, bool light = false)
    {
        using (t)
        {
            var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
            window.Width = width;
            window.Height = height;
            window.Show();
            var shell = (ShellViewModel)window.DataContext!;
            await shell.InitializeAsync();
            if (light)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            }

            await ScreenshotTests.Settle(200);
            try
            {
                await arrange(shell, window);
                Directory.CreateDirectory(s_folder);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(s_folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            finally
            {
                window.Close();
                MotionService.Apply(false);
                Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            }
        }
    }

    private static void Tab(Window window, int times)
    {
        for (var i = 0; i < times; i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
        }
    }

    private static async Task OpenNews(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_News");
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.ShowContent);
        news.Selected = news.Items[0];
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
        await ScreenshotTests.Settle(400);
    }

    /// <summary>Tastatur: Fokusrahmen (Off-White) auf einem Eintrag der Seitenleiste.</summary>
    [AvaloniaFact]
    public async Task Focus_frame_sidebar() =>
        await Shoot("a11y_focus_sidebar_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async (shell, window) =>
        {
            shell.NavigateTo("Nav_Settings");
            await ScreenshotTests.Settle(300);
            Tab(window, 3);
            await ScreenshotTests.Settle(100);
        });

    /// <summary>Tastatur: Fokusrahmen im Inhalt (Einstellungen, helles Design).</summary>
    [AvaloniaFact]
    public async Task Focus_frame_content_light() =>
        await Shoot("a11y_focus_content_light_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async (shell, window) =>
        {
            shell.NavigateTo("Nav_Settings");
            await ScreenshotTests.Settle(300);
            var page = window.GetVisualDescendants().OfType<RoG.Linux.App.Views.SettingsView>().First();
            var first = page.GetVisualDescendants().OfType<InputElement>().First(e => e.Focusable && e.IsEffectivelyVisible && e is ComboBox or Button);
            first.Focus(NavigationMethod.Tab);
            await ScreenshotTests.Settle(100);
        }, light: true);

    /// <summary>Kontrast: Kicker/Links in RoGRedText, Primär-Knopf mit weißer Schrift – Dunkel und Hell.</summary>
    [AvaloniaFact]
    public async Task Red_text_dark() =>
        await Shoot("a11y_news_red_text_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), (shell, _) => OpenNews(shell));

    [AvaloniaFact]
    public async Task Red_text_light() =>
        await Shoot("a11y_news_red_text_light_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), (shell, _) => OpenNews(shell), light: true);

    [AvaloniaFact]
    public async Task Login_primary_button() =>
        await Shoot("a11y_login_primary_1280x800", 1280, 800, await TestServices.CreateAsync(), (_, _) => ScreenshotTests.Settle(100));

    /// <summary>Galerie-Großansicht bleibt im hellen Design dunkel (feste Medienfarben statt Einweg-Farben in der Ansicht).</summary>
    [AvaloniaFact]
    public async Task Lightbox_light() =>
        await Shoot("a11y_media_viewer_light_1280x800", 1280, 800, await TestServices.CreateAsync(TestTokens.Wahke), async (shell, _) =>
        {
            shell.NavigateTo("Nav_Media");
            var media = Assert.IsType<MediaViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => media.ShowContent);
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await ViewModelTests.WaitUntil(() => media.Items.Count == 3 && media.Items.All(i => i.Thumb is not null || i.IsVideo));
            media.ViewCommand.Execute(media.Items[0]);
            await ViewModelTests.WaitUntil(() => media.Viewed?.Full is not null);
            await ScreenshotTests.Settle(400);
        }, light: true);

    [AvaloniaFact]
    public async Task Album_light() =>
        await Shoot("a11y_media_album_light_480x900", 480, 900, await TestServices.CreateAsync(TestTokens.Wahke), async (shell, _) =>
        {
            shell.NavigateTo("Nav_Media");
            var media = Assert.IsType<MediaViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => media.ShowContent);
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await ViewModelTests.WaitUntil(() => media.Items.Count == 3 && media.Items.All(i => i.Thumb is not null || i.IsVideo));
            await ScreenshotTests.Settle(400);
        }, light: true);

    /// <summary>„Bewegung reduzieren“: die Ladeleiste der Anmeldung steht als halb deckende Leiste still.</summary>
    [AvaloniaFact]
    public async Task Reduced_motion_busy_bar() =>
        await Shoot("a11y_reduced_motion_login_480x800", 480, 800, await TestServices.CreateAsync(), async (shell, window) =>
        {
            MotionService.Apply(true);
            var bar = new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Avalonia.Thickness(0, 12) };
            var panel = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.IsEffectivelyVisible && p.Children.OfType<Button>().Any());
            panel.Children.Add(bar);
            await ScreenshotTests.Settle(300);
        });
}
