using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RoG.Core.Features.Map;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Schlanke Kachelkarte für die Mitgliederkarte (wie <c>MemberMapView</c> der Windows-App): Kacheln nur aus der
/// signierten Vorlage der Plattform (<see cref="MemberMapViewModel.Tiles"/>), Ziehen zum Verschieben, Mausrad und
/// +/−/Pfeiltasten, nahe Orte als Cluster mit Zahl. Klick (oder Enter) auf einen Punkt zeigt die Namen daneben.
/// Rechnung in <see cref="WebMercator"/> (Kern). DataContext: <see cref="MemberMapViewModel"/>.
/// </summary>
public sealed class MemberMapView : UserControl
{
    /// <summary>Orte, deren Punkte näher als das (Bildschirmpixel) liegen, werden zusammengefasst.</summary>
    public const double ClusterRadius = 44;

    private const double KeyStep = 80;

    private readonly TileLayer _tileLayer;
    private readonly Canvas _pinLayer = new() { ClipToBounds = true };
    private readonly Button _attribution;
    private readonly List<(MapCluster Cluster, Button Button)> _buttons = [];
    private MemberMapViewModel? _vm;
    private MemberMap? _map;
    private int _clusterZoom = -1;
    private bool _fitted;
    private Point? _dragFrom;

    public MemberMapView()
    {
        _tileLayer = new TileLayer(this);
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetName(this, Loc.Get("Linux_MapHint"));
        Bind(BackgroundProperty, this.GetResourceObservable("RoGBg2Brush"));

        var zoom = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Spacing = 6 };
        zoom.Children.Add(ZoomButton("+", 1, "Map_ZoomIn"));
        zoom.Children.Add(ZoomButton("−", -1, "Map_ZoomOut"));

        _attribution = new Button
        {
            Classes = { "link" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
            Padding = new Thickness(8, 3),
            FontSize = 12,
            IsVisible = false,
        };
        _attribution.Bind(Button.BackgroundProperty, _attribution.GetResourceObservable("RoGBgBrush"));
        _attribution.Click += (_, _) => _vm?.OpenAttributionCommand.Execute(null);

        var root = new Panel();
        root.Children.Add(_tileLayer);
        root.Children.Add(_pinLayer);
        root.Children.Add(zoom);
        root.Children.Add(_attribution);
        Content = root;
        SizeChanged += (_, _) => Update();
    }

    /// <summary>Aktuelle Zoomstufe (Tests).</summary>
    public int Zoom { get; private set; } = 4;

    /// <summary>Mitte des Ausschnitts in Weltpixeln der aktuellen Zoomstufe.</summary>
    public Point Center { get; private set; }

    /// <summary>Sichtbare Punkte/Cluster (Tests, Screenreader).</summary>
    internal IReadOnlyList<(MapCluster Cluster, Button Button)> Markers => _buttons;

    private int TileSize => _map?.Tiles?.TileSize ?? 256;

    private int MinZoom => _map?.Tiles?.MinZoom ?? 2;

