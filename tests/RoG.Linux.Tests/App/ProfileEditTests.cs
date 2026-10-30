using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 5: Profil bearbeiten (Texte, Ort, Einwilligungen, Avatar/Banner mit Größengrenze).</summary>
public class ProfileEditTests
{
    [AvaloniaFact]
    public async Task Profile_loads_on_expand_saves_and_replaces_images()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var account = new AccountViewModel(t.Services, () => Task.CompletedTask, () => { });
        var editor = account.Editor;
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/me/profile");
        Assert.False(editor.SaveCommand.CanExecute(null));

        editor.IsExpanded = true;
        await ViewModelTests.WaitUntil(() => editor.ShowContent);
        Assert.Equal("Trier", editor.City);
        Assert.True(editor.ShowOnMap);
        Assert.False(editor.HallOfFameConsent);

        editor.Motto = "Objective first";
        editor.HallOfFameConsent = true;
        await editor.SaveCommand.ExecuteAsync(null);
        var patch = t.Api.Writes.Single(w => w.Method == "PATCH" && w.Path == "/v1/me/profile");
        Assert.Contains("\"motto\":\"Objective first\"", patch.Body, StringComparison.Ordinal);
        Assert.Contains("\"hall_of_fame_consent\":true", patch.Body, StringComparison.Ordinal);
        Assert.True(editor.Feedback.IsSuccess);

        // Avatar: zu groß → nicht senden; passend → senden und Profil neu laden
        await editor.SetImageAsync(new UploadSource("riesig.png", 3 * 1024 * 1024, () => Task.FromResult<Stream>(new MemoryStream())), banner: false);
        Assert.True(editor.Feedback.IsError);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/avatar");

        await editor.SetImageAsync(new UploadSource("ich.png", 1024, () => Task.FromResult<Stream>(new MemoryStream(new byte[1024]))), banner: false);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/me/avatar");
        Assert.True(editor.Feedback.IsSuccess);
        Assert.NotNull(t.Services.CurrentUser);

        await editor.SetImageAsync(new UploadSource("banner.txt", 10, () => Task.FromResult<Stream>(new MemoryStream())), banner: true);
        Assert.True(editor.Feedback.IsInfo);
    }
}
