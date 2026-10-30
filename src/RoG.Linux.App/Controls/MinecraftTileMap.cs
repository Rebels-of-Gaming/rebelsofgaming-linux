using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RoG.Core.Features.Minecraft;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Weltkarte des Minecraft-Netzwerks (wie <c>MinecraftMapView</c> der Windows-App, Guide 48): Kacheln 512 × 512 nur aus
/// dem Speicher der Plattform – und nur die, die der Kachel-Index der sichtbaren Regionen nennt. Ziehen zum Verschieben,
/// Mausrad und +/−/Pfeiltasten/Pos1, Warps (gold), Spawn (hell) und Live-Spieler (grün, Kopf aus dem Speicher der
/// Plattform oder Initiale). Rechnung in <see cref="MapViewport"/> des Kerns; Norden oben.
/// DataContext: <see cref="MinecraftMapViewModel"/>.
/// </summary>
public sealed class MinecraftTileMap : UserControl
{
    private const double ZoomStep = 1.5;
    private const double KeyStep = 80;

    private readonly MapLayer _layer;
    private readonly TextBlock _coordinates;
    private readonly Border _coordinatesBox;
    private readonly Dictionary<(int Zoom, int X, int Z), Dictionary<(int X, int Z), string>> _regions = [];
    private readonly HashSet<(int Zoom, int X, int Z)> _loading = [];
    private MinecraftMapViewModel? _vm;
    private MapViewport _view = new(0);
    private MapWorld? _world;
    private IReadOnlyList<MapLivePlayer> _players = [];
    private Point? _dragFrom;
    private int _generation;

    public MinecraftTileMap()
    {
        _layer = new MapLayer(this);
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetName(this, Loc.Get("MinecraftMap_ViewName"));
        Bind(BackgroundProperty, this.GetResourceObservable("RoGBg2Brush"));

        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Spacing = 6 };
        buttons.Children.Add(MapButton("+", "Map_ZoomIn", () => ZoomAt(ZoomStep, null)));
        buttons.Children.Add(MapButton("−", "Map_ZoomOut", () => ZoomAt(1 / ZoomStep, null)));
        buttons.Children.Add(MapButton("⌂", "MinecraftMap_Spawn", GoToSpawn));

        _coordinates = new TextBlock
        {
            Classes = { "muted", "small" },
            Margin = new Thickness(10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        var coordinates = _coordinatesBox = new Border
        {
            Child = _coordinates,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Opacity = 0.9,
            IsVisible = false,
        };
        coordinates.Bind(Border.BackgroundProperty, coordinates.GetResourceObservable("RoGBgBrush"));

        var root = new Panel();
        root.Children.Add(_layer);
        root.Children.Add(buttons);
        root.Children.Add(coordinates);
        Content = root;
        SizeChanged += (_, e) =>
        {
            _view.Resize(e.NewSize.Width, e.NewSize.Height);
            _layer.InvalidateVisual();
        };
    }

    /// <summary>Sichtbereich (Tests).</summary>
    internal MapViewport Viewport => _view;

    /// <summary>Zuletzt gezeichnete Kacheln (Tests).</summary>
    internal int DrawnTiles { get; private set; }

    /// <summary>Angefragte Regionen des Kachel-Index (Tests).</summary>
    internal int LoadedRegions => _regions.Count;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.WorldChanged -= OnWorldChanged;
            _vm.PlayersChanged -= OnPlayersChanged;
            _vm.TilesReset -= OnTilesReset;
            _vm.CenterRequested -= OnCenterRequested;
        }

