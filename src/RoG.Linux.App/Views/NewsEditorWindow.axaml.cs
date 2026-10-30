using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Core.Features.News;
using RoG.Linux.App.Localization;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// News-Redaktion im eigenen Fenster (wie <c>NewsEditorWindow</c> der Windows-App, höchstens eins – erneutes Öffnen
/// holt es nach vorn und öffnet ggf. die gewünschte News). Titelbild über die Dateiauswahl (unter Linux das
/// FileChooser-Portal, auch in Flatpak). Escape schließt eine Rückfrage/Meldung bzw. das Fenster, Strg+W schließt.
/// </summary>
public sealed partial class NewsEditorWindow : Window
{
    private static NewsEditorWindow? s_open;

    public NewsEditorWindow()
    {
        InitializeComponent();
    }

    internal NewsEditorWindow(NewsEditorViewModel editor)
        : this()
    {
        DataContext = editor;
    }

    /// <summary>Offenes Redaktionsfenster (Tests).</summary>
    internal static NewsEditorWindow? OpenWindow => s_open;

    internal NewsEditorViewModel? Editor => DataContext as NewsEditorViewModel;

    /// <summary>Redaktion öffnen, optional direkt mit einer News (Slug); <paramref name="closed"/> bekommt „geändert“.</summary>
    public static NewsEditorWindow Open(AppServices services, NewsRights rights, string? slug, Action<bool>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (s_open is null)
        {
            var editor = new NewsEditorViewModel(services, rights);
            var window = new NewsEditorWindow(editor);
            s_open = window;
            window.Closed += (_, _) =>
            {
                if (s_open == window)
                {
                    s_open = null;
                }

                closed?.Invoke(editor.Changed);
            };
            window.Show();
            _ = editor.StartAsync(slug);
        }
        else if (slug is not null)
        {
            _ = s_open.Editor?.StartAsync(slug);
        }

        RoG.Linux.App.App.BringToFront(s_open);
        return s_open;
    }

    private async void OnSetCoverClick(object? sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor || StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get("NewsEdit_SetCover.Content"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_WikiImageTypes")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });
        if (files.Count > 0)
        {
            var file = files[0];
            var props = await file.GetBasicPropertiesAsync();
            await editor.SetCoverAsync(new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Editor?.CloseOverlay() != true)
            {
                Close();
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            e.Handled = true;
            Close();
        }
    }
}
