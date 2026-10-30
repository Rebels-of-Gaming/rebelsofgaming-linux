using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Liste und Detail nebeneinander (ab <see cref="WideThreshold"/>), schmal nur eines von beiden – wie
/// <c>ListDetailPanel</c> der Windows-App. Erstes Kind = Liste (Trennlinie rechts gibt die Seite vor), zweites = Detail.
/// </summary>
public sealed class ListDetailPanel : Panel
{
    public static readonly StyledProperty<bool> IsDetailOpenProperty = AvaloniaProperty.Register<ListDetailPanel, bool>(nameof(IsDetailOpen));

    public static readonly StyledProperty<double> ListWidthProperty = AvaloniaProperty.Register<ListDetailPanel, double>(nameof(ListWidth), 380);

    public static readonly StyledProperty<double> WideThresholdProperty = AvaloniaProperty.Register<ListDetailPanel, double>(nameof(WideThreshold), 760);

    public static readonly DirectProperty<ListDetailPanel, bool> IsNarrowProperty =
        AvaloniaProperty.RegisterDirect<ListDetailPanel, bool>(nameof(IsNarrow), p => p.IsNarrow);

    private bool _isNarrow;

    static ListDetailPanel()
    {
        AffectsMeasure<ListDetailPanel>(IsDetailOpenProperty, ListWidthProperty, WideThresholdProperty);
    }

    public bool IsDetailOpen
    {
        get => GetValue(IsDetailOpenProperty);
        set => SetValue(IsDetailOpenProperty, value);
    }

    public double ListWidth
    {
        get => GetValue(ListWidthProperty);
        set => SetValue(ListWidthProperty, value);
    }

    public double WideThreshold
    {
        get => GetValue(WideThresholdProperty);
        set => SetValue(WideThresholdProperty, value);
    }

    /// <summary>Nur eine Spalte sichtbar (Zurück-Knopf im Detail zeigen).</summary>
    public bool IsNarrow
    {
        get => _isNarrow;
        private set => SetAndRaise(IsNarrowProperty, ref _isNarrow, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        IsNarrow = !double.IsInfinity(availableSize.Width) && availableSize.Width < WideThreshold;
        var (list, detail) = Parts();
        if (IsNarrow)
        {
            Show(list, !IsDetailOpen);
            Show(detail, IsDetailOpen);
            list?.Measure(availableSize);
            detail?.Measure(availableSize);
        }
        else
        {
            Show(list, true);
            Show(detail, true);
            list?.Measure(availableSize.WithWidth(ListWidth));
            detail?.Measure(availableSize.WithWidth(Math.Max(0, availableSize.Width - ListWidth)));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (list, detail) = Parts();
        if (IsNarrow)
        {
            list?.Arrange(new Rect(finalSize));
            detail?.Arrange(new Rect(finalSize));
        }
        else
        {
            list?.Arrange(new Rect(0, 0, ListWidth, finalSize.Height));
            detail?.Arrange(new Rect(ListWidth, 0, Math.Max(0, finalSize.Width - ListWidth), finalSize.Height));
        }

        return finalSize;
    }


    private (Control? List, Control? Detail) Parts() =>
        (Children.Count > 0 ? Children[0] : null, Children.Count > 1 ? Children[1] : null);

    private static void Show(Control? control, bool visible)
    {
        if (control is not null && control.IsVisible != visible)
        {
            control.IsVisible = visible;
        }
    }
}
