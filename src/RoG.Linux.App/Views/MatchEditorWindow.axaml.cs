using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Linux.App.Localization;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Match eintragen/bearbeiten im eigenen Fenster (wie <c>MatchEditorWindow</c> der Windows-App); je Match höchstens eins
/// (ein Fenster für „Neues Match“). Anhänge über die Dateiauswahl (FileChooser-Portal). Escape schließt eine
/// Rückfrage/Meldung bzw. das Fenster, Strg+W schließt. Unter 760 px steht das Line-up unter dem Formular.
/// </summary>
public sealed partial class MatchEditorWindow : Window
{
    private static readonly Dictionary<string, MatchEditorWindow> s_open = new(StringComparer.Ordinal);

    public MatchEditorWindow()
    {
        InitializeComponent();
    }

    internal MatchEditorWindow(MatchEditorViewModel editor)
        : this()
    {
        DataContext = editor;
    }

    /// <summary>Offene Editoren je Match-ID (leer = neues Match) – Tests.</summary>
    internal static IReadOnlyDictionary<string, MatchEditorWindow> OpenWindows => s_open;

    internal MatchEditorViewModel? Editor => DataContext as MatchEditorViewModel;

    /// <summary>Neues Match (<paramref name="matchId"/> <c>null</c>) oder vorhandenes bearbeiten; <paramref name="closed"/> bekommt „geändert“.</summary>
    public static MatchEditorWindow Open(AppServices services, string? matchId, Action<bool>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var key = matchId ?? string.Empty;
        if (!s_open.TryGetValue(key, out var window))
        {
            var editor = new MatchEditorViewModel(services);
            window = new MatchEditorWindow(editor);
            s_open[key] = window;
            var opened = window;
            window.Closed += (_, _) =>
            {
                if (s_open.TryGetValue(key, out var current) && current == opened)
                {
                    s_open.Remove(key);
                }

                closed?.Invoke(editor.Changed);
            };
            window.Show();
            _ = editor.StartAsync(matchId);
        }

        RoG.Linux.App.App.BringToFront(window);
        return window;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < 760;
        // Formular höchstens 640 px breit, Line-up daneben (32 Abstand, 320 breit); Rand 24 links/rechts + Scrollleiste
        FormColumn.Width = narrow ? Math.Max(0, e.NewSize.Width - 48 - 12) : Math.Min(640, e.NewSize.Width - 48 - 12 - 352);
        Layout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(320);
        Grid.SetColumn(LineupColumn, narrow ? 0 : 1);
        Grid.SetRow(LineupColumn, narrow ? 1 : 0);
        LineupColumn.Margin = narrow ? new Avalonia.Thickness(0, 24, 0, 0) : new Avalonia.Thickness(32, 0, 0, 0);
    }

    private async void OnAttachClick(object? sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor || StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.Get("MatchEdit_Attach.Content"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Linux_MatchAttachmentTypes")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.pdf", "*.zip", "*.7z"] },
            ],
        });
        var sources = new List<UploadSource>();
        foreach (var file in files)
        {
            var props = await file.GetBasicPropertiesAsync();
            sources.Add(new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync));
        }

        await editor.AttachAsync(sources);
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
