using Avalonia.Controls;
using Avalonia.Input;

namespace RoG.Linux.App.Views;

/// <summary>Öffentliche Ban-Liste; Strg+F springt in die Suche.</summary>
public sealed partial class BansView : UserControl
{
    public BansView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            e.Handled = true;
            SearchBox.Focus();
            return;
        }

        base.OnKeyDown(e);
    }
}
