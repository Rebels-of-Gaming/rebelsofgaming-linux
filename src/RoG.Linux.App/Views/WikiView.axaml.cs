using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Wiki: Bild für den Editor über den Datei-Dialog (in Flatpak das FileChooser-Portal) und Sprung zu Überschriften aus
/// dem Inhaltsverzeichnis.
/// </summary>
public sealed partial class WikiView : UserControl
{
    private WikiViewModel? _wiki;

    public WikiView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_wiki is not null)
        {
            _wiki.HeadingRequested -= OnHeadingRequested;
            _wiki.PropertyChanged -= OnWikiPropertyChanged;
        }

        _wiki = DataContext as WikiViewModel;
        if (_wiki is not null)
        {
            _wiki.HeadingRequested += OnHeadingRequested;
            _wiki.PropertyChanged += OnWikiPropertyChanged;
        }
    }

    /// <summary>Vergleich geöffnet (Versionen unten, Konflikt beim Speichern): die Unterschiede in den Blick holen.</summary>
    private void OnWikiPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WikiViewModel.HasDiff) && _wiki is { HasDiff: true })
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => DiffPanel.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>Überschrift im gezeichneten Inhalt suchen (in Versalien gesetzt) und in den sichtbaren Bereich holen.</summary>
    private void OnHeadingRequested(object? sender, string text)
    {
        var wanted = text.Trim().ToUpper(CultureInfo.CurrentCulture);
        var heading = Reader.GetVisualDescendants().OfType<SelectableTextBlock>()
            .FirstOrDefault(t => Avalonia.Automation.AutomationProperties.GetHeadingLevel(t) > 0 && string.Equals(t.Text?.Trim(), wanted, StringComparison.Ordinal));
        if (heading is not null)
        {
            heading.BringIntoView();
            heading.Focus();
        }
    }

    private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
    {
        if (_wiki is not { } wiki || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get("WikiPage_InsertImage.Content"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_WikiImageTypes")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"] },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        var props = await file.GetBasicPropertiesAsync();
        await using var stream = await file.OpenReadAsync();
        await wiki.InsertImageAsync(stream, file.Name, (long)(props.Size ?? (ulong)stream.Length));
        Editor.Focus();
        Editor.CaretIndex = Editor.Text?.Length ?? 0;
    }
}
