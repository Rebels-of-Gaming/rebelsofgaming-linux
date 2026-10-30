using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RoG.Core.Features.Members;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Rundes Profilbild mit Anfangsbuchstaben als Platzhalter und optionalem Status-Abzeichen unten rechts. Der Status ist
/// nicht nur Farbe, sondern auch Form (Barrierefreiheit): online = voller Punkt, Voice = Headset, RoG-Server =
/// Server-Symbol, im Spiel = Controller, offline = leerer Ring. Farben aus dem Theme (<c>RoGStatus*Brush</c>):
/// grün online · blau Voice · rot auf RoG-Server · gold im Spiel · grau offline. Tooltip/Name = Status-Text.
/// </summary>
public sealed class AvatarView : UserControl
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<AvatarView, IImage?>(nameof(Source));

    public static readonly StyledProperty<string?> DisplayNameProperty = AvaloniaProperty.Register<AvatarView, string?>(nameof(DisplayName));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<AvatarView, double>(nameof(Size), 44);

    public static readonly StyledProperty<Presence?> PresenceProperty = AvaloniaProperty.Register<AvatarView, Presence?>(nameof(Presence));

    public static readonly StyledProperty<string?> PresenceTextProperty = AvaloniaProperty.Register<AvatarView, string?>(nameof(PresenceText));

    private readonly Border _placeholder;
    private readonly TextBlock _initial;
    private readonly Border _imageClip;
    private readonly Image _image;
    private readonly Border _badge;
    private readonly PathIcon _badgeIcon;
    private IDisposable? _badgeBrush;

    public AvatarView()
    {
        _initial = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _initial.Bind(TextBlock.FontFamilyProperty, _initial.GetResourceObservable("RoGDisplayFont"));
        _initial.Bind(TextBlock.ForegroundProperty, _initial.GetResourceObservable("RoGMutedBrush"));
        _placeholder = new Border { BorderThickness = new Thickness(1), Child = _initial };
        _placeholder.Bind(Border.BackgroundProperty, _placeholder.GetResourceObservable("RoGBg3Brush"));
        _placeholder.Bind(Border.BorderBrushProperty, _placeholder.GetResourceObservable("RoGLineBrush"));

        _image = new Image { Stretch = Stretch.UniformToFill };
        _imageClip = new Border { ClipToBounds = true, Child = _image };

        _badgeIcon = new PathIcon { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _badgeIcon.Bind(PathIcon.ForegroundProperty, _badgeIcon.GetResourceObservable("RoGOnAccentBrush"));
        _badge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = _badgeIcon,
        };

        var root = new Panel();
        root.Children.Add(_placeholder);
        root.Children.Add(_imageClip);
        root.Children.Add(_badge);
        Content = root;
        Update();
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Name der Person – erster Buchstabe als Platzhalter, solange kein Bild da ist.</summary>
    public string? DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Online-Stand; <c>null</c> = kein Abzeichen (z. B. Team-Ansicht ohne Status).</summary>
    public Presence? Presence
    {
        get => GetValue(PresenceProperty);
        set => SetValue(PresenceProperty, value);
    }

    /// <summary>Text zum Status (Tooltip, Screenreader), z. B. „Im Voice“.</summary>
    public string? PresenceText
    {
        get => GetValue(PresenceTextProperty);
        set => SetValue(PresenceTextProperty, value);
    }

    /// <summary>Symbol des Status-Abzeichens (Ressourcenschlüssel) – <c>null</c> = ohne Symbol.</summary>
    public static string? IconKey(Presence presence) => presence switch
    {
        RoG.Core.Features.Members.Presence.Voice => "IconHeadset",
        RoG.Core.Features.Members.Presence.Server => "IconServers",
        RoG.Core.Features.Members.Presence.Ingame => "IconGamepad",
        _ => null,
    };

    /// <summary>Farbe des Status (Ressourcenschlüssel).</summary>
    public static string BrushKey(Presence presence) => presence switch
    {
        RoG.Core.Features.Members.Presence.Online => "RoGStatusOnlineBrush",
        RoG.Core.Features.Members.Presence.Voice => "RoGStatusVoiceBrush",
        RoG.Core.Features.Members.Presence.Server => "RoGStatusServerBrush",
        RoG.Core.Features.Members.Presence.Ingame => "RoGStatusInGameBrush",
        _ => "RoGStatusOfflineBrush",
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == DisplayNameProperty || change.Property == SizeProperty
            || change.Property == PresenceProperty || change.Property == PresenceTextProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var size = Size;
        Width = size;
        Height = size;
        var round = new CornerRadius(size / 2);
        _placeholder.CornerRadius = round;
        _imageClip.CornerRadius = round;
        _initial.FontSize = Math.Max(10, size * 0.45);
        _initial.Text = DisplayName is { Length: > 0 } name ? name.Trim()[..1].ToUpperInvariant() : string.Empty;
        _image.Source = Source;
        _imageClip.IsVisible = Source is not null;

        _badgeBrush?.Dispose();
        _badgeBrush = null;
        if (Presence is not { } presence)
        {
            _badge.IsVisible = false;
            ToolTip.SetTip(this, null);
            AutomationProperties.SetName(this, DisplayName);
            return;
        }

        // Abzeichen: ab 40 px mit Symbol (gut lesbar), kleiner nur Punkt/Ring
        var withIcon = size >= 40 && IconKey(presence) is not null;
        var badge = Math.Round(Math.Max(12, size * (withIcon ? 0.42 : 0.3)));
        _badge.IsVisible = true;
        _badge.Width = badge;
        _badge.Height = badge;
        _badge.CornerRadius = new CornerRadius(badge / 2);
        _badge.Margin = new Thickness(0, 0, -1, -1);
        _badge.BorderThickness = new Thickness(2);
        _badge.Bind(Border.BorderBrushProperty, _badge.GetResourceObservable("RoGBg2Brush"));
        var key = BrushKey(presence);
        if (presence == RoG.Core.Features.Members.Presence.Offline)
        {
            // leerer Ring: Fläche wie der Hintergrund, Rand in Statusfarbe
            _badge.Bind(Border.BackgroundProperty, _badge.GetResourceObservable("RoGBg2Brush"));
            _badge.BorderThickness = new Thickness(Math.Max(2, badge * 0.22));
            _badgeBrush = _badge.Bind(Border.BorderBrushProperty, _badge.GetResourceObservable(key));
        }
        else
        {
            _badgeBrush = _badge.Bind(Border.BackgroundProperty, _badge.GetResourceObservable(key));
        }

        if (withIcon && Application.Current?.TryGetResource(IconKey(presence)!, null, out var geometry) == true && geometry is Geometry g)
        {
            _badgeIcon.Data = g;
            _badgeIcon.Width = badge * 0.58;
            _badgeIcon.Height = badge * 0.58;
            _badgeIcon.IsVisible = true;
        }
        else
        {
            _badgeIcon.IsVisible = false;
        }

        ToolTip.SetTip(this, PresenceText);
        AutomationProperties.SetName(this, string.Join(" – ", new[] { DisplayName, PresenceText }.Where(s => !string.IsNullOrEmpty(s))));
    }
}
