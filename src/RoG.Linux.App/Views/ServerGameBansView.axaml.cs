using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter „Spiel-Bans“: lädt beim ersten Anzeigen, holt Rückfragen ins Bild (Fokus ins Feld), Escape schließt sie.
/// </summary>
public sealed partial class ServerGameBansView : UserControl
{
    private ServerGameBansViewModel? _vm;

    public ServerGameBansView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => _ = _vm?.EnsureLoadedAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Prompt.PropertyChanged -= OnOverlayChanged;
            _vm.Token.PropertyChanged -= OnOverlayChanged;
            _vm.Confirmation.PropertyChanged -= OnOverlayChanged;
        }

        _vm = DataContext as ServerGameBansViewModel;
        if (_vm is not null)
        {
            _vm.Prompt.PropertyChanged += OnOverlayChanged;
            _vm.Token.PropertyChanged += OnOverlayChanged;
            _vm.Confirmation.PropertyChanged += OnOverlayChanged;
            if (VisualRoot is not null)
            {
                _ = _vm.EnsureLoadedAsync();
            }
        }
    }

    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "IsOpen" || _vm is null)
        {
            return;
        }

        Control? target = sender == _vm.Prompt && _vm.Prompt.IsOpen ? PromptPanel
            : sender == _vm.Token && _vm.Token.IsOpen ? TokenPanel
            : sender == _vm.Confirmation && _vm.Confirmation.IsOpen ? ConfirmBar
            : null;
        if (target is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                target.BringIntoView();
                if (target == PromptPanel)
                {
                    PromptReason.Focus();
                }
            });
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm?.CloseOverlay() == true)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
