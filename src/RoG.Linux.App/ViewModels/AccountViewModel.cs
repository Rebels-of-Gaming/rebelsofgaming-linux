using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Konto (Spike): wer ist angemeldet, wo liegt der Token, Version, Abmelden bzw. Anmelden im Gastmodus.</summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<Task> _signOut;
    private readonly Action _signIn;

    public AccountViewModel(AppServices services, Func<Task> signOut, Action signIn)
    {
        Editor = new ProfileEditViewModel(services);
        _services = services;
        _signOut = signOut;
        _signIn = signIn;
        services.CurrentUserChanged += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuest))]
    public partial bool IsSignedIn { get; set; }

    public bool IsGuest => !IsSignedIn;

    /// <summary>„Profil bearbeiten“ (lädt beim Aufklappen).</summary>
    public ProfileEditViewModel Editor { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Initial { get; set; } = "?";

    [ObservableProperty]
    public partial string? Rank { get; set; }

    [ObservableProperty]
    public partial string? Details { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAvatar))]
    public partial Bitmap? Avatar { get; set; }

    public bool HasNoAvatar => Avatar is null;

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string StorageText => _services.TokensPersistent ? _services.T("Linux_KeyringStored") : _services.T("Linux_TokenNotPersistent");

    public bool StorageIsWarning => !_services.TokensPersistent;

    public string EnvironmentText => _services.Format("Linux_Environment", _services.Environment.Name);

    public string VersionText => _services.Format("Settings_AppVersion", AppServices.AppVersion);

    private async void Refresh()
    {
        var user = _services.CurrentUser;
        var token = user is null ? await _services.Tokens.GetAsync() : null;
        IsSignedIn = user is not null || token is not null;
        Name = user?.Name ?? token?.UserName ?? string.Empty;
        Initial = string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpper(_services.Culture);
        Rank = user?.RankLabel?.ToUpper(_services.Culture);
        Details = IsSignedIn ? _services.Format("Linux_SignedInAs", Name) : _services.T("Linux_GuestMode");
        Avatar = await _services.Images.LoadAsync(user?.AvatarUrl is { } a && Uri.TryCreate(a, UriKind.Absolute, out var uri) ? uri : null, 160);
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StorageIsWarning));
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            await _signOut();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SignIn() => _signIn();
}

/// <summary>Bereich, den die Linux-App noch nicht nativ hat: Hinweis und „Im Browser öffnen“ (Website, gleiche Sprache).</summary>
public sealed partial class PlaceholderViewModel(AppServices services, NavItem item)
{
    public string Title { get; } = item.Label.ToUpper(services.Culture);

    public Avalonia.Media.Geometry? Icon { get; } = item.Icon;

    public string Text { get; } = services.T("Common_ComingSoon");

    public string WebHint { get; } = services.T("Linux_ComingSoonWeb");

    [RelayCommand]
    private Task OpenInBrowserAsync() => services.Browser.OpenAsync(services.Environment.WebPage(services.Language, item.WebPath));
}
