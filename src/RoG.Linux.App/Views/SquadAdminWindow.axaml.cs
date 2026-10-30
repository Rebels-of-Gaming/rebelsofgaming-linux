using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoG.Core.Features.Squads;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Squad verwalten im eigenen Fenster (wie <c>SquadAdminWindow</c> der Windows-App); je Squad höchstens eins – erneutes
/// Öffnen holt es nach vorn. Escape schließt Vorschläge bzw. Rückfrage, sonst das Fenster; Strg+W schließt. Nach dem
/// Schließen lädt die Squad-Seite neu, wenn etwas geändert wurde.
/// </summary>
public sealed partial class SquadAdminWindow : Window
{
    private static readonly Dictionary<string, SquadAdminWindow> s_open = new(StringComparer.Ordinal);

    public SquadAdminWindow()
    {
        InitializeComponent();
    }

    /// <summary>Fenster zu einem ViewModel (Tests, <see cref="Open"/>).</summary>
    internal SquadAdminWindow(SquadAdminViewModel admin)
        : this()
    {
        DataContext = admin;
        TitleText.Text = admin.Title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Opened += (_, _) => _ = admin.StartAsync();
        Closed += (_, _) =>
        {
            if (s_open.TryGetValue(admin.Slug, out var open) && open == this)
            {
                s_open.Remove(admin.Slug);
            }

            admin.NotifyClosed();
        };
    }

    /// <summary>Offene Verwaltungsfenster (Tests).</summary>
    internal static IReadOnlyCollection<SquadAdminWindow> OpenWindows => s_open.Values;

    internal SquadAdminViewModel? Admin => DataContext as SquadAdminViewModel;

    /// <summary>„Verwalten“ auf der Squad-Seite: Fenster öffnen bzw. das offene des Squads nach vorn holen.</summary>
    public static SquadAdminWindow Open(SquadAdminViewModel admin)
    {
        ArgumentNullException.ThrowIfNull(admin);
        if (!s_open.TryGetValue(admin.Slug, out var window))
        {
            window = new SquadAdminWindow(admin);
            s_open[admin.Slug] = window;
            window.Show();
        }

        RoG.Linux.App.App.BringToFront(window);
        return window;
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
            if (Admin?.HandleEscape() != true)
            {
                Close();
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && e.Source == MemberBox && Admin is { } admin)
        {
            e.Handled = true;
            if (admin.Suggestions.Count > 0 && admin.NewMember is null)
            {
                admin.Suggestions[0].ChooseCommand.Execute(null);
            }
            else if (admin.AddMemberCommand.CanExecute(null))
            {
                admin.AddMemberCommand.Execute(null);
            }
        }
    }

    private async void OnSetLogoClick(object? sender, RoutedEventArgs e) => await PickAsync(SquadImage.Logo);

    private async void OnSetHeaderClick(object? sender, RoutedEventArgs e) => await PickAsync(SquadImage.Header);

    private async Task PickAsync(SquadImage kind)
    {
        if (Admin is not { } admin || StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Get(kind == SquadImage.Logo ? "SquadAdmin_SetLogo.Content" : "SquadAdmin_SetHeader.Content"),
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        var props = await file.GetBasicPropertiesAsync();
        await admin.SetImageAsync(kind, new UploadSource(file.Name, (long)(props.Size ?? 0), file.OpenReadAsync));
    }
}
