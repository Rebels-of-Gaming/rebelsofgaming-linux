using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Konto: Avatar/Banner über die Dateiauswahl (FileChooser-Portal); Fokus ins Minecraft-Code-Feld (Link) bzw. ins
/// Bestätigungsfeld beim Löschen; Enter im Code-Feld prüft den Code.
/// </summary>
public sealed partial class AccountView : UserControl
{
    private AccountViewModel? _account;

    public AccountView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_account is not null)
        {
            _account.Minecraft.FocusCodeRequested -= OnFocusCode;
            _account.Data.FocusConfirmationRequested -= OnFocusConfirmation;
        }

        _account = DataContext as AccountViewModel;
        if (_account is not null)
        {
            _account.Minecraft.FocusCodeRequested += OnFocusCode;
            _account.Data.FocusConfirmationRequested += OnFocusConfirmation;
        }
    }

    private void OnFocusCode(object? sender, EventArgs e) => FocusLater(MinecraftCodeBox);

    private void OnFocusConfirmation(object? sender, EventArgs e) => FocusLater(DeleteConfirmationBox);

    /// <summary>Nach dem Aufklappen (Layout) sichtbar machen und fokussieren.</summary>
    private static void FocusLater(Control target) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            target.BringIntoView();
            target.Focus();
        }, Avalonia.Threading.DispatcherPriority.Background);

    private void OnCodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _account?.Minecraft.CheckCommand.CanExecute(null) == true)
        {
            _account.Minecraft.CheckCommand.Execute(null);
            e.Handled = true;
        }
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
