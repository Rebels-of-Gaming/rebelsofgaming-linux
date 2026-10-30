using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Friends;
using RoG.Core.Features.Members;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Freunde (Guide 45, wie die Windows-App): eigene Freundesliste mit Online-Status, offene Anfragen (an mich:
/// annehmen/ablehnen, von mir: zurückziehen), Freund per Name/Profil-Link hinzufügen, entfernen. Ablehnen und Entfernen
/// werden in der Zeile bestätigt (statt Dialog, Escape bricht ab). Die Liste ist privat; Grenzen, Sperren und Blockieren
/// prüft der Server. Ältere Geräte-Tokens haben <c>friends:read</c>/<c>friends:write</c> erst nach neuer Anmeldung.
/// </summary>
public sealed partial class FriendsViewModel : LoadableViewModel, IDetailPage
{
    private readonly FriendsService _friends;
    private readonly Action _requestSignIn;

    public FriendsViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _friends = new FriendsService(services.Api.Http);
        _requestSignIn = requestSignIn;
    }

    public ObservableCollection<FriendItemViewModel> Friends { get; } = [];

    public ObservableCollection<FriendRequestItemViewModel> Incoming { get; } = [];

    public ObservableCollection<FriendRequestItemViewModel> Outgoing { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Ohne Anmeldung: Hinweis + „Anmelden“.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    public partial bool IsGuest { get; set; }

    public bool ShowList => !IsGuest && ShowContent;

    [ObservableProperty]
    public partial bool CanRead { get; set; } = true;

    [ObservableProperty]
    public partial bool CanWrite { get; set; } = true;

    /// <summary>Token ohne Freunde-Scopes: Hinweis „einmal ab- und wieder anmelden“.</summary>
    [ObservableProperty]
    public partial bool NeedsSignIn { get; set; }

    [ObservableProperty]
    public partial string AddInput { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    public partial string? CountText { get; set; }

    public bool HasCount => !string.IsNullOrEmpty(CountText);

    [ObservableProperty]
    public partial bool HasIncoming { get; set; }

    [ObservableProperty]
    public partial bool HasOutgoing { get; set; }

    [ObservableProperty]
    public partial bool HasNoFriends { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Zeile, die gerade eine Bestätigung zeigt (Ablehnen/Entfernen).</summary>
    private IConfirmable? _confirming;

    public bool IsDetailOpen => _confirming is not null;

    /// <summary>Escape: offene Bestätigung abbrechen.</summary>
    public void CloseDetail() => SetConfirming(null);

    private bool _loaded;

    protected override bool HasData => IsGuest || (_loaded && CanRead);

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Clear();
            return;
        }

        // Ohne geladenes /v1/me (offline, gerade gestartet) entscheidet der Server (403 insufficient_scope)
        var me = Services.CurrentUser;
        CanRead = me?.HasScope(FriendsService.ReadScope) ?? true;
        CanWrite = me?.HasScope(FriendsService.WriteScope) ?? true;
        NeedsSignIn = !(CanRead && CanWrite);
        if (!CanRead)
        {
            Clear();
            return;
        }

        var friends = new List<FriendEntry>();
        FriendPage? page = null;
        do
        {
            page = await _friends.FriendsAsync(page?.NextCursor, cancellationToken);
            friends.AddRange(page.Items);
        }
        while (page.HasMore && friends.Count < 500);

        var requests = new List<FriendRequest>();
        Page<FriendRequest>? r = null;
        do
        {
            r = await _friends.RequestsAsync(null, r?.NextCursor, cancellationToken);
            requests.AddRange(r.Items);
        }
        while (r.HasMore && requests.Count < 500);

        SetConfirming(null);
        Friends.Clear();
        foreach (var f in friends.OrderBy(f => f.Status is null or Presence.Offline).ThenBy(f => f.User.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Friends.Add(new FriendItemViewModel(f, this, Services));
        }

        Incoming.Clear();
        Outgoing.Clear();
        foreach (var request in requests)
        {
            (request.Incoming ? Incoming : Outgoing).Add(new FriendRequestItemViewModel(request, this, Services));
        }

        HasIncoming = Incoming.Count > 0;
        HasOutgoing = Outgoing.Count > 0;
        HasNoFriends = Friends.Count == 0;
        CountText = page.Limit is { } limit ? Services.Format(StringKeys.FriendsCount, page.Total ?? Friends.Count, limit) : null;
        _loaded = true;
    }

    private void Clear()
    {
        SetConfirming(null);
        Friends.Clear();
        Incoming.Clear();
        Outgoing.Clear();
        HasIncoming = false;
        HasOutgoing = false;
        HasNoFriends = false;
        CountText = null;
    }

    partial void OnIsGuestChanged(bool value) => DataChanged();

    [RelayCommand]
    private void SignIn() => _requestSignIn();

    [RelayCommand]
    private async Task AddAsync()
    {
        if (FriendsService.NormalizeUser(AddInput) is not { } user)
        {
            Feedback.Show(Services.T(StringKeys.FriendsAddInvalid), MessageTone.Error);
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _friends.SendAsync(user);
            AddInput = string.Empty;
            Feedback.Show(Services.Format(result.IsAccepted ? StringKeys.FriendsNowFriends : StringKeys.FriendsRequestSent, result.User.Name), MessageTone.Success);
        });
    }

    internal Task AcceptAsync(FriendRequestItemViewModel item) => RunAsync(async () =>
    {
        var friend = await _friends.AcceptAsync(item.Id);
        Feedback.Show(Services.Format(StringKeys.FriendsNowFriends, friend.User.Name), MessageTone.Success);
    });

    internal Task DeclineAsync(FriendRequestItemViewModel item) => RunAsync(async () =>
    {
        await _friends.DeclineAsync(item.Id);
        Feedback.Show(Services.Format(StringKeys.FriendsDeclined, item.Name), MessageTone.Info);
    });

    internal Task CancelAsync(FriendRequestItemViewModel item) => RunAsync(async () =>
    {
        await _friends.CancelAsync(item.Id);
        Feedback.Show(Services.Format(StringKeys.FriendsCancelled, item.Name), MessageTone.Info);
    });

    internal Task RemoveAsync(FriendItemViewModel item) => RunAsync(async () =>
    {
        await _friends.RemoveAsync(item.UserId);
        Feedback.Show(Services.Format(StringKeys.FriendsRemoved, item.Name), MessageTone.Info);
    });

    /// <summary>Bestätigung in genau einer Zeile zeigen (<c>null</c> = keine).</summary>
    internal void SetConfirming(IConfirmable? item)
    {
        if (ReferenceEquals(_confirming, item))
        {
            return;
        }

        _confirming?.SetConfirming(false);
        _confirming = item;
        item?.SetConfirming(true);
        OnPropertyChanged(nameof(IsDetailOpen));
    }

    internal void OpenProfile(string slug) => Services.Navigate(new AppRoute(AppSection.Members, slug));

    internal async Task MessageAsync(FriendItemViewModel item)
    {
        try
        {
            var conversation = await new RoG.Core.Features.Messages.MessageService(Services.Api.Client).StartDirectAsync(item.UserId);
            Services.Navigate(new AppRoute(AppSection.Messages, conversation));
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    /// <summary>Aktion ausführen und danach still neu laden (die Liste kommt immer vom Server).</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (!CanWrite || IsBusy || IsGuest)
        {
            return;
        }

        IsBusy = true;
        SetConfirming(null);
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(
                Services.Localizer.GetError(ex.Problem.Code),
                ex.Problem.Code is "already_friends" or "already_requested" ? MessageTone.Info : MessageTone.Error);
        }
        catch (HttpRequestException)
        {
            Feedback.Show(Services.Localizer.GetError("network_error"), MessageTone.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(silent: true);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ShowContent))
        {
            OnPropertyChanged(nameof(ShowList));
        }
    }
}

/// <summary>Zeile mit Bestätigung im Inhalt (statt Dialog).</summary>
internal interface IConfirmable
{
    void SetConfirming(bool value);
}

/// <summary>Freund: Avatar mit Status-Abzeichen, Name (Profil), „Online · Freunde seit …“, Nachricht, Entfernen.</summary>
public sealed partial class FriendItemViewModel : ObservableObject, IConfirmable
{
    private readonly FriendEntry _friend;
    private readonly FriendsViewModel _owner;

    public FriendItemViewModel(FriendEntry friend, FriendsViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(friend);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);
        _friend = friend;
        _owner = owner;
        Presence = friend.Status;
        PresenceText = friend.Status is { } s ? UiResources.PresenceLabel(s, services.Localizer) : null;
        Meta = string.Join(" · ", new[]
        {
            PresenceText,
            friend.Since is { } since ? services.Format(StringKeys.FriendsSince, since.ToLocalTime().ToString("d", services.Culture)) : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        ConfirmTitle = services.T(StringKeys.FriendsRemoveTitle);
        ConfirmText = services.Format(StringKeys.FriendsRemoveText, friend.User.Name);
        CanWrite = owner.CanWrite;
        _ = LoadAvatarAsync(services);
    }

    public string UserId => _friend.User.Id;

    public string Name => _friend.User.Name;

    public Presence? Presence { get; }

    public string? PresenceText { get; }

    public string Meta { get; }

    public bool HasMeta => Meta.Length > 0;

    public bool CanWrite { get; }

    public string ConfirmTitle { get; }

    public string ConfirmText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsConfirming { get; set; }

    public bool ShowActions => !IsConfirming;

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    void IConfirmable.SetConfirming(bool value) => IsConfirming = value;

    [RelayCommand]
    private void OpenProfile() => _owner.OpenProfile(_friend.User.Slug);

    [RelayCommand]
    private Task MessageAsync() => _owner.MessageAsync(this);

    /// <summary>„Entfernen“: erst die Bestätigung in der Zeile zeigen.</summary>
    [RelayCommand]
    private void Remove() => _owner.SetConfirming(this);

    [RelayCommand]
    private Task ConfirmAsync() => _owner.RemoveAsync(this);

    [RelayCommand]
    private void CancelConfirm() => _owner.SetConfirming(null);

    private async Task LoadAvatarAsync(AppServices services) => Avatar = await services.Images.LoadAsync(_friend.User.AvatarUrl, 96);
}

/// <summary>Offene Anfrage: an mich (annehmen/ablehnen mit Bestätigung) oder von mir (zurückziehen).</summary>
public sealed partial class FriendRequestItemViewModel : ObservableObject, IConfirmable
{
    private readonly FriendRequest _request;
    private readonly FriendsViewModel _owner;

    public FriendRequestItemViewModel(FriendRequest request, FriendsViewModel owner, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);
        _request = request;
        _owner = owner;
        Meta = request.CreatedAt is { } at
            ? services.Format(request.Incoming ? StringKeys.FriendsReceivedAt : StringKeys.FriendsSentAt, at.ToLocalTime().ToString("g", services.Culture))
            : string.Empty;
        ConfirmTitle = services.T(StringKeys.FriendsDeclineTitle);
        ConfirmText = services.Format(StringKeys.FriendsDeclineText, request.User.Name);
        CanWrite = owner.CanWrite;
        _ = LoadAvatarAsync(services);
    }

    public string Id => _request.Id;

    public string Name => _request.User.Name;

    public bool IsIncoming => _request.Incoming;

    public string Meta { get; }

    public bool HasMeta => Meta.Length > 0;

    public bool CanWrite { get; }

    public string ConfirmTitle { get; }

    public string ConfirmText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsConfirming { get; set; }

    public bool ShowActions => CanWrite && !IsConfirming;

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    void IConfirmable.SetConfirming(bool value) => IsConfirming = value;

    [RelayCommand]
    private void OpenProfile() => _owner.OpenProfile(_request.User.Slug);

    [RelayCommand]
    private Task AcceptAsync() => _owner.AcceptAsync(this);

    /// <summary>„Ablehnen“: erst die Bestätigung in der Zeile zeigen.</summary>
    [RelayCommand]
    private void Decline() => _owner.SetConfirming(this);

    [RelayCommand]
    private Task ConfirmAsync() => _owner.DeclineAsync(this);

    [RelayCommand]
    private void CancelConfirm() => _owner.SetConfirming(null);

    [RelayCommand]
    private Task CancelAsync() => _owner.CancelAsync(this);

    private async Task LoadAvatarAsync(AppServices services) => Avatar = await services.Images.LoadAsync(_request.User.AvatarUrl, 96);
}
