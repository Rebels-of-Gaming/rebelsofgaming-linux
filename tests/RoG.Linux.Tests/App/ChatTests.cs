using Avalonia.Headless.XUnit;
using RoG.Core.Features.Messages;
using RoG.Core.Offline;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 4: Direktnachrichten schreiben, zitieren, live, Anhänge, Warteschlange ohne Netz, eigenes Fenster.</summary>
public class ChatTests
{
    private static async Task<(TestServices T, ChatViewModel Chat)> OpenChatAsync()
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var chat = new ChatViewModel(t.Services, "01CONV1", "Nyx");
        await chat.LoadAsync();
        return (t, chat);
    }

    [AvaloniaFact]
    public async Task Send_with_quote_appends_the_message_and_clears_the_composer()
    {
        var (t, chat) = await OpenChatAsync();
        using (t)
        using (chat)
        {
            Assert.Equal(6, chat.Messages.Count);
            Assert.False(chat.SendCommand.CanExecute(null));
            var sent = 0;
            chat.Sent += (_, _) => sent++;

            var nyx = chat.Messages.Last(m => m.Author == "Nyx" && !m.IsDeleted);
            nyx.ReplyCommand.Execute(null);
            Assert.True(chat.HasQuote);
            chat.Draft = "  Bin dabei!  ";
            Assert.True(chat.SendCommand.CanExecute(null));
            await chat.SendCommand.ExecuteAsync(null);

            var mine = chat.Messages[^1];
            Assert.True(mine.IsMine);
            Assert.Equal("Bin dabei!", mine.Body);
            Assert.True(mine.HasQuote);
            Assert.Equal(string.Empty, chat.Draft);
            Assert.False(chat.HasQuote);
            Assert.Equal(1, sent);
            var write = t.Api.Writes.Single(w => w.Path.EndsWith("/messages", StringComparison.Ordinal));
            Assert.Contains("\"body\":\"Bin dabei!\"", write.Body, StringComparison.Ordinal);
            Assert.Contains("\"quote_id\":\"" + nyx.Message.Id + "\"", write.Body, StringComparison.Ordinal);

            // Dieselbe Nachricht kommt live zurück → nicht doppelt
            chat.OnLiveMessage(mine.Message);
            Assert.Equal(7, chat.Messages.Count);
        }
    }

    [AvaloniaFact]
    public async Task Live_messages_and_deletions_update_the_history()
    {
        var (t, chat) = await OpenChatAsync();
        using (t)
        using (chat)
        {
            var appended = 0;
            chat.MessageAppended += (_, _) => appended++;
            var live = MessageService.FromRealtime("""
                {"id":"01M8","conversation_id":"01CONV1","author":{"id":"01U2","slug":"nyx","name":"Nyx"},"body":"Bin in 5 min da","deleted":false,"quote":null,"attachments":[],"source":null,"created_at":"2026-10-30T08:06:00Z"}
                """);
            chat.OnLiveMessage(live!);
            Assert.Equal("Bin in 5 min da", chat.Messages[^1].Body);
            Assert.Equal(1, appended);

            chat.OnLiveDeleted("01M8");
            Assert.True(chat.Messages[^1].IsDeleted);
            Assert.Equal(t.Services.T("Messages_Deleted"), chat.Messages[^1].Body);
            Assert.False(chat.Messages[^1].CanReply);
        }
    }

    [AvaloniaFact]
    public async Task Offline_send_goes_to_the_outbox_in_memory_only()
    {
        var (t, chat) = await OpenChatAsync();
        using (t)
        using (chat)
        {
            t.Api.Offline = true;
            chat.Draft = "Kommt später an";
            await chat.SendCommand.ExecuteAsync(null);

            var item = Assert.Single(t.Services.Outbox.Items);
            Assert.Equal(OutboxKind.DirectMessage, item.Kind);
            Assert.Equal("01CONV1", item.Target);
            Assert.True(chat.Feedback.IsInfo);
            Assert.Equal(t.Services.T("Outbox_Queued"), chat.Feedback.Message);
            Assert.Equal(string.Empty, chat.Draft);

            t.Api.Offline = false;
            Assert.Equal(1, await t.Services.Outbox.FlushAsync());
            Assert.Empty(t.Services.Outbox.Items);
        }
    }

    [AvaloniaFact]
    public async Task Too_long_messages_cannot_be_sent_and_copy_reports_feedback()
    {
        var (t, chat) = await OpenChatAsync();
        using (t)
        using (chat)
        {
            chat.Draft = new string('x', MessageService.MaxLength + 1);
            Assert.True(chat.IsTooLong);
            Assert.False(chat.SendCommand.CanExecute(null));
            Assert.Equal("-1", chat.RemainingText);

            var first = chat.Messages.First(m => !m.IsDeleted);
            first.CopyCommand.Execute(null);
            Assert.Equal(first.Message.Body, t.Clipboard.Text);
            Assert.True(chat.Feedback.IsSuccess);
        }
    }

    [AvaloniaFact]
    public async Task Attachments_show_image_previews_and_open_files_in_the_browser()
    {
        var (t, chat) = await OpenChatAsync();
        using (t)
        using (chat)
        {
            var image = chat.Messages.SelectMany(m => m.Attachments).Single(a => a.CanPreview);
            Assert.Equal("aufstellung.png", image.Name);
            Assert.Equal("47 KB", image.SizeText);
            await image.LoadPreviewAsync();
            Assert.True(image.HasPreview);

            var file = chat.Messages.SelectMany(m => m.Attachments).Single(a => a.IsFile);
            await file.OpenCommand.ExecuteAsync(null);
            Assert.EndsWith("/v1/attachments/01A1", t.Browser.Opened[^1].AbsolutePath, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Pop_out_opens_one_window_per_conversation()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var messages = new MessagesViewModel(t.Services, () => { });
        await messages.LoadAsync();
        messages.Selected = messages.Items[0];
        await ViewModelTests.WaitUntil(() => messages.Chat?.ShowContent == true);

        var popped = new List<ChatViewModel>();
        messages.PopOutRequested += (_, c) => popped.Add(c);
        messages.PopOutCommand.Execute(null);
        var chat = Assert.Single(popped);
        Assert.Equal(messages.Chat!.ConversationId, chat.ConversationId);

        var window = ChatWindow.Open(chat);
        await ViewModelTests.WaitUntil(() => chat.ShowContent);
        Assert.Same(window, ChatWindow.Open(new ChatViewModel(t.Services, chat.ConversationId, chat.Title)));
        Assert.Single(ChatWindow.OpenWindows);
        window.Close();
        Assert.Empty(ChatWindow.OpenWindows);
    }

    [AvaloniaFact]
    public async Task Profile_send_message_opens_the_conversation_in_messages()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://u/nyx")));
        var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => members.Detail.ShowContent);
        Assert.True(members.Detail.CanMessage);

        await members.Detail.SendMessageCommand.ExecuteAsync(null);
        var messages = Assert.IsType<MessagesViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => messages.Chat?.ConversationId == "01CONV1");
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/conversations" && w.Body.Contains("\"user\"", StringComparison.Ordinal));
    }
}
