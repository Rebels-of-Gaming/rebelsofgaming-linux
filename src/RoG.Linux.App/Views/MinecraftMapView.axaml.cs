using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RoG.Core.Features.Minecraft;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Weltkarte: Live-Spieler alle 5 s, solange die Seite sichtbar ist (Guide 48). Ab <see cref="WideFrom"/> px Karte und
/// Seitenleiste nebeneinander, darunter übereinander (Karte oben). Escape schließt die Rückfrage „Kacheln löschen“.
/// </summary>
public sealed partial class MinecraftMapView : UserControl
{
    /// <summary>Ab dieser Breite (Inhalt) steht die Seitenleiste rechts neben der Karte.</summary>
    public const double WideFrom = 820;

    private readonly DispatcherTimer _live = new() { Interval = MinecraftService.LiveInterval };

    public MinecraftMapView()
    {
        InitializeComponent();
        _live.Tick += async (_, _) =>
        {
            if (DataContext is MinecraftMapViewModel map && IsEffectivelyVisible)
            {
                await map.RefreshLiveAsync();
            }
        };
    }

    /// <summary>Die Karte (Tests).</summary>
    internal Controls.MinecraftTileMap MapControl => Map;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _live.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _live.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var wide = e.NewSize.Width >= WideFrom;
        Body.ColumnDefinitions = wide ? new ColumnDefinitions("*,300") : new ColumnDefinitions("*");
        Body.RowDefinitions = wide ? new RowDefinitions("*") : new RowDefinitions("3*,2*");
        Grid.SetColumn(Side, wide ? 1 : 0);
        Grid.SetRow(Side, wide ? 0 : 1);
        Side.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 16, 0, 0);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && DataContext is MinecraftMapViewModel { Confirm.IsOpen: true } map)
        {
            map.Confirm.Cancel();
            e.Handled = true;
        }
    }
}
