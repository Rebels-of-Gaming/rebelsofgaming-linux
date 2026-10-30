using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoG.Core.Features.News;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 9 „N – Inhalte“: News-Redaktion und Match-Editor (eigene Fenster, 1280×800 und schmal), Reiter Galerie und
/// Uploads (in einem Test-Fenster über das DataTemplate aus <c>App.axaml</c>), Einstiege auf News- und Matches-Seite.
/// </summary>
[Trait("Category", "Screenshots")]
public class ContentAdminScreenshotTests
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

    private static async Task ScrollTo(Window window, double y)
    {
        await ScreenshotTests.Settle(200);
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Scroller");
        scroller.Offset = new Avalonia.Vector(0, y);
    }

    private static async Task ShootNews(string name, int width, int height, bool open, Func<NewsEditorWindow, Task>? arrange = null)
    {
        using var t = await ContentAdminTests.SignedInAsync();
        t.Api.Overrides["/v1/news/" + FakeContentAdminRoutes.DraftSlug] = FakeContentAdminRoutes.DraftDetail;
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var window = NewsEditorWindow.Open(t.Services, NewsRights.For(t.Services.CurrentUser), open ? FakeContentAdminRoutes.DraftSlug : null);
        window.Width = width;
        window.Height = height;
        var editor = window.Editor!;
        try
        {
            await ViewModelTests.WaitUntil(() => editor.Drafts.Count == 3 && (!open || editor.Id is not null));
            if (open)
            {
                editor.SelectedDraft = editor.Drafts.First(d => d.Slug == FakeContentAdminRoutes.DraftSlug);
                await ViewModelTests.WaitUntil(() => editor.Social.Targets.Count > 0);
            }

            if (arrange is not null)
            {
                await arrange(window);
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
    public Task News_editor_new() => ShootNews("news_editor_new_1280x800", 1280, 800, open: false);

    [AvaloniaFact]
    public Task News_editor_draft() => ShootNews("news_editor_draft_1280x800", 1280, 800, open: true);

    [AvaloniaFact]
    public Task News_editor_preview() => ShootNews("news_editor_preview_1280x800", 1280, 800, open: true, async w =>
    {
        w.Editor!.IsPreviewOpen = true;
        await ScrollTo(w, 420);
    });

    [AvaloniaFact]
    public Task News_editor_settings_and_social() => ShootNews("news_editor_social_1280x800", 1280, 800, open: true, async w =>
    {
        w.Editor!.SelectedStatus = w.Editor.StatusOptions[2];
        await ScrollTo(w, 2000);
    });

    [AvaloniaFact]
    public Task News_editor_narrow() => ShootNews("news_editor_narrow_480x900", 480, 900, open: true);

    [AvaloniaFact]
    public Task News_editor_narrow_drafts() => ShootNews("news_editor_drafts_narrow_480x900", 480, 900, open: false, w =>
    {
        w.Editor!.ShowDraftsCommand.Execute(null);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task News_editor_narrow_bottom() => ShootNews("news_editor_bottom_narrow_480x900", 480, 900, open: true, async w =>
    {
        _ = w.Editor!.DeleteCommand.ExecuteAsync(null);
        await ScreenshotTests.Settle(200);
        await ScrollTo(w, 3000);
    });

    private static async Task ShootMatch(string name, int width, int height, string? matchId, Func<MatchEditorWindow, Task>? arrange = null)
    {
        using var t = await ContentAdminTests.SignedInAsync();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        var window = MatchEditorWindow.Open(t.Services, matchId);
        window.Width = width;
        window.Height = height;
        var editor = window.Editor!;
        try
        {
            await ViewModelTests.WaitUntil(() => editor.Players.Count == 4 && (matchId is null || editor.Players[0].Selected));
            if (matchId is not null)
            {
                await ViewModelTests.WaitUntil(() => editor.Social.StateText is not null);
            }

            if (arrange is not null)
            {
                await arrange(window);
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
    public Task Match_editor_new() => ShootMatch("match_editor_new_1280x800", 1280, 800, null, w => w.Editor!.SaveCommand.ExecuteAsync(null));

    [AvaloniaFact]
    public Task Match_editor_edit() => ShootMatch("match_editor_edit_1280x800", 1280, 800, FakeContentAdminRoutes.MatchId);

    [AvaloniaFact]
    public Task Match_editor_edit_bottom() => ShootMatch("match_editor_bottom_1280x800", 1280, 800, FakeContentAdminRoutes.MatchId, w => ScrollTo(w, 2000));

    [AvaloniaFact]
    public Task Match_editor_narrow() => ShootMatch("match_editor_narrow_480x900", 480, 900, FakeContentAdminRoutes.MatchId);

    [AvaloniaFact]
    public Task Match_editor_narrow_lineup() => ShootMatch("match_editor_lineup_narrow_480x900", 480, 900, FakeContentAdminRoutes.MatchId, w => ScrollTo(w, 1500));

    /// <summary>Reiter im Test-Fenster über das DataTemplate aus <c>App.axaml</c> (wie der ContentControl der Verwaltungsseite).</summary>
    private static async Task ShootTab(string name, int width, int height, Func<TestServices, MediaAdminTabViewModel> create, Func<MediaAdminTabViewModel, Task>? arrange = null)
    {
        using var t = await ContentAdminTests.SignedInAsync();
        t.Api.Overrides["/v1/gallery/albums/clanabend-2026"] = FakeContentAdminRoutes.AlbumWithPending;
        t.Api.Overrides["/v1/gallery/albums/scrims"] = FakeContentAdminRoutes.AlbumNothingPending;
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
            await vm.EnsureLoadedAsync();
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
    public Task Gallery_tab() => ShootTab("admin_gallery_1280x800", 1280, 800, t => new GalleryAdminViewModel(t.Services));

    [AvaloniaFact]
    public Task Gallery_tab_narrow() => ShootTab("admin_gallery_narrow_480x900", 480, 900, t => new GalleryAdminViewModel(t.Services), vm =>
    {
        _ = ((GalleryAdminViewModel)vm).Pending[0].RejectCommand.ExecuteAsync(null);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task Gallery_tab_empty() => ShootTab("admin_gallery_empty_1280x800", 1280, 800, t =>
    {
        t.Api.Overrides["/v1/gallery/albums/clanabend-2026"] = FakeContentAdminRoutes.AlbumNothingPending;
        return new GalleryAdminViewModel(t.Services);
    });

    [AvaloniaFact]
    public Task Uploads_tab() => ShootTab("admin_uploads_1280x800", 1280, 800, t => new UploadReportsAdminViewModel(t.Services), async vm =>
        await ((UploadReportsAdminViewModel)vm).Reports[1].KeepCommand.ExecuteAsync(null));

    [AvaloniaFact]
    public Task Uploads_tab_narrow() => ShootTab("admin_uploads_narrow_480x900", 480, 900, t => new UploadReportsAdminViewModel(t.Services));

    private static async Task<TestServices> Editor()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeContentAdminRoutes.MeEditor;
        return t;
    }

    [AvaloniaFact]
    public async Task News_page_with_write_and_edit() =>
        await ScreenshotTests.Shoot("news_editor_entry_1280x800", 1280, 800, await Editor(), async shell =>
        {
            shell.NavigateTo("Nav_News");
            var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => news.ShowContent);
            news.Selected = news.Items[0];
            await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task Matches_page_with_add_and_edit() =>
        await ScreenshotTests.Shoot("matches_editor_entry_1280x800", 1280, 800, await Editor(), async shell =>
        {
            shell.NavigateTo("Nav_Matches");
            var matches = Assert.IsType<MatchesViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => matches.ShowContent);
            matches.Selected = matches.Items[0];
            await ViewModelTests.WaitUntil(() => matches.Detail.ShowContent);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task Album_with_social_card() =>
        await ScreenshotTests.Shoot("media_album_social_1280x800", 1280, 800, await Editor(), async shell =>
        {
            shell.NavigateTo("Nav_Media");
            var media = Assert.IsType<MediaViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => media.ShowContent);
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await ViewModelTests.WaitUntil(() => media.Social.StateText is not null && media.Items.Count > 0);
            await ScreenshotTests.Settle(400);
        });
}
