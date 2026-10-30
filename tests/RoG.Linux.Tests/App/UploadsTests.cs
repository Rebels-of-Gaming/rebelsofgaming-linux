using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 5: Uploads (vorab signiert, Link kopieren, nicht unterstützte Dateien) und Kurzlinks.</summary>
public class UploadsTests
{
    private static UploadSource Png(string name, int size = 2048) =>
        new(name, size, () => Task.FromResult<Stream>(new MemoryStream(new byte[size])));

    [AvaloniaFact]
    public async Task Upload_goes_to_the_presigned_url_without_token_and_copies_the_link()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var uploads = new UploadsViewModel(t.Services, () => { });
        await uploads.LoadAsync();
        Assert.Equal(3, uploads.Items.Count);
        Assert.True(uploads.Items[0].HasLinks);
        Assert.Contains(t.Services.T("Uploads_Processing"), uploads.Items[1].Meta, StringComparison.Ordinal);
        Assert.True(uploads.Items[2].IsFailed);
        Assert.False(uploads.Items[2].HasLinks);
        await ViewModelTests.WaitUntil(() => uploads.Items[0].HasThumb); // öffentlich → Vorschaubild
        Assert.False(uploads.Items[1].HasThumb); // nur Mitglieder → kein Vorschaubild

        uploads.SelectedVisibility = uploads.Visibilities[2];
        await ViewModelTests.WaitUntil(() => t.Services.Settings.UploadVisibility == "private");

        await uploads.UploadFilesAsync([Png("screenshot.png"), Png("notizen.txt")]);
        var put = Assert.Single(t.Storage.Puts);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("s3.example.test", put.Url.Host);
        Assert.Equal(2048, put.Length);
        Assert.False(put.HasAuthorization); // Token nie an den Speicher
        var presign = t.Api.Writes.Single(w => w.Path == "/v1/uploads/presign");
        Assert.Contains("\"visibility\":\"private\"", presign.Body, StringComparison.Ordinal);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/uploads/01UPNEW/complete");

        Assert.Equal(4, uploads.Items.Count);
        Assert.Equal("screenshot.png", uploads.Items[0].Title);
        Assert.Equal("https://i.example.test/01UPNEW.webp", t.Clipboard.Text);
        Assert.True(uploads.Feedback.IsInfo); // eine Datei übersprungen
        Assert.Equal(t.Services.T("Uploads_DoneCopied"), uploads.Feedback.Message);

        uploads.Items[0].CopyBbCodeCommand.Execute(null);
        Assert.StartsWith("[img]", t.Clipboard.Text, StringComparison.Ordinal);
        await uploads.Items[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal("https://i.example.test/01UPNEW", t.Browser.Opened[^1].ToString());
    }

    [AvaloniaFact]
    public async Task Only_unsupported_files_show_a_hint_and_storage_errors_are_reported()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var uploads = new UploadsViewModel(t.Services, () => { });
        await uploads.LoadAsync();
        await uploads.UploadFilesAsync([Png("liste.pdf")]);
        Assert.Empty(t.Storage.Puts);
        Assert.Equal(t.Services.T("Uploads_Unsupported"), uploads.Feedback.Message);

        t.Storage.Status = System.Net.HttpStatusCode.Forbidden;
        await uploads.UploadFilesAsync([Png("bild.png")]);
        Assert.True(uploads.Feedback.IsError);
        Assert.Equal(3, uploads.Items.Count);
        Assert.False(uploads.IsUploading);
    }

    [AvaloniaFact]
    public async Task Guests_see_the_sign_in_hint()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var signIn = 0;
        var uploads = new UploadsViewModel(t.Services, () => signIn++);
        await uploads.LoadAsync();
        Assert.True(uploads.IsGuest);
        Assert.False(uploads.ShowUploader);
        Assert.False(uploads.IsEmptyForMember);
        await uploads.UploadFilesAsync([Png("bild.png")]);
        Assert.Empty(t.Storage.Puts);
        uploads.SignInCommand.Execute(null);
        Assert.Equal(1, signIn);
    }

    [AvaloniaFact]
    public async Task Short_links_are_listed_created_and_copied()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var uploads = new UploadsViewModel(t.Services, () => { });
        uploads.IsLinksTab = true;
        var links = uploads.Links;
        await ViewModelTests.WaitUntil(() => links.ShowContent);
        Assert.Equal(2, links.Items.Count);
        Assert.Equal("https://rogclan.de/scrim", links.Items[0].ShortUrl);
        Assert.Contains("42", links.Items[0].Meta, StringComparison.Ordinal);
        Assert.True(links.Items[1].IsBlocked);

        Assert.False(links.CreateCommand.CanExecute(null));
        links.Target = "ftp://nope";
        Assert.False(links.CreateCommand.CanExecute(null));
        links.Target = "https://rebelsofgaming.com/de/servers/teamspeak";
        links.Code = "ts";
        await links.CreateCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/links" && w.Body.Contains("\"code\":\"ts\"", StringComparison.Ordinal));
        Assert.Equal(3, links.Items.Count);
        Assert.Equal("https://rogclan.de/ts", t.Clipboard.Text);
        Assert.Equal(t.Services.T("Links_CreatedCopied"), uploads.Feedback.Message);
        Assert.Equal(string.Empty, links.Target);

        links.Items[1].CopyCommand.Execute(null);
        Assert.Equal("https://rogclan.de/scrim", t.Clipboard.Text);
    }
}
