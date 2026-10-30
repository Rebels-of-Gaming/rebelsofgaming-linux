using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter „Configs“: lädt beim ersten Anzeigen, Escape schließt Rückfrage/Formular, Strg+S speichert (mit Diff und
/// Rückfrage), „Aus Datei laden“ über den Datei-Dialog des Systems (unter Linux das FileChooser-Portal, auch in
/// Flatpak ohne Dateisystem-Recht). Unter 900 px stehen Editor und Seitenspalte untereinander.
/// </summary>
public sealed partial class ServerConfigsView : UserControl
{
    private const double WideFrom = 900;
    private const double SaveBarWideFrom = 700;

    public ServerConfigsView()
    {
        InitializeComponent();
    }

    private ServerConfigsViewModel? Configs => DataContext as ServerConfigsViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = Configs?.EnsureLoadedAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            _ = Configs?.EnsureLoadedAsync();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var wide = e.NewSize.Width >= WideFrom;
        Body.ColumnDefinitions = wide ? new ColumnDefinitions("*,360") : new ColumnDefinitions("*");
        Body.RowDefinitions = wide ? new RowDefinitions("*") : new RowDefinitions("3*,2*");
        Grid.SetColumn(Side, wide ? 1 : 0);
        Grid.SetRow(Side, wide ? 0 : 1);
        Side.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 16, 0, 0);

        // Speichern-Leiste: breit eine Zeile, schmal Notiz oben, darunter Auswahl und Knöpfe
        var bar = e.NewSize.Width >= SaveBarWideFrom;
        SaveBar.ColumnDefinitions = bar ? new ColumnDefinitions("*,Auto,Auto,Auto") : new ColumnDefinitions("*,Auto,Auto");
        SaveBar.RowDefinitions = bar ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        var children = SaveBar.Children;
        Grid.SetColumnSpan(children[0], bar ? 1 : 3);
        Grid.SetRow(children[1], bar ? 0 : 1);
        Grid.SetColumn(children[1], bar ? 1 : 0);
        children[1].Margin = bar ? new Thickness(12, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        ((ComboBox)children[1]).HorizontalAlignment = bar ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        ((ComboBox)children[1]).MinWidth = bar ? 200 : 0;
        for (var i = 2; i < 4; i++)
        {
            Grid.SetRow(children[i], bar ? 0 : 1);
            Grid.SetColumn(children[i], bar ? i : i - 1);
            children[i].Margin = bar ? new Thickness(12, 0, 0, 0) : new Thickness(12, 10, 0, 0);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Configs is not { } configs)
        {
            return;
        }

        if (e.Key == Key.Escape && configs.CloseOverlay())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control && configs.SaveCommand.CanExecute(null))
        {
            e.Handled = true;
            _ = configs.SaveCommand.ExecuteAsync(null);
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (Configs is not { } configs || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get("Linux_ConfigImportPicker"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_ConfigImportTypes"))
                {
                    Patterns = ["*.cfg", "*.txt", "*.json", "*.yml", "*.yaml", "*.properties", "*.ini", "*.conf", "*.bak", "*.old"],
                    MimeTypes = ["text/plain", "application/json", "application/yaml"],
                },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        var props = await file.GetBasicPropertiesAsync();
        await configs.ImportAsync(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync);
        Editor.Focus();
    }
}
