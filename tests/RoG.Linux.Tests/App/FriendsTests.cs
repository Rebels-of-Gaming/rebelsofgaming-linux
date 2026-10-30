using Avalonia.Headless.XUnit;
using RoG.Core.Features.Members;
using RoG.Core.Navigation;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 6: Freunde – Liste mit Status, Anfragen, Bestätigung in der Zeile, „Als Freund hinzufügen“ im Profil.</summary>
public class FriendsTests
{
    /// <summary><c>/v1/me</c> mit den Freunde-Scopes (neue Anmeldung).</summary>
    internal static string MeWithFriends => FakeData.Me.Replace("\"notifications:read\"]", "\"notifications:read\",\"friends:read\",\"friends:write\"]", StringComparison.Ordinal);

    internal static async Task<TestServices> SignedInAsync(bool friendScopes = true)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        if (friendScopes)
        {
            t.Api.Overrides["/v1/me"] = MeWithFriends;
        }

        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    [AvaloniaFact]
    public async Task Lists_friends_online_first_with_requests_and_count()
    {
        using var t = await SignedInAsync();
        var friends = new FriendsViewModel(t.Services, () => { });
        await friends.LoadAsync();

        Assert.True(friends.ShowList);
        Assert.False(friends.NeedsSignIn);
        Assert.Equal(["Kaito", "Mara", "Nyx", "Rookie", "BlackWolf", "Leise"], friends.Friends.Select(f => f.Name));
        Assert.Equal(Presence.Voice, friends.Friends[0].Presence);
        Assert.Null(friends.Friends[5].Presence); // Status nicht freigegeben → kein Abzeichen
        Assert.StartsWith(t.Services.T(RoG.Core.Localization.StringKeys.PresenceVoice), friends.Friends[0].Meta, StringComparison.Ordinal);
        Assert.Equal("SniperJoe", Assert.Single(friends.Incoming).Name);
        Assert.Equal("Valkyrie", Assert.Single(friends.Outgoing).Name);
        Assert.True(friends.HasIncoming && friends.HasOutgoing && !friends.HasNoFriends);
        Assert.Equal(t.Services.Format("Friends_Count", 6, 100), friends.CountText);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query.Contains("per_page=100", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Remove_and_decline_are_confirmed_in_the_row_and_escape_cancels()
    {
        using var t = await SignedInAsync();
        var friends = new FriendsViewModel(t.Services, () => { });
        await friends.LoadAsync();

        var nyx = friends.Friends.First(f => f.Name == "Nyx");
        nyx.RemoveCommand.Execute(null);
        Assert.True(nyx.IsConfirming);
        Assert.False(nyx.ShowActions);
        Assert.True(friends.IsDetailOpen);
        Assert.Contains("Nyx", nyx.ConfirmText, StringComparison.Ordinal);

        // nur eine Bestätigung zugleich
        friends.Incoming[0].DeclineCommand.Execute(null);
        Assert.False(nyx.IsConfirming);
        Assert.True(friends.Incoming[0].IsConfirming);

        friends.CloseDetail(); // Escape
        Assert.False(friends.Incoming[0].IsConfirming);
        Assert.False(friends.IsDetailOpen);
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");

        nyx.RemoveCommand.Execute(null);
        await nyx.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/me/friends/01JB8Y3Q2X5V7N9R4T6W8Z0C2D");
        Assert.Equal(t.Services.Format("Friends_Removed", "Nyx"), friends.Feedback.Message);
        Assert.True(friends.Feedback.IsInfo);

        friends.Incoming[0].DeclineCommand.Execute(null);
        await friends.Incoming[0].ConfirmCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == $"/v1/me/friends/requests/{FakeFriendsRoutes.IncomingId}/decline");
        Assert.Equal(t.Services.Format("Friends_Declined", "SniperJoe"), friends.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Accept_cancel_and_add_by_name_or_profile_link()
    {
        using var t = await SignedInAsync();
        var friends = new FriendsViewModel(t.Services, () => { });
        await friends.LoadAsync();

        await friends.Incoming[0].AcceptCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == $"/v1/me/friends/requests/{FakeFriendsRoutes.IncomingId}/accept");
        Assert.Equal(t.Services.Format("Friends_NowFriends", "SniperJoe"), friends.Feedback.Message);
        Assert.True(friends.Feedback.IsSuccess);

        await friends.Outgoing[0].CancelCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == $"/v1/me/friends/requests/{FakeFriendsRoutes.OutgoingId}");

        friends.AddInput = "kein gültiger name!";
        await friends.AddCommand.ExecuteAsync(null);
        Assert.True(friends.Feedback.IsError);
        Assert.Equal(t.Services.T("Friends_AddInvalid"), friends.Feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/me/friends/requests");

        friends.AddInput = "https://rebelsofgaming.com/de/u/frostbyte";
        await friends.AddCommand.ExecuteAsync(null);
        var sent = Assert.Single(t.Api.Writes, w => w.Method == "POST" && w.Path == "/v1/me/friends/requests");
        Assert.Contains("\"user\":\"frostbyte\"", sent.Body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, friends.AddInput);
        Assert.Equal(t.Services.Format("Friends_RequestSent", "FrostByte"), friends.Feedback.Message);

        // Gegenanfrage = Annahme
        friends.AddInput = "@valkyrie";
        await friends.AddCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.Format("Friends_NowFriends", "Valkyrie"), friends.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Server_errors_show_feedback_and_message_opens_the_conversation()
    {
        using var t = await SignedInAsync();
        var routes = new List<AppRoute>();
        t.Services.Navigator = r =>
        {
            routes.Add(r);
            return true;
        };
        var friends = new FriendsViewModel(t.Services, () => { });
        await friends.LoadAsync();

        await friends.Friends[0].MessageCommand.ExecuteAsync(null);
        Assert.Equal(new AppRoute(AppSection.Messages, "01CONV1"), routes[^1]);
        friends.Friends[0].OpenProfileCommand.Execute(null);
        Assert.Equal(new AppRoute(AppSection.Members, "kaito"), routes[^1]);

        t.Api.FailWith = System.Net.HttpStatusCode.InternalServerError;
        friends.AddInput = "frostbyte";
        await friends.AddCommand.ExecuteAsync(null);
        Assert.True(friends.Feedback.IsError);
        Assert.False(friends.IsBusy);
        Assert.Equal(6, friends.Friends.Count); // Liste bleibt stehen
    }

    [AvaloniaFact]
    public async Task Old_tokens_without_friend_scopes_get_the_sign_in_hint()
    {
        using var t = await SignedInAsync(friendScopes: false);
        var friends = new FriendsViewModel(t.Services, () => { });
        await friends.LoadAsync();
        Assert.True(friends.NeedsSignIn);
        Assert.False(friends.ShowList);
        Assert.True(friends.IsEmpty);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/me/friends", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Guests_see_a_hint_and_sign_in()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var signIn = 0;
        var friends = new FriendsViewModel(t.Services, () => signIn++);
        await friends.LoadAsync();
        Assert.True(friends.IsGuest);
        Assert.False(friends.ShowList);
        Assert.False(friends.IsEmpty);
        friends.SignInCommand.Execute(null);
        Assert.Equal(1, signIn);
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/v1/me/friends", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Deep_link_opens_friends_and_profile_can_send_a_request()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = MeWithFriends;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://friends")));
        var friends = Assert.IsType<FriendsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => friends.ShowList);

        Assert.True(shell.OpenDeepLink(new Uri("rog://u/nyx")));
        var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => members.Detail.ShowContent);
        Assert.True(members.Detail.CanBefriend);
        await members.Detail.BefriendCommand.ExecuteAsync(null);
        var sent = Assert.Single(t.Api.Writes, w => w.Path == "/v1/me/friends/requests");
        Assert.Contains("\"user\":\"01U2\"", sent.Body, StringComparison.Ordinal);
        Assert.True(members.Detail.FriendFeedback.IsSuccess);
        Assert.Equal(t.Services.Format("Friends_RequestSent", "FrostByte"), members.Detail.FriendFeedback.Message);
    }

    [AvaloniaFact]
    public async Task Profile_hides_befriend_without_scope()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();
        Assert.True(shell.OpenDeepLink(new Uri("rog://u/nyx")));
        var members = Assert.IsType<MembersViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => members.Detail.ShowContent);
        Assert.True(members.Detail.CanMessage);
        Assert.False(members.Detail.CanBefriend);
    }
}
