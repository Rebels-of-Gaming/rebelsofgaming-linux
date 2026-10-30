using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>Hinweisbalken über der Seite: schmal steht der Knopf unter dem Text.</summary>
public sealed partial class AcceptanceBanner : UserControl
{
    public AcceptanceBanner()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < 560;
        Grid.SetRow(OpenButton, narrow ? 1 : 0);
        Grid.SetColumn(OpenButton, narrow ? 1 : 2);
        OpenButton.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(12, 0, 0, 0);
        OpenButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
    }
}
