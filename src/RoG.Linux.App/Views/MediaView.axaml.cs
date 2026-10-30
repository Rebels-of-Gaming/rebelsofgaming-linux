using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Medien: Pfeiltasten blättern in der Großansicht, Dateiauswahl (Portal) zum Einreichen.</summary>
public sealed partial class MediaView : UserControl
{
    public MediaView()
    {
        InitializeComponent();
    }

    private MediaViewModel? Media => DataContext as MediaViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Media is not { IsViewerOpen: true } media)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                media.Step(-1);
                e.Handled = true;
                break;
            case Key.Right:
                media.Step(1);
                e.Handled = true;
                break;
        }
    }

    private async void OnSubmitPickClick(object? sender, RoutedEventArgs e)
    {
        if (Media is not { } media || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.Get("Gallery_SubmitTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_UploadFileTypes")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.mp4", "*.webm", "*.mov", "*.mkv"] },
            ],
        });
        var sources = new List<UploadSource>();
        foreach (var file in files)
        {
            var props = await file.GetBasicPropertiesAsync();
            sources.Add(new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync));
        }

        await media.SubmitAsync(sources);
    }
}
