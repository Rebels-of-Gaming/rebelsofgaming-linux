using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Konto: Avatar/Banner über die Dateiauswahl (FileChooser-Portal).</summary>
public sealed partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
    }

    private async void OnAvatarClick(object? sender, RoutedEventArgs e) => await PickImageAsync(banner: false);

    private async void OnBannerClick(object? sender, RoutedEventArgs e) => await PickImageAsync(banner: true);

    private async Task PickImageAsync(bool banner)
    {
        if (DataContext is not AccountViewModel account || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get(banner ? "AccountPage_ChangeBanner.Content" : "AccountPage_ChangeAvatar.Content"),
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        var props = await file.GetBasicPropertiesAsync();
        await account.Editor.SetImageAsync(new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync), banner);
    }
}
