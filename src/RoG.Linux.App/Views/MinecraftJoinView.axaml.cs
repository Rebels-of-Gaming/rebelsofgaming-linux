using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter Minecraft der Server-Seite. Ressourcenpaket hochladen über die Dateiauswahl (Avalonia nutzt unter Linux das
/// FileChooser-Portal – auch in Flatpak ohne Dateisystem-Recht); Escape schließt die Rückfrage „Paket löschen“.
/// </summary>
public sealed partial class MinecraftJoinView : UserControl
{
    public MinecraftJoinView()
    {
        InitializeComponent();
    }

    private MinecraftJoinViewModel? Join => DataContext as MinecraftJoinViewModel;

    private async void OnPackUploadClick(object? sender, RoutedEventArgs e)
    {
        if (Join is not { CanEdit: true } join || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get("McJoin_PackUpload.Content"),
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Linux_ResourcePackTypes")) { Patterns = ["*.zip"], MimeTypes = ["application/zip"] }],
        });
        if (files.Count == 0 || files[0] is not { } file)
        {
            return;
        }

        var props = await file.GetBasicPropertiesAsync();
        await join.UploadPackAsync(new UploadSource(file.Name, props.Size is { } size ? (long)size : 1, file.OpenReadAsync));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && Join?.CloseOverlay() == true)
        {
            e.Handled = true;
        }
    }
}
