using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Uploader: Drag &amp; Drop aus dem Dateimanager, Dateiauswahl (Avalonia nutzt unter Linux das FileChooser-Portal –
/// auch in Flatpak ohne Dateisystem-Recht) und Strg+V (kopierte Dateien oder ein Bild, z. B. ein Screenshot).
/// </summary>
public sealed partial class UploadsView : UserControl
{
    public UploadsView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropZone.Classes.Set("over", false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private UploadsViewModel? Uploads => DataContext as UploadsViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var ok = Uploads is { ShowUploader: true, IsUploading: false } && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Classes.Set("over", ok);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Set("over", false);
        if (Uploads is not { ShowUploader: true } uploads || e.DataTransfer.TryGetFiles() is not { } items)
        {
            return;
        }

        uploads.IsUploadsTab = true;
        await uploads.UploadFilesAsync(await SourcesAsync(items));
    }

    private async void OnPickClick(object? sender, RoutedEventArgs e)
    {
        if (Uploads is not { } uploads || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.Get("UploadsPage_Pick.Content"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_UploadFileTypes")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.avif", "*.mp4", "*.webm", "*.mov", "*.mkv"] },
            ],
        });
        await uploads.UploadFilesAsync(await SourcesAsync(files));
    }

    private async void OnPasteClick(object? sender, RoutedEventArgs e) => await PasteAsync();

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V && e.Source is not TextBox && Uploads?.IsUploadsTab == true)
        {
            e.Handled = true;
            await PasteAsync();
        }
    }

    /// <summary>Zwischenablage: kopierte Dateien bzw. ein Bild (Screenshot) als PNG hochladen.</summary>
    private async Task PasteAsync()
    {
        if (Uploads is not { ShowUploader: true } uploads || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files)
        {
            await uploads.UploadFilesAsync(await SourcesAsync(files));
            return;
        }

        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using (bitmap)
            {
                var png = new MemoryStream();
                bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                var bytes = png.ToArray();
                var name = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
                await uploads.UploadFilesAsync([new UploadSource(name, bytes.Length, () => Task.FromResult<Stream>(new MemoryStream(bytes)))]);
            }
        }
    }

    private static async Task<IReadOnlyList<UploadSource>> SourcesAsync(IEnumerable<IStorageItem> items)
    {
        var result = new List<UploadSource>();
        foreach (var file in items.OfType<IStorageFile>())
        {
            var props = await file.GetBasicPropertiesAsync();
            result.Add(new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync));
        }

        // Ordner fallen über OfType raus; nicht unterstützte Typen meldet das Modell
        return result;
    }
}
