using Avalonia.Headless.XUnit;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>Wiki: Seite, schmal, Bearbeiten, Konflikt, Versionsvergleich, neue Seite, Gast → <c>artifacts/screenshots/</c>.</summary>
[Trait("Category", "Screenshots")]
public class WikiScreenshotTests
{
    [AvaloniaFact]
    public async Task Page() =>
        await ScreenshotTests.Shoot("wiki_page_1280x800", 1280, 800, await MemberAsync(), shell => OpenRulesAsync(shell));

    [AvaloniaFact]
    public async Task List_narrow() =>
        await ScreenshotTests.Shoot("wiki_list_narrow_480x900", 480, 900, await MemberAsync(), async shell =>
        {
            shell.NavigateTo("Nav_Wiki");
            var wiki = Assert.IsType<WikiViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => wiki.ShowContent);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Page_narrow() =>
        await ScreenshotTests.Shoot("wiki_page_narrow_480x900", 480, 900, await MemberAsync(), shell => OpenRulesAsync(shell));

    [AvaloniaFact]
    public async Task Edit_with_preview() =>
        await ScreenshotTests.Shoot("wiki_edit_1280x800", 1280, 800, await MemberAsync(), async shell =>
        {
            var wiki = await OpenRulesAsync(shell);
            await wiki.EditCommand.ExecuteAsync(null);
            wiki.EditSummary = "Fairplay ergänzt";
            await wiki.PreviewCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Edit_conflict() =>
        await ScreenshotTests.Shoot("wiki_conflict_1280x800", 1280, 800, await MemberAsync(), async shell =>
        {
            var wiki = await OpenRulesAsync(shell);
            await wiki.EditCommand.ExecuteAsync(null);
            wiki.EditContent = FakeWikiRoutes.CurrentContent + "\nKein Spam im Chat.";
            wiki.EditSummary = FakeWikiRoutes.ConflictSummary;
            await wiki.SaveCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Revisions_diff() =>
        await ScreenshotTests.Shoot("wiki_diff_1280x800", 1280, 800, await MemberAsync(), async shell =>
        {
            var wiki = await OpenRulesAsync(shell);
            wiki.IsRevisionsExpanded = true;
            await ViewModelTests.WaitUntil(() => wiki.Revisions.Count == 3);
            await wiki.Revisions[2].CompareCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task Revisions_narrow() =>
        await ScreenshotTests.Shoot("wiki_revisions_narrow_480x900", 480, 900, await MemberAsync(), async shell =>
        {
            var wiki = await OpenRulesAsync(shell);
            wiki.IsRevisionsExpanded = true;
            await ViewModelTests.WaitUntil(() => wiki.Revisions.Count == 3);
            await wiki.Revisions[2].CompareCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task New_page() =>
        await ScreenshotTests.Shoot("wiki_new_page_1280x800", 1280, 800, await MemberAsync(), async shell =>
        {
            shell.NavigateTo("Nav_Wiki");
            var wiki = Assert.IsType<WikiViewModel>(shell.CurrentPage);
            await ViewModelTests.WaitUntil(() => wiki.ShowContent);
            wiki.StartCreateCommand.Execute(null);
            wiki.NewTitle = "Clan-Abend";
            wiki.NewContent = "== Ablauf ==\nJeden Freitag um 20 Uhr im Discord.";
            await ScreenshotTests.Settle(300);
        });

    [AvaloniaFact]
    public async Task Guest() =>
        await ScreenshotTests.Shoot("wiki_guest_1280x800", 1280, 800, await TestServices.CreateAsync(guest: true), shell => OpenRulesAsync(shell));

    private static async Task<TestServices> MemberAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeWikiRoutes.MeWithWikiRights;
        return t;
    }

    private static async Task<WikiViewModel> OpenRulesAsync(ShellViewModel shell)
    {
        shell.NavigateTo("Nav_Wiki");
        var wiki = Assert.IsType<WikiViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => wiki.ShowContent);
        wiki.Selected = wiki.Items[0];
        await ViewModelTests.WaitUntil(() => wiki.ShowPage);
        await ScreenshotTests.Settle(400);
        return wiki;
    }
}