        _vm = DataContext as MinecraftMapViewModel;
        if (_vm is not null)
        {
            _vm.WorldChanged += OnWorldChanged;
            _vm.PlayersChanged += OnPlayersChanged;
            _vm.TilesReset += OnTilesReset;
            _vm.CenterRequested += OnCenterRequested;
            _players = _vm.LivePlayers;
            SetWorld(_vm.SelectedWorld?.World);
        }
    }

    private void OnWorldChanged(object? sender, MapWorldChange e) => SetWorld(e.World, e.X, e.Z);

    private void OnPlayersChanged(object? sender, IReadOnlyList<MapLivePlayer> players)
    {
        _players = players;
        _layer.InvalidateVisual();
    }

    private void OnTilesReset(object? sender, EventArgs e) => ReloadTiles();

    private void OnCenterRequested(object? sender, MapPointItem point)
    {
        CenterOn(point.X, point.Z);
        Focus();
    }

    /// <summary>Neue Welt: Kachel-Index verwerfen, auf <paramref name="x"/>/<paramref name="z"/> (sonst Spawn) zentrieren.</summary>
    public void SetWorld(MapWorld? world, int? x = null, int? z = null)
    {
        var same = _world is { } old && world is not null && old.Id == world.Id && old.TileUrl == world.TileUrl;
        _world = world;
        if (!same)
        {
            _generation++;
            _regions.Clear();
            _loading.Clear();
            _view = new MapViewport(world?.ZoomLevels ?? 0);
            _view.Resize(Bounds.Width, Bounds.Height);
            _view.SetScale(1);
            _view.CenterOn(x ?? world?.SpawnX ?? 0, z ?? world?.SpawnZ ?? 0);
        }
        else if (x is not null || z is not null)
        {
            _view.CenterOn(x ?? _view.CenterX, z ?? _view.CenterZ);
        }

        _layer.InvalidateVisual();
    }

    /// <summary>Kachel-Index neu laden (nach „Aktualisieren“, „Kacheln löschen“), ohne die Ansicht zu verschieben.</summary>
    public void ReloadTiles()
    {
        _generation++;
        _regions.Clear();
        _loading.Clear();
        _layer.InvalidateVisual();
    }

    public void CenterOn(double x, double z)
    {
        _view.CenterOn(x, z);
        _layer.InvalidateVisual();
    }

    /// <summary>Vergrößern um <paramref name="factor"/>; der Punkt unter <paramref name="at"/> bleibt stehen (ohne: Mitte).</summary>
    public void ZoomAt(double factor, Point? at)
    {
        var p = at ?? new Point(_view.Width / 2, _view.Height / 2);
        _view.ZoomAt(factor, p.X, p.Y);
        _layer.InvalidateVisual();
    }

    private void GoToSpawn()
    {
        if (_world is { } w)
        {
            CenterOn(w.SpawnX, w.SpawnZ);
        }
    }

    private static Button MapButton(string text, string nameKey, Action click)
    {
        var button = new Button
        {
            Classes = { "secondary" },
            Content = text,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, Loc.Get(nameKey));
        ToolTip.SetTip(button, Loc.Get(nameKey));
        button.Click += (_, _) => click();
        return button;
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
        var now = e.GetPosition(this);
        if (_world is not null)
        {
            var (bx, bz) = _view.ScreenToBlock(now.X, now.Y);
            _coordinates.Text = string.Format(CultureInfo.CurrentCulture, "x {0:0} · z {1:0}", Math.Floor(bx), Math.Floor(bz));
            _coordinatesBox.IsVisible = true;
        }

        if (_dragFrom is not { } from)
        {
            return;
        }

        _dragFrom = now;
        _view.Pan(now.X - from.X, now.Y - from.Y);
        _layer.InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            Cursor = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragFrom = null;
        Cursor = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragFrom is null)
        {
            _coordinatesBox.IsVisible = false;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y != 0)
        {
            ZoomAt(Math.Pow(ZoomStep, Math.Clamp(e.Delta.Y, -3, 3)), e.GetPosition(this));
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
            case Key.Left: _view.Pan(KeyStep, 0); break;
            case Key.Right: _view.Pan(-KeyStep, 0); break;
            case Key.Up: _view.Pan(0, KeyStep); break;
            case Key.Down: _view.Pan(0, -KeyStep); break;
            case Key.Add or Key.OemPlus: ZoomAt(ZoomStep, null); break;
            case Key.Subtract or Key.OemMinus: ZoomAt(1 / ZoomStep, null); break;
            case Key.Home: GoToSpawn(); break;
            default: return;
        }

        e.Handled = true;
        _layer.InvalidateVisual();
    }

    /// <summary>Region anfragen – außerhalb des Zeichnens (Antwort darf nicht während des Render-Durchgangs ankommen).</summary>
    private void RequestRegion(MapWorld world, (int Zoom, int X, int Z) region)
    {
        if (_vm is null || !_loading.Add(region))
        {
            return;
        }

        var generation = _generation;
        Dispatcher.UIThread.Post(() => LoadRegion(world, region, generation), DispatcherPriority.Background);
    }

    private async void LoadRegion(MapWorld world, (int Zoom, int X, int Z) region, int generation)
    {
        if (_vm is null || generation != _generation)
        {
            return;
        }

        IReadOnlyList<MapTileRef> tiles;
        try
        {
            tiles = await _vm.LoadTilesAsync(world.Id, region.Zoom, region.X, region.Z);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            tiles = []; // eine fehlende Region darf die Karte nicht kaputt machen – leer anzeigen
        }

        if (generation != _generation)
        {
            return; // inzwischen andere Welt bzw. neu geladen
        }

        _loading.Remove(region);
        _regions[region] = tiles.GroupBy(t => (t.X, t.Z)).ToDictionary(g => g.Key, g => g.First().Version);
        _layer.InvalidateVisual();
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) ? b as IBrush : null;

    /// <summary>Zeichnet Kacheln, Spawn, Warps und Live-Spieler; fehlende Kacheln und Regionen kommen nach.</summary>
    private sealed class MapLayer(MinecraftTileMap owner) : Control
    {
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var view = owner._view;
            if (owner._world is not { } world || owner._vm is not { } vm || view.Width <= 0 || view.Height <= 0)
            {
                return;
            }

            var zoom = view.TileZoom;
            var drawn = 0;
            foreach (var (x, z) in view.VisibleTiles())
            {
                var region = (zoom, MinecraftMapGeometry.RegionIndex(x, world.RegionSize), MinecraftMapGeometry.RegionIndex(z, world.RegionSize));
                if (!owner._regions.TryGetValue(region, out var index))
                {
                    owner.RequestRegion(world, region);
                    continue;
                }

                if (!index.TryGetValue((x, z), out var version) || MinecraftMapGeometry.TileUri(world.TileUrl, zoom, x, z, version) is not { } uri)
                {
                    continue;
                }

                if (vm.Tiles.TryGet(uri, out var bitmap) && bitmap is not null)
                {
                    var (left, top, size) = view.TileRect(zoom, x, z);
                    // halber Pixel gegen Haarlinien zwischen Kacheln; Pixel-Look beim Hochskalieren
                    using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = view.PixelsPerBlock > 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
                    {
                        context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(left, top, size + 0.5, size + 0.5));
                    }

                    drawn++;
                }
                else
                {
                    vm.Tiles.Request(uri, InvalidateVisual);
                }
            }

            owner.DrawnTiles = drawn;
            DrawOverlay(context, world, view, vm);
        }

        private void DrawOverlay(DrawingContext context, MapWorld world, MapViewport view, MinecraftMapViewModel vm)
        {
            var ink = owner.Brush("RoGInkBrush");
            var gold = owner.Brush("RoGGoldBrush");
            var green = owner.Brush("RoGGreenBrush");
            var bg = owner.Brush("RoGBgBrush");
            var bg3 = owner.Brush("RoGBg3Brush");
            var outline = bg is null ? null : new Pen(bg, 1.5);

            Diamond(context, view, world.SpawnX + 0.5, world.SpawnZ + 0.5, ink, outline, Loc.Get("MinecraftMap_Spawn"), ink, bg, showLabel: false);
            foreach (var marker in world.Markers)
            {
                Diamond(context, view, marker.X + 0.5, marker.Z + 0.5, gold, outline, marker.Label, gold, bg, showLabel: view.PixelsPerBlock >= 0.25);
            }

            foreach (var player in owner._players.Where(p => p.WorldId == world.Id || (p.WorldId is null && p.World == world.World)))
            {
                var (x, y) = view.BlockToScreen(player.X, player.Z);
                if (!OnScreen(view, x, y))
                {
                    continue;
                }

                var circle = new Rect(x - 11, y - 11, 22, 22);
                context.DrawEllipse(bg3, green is null ? null : new Pen(green, 2), circle);
                Bitmap? head = null;
                if (player.HeadUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    if (!vm.Tiles.TryGet(uri, out head))
                    {
                        vm.Tiles.Request(uri, InvalidateVisual);
                    }
                }

                if (head is not null)
                {
                    using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                    {
                        context.DrawImage(head, new Rect(head.Size), new Rect(x - 7, y - 7, 14, 14));
                    }
                }
                else if (ink is not null && player.Name.Length > 0)
                {
                    var initial = Text(player.Name[..1].ToUpperInvariant(), 11, ink, FontWeight.SemiBold);
                    context.DrawText(initial, new Point(x - (initial.Width / 2), y - (initial.Height / 2)));
                }

                Label(context, player.Bedrock ? player.Name + " · Bedrock" : player.Name, x + 15, y, green, bg);
            }
        }

        private static bool OnScreen(MapViewport view, double x, double y) => x >= -40 && y >= -40 && x <= view.Width + 40 && y <= view.Height + 40;

        private static void Diamond(DrawingContext context, MapViewport view, double bx, double bz, IBrush? fill, IPen? outline, string label, IBrush? text, IBrush? bg, bool showLabel)
        {
            var (x, y) = view.BlockToScreen(bx, bz);
            if (!OnScreen(view, x, y) || fill is null)
            {
                return;
            }

            const double r = 7;
            var shape = new PolylineGeometry([new Point(x, y - r), new Point(x + r, y), new Point(x, y + r), new Point(x - r, y)], true);
            context.DrawGeometry(fill, outline, shape);
            if (showLabel)
            {
                Label(context, label, x + 10, y, text, bg);
            }
        }

        private static void Label(DrawingContext context, string label, double x, double y, IBrush? text, IBrush? bg)
        {
            if (text is null)
            {
                return;
            }

            var formatted = Text(label, 12, text, FontWeight.SemiBold);
            var box = new Rect(x - 3, y - (formatted.Height / 2) - 1, formatted.Width + 6, formatted.Height + 2);
            if (bg is not null)
            {
                using (context.PushOpacity(0.75))
                {
                    context.DrawRectangle(bg, null, box, 3, 3);
                }
            }

            context.DrawText(formatted, new Point(x, y - (formatted.Height / 2)));
        }

        private static FormattedText Text(string text, double size, IBrush brush, FontWeight weight) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
    }
}
