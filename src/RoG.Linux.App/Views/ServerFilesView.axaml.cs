using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter „Dateien“: lädt beim ersten Anzeigen, Escape schließt Eingabe/Rückfrage/Vorschau, Eingabe übernimmt den
/// Namen, Rücktaste geht einen Ordner nach oben. Ab 760 px Liste und Vorschau nebeneinander, schmaler zeigt die
/// geöffnete Vorschau statt der Liste.
/// </summary>
public sealed partial class ServerFilesView : UserControl
{
    private const double WideFrom = 760;
    private ServerFilesViewModel? _files;

    public ServerFilesView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = _files?.EnsureLoadedAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_files is not null)
        {
            _files.PropertyChanged -= OnFilesChanged;
            _files.Prompt.PropertyChanged -= OnPromptChanged;
        }

        _files = DataContext as ServerFilesViewModel;
        if (_files is not null)
        {
            _files.PropertyChanged += OnFilesChanged;
            _files.Prompt.PropertyChanged += OnPromptChanged;
            if (VisualRoot is not null)
            {
                _ = _files.EnsureLoadedAsync();
            }
        }

        UpdateLayoutMode(Bounds.Width);
    }

    private void OnFilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerFilesViewModel.HasPreview))
        {
            UpdateLayoutMode(Bounds.Width);
        }
    }

    private void OnPromptChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InlinePromptViewModel.IsOpen) && _files?.Prompt.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PromptBox.Focus();
                PromptBox.SelectAll();
            });
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateLayoutMode(e.NewSize.Width);
    }

    private void UpdateLayoutMode(double width)
    {
        var wide = width <= 0 || width >= WideFrom;
        var preview = _files?.HasPreview == true;
        Body.ColumnDefinitions = wide ? new ColumnDefinitions("*,*") : new ColumnDefinitions("*");
        Grid.SetColumn(PreviewArea, wide ? 1 : 0);
        PreviewArea.Margin = wide ? new Thickness(16, 0, 0, 0) : default;
        PreviewArea.IsVisible = wide || preview;
        ListArea.IsVisible = wide || !preview;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _files is not { } files)
        {
            return;
        }

        if (e.Key == Key.Escape && files.CloseOverlay())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && files.Prompt.IsOpen && e.Source == PromptBox)
        {
            e.Handled = true;
            files.Prompt.AcceptCommand.Execute(null);
        }
        else if (e.Key == Key.Back && e.Source is not TextBox && files.UpCommand.CanExecute(null))
        {
            e.Handled = true;
            _ = files.UpCommand.ExecuteAsync(null);
        }
    }
}
