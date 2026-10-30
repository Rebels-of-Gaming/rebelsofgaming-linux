using Avalonia.Headless.XUnit;
using RoG.Core.Features.Events;
using RoG.Core.Offline;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 4: Zusagen im Kalender (Dabei/Vielleicht/Absagen), ohne Netz in die Warteschlange.</summary>
public class EventRsvpTests
{
    [AvaloniaFact]
    public async Task Members_answer_events_and_the_list_reloads()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var events = new EventsViewModel(t.Services);
        await events.LoadAsync();
        events.Open("01EV1");
        var d = events.Detail;
        await ViewModelTests.WaitUntil(() => d.ShowContent && d.CanRespond);
        Assert.True(d.IsYes);

        var before = t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/events");
        await d.RespondMaybeCommand.ExecuteAsync(null);
        var write = t.Api.Writes.Single(w => w.Path.EndsWith("/rsvp", StringComparison.Ordinal));
        Assert.Contains("\"status\":\"maybe\"", write.Body, StringComparison.Ordinal);
        Assert.True(d.Feedback.IsSuccess, d.Feedback.Message);
        Assert.Equal(t.Services.Format("Events_RsvpSaved", t.Services.T("Events_RsvpMaybe")), d.Feedback.Message);
        await ViewModelTests.WaitUntil(() => t.Api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/events") > before);

        // Ohne Netz: Warteschlange, vorläufig angezeigt
        t.Api.Offline = true;
        await d.RespondNoCommand.ExecuteAsync(null);
        var item = Assert.Single(t.Services.Outbox.Items);
        Assert.Equal(OutboxKind.EventRsvp, item.Kind);
        Assert.Contains("\"no\"", item.Payload, StringComparison.Ordinal);
        Assert.True(d.IsNo);
        Assert.True(d.Feedback.IsInfo);
    }

    [AvaloniaFact]
    public async Task Guests_cannot_answer()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var events = new EventsViewModel(t.Services);
        await events.LoadAsync();
        events.Open("01EV1");
        await ViewModelTests.WaitUntil(() => events.Detail.ShowContent);
        Assert.False(events.Detail.CanRespond);
        Assert.Equal(Rsvp.None, events.Detail.MyRsvp);
    }
}
