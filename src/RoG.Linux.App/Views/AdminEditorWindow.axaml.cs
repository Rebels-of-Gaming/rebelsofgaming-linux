using Avalonia.Controls;
using Avalonia.Input;
using RoG.Core.Features.Admin;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Ein Bereich der Website-Verwaltung im eigenen Fenster (wie AdminEditorWindow der Windows-App); je Bereich höchstens
/// eins. Escape schließt eine offene Rückfrage, dann das Formular (schmal), sonst das Fenster; Strg+W schließt,
/// Strg+F springt in die Suche, Strg+N legt neu an, Strg+S speichert.
/// </summary>
public sealed partial class AdminEditorWindow : Window
{
    private static readonly Dictionary<string, AdminEditorWindow> s_open = new(StringComparer.Ordinal);

    public AdminEditorWindow()
    {
        InitializeComponent();
    }

    private AdminEditorWindow(AdminEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        TitleText.Text = viewModel.Title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        _ = viewModel.LoadAsync();
    }

    /// <summary>Offene Fenster je Bereich – Tests.</summary>
    internal static IReadOnlyDictionary<string, AdminEditorWindow> OpenWindows => s_open;

    /// <summary>Editor des Bereichs öffnen bzw. nach vorn holen.</summary>
    public static AdminEditorWindow OpenFor(AppServices services, AdminArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (!s_open.TryGetValue(area.Key, out var window))
        {
            window = new AdminEditorWindow(new AdminEditorViewModel(services, area));
            s_open[area.Key] = window;
            window.Closed += (_, _) => s_open.Remove(area.Key);
            window.Show();
        }

        RoG.Linux.App.App.BringToFront(window);
        return window;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not AdminEditorViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (vm.CloseOverlay())
            {
                return;
            }

            if (vm.HasForm && Panel.IsNarrow && vm.ShowList)
            {
                vm.CloseForm();
            }
            else
            {
                Close();
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            e.Handled = true;
            Close();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F && vm.ShowList)
        {
            e.Handled = true;
            SearchBox.Focus();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N && vm.CanCreate)
        {
            e.Handled = true;
            vm.NewCommand.Execute(null);
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S && vm.CanSave)
        {
            e.Handled = true;
            vm.SaveCommand.Execute(null);
        }
    }
}
