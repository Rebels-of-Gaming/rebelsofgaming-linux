using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Zeigt einen <see cref="ReportFormViewModel"/> (DataContext): beim Öffnen ins Bild holen und Fokus auf den Grund,
/// nach dem Senden auf „Schließen“; Escape schließt das Formular.
/// </summary>
public sealed partial class ReportForm : UserControl
{
    private ReportFormViewModel? _vm;

    public ReportForm()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
        }

        _vm = DataContext as ReportFormViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnChanged;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (e.PropertyName == nameof(ReportFormViewModel.IsOpen) && _vm.IsOpen)
        {
            // nach dem Layout, sonst ist das Formular noch ohne Größe
            Dispatcher.UIThread.Post(() =>
            {
                this.BringIntoView();
                ReasonBox.Focus();
            }, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(ReportFormViewModel.IsSent) && _vm.IsSent)
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.BringIntoView();
                DoneButton.Focus();
            }, DispatcherPriority.Background);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm?.Close() == true)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
