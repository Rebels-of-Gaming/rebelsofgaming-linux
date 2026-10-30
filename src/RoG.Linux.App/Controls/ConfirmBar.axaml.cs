using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RoG.Linux.App.Controls;

/// <summary>Zeigt einen <see cref="ViewModels.InlineConfirmViewModel"/> (DataContext); beim Öffnen Fokus auf „Abbrechen“.</summary>
public sealed partial class ConfirmBar : UserControl
{
    public ConfirmBar()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.BringIntoView();
                CancelButton.Focus();
            });
        }
    }
}
