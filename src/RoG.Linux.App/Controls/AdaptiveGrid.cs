using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Kacheln, die sich auf die Breite verteilen (wie <c>UniformGridLayout ItemsStretch="Fill"</c> der Windows-App):
/// so viele Spalten, wie mit <see cref="MinItemWidth"/> passen, alle gleich breit; Zeilenhöhe = höchste Kachel der Zeile.
/// Unsichtbare Kinder belegen keinen Platz.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(MinItemWidth), 200);

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(Spacing), 16);

    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<AdaptiveGrid, int>(nameof(MaxColumns), int.MaxValue);

    static AdaptiveGrid()
    {
        AffectsMeasure<AdaptiveGrid>(MinItemWidthProperty, SpacingProperty, MaxColumnsProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Spaltenzahl für eine Breite (öffentlich für Tests).</summary>
    public static int ColumnsFor(double width, double minItemWidth, double spacing, int maxColumns, int count)
    {
        if (count == 0)
        {
            return 1;
        }

        var columns = double.IsInfinity(width) ? count : (int)Math.Floor((width + spacing) / (minItemWidth + spacing));
        return Math.Clamp(Math.Min(columns, Math.Min(count, maxColumns)), 1, int.MaxValue);
    }

    private List<Control> Visible() => Children.Where(c => c.IsVisible).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Visible();
        var columns = ColumnsFor(availableSize.Width, MinItemWidth, Spacing, MaxColumns, items.Count);
        var itemWidth = double.IsInfinity(availableSize.Width) ? MinItemWidth : Math.Max(0, (availableSize.Width - (Spacing * (columns - 1))) / columns);
        var height = 0.0;
        for (var start = 0; start < items.Count; start += columns)
        {
            var row = 0.0;
            foreach (var item in items.Skip(start).Take(columns))
            {
                item.Measure(new Size(itemWidth, double.PositiveInfinity));
                row = Math.Max(row, item.DesiredSize.Height);
            }

            height += row + (start > 0 ? Spacing : 0);
        }

        var width = double.IsInfinity(availableSize.Width) ? (itemWidth * columns) + (Spacing * (columns - 1)) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Visible();
        var columns = ColumnsFor(finalSize.Width, MinItemWidth, Spacing, MaxColumns, items.Count);
        var itemWidth = Math.Max(0, (finalSize.Width - (Spacing * (columns - 1))) / columns);
        var y = 0.0;
        for (var start = 0; start < items.Count; start += columns)
        {
            var rowItems = items.Skip(start).Take(columns).ToList();
            // Breite kann von der Messung abweichen (Bildlaufleiste ein/aus) – dann neu messen, sonst stimmen
            // umbrechende Beschriftungen und Zeilenhöhen nicht
            foreach (var item in rowItems)
            {
                item.Measure(new Size(itemWidth, double.PositiveInfinity));
            }

            var row = rowItems.Max(i => i.DesiredSize.Height);
            for (var c = 0; c < rowItems.Count; c++)
            {
                rowItems[c].Arrange(new Rect(c * (itemWidth + Spacing), y, itemWidth, row));
            }

            y += row + Spacing;
        }

        return finalSize;
    }
}