    private int MaxZoom => _map?.Tiles?.MaxZoom ?? 12;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }

        _vm = DataContext as MemberMapViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
        }

        SetMap(_vm?.Map);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemberMapViewModel.Map))
        {
            SetMap(_vm?.Map);
        }
    }

    private void SetMap(MemberMap? map)
    {
        _map = map;
        _clusterZoom = -1;
        _attribution.Content = map?.Tiles?.Attribution;
        _attribution.IsVisible = !string.IsNullOrEmpty(map?.Tiles?.Attribution);
        if (map is not null && !_fitted && Bounds.Width > 0)
        {
            Fit();
        }

        Update();
    }

    /// <summary>Auf alle Orte einpassen (beim ersten Laden).</summary>
    public void Fit()
    {
        if (_map is null)
        {
            return;
        }

        var width = Bounds.Width > 0 ? Bounds.Width : 1000;
        var height = Bounds.Height > 0 ? Bounds.Height : 700;
        var (zoom, x, y) = WebMercator.Fit(_map.Pins.Select(p => (p.Lat, p.Lng)), width, height, MinZoom, MaxZoom, TileSize);
        Zoom = zoom;
        Center = new Point(x, y);
        _clusterZoom = -1;
        _fitted = true;
        Update();
    }

    /// <summary>Zoomstufe ändern, der Punkt unter <paramref name="at"/> bleibt stehen (ohne Punkt: Mitte).</summary>
    public void ZoomBy(int delta, Point? at = null)
    {
        var zoom = Math.Clamp(Zoom + delta, MinZoom, MaxZoom);
        if (zoom == Zoom)
        {
            return;
        }

        var p = at ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var factor = Math.Pow(2, zoom - Zoom);
        var dx = p.X - (Bounds.Width / 2);
        var dy = p.Y - (Bounds.Height / 2);
        Center = new Point(((Center.X + dx) * factor) - dx, ((Center.Y + dy) * factor) - dy);
        Zoom = zoom;
        Update();
    }

    public void PanBy(double dx, double dy)
    {
        Center = new Point(Center.X + dx, Center.Y + dy);
        Update();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _dragFrom = e.GetPosition(this);
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from)
        {
            return;
        }

        var now = e.GetPosition(this);
        _dragFrom = now;
        PanBy(from.X - now.X, from.Y - now.Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragFrom = null;
        Cursor = null;
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragFrom is null)
        {
            return;
        }

        _dragFrom = null;
        Cursor = null;
        pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y != 0)
        {
            ZoomBy(e.Delta.Y > 0 ? 1 : -1, e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left: PanBy(-KeyStep, 0); break;
            case Key.Right: PanBy(KeyStep, 0); break;
            case Key.Up: PanBy(0, -KeyStep); break;
            case Key.Down: PanBy(0, KeyStep); break;
            case Key.Add or Key.OemPlus: ZoomBy(1); break;
            case Key.Subtract or Key.OemMinus: ZoomBy(-1); break;
            default: return;
        }

        e.Handled = true;
    }

    private Button ZoomButton(string text, int delta, string nameKey)
    {
        var button = new Button
        {
            Classes = { "secondary" },
            Content = text,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, Loc.Get(nameKey));
        ToolTip.SetTip(button, Loc.Get(nameKey));
        button.Click += (_, _) => ZoomBy(delta);
        return button;
    }

    private void Update()
    {
        _tileLayer.InvalidateVisual();
        if (_map is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            ClearMarkers();
            return;
        }

        if (!_fitted)
        {
            Fit();
            return;
        }

        // nicht über Nord-/Südrand hinaus verschieben
        var world = WebMercator.WorldSize(Zoom, TileSize);
        var half = Bounds.Height / 2;
        var y = Math.Clamp(Center.Y, Math.Min(half, world / 2), Math.Max(world - half, world / 2));
        if (y != Center.Y)
        {
            Center = new Point(Center.X, y);
        }

        if (_clusterZoom != Zoom)
        {
            RebuildMarkers();
        }

        var originX = Center.X - (Bounds.Width / 2);
        var originY = Center.Y - (Bounds.Height / 2);
        foreach (var (cluster, button) in _buttons)
        {
            var px = cluster.X + (Math.Round((Center.X - cluster.X) / world) * world); // nächste Weltkopie
            var sx = px - originX;
            var sy = cluster.Y - originY;
            var size = button.Width;
            button.IsVisible = sx > -size && sy > -size && sx < Bounds.Width + size && sy < Bounds.Height + size;
            Canvas.SetLeft(button, sx - (size / 2));
            Canvas.SetTop(button, sy - (size / 2));
        }
    }

    private void ClearMarkers()
    {
        _buttons.Clear();
        _pinLayer.Children.Clear();
        _clusterZoom = -1;
    }

    private void RebuildMarkers()
    {
        _buttons.Clear();
        _pinLayer.Children.Clear();
        _clusterZoom = Zoom;
        if (_map is null)
        {
            return;
        }

        foreach (var cluster in MapClustering.Build(_map.Pins, Zoom, TileSize, ClusterRadius).OrderBy(c => c.Y))
        {
            var button = Marker(cluster);
            _buttons.Add((cluster, button));
            _pinLayer.Children.Add(button);
        }
    }

    private Button Marker(MapCluster cluster)
    {
        var count = cluster.Count;
        var size = Math.Round(28 + (Math.Min(count, 30) * 0.8) + (cluster.Pins.Count > 1 ? 6 : 0));
        var grid = new Panel { Width = size, Height = size };
        if (cluster.Pins.Count > 1)
        {
            // mehrere Orte: heller Hof um den Punkt
            var halo = new Ellipse { Opacity = 0.35 };
            halo.Bind(Shape.FillProperty, halo.GetResourceObservable("RoGRedBrush"));
            grid.Children.Add(halo);
        }

        var dot = new Ellipse { StrokeThickness = 2, Margin = new Thickness(cluster.Pins.Count > 1 ? 5 : 0) };
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable("RoGActionRedBrush"));
        dot.Bind(Shape.StrokeProperty, dot.GetResourceObservable("RoGInkBrush"));
        grid.Children.Add(dot);
        var text = new TextBlock
        {
            Text = count.ToString(CultureInfo.CurrentCulture),
            FontWeight = FontWeight.Bold,
            FontSize = count > 99 ? 11 : 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("RoGOnAccentBrush"));
        grid.Children.Add(text);

        var button = new Button
        {
            Content = grid,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2),
            Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
            }),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var label = $"{cluster.Label}: {count}";
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, cluster.Label);
        button.Click += (_, _) => _vm?.ShowCluster(cluster);
        return button;
    }

    /// <summary>Zeichnet die Kacheln; fehlende kommen aus dem Speicher nach (bis dahin die gröbere Stufe, falls da).</summary>
    private sealed class TileLayer(MemberMapView owner) : Control
    {
        // Größenänderung zeichnet nicht von selbst neu – sonst fehlen nach dem Vergrößern die Kacheln am Rand
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var map = owner._map;
            var vm = owner._vm;
            if (map?.Tiles is not { } tiles || vm is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }

            var size = tiles.TileSize;
            var zoom = owner.Zoom;
            var center = owner.Center;
            var originX = center.X - (Bounds.Width / 2);
            var originY = center.Y - (Bounds.Height / 2);
            var placeholder = this.TryFindResource("RoGBg3Brush", ActualThemeVariant, out var b) ? b as IBrush : null;
            foreach (var (x, y, column) in WebMercator.VisibleTiles(center.X, center.Y, Bounds.Width, Bounds.Height, zoom, size))
            {
                var dest = new Rect((column * size) - originX, (y * size) - originY, size, size);
                var url = WebMercator.TileUrl(tiles.UrlTemplate, zoom, x, y);
                if (vm.Tiles.TryGet(url, out var bitmap) && bitmap is not null)
                {
                    context.DrawImage(bitmap, new Rect(bitmap.Size), dest);
                    continue;
                }

                vm.Tiles.Request(url, InvalidateVisual);
                if (zoom > tiles.MinZoom
                    && vm.Tiles.TryGet(WebMercator.TileUrl(tiles.UrlTemplate, zoom - 1, x / 2, y / 2), out var parent)
                    && parent is Bitmap coarse)
                {
                    var half = coarse.Size.Width / 2;
                    context.DrawImage(coarse, new Rect((x % 2) * half, (y % 2) * half, half, half), dest);
                }
                else if (placeholder is not null)
                {
                    context.DrawRectangle(placeholder, null, dest.Deflate(0.5));
                }
            }
        }
    }
}
