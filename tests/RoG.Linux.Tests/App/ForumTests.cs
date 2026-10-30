using Avalonia.Headless.XUnit;
using RoG.Core.Offline;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 4: Forum lesen und schreiben (Antwort, Zitat, Vorschau, Reaktionen, Umfrage, neues Thema, Links).</summary>
public class ForumTests
{
    private static async Task<(TestServices T, ForumViewModel Forum)> OpenThreadAsync(bool signedIn = true)
    {
        var t = signedIn ? await TestServices.CreateAsync(TestTokens.Wahke) : await TestServices.CreateAsync(guest: true);
        var forum = new ForumViewModel(t.Services);
        await forum.LoadAsync();
        forum.OpenCategoryCommand.Execute(forum.Categories.Single(c => c.Slug == "allgemein"));
        await ViewModelTests.WaitUntil(() => forum.Threads.Count == 3);
        forum.SelectedThread = forum.Threads[0];
        await ViewModelTests.WaitUntil(() => forum.Thread.ShowContent || forum.Thread.HasError);
        Assert.Null(forum.Thread.ErrorMessage);
        return (t, forum);
    }

    [AvaloniaFact]
    public async Task Categories_threads_and_a_thread_with_poll_reactions_and_blocked_post()
    {
        var (t, forum) = await OpenThreadAsync();
        using (t)
        using (forum)
        {
            Assert.Equal(["Ankündigungen", "Allgemein", "Scrims & Training", "Off-Topic"], forum.Categories.Select(c => c.Name));
            Assert.True(forum.Categories[2].Indent.Left > 0); // Unterbereich
            Assert.Contains("Squad CS2", forum.Categories[2].Stats, StringComparison.Ordinal);
            Assert.True(forum.CanCreateThread);
            Assert.False(forum.Threads[0].Unread); // beim Öffnen gelesen
            Assert.True(forum.Threads[0].Pinned);
            Assert.True(forum.Threads[2].Locked);

            var thread = forum.Thread;
            Assert.Equal("Clanabend im November – welcher Tag passt?", thread.Title);
            Assert.True(thread.CanReply);
            Assert.True(thread.HasPoll);
            Assert.Equal(3, thread.PollOptions.Count);
            Assert.StartsWith("✓", thread.PollOptions[0].Label, StringComparison.Ordinal);
            Assert.Equal(56, thread.PollOptions[0].Percent);
            Assert.Equal(4, thread.Posts.Count);
            var first = thread.Posts[0];
            Assert.Equal(["👍 4", "❤️ 2"], first.Reactions.Select(r => r.Text));
            Assert.True(first.Reactions[0].Mine);
            Assert.Equal(3, first.ReactionChoices.Count); // nur aktive
            Assert.Contains(t.Services.T("Forum_Edited"), thread.Posts[1].Meta, StringComparison.Ordinal);
            Assert.Equal("configs.zip", thread.Posts[1].Attachments);

            // Blockierte Person: eingeklappt, „trotzdem anzeigen“
            var blocked = thread.Posts[2];
            Assert.True(blocked.IsCollapsed);
            Assert.Equal(t.Services.T("Forum_BlockedAuthor"), blocked.Author);
            Assert.False(blocked.CanQuote);
            await blocked.RevealCommand.ExecuteAsync(null);
            Assert.False(blocked.IsCollapsed);
            Assert.Contains("blockierten", blocked.PlainText, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Reply_with_quote_preview_and_offline_queue()
    {
        var (t, forum) = await OpenThreadAsync();
        using (t)
        using (forum)
        {
            var thread = forum.Thread;
            var replaced = 0;
            thread.DraftReplaced += (_, _) => replaced++;
            thread.Posts[1].QuoteCommand.Execute(null);
            Assert.StartsWith("[quote=Kaito]", thread.Draft, StringComparison.Ordinal);
            Assert.Equal(1, replaced);
            thread.Draft += "Bin dabei!";

            await thread.TogglePreviewCommand.ExecuteAsync(null);
            Assert.True(thread.ShowPreview);
            Assert.NotNull(thread.PreviewDocument);

            await thread.SendCommand.ExecuteAsync(null);
            var write = t.Api.Writes.Single(w => w.Path.EndsWith("/posts", StringComparison.Ordinal));
            Assert.Contains("[quote=Kaito]", write.Body, StringComparison.Ordinal);
            Assert.Equal(string.Empty, thread.Draft);
            Assert.False(thread.ShowPreview);
            Assert.True(thread.Feedback.IsSuccess);

            t.Api.Offline = true;
            thread.Draft = "Später";
            await thread.SendCommand.ExecuteAsync(null);
            var item = Assert.Single(t.Services.Outbox.Items);
            Assert.Equal(OutboxKind.ForumReply, item.Kind);
            Assert.Equal(thread.ThreadId, item.Target);
            Assert.True(thread.Feedback.IsInfo);
        }
    }

    [AvaloniaFact]
    public async Task Reactions_and_poll_votes_are_sent()
    {
        var (t, forum) = await OpenThreadAsync();
        using (t)
        using (forum)
        {
            var thread = forum.Thread;
            await thread.Posts[3].ReactionChoices[2].ChooseCommand.ExecuteAsync(null);
            Assert.Contains(t.Api.Writes, w => w.Path == "/v1/forum/posts/01p4/reactions" && w.Body.Contains("\"gg\"", StringComparison.Ordinal));
            await thread.Posts[0].Reactions[0].ToggleCommand.ExecuteAsync(null);
            Assert.Contains(t.Api.Writes, w => w.Path == "/v1/forum/posts/01p1/reactions" && w.Body.Contains("\"like\"", StringComparison.Ordinal));

            Assert.True(thread.CanVote);
            await thread.PollOptions[1].VoteCommand.ExecuteAsync(null);
            Assert.Contains(t.Api.Writes, w => w.Path == "/v1/forum/polls/01poll1/votes" && w.Body.Contains("01o2", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public async Task New_thread_is_created_and_opened()
    {
        var (t, forum) = await OpenThreadAsync();
        using (t)
        using (forum)
        {
            forum.StartNewThreadCommand.Execute(null);
            Assert.True(forum.IsComposing);
            Assert.False(forum.ShowThread);
            Assert.Equal("Allgemein", forum.NewThread.CategoryName);
            Assert.False(forum.NewThread.CreateCommand.CanExecute(null));
            forum.NewThread.Title = "Scrim am Freitag?";
            forum.NewThread.Body = "Wer ist dabei? @Nyx";
            await forum.NewThread.CreateCommand.ExecuteAsync(null);

            var write = t.Api.Writes.Single(w => w.Path == "/v1/forum/categories/allgemein/threads");
            Assert.Contains("Scrim am Freitag?", write.Body, StringComparison.Ordinal);
            Assert.False(forum.IsComposing);
            Assert.True(forum.ShowThread);
        }
    }

    [AvaloniaFact]
    public async Task Guests_read_but_cannot_write()
    {
        var (t, forum) = await OpenThreadAsync(signedIn: false);
        using (t)
        using (forum)
        {
            Assert.False(forum.CanCreateThread);
            Assert.False(forum.Thread.CanReply);
            Assert.False(forum.Thread.ShowReadOnlyHint);
            Assert.False(forum.Thread.CanVote);
            Assert.False(forum.Thread.Posts[0].CanReact);
        }
    }

    [AvaloniaFact]
    public async Task Empty_category_shows_its_empty_state_and_links_open_threads_or_sections()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        Assert.True(shell.OpenDeepLink(new Uri("rog://forum/off-topic")));
        var forum = Assert.IsType<ForumViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => forum.Category?.Slug == "off-topic" && !forum.IsLoadingThreads);
        Assert.True(forum.ShowThreadsEmpty);

        // Website-Adresse eines Themas
        var route = t.Services.RouteFor(new Uri("https://rebelsofgaming.com/de/forum/t/" + FakeData.ForumThreadId + "/clanabend"));
        Assert.NotNull(route);
        Assert.True(shell.Navigate(route));
        await ViewModelTests.WaitUntil(() => forum.Thread.ShowContent);
        Assert.True(forum.ThreadForced);
        Assert.Empty(t.Browser.Opened);
    }
}
