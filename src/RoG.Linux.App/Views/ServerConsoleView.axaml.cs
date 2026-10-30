using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Konsole: folgt dem Ende, solange man unten ist (hochscrollen hält an, „Zum Ende“ springt zurück), Strg+C kopiert die
/// markierten Zeilen, ↑/↓ in der Eingabe blättert durch den Verlauf, Enter sendet.
/// </summary>
public sealed partial class ServerConsoleView : UserControl
{
    private ServerConsoleViewModel? _vm;
    private ScrollViewer? _scroller;
    private bool _scrollPending;

    public ServerConsoleView()
    {
        InitializeComponent();
        Output.TemplateApplied += (_, _) => AttachScroller();
        Output.KeyDown += OnOutputKeyDown;
    }

    private ServerConsoleViewModel? Console => DataContext as ServerConsoleViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.LinesAdded -= OnLinesAdded;
        }

        _vm = Console;
        if (_vm is not null)
        {
            _vm.LinesAdded += OnLinesAdded;
            ScrollToEndSoon();
        }
    }

    /// <summary>Eingabe fokussieren (Fenster geöffnet, Reiter gewechselt).</summary>
    public void FocusCommand()
    {
        if (CommandBox.IsEnabled)
        {
            CommandBox.Focus();
        }
        else
        {
            Output.Focus();
        }
    }

    private void AttachScroller()
    {
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
        }

        _scroller = Output.Scroll as ScrollViewer;
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }
    }

    // Nur Bewegungen ohne neue Zeilen sind Nutzer-Scrollen: dann merken, ob man (wieder) unten ist
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_vm is null || _scroller is null || e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            return;
        }

        _vm.IsFollowing = IsAtEnd(_scroller);
    }

    private static bool IsAtEnd(ScrollViewer scroller) =>
        scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 24;

    private void OnLinesAdded(object? sender, EventArgs e)
    {
        if (_vm?.IsFollowing == true)
        {
            ScrollToEndSoon();
        }
    }

    // Viele Zeilen auf einmal: nur einmal je Layout-Durchgang scrollen
    private void ScrollToEndSoon()
    {
        if (_scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            if (_vm is { Lines.Count: > 0 } vm)
            {
                Output.ScrollIntoView(vm.Lines.Count - 1);
                _scroller?.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    private void OnJumpToEnd(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.IsFollowing = true;
        Output.SelectedItems?.Clear();
        ScrollToEndSoon();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e) => Copy();

    private void Copy() => _vm?.CopyLines(SelectedLines());

    private List<string> SelectedLines()
    {
        if (_vm is null || Output.Selection.SelectedIndexes.Count == 0)
        {
            return [];
        }

        // in der Reihenfolge der Ausgabe, nicht der Auswahl
        return Output.Selection.SelectedIndexes.Order().Where(i => i < _vm.Lines.Count).Select(i => _vm.Lines[i]).ToList();
    }

    private void OnOutputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            e.Handled = true;
            Copy();
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            e.Handled = true;
            Output.SelectAll();
        }
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when _vm.SendCommand.CanExecute(null):
                e.Handled = true;
                _vm.IsFollowing = true;
                _vm.SendCommand.Execute(null);
                break;
            case Key.Up when _vm.PreviousCommand() is not null:
                e.Handled = true;
                CommandBox.CaretIndex = _vm.Command.Length;
                break;
            case Key.Down:
                e.Handled = true;
                _vm.NextCommand();
                CommandBox.CaretIndex = _vm.Command.Length;
                break;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
        {
            _vm.LinesAdded -= OnLinesAdded;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_vm is not null)
        {
            _vm.LinesAdded -= OnLinesAdded;
            _vm.LinesAdded += OnLinesAdded;
        }
    }
}
