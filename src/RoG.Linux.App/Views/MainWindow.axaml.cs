using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Hauptfenster. Tastatur: Strg+1 … Strg+9 öffnen die ersten Bereiche der Seitenleiste, F5 aktualisiert,
/// Escape schließt das Detail (Liste-Detail-Seiten), Tab/Umschalt+Tab und Pfeile wie gewohnt.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && Shell is { } shell)
        {
            shell.IsWide = Bounds.Width >= ShellViewModel.ExpandedThreshold;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Shell is not { IsShellVisible: true } shell)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < shell.MainItems.Count)
            {
                shell.Navigate(shell.MainItems[index]);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5 && shell.CurrentPage is LoadableViewModel page)
        {
            _ = page.LoadAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            switch (shell.CurrentPage)
            {
                case NewsViewModel news when news.IsDetailOpen:
                    news.CloseDetailCommand.Execute(null);
                    e.Handled = true;
                    break;
                case MessagesViewModel messages when messages.IsDetailOpen:
                    messages.CloseDetailCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}
