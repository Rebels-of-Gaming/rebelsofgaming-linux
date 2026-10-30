using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 5: Galerie (Alben, Großansicht, Einreichen) und Downloads (Prüfsumme, Ordner).</summary>
public class MediaTests
{
    [AvaloniaFact]
    public async Task Albums_items_viewer_and_videos()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var media = new MediaViewModel(t.Services);
        await media.LoadAsync();
        Assert.Equal(2, media.Albums.Count);
        Assert.Contains("3", media.Albums[0].Meta, StringComparison.Ordinal);
        Assert.Contains("Squad CS2", media.Albums[1].Meta, StringComparison.Ordinal);

        media.OpenAlbumCommand.Execute(media.Albums[0]);
        await ViewModelTests.WaitUntil(() => !media.IsLoadingItems && media.Items.Count > 0);
        Assert.Equal(3, media.Items.Count); // „pending“ wird nicht gezeigt
        Assert.True(media.CanUpload);
        Assert.True(media.Items[1].IsVideo);

        // Großansicht mit Blättern (Video übersprungen)
        media.ViewCommand.Execute(media.Items[0]);
        Assert.True(media.IsViewerOpen);
        await ViewModelTests.WaitUntil(() => media.Viewed?.Full is not null);
        media.Step(1);
        Assert.Same(media.Items[2], media.Viewed);
        media.Step(1);
        Assert.Same(media.Items[0], media.Viewed);
        ((IDetailPage)media).CloseDetail();
        Assert.False(media.IsViewerOpen);

        // Video → Browser
        media.ViewCommand.Execute(media.Items[1]);
        await ViewModelTests.WaitUntil(() => t.Browser.Opened.Count > 0);
        Assert.Equal("www.youtube-nocookie.com", t.Browser.Opened[^1].Host);
        Assert.False(media.IsViewerOpen);

        // Leeres Album
        media.OpenAlbumCommand.Execute(media.Albums[1]);
        await ViewModelTests.WaitUntil(() => media.ShowItemsEmpty);
    }

    [AvaloniaFact]
    public async Task Submitting_needs_confirmed_rights_and_goes_to_the_album()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var media = new MediaViewModel(t.Services);
        await media.LoadAsync();
        media.OpenAlbumCommand.Execute(media.Albums[0]);
        await ViewModelTests.WaitUntil(() => media.CanUpload);
        media.ToggleSubmitCommand.Execute(null);
        Assert.True(media.IsSubmitOpen);
        var file = new UploadSource("foto.jpg", 4096, () => Task.FromResult<Stream>(new MemoryStream(new byte[4096])));

        await media.SubmitAsync([file]);
        Assert.Empty(t.Storage.Puts);
        Assert.True(media.Feedback.IsInfo);

        media.RightsConfirmed = true;
        media.Caption = "Clanabend";
        await media.SubmitAsync([file]);
        Assert.Single(t.Storage.Puts);
        var submit = t.Api.Writes.Single(w => w.Path == "/v1/gallery/albums/clanabend-2026/items");
        Assert.Contains("\"rights_confirmed\":true", submit.Body, StringComparison.Ordinal);
        Assert.Contains("Clanabend", submit.Body, StringComparison.Ordinal);
        Assert.Equal(t.Services.Format("Gallery_SubmittedPending", 1), media.Feedback.Message);
        Assert.False(media.IsSubmitOpen);
    }

    [AvaloniaFact]
    public async Task Downloads_are_saved_with_checksum_and_shown_in_the_folder()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var media = new MediaViewModel(t.Services);
        media.IsDownloadsTab = true;
        var downloads = media.Downloads;
        await ViewModelTests.WaitUntil(() => downloads.ShowContent);
        Assert.Equal(2, downloads.Items.Count);
        Assert.Contains("v2.3", downloads.Items[0].Meta, StringComparison.Ordinal);
        Assert.Contains("128", downloads.Items[0].Meta, StringComparison.Ordinal);
        Assert.True(downloads.Items[0].HasChangelog);
        Assert.False(downloads.Items[1].CanDownload); // noch in Verarbeitung

        await downloads.Items[0].DownloadCommand.ExecuteAsync(null);
        var path = downloads.Items[0].SavedPath;
        Assert.True(path is not null, downloads.Feedback.Message);
        Assert.Equal("rog-et.cfg", Path.GetFileName(path)); // „../“ aus dem Namen entfernt
        Assert.StartsWith(Path.Combine(t.Folder, "Downloads"), path, StringComparison.Ordinal);
        Assert.Equal(FakeData.DownloadBytes, await File.ReadAllBytesAsync(path));
        Assert.True(downloads.Feedback.IsSuccess);

        await downloads.Items[0].ShowInFolderCommand.ExecuteAsync(null);
        Assert.Equal(path, Assert.Single(t.Files.Shown));

        // Prüfsumme stimmt nicht → Datei wird gelöscht, Fehler
        t.Storage.GetContent = "manipuliert"u8.ToArray();
        await downloads.Items[0].DownloadCommand.ExecuteAsync(null);
        Assert.True(downloads.Feedback.IsError);
        Assert.Single(Directory.GetFiles(Path.Combine(t.Folder, "Downloads")));
    }
}
