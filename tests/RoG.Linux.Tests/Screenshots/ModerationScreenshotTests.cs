using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using RoG.Linux.App.Controls;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.App;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.Screenshots;

/// <summary>
/// Stufe 9 (Teil L): Moderations-Reiter in einem Test-Fenster (die Verwaltungsseite baut Teil J) – Steam Deck 1280×800
/// und schmal 480 px – sowie „Melden“ an Forum, Profil, Galerie und Direktnachricht im Hauptfenster.
/// </summary>
[Trait("Category", "Screenshots")]
public class ModerationScreenshotTests
{
    // Aus dem Quellpfad statt LocalizationTests.RepoRoot(): bin/obj können per Symlink außerhalb des Klons liegen
    private static readonly string s_folder = Path.Combine(RepoRoot(), "artifacts", "screenshots");

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".."));

    /// <summary>Reiter wie in der Verwaltung: Titel, Rückmeldung, Rückfrage, Inhalt des Reiters.</summary>
    private static async Task ShootTab(string name, int width, int height, Func<TestServices, FeedbackViewModel, InlineConfirmViewModel, ModerationTabViewModel> create,
        Func<ModerationTabViewModel, Task>? arrange = null, string me = FakeModerationRoutes.MeModerator)
    {
        using var t = await ModerationTests.AsAsync(me);
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services); // Übersetzungen/Theme wie in der App
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var tab = create(t, feedback, confirm);
        var header = new StackPanel { Margin = new Avalonia.Thickness(16, 16, 16, 4), Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "VERWALTUNG", Classes = { "display" } });
        header.Children.Add(new FeedbackBar { DataContext = feedback });
        header.Children.Add(new ConfirmBar { DataContext = confirm });
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ContentControl { Content = tab, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch });
        var window = new Window { Width = width, Height = height, Content = root };
        window.Show();
        try
        {
            await WaitUntil(() => !tab.IsLoading && (tab.ShowContent || tab.IsEmpty || tab.HasError));
            await ScreenshotTests.Settle(200);
            if (arrange is not null)
            {
                await arrange(tab);
            }

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

    /// <summary>Kein Knopf ragt rechts aus dem Fenster (schmal: kein horizontales Scrollen).</summary>
    private static void AssertNoHorizontalOverflow(Window window)
    {
        foreach (var control in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>())
        {
            if (control.IsEffectivelyVisible && Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width, 0), window) is { } right)
            {
                Assert.True(right.X <= window.Width + 1, $"Knopf ragt aus dem Fenster: {control.Content}");
            }
        }
    }

    private static ReportsAdminViewModel Reports(TestServices t, FeedbackViewModel f, InlineConfirmViewModel c) => new(t.Services, f, c);

    private static async Task OpenMember(ModerationTabViewModel tab)
    {
        var reports = (ReportsAdminViewModel)tab;
        await reports.Items[0].ModerateCommand.ExecuteAsync(null);
        reports.Member.Reason = "Unangemessene DMs an Minderjährige";
    }

    [AvaloniaFact]
    public Task Reports_steam_deck() => ShootTab("moderation_reports_1280x800", 1280, 800, Reports);

    [AvaloniaFact]
    public Task Reports_narrow() => ShootTab("moderation_reports_narrow_480x900", 480, 900, Reports);

    [AvaloniaFact]
    public Task Reports_member_steam_deck() => ShootTab("moderation_member_1280x800", 1280, 800, Reports, OpenMember);

    [AvaloniaFact]
    public Task Reports_member_narrow() => ShootTab("moderation_member_narrow_480x900", 480, 900, Reports, OpenMember);

    [AvaloniaFact]
    public Task Reports_read_only() => ShootTab("moderation_reports_readonly_1280x800", 1280, 800, Reports, me: FakeModerationRoutes.MeReader);

    [AvaloniaFact]
    public Task Bans_lift_confirm() => ShootTab("moderation_bans_confirm_1280x800", 1280, 800, (t, f, c) => new ModBansAdminViewModel(t.Services, f, c), async tab =>
    {
        _ = ((ModBansAdminViewModel)tab).Items[0].LiftCommand.ExecuteAsync(null);
        await WaitUntil(() => tab.Confirmation.IsOpen);
    });

    [AvaloniaFact]
    public Task Bans_narrow() => ShootTab("moderation_bans_narrow_480x900", 480, 900, (t, f, c) => new ModBansAdminViewModel(t.Services, f, c));

    private static async Task OpenDecision(ModerationTabViewModel tab)
    {
        var appeals = (BanAppealsViewModel)tab;
        _ = appeals.Items[0].RejectCommand.ExecuteAsync(null);
        await WaitUntil(() => appeals.Decision.IsOpen);
        appeals.Decision.Note = "Demo zeigt eindeutig Wallhack in Runde 3.";
    }

    [AvaloniaFact]
    public Task Appeals_steam_deck() => ShootTab("moderation_appeals_1280x800", 1280, 800, (t, f, c) => new BanAppealsViewModel(t.Services, f, c), async tab =>
    {
        var appeals = (BanAppealsViewModel)tab;
        appeals.SelectedStatus = appeals.StatusOptions.Single(o => o.Value == "all");
        await WaitUntil(() => !appeals.IsLoading && appeals.Items.Count == 2);
    });

    [AvaloniaFact]
    public Task Appeals_decision_narrow() => ShootTab("moderation_appeals_decision_narrow_480x900", 480, 900, (t, f, c) => new BanAppealsViewModel(t.Services, f, c), OpenDecision);

    [AvaloniaFact]
    public Task Words_steam_deck() => ShootTab("moderation_words_1280x800", 1280, 800, (t, f, c) => new WordFilterViewModel(t.Services, f, c));

    [AvaloniaFact]
    public Task Words_narrow() => ShootTab("moderation_words_narrow_480x900", 480, 900, (t, f, c) => new WordFilterViewModel(t.Services, f, c));

    // --- Melden im Hauptfenster -------------------------------------------------------------------------------------

    private static async Task<TestServices> Member()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeModerationRoutes.MeMember;
        return t;
    }

    private static async Task ReportForumPost(ShellViewModel shell)
    {
        Assert.True(shell.OpenDeepLink(new Uri("rog://forum/allgemein")));
        var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
        await WaitUntil(() => forum.Threads.Count > 0);
        forum.SelectedThread = forum.Threads[0];
        await WaitUntil(() => forum.Thread.ShowContent);
        forum.Thread.Posts[1].ReportCommand.Execute(null);
        forum.Thread.Report.SelectedReason = forum.Thread.Report.Reasons.Single(r => r.Value == "insult");
        forum.Thread.Report.Comment = "Beleidigt andere im Scrim-Thread.";
        await ScreenshotTests.Settle(400);
    }

    [AvaloniaFact]
    public async Task Report_forum_steam_deck() => await ScreenshotTests.Shoot("report_forum_1280x800", 1280, 800, await Member(), ReportForumPost);

    [AvaloniaFact]
    public async Task Report_forum_narrow() => await ScreenshotTests.Shoot("report_forum_narrow_480x900", 480, 900, await Member(), ReportForumPost);

    [AvaloniaFact]
    public async Task Report_profile_sent_narrow() =>
        await ScreenshotTests.Shoot("report_profile_sent_narrow_480x900", 480, 900, await Member(), async shell =>
        {
            shell.NavigateTo("Nav_Members");
            var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
            await WaitUntil(() => members.ShowContent);
            members.Selected = members.Items[1];
            await WaitUntil(() => members.Detail.ShowContent);
            await ScreenshotTests.Settle(300);
            members.Detail.ReportProfileCommand.Execute(null);
            await ScreenshotTests.Settle(300);
            members.Detail.Report.SelectedReason = members.Detail.Report.Reasons[2];
            await members.Detail.Report.SendCommand.ExecuteAsync(null);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task Report_gallery_steam_deck() =>
        await ScreenshotTests.Shoot("report_gallery_1280x800", 1280, 800, await Member(), async shell =>
        {
            shell.NavigateTo("Nav_Media");
            var media = Assert.IsType<MediaViewModel>(shell.CurrentPage);
            await WaitUntil(() => media.ShowContent);
            media.OpenAlbumCommand.Execute(media.Albums[0]);
            await WaitUntil(() => media.Items.Count == 3);
            media.ViewCommand.Execute(media.Items[0]);
            await WaitUntil(() => media.Viewed?.Full is not null);
            media.ReportViewedCommand.Execute(null);
            await ScreenshotTests.Settle(400);
        });

    [AvaloniaFact]
    public async Task Report_message_steam_deck() =>
        await ScreenshotTests.Shoot("report_message_1280x800", 1280, 800, await Member(), async shell =>
        {
            shell.NavigateTo("Nav_Messages");
            var messages = Assert.IsType<MessagesViewModel>(shell.CurrentPage);
            await WaitUntil(() => messages.ShowContent);
            messages.Selected = messages.Items[0];
            await WaitUntil(() => messages.Chat?.ShowContent == true);
            messages.Chat!.Messages.Last(m => m.IsOther && !m.IsDeleted).ReportCommand.Execute(null);
            await ScreenshotTests.Settle(400);
        });
}
