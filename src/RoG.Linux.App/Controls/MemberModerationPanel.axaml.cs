using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Controls;

/// <summary>Zeigt einen <see cref="MemberModerationViewModel"/>: beim Öffnen ins Bild holen, Fokus in den Grund; Escape schließt.</summary>
public sealed partial class MemberModerationPanel : UserControl
{
    private MemberModerationViewModel? _vm;

    public MemberModerationPanel()
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

        _vm = DataContext as MemberModerationViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnChanged;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemberModerationViewModel.IsOpen) && _vm?.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() => this.BringIntoView(), DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(MemberModerationViewModel.IsLoaded) && _vm?.IsLoaded == true && _vm.CanAct)
        {
            Dispatcher.UIThread.Post(() => ReasonBox.Focus());
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
