using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Notifications;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Glocke: Postfach mit „nur ungelesen“, „alle gelesen“, Ziel öffnen; neue Einträge kommen live dazu.</summary>
public sealed partial class NotificationsViewModel : LoadableViewModel
{
    private readonly NotificationService _notifications;
    private readonly Action _requestSignIn;
    private string? _cursor;

    public NotificationsViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _notifications = new NotificationService(services.Api.Client);
        _requestSignIn = requestSignIn;
    }

    public ObservableCollection<NotificationRowViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool UnreadOnly { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    public partial bool IsGuest { get; set; }

    public bool ShowList => !IsGuest;

    public bool HasUnread => Items.Any(i => i.IsUnread);

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            HasMore = false;
            return;
        }

        var page = await _notifications.ListAsync(null, UnreadOnly, cancellationToken);
        Items.Clear();
        foreach (var n in page.Items)
        {
            Items.Add(new NotificationRowViewModel(n, this, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        OnPropertyChanged(nameof(HasUnread));
    }

    /// <summary>Live-Eintrag vorn anstellen.</summary>
    public void Prepend(NotificationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsGuest && Items.All(i => i.Item.Id != item.Id))
        {
            Items.Insert(0, new NotificationRowViewModel(item, this, Services));
            DataChanged();
            OnPropertyChanged(nameof(HasUnread));
        }
    }

    partial void OnUnreadOnlyChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            var page = await _notifications.ListAsync(_cursor, UnreadOnly);
            foreach (var n in page.Items)
            {
                Items.Add(new NotificationRowViewModel(n, this, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // „Mehr laden“ erneut
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await _notifications.MarkAllReadAsync();
            foreach (var row in Items)
            {
                row.IsUnread = false;
            }

            Services.Realtime.UnreadNotifications = 0;
            OnPropertyChanged(nameof(HasUnread));
            if (UnreadOnly)
            {
                await LoadAsync();
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // Liste bleibt; der nächste Abgleich korrigiert den Zähler
        }
    }

    [RelayCommand]
    private void SignIn() => _requestSignIn();

    /// <summary>Eintrag öffnen: gelesen markieren, Ziel in der App (sonst Browser).</summary>
    internal async Task OpenAsync(NotificationRowViewModel row)
    {
        if (row.IsUnread)
        {
            row.IsUnread = false;
            Services.Realtime.UnreadNotifications = Math.Max(0, Services.Realtime.UnreadNotifications - 1);
            OnPropertyChanged(nameof(HasUnread));
            try
            {
                await _notifications.MarkReadAsync(row.Item.Id);
            }
            catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
            {
                // nicht schlimm – der nächste Abgleich korrigiert den Zähler
            }
        }

        if (row.Item.Url is { } url)
        {
            await Services.OpenLinkAsync(url);
        }
    }
}

public sealed partial class NotificationRowViewModel(NotificationItem item, NotificationsViewModel owner, AppServices services) : ObservableObject
{
    public NotificationItem Item => item;

    public string Title => item.Title;

    public string Body => item.Body ?? string.Empty;

    public bool HasBody => !string.IsNullOrEmpty(item.Body);

    public string Time { get; } = item.CreatedAt?.ToLocalTime().ToString("g", services.Culture) ?? string.Empty;

    /// <summary>Symbol nach <c>icon</c> der API (bell, message, calendar …), sonst Glocke.</summary>
    public Avalonia.Media.Geometry? Icon { get; } = Avalonia.Application.Current?.TryGetResource(IconKey(item.Icon), null, out var g) == true ? g as Avalonia.Media.Geometry : null;

    [ObservableProperty]
    public partial bool IsUnread { get; set; } = item.IsUnread;

    [RelayCommand]
    private Task OpenAsync() => owner.OpenAsync(this);

    private static string IconKey(string icon) => icon switch
    {
        "message" or "messages" or "chat" or "mail" => "IconMessages",
        "calendar" or "event" => "IconCalendar",
        "forum" or "reply" or "mention" or "quote" => "IconForum",
        "server" or "servers" => "IconServers",
        "users" or "user" or "friends" => "IconFriends",
        "news" => "IconNews",
        "trophy" or "award" => "IconTrophy",
        "upload" or "image" => "IconMedia",
        _ => "IconBell",
    };
}
