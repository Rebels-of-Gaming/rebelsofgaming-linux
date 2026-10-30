using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>
/// Seitenleiste (fest links bzw. als Überlagerung in der schmalen Ansicht). Kompakt (36 px je Eintrag), damit alle
/// Bereiche bei 1280×800 (Steam Deck) ohne Scrollen passen; bei sehr niedrigen Fenstern scrollt die Hauptliste.
/// Im Spielmodus 44 px je Eintrag (Controller/Touch) – dann scrollt die Hauptliste auch bei 1280×800.
/// </summary>
public sealed partial class NavPane : UserControl
{
    public static readonly StyledProperty<bool> ShowLabelsProperty = AvaloniaProperty.Register<NavPane, bool>(nameof(ShowLabels), true);

    public NavPane()
    {
        InitializeComponent();
    }

    /// <summary>Beschriftungen zeigen (aufgeklappt) oder nur Symbole.</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }
}
