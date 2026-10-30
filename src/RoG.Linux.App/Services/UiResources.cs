using System.Globalization;
using Avalonia;
using Avalonia.Media;
using RoG.Core.Features.Members;
using RoG.Core.Localization;

namespace RoG.Linux.App.Services;

/// <summary>Farben aus dem Theme und aus der API (Rang-/Rollenfarben) für ViewModels.</summary>
public static class UiResources
{
    /// <summary>Pinsel aus dem aktuellen Design (z. B. <c>RoGGoldBrush</c>); ohne App ein neutraler Ersatz.</summary>
    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        return app?.TryGetResource(key, app.ActualThemeVariant, out var value) == true && value is IBrush brush ? brush : Brushes.Gray;
    }

    /// <summary>
    /// <c>#D4AE3C</c> bzw. <c>#RGB</c> aus der API; sonst <paramref name="fallbackKey"/> aus dem Theme. Farben mit zu wenig
    /// Kontrast zum Hintergrund des aktuellen Designs (z. B. Rang „Member“ in Off-White auf Hell) werden durch die
    /// Textfarbe ersetzt – lesbar in RoG-Dunkel und Hell.
    /// </summary>
    public static IBrush FromHex(string? hex, string fallbackKey)
    {
        if (TryParse(hex) is not { } color)
        {
            return Brush(fallbackKey);
        }

        var app = Application.Current;
        var background = app?.TryGetResource("RoGBgColor", app.ActualThemeVariant, out var bg) == true && bg is Color c ? c : Color.Parse("#1C1F26");
        return Contrast(color, background) < 2.2 ? Brush("RoGInkBrush") : new SolidColorBrush(color);
    }

    /// <summary>Kontrastverhältnis nach WCAG (1 … 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    public static Color? TryParse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var value = hex.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        return value.Length is 4 or 7 or 9 && Color.TryParse(value, out var color) ? color : null;
    }

    public static string PresenceLabel(Presence presence, ILocalizer localizer) => localizer.GetString(presence switch
    {
        Presence.Online => StringKeys.PresenceOnline,
        Presence.Voice => StringKeys.PresenceVoice,
        Presence.Server => StringKeys.PresenceServer,
        Presence.Ingame => StringKeys.PresenceIngame,
        _ => StringKeys.PresenceOffline,
    });

    /// <summary>„1.234“ in der App-Sprache.</summary>
    public static string Number(long value, CultureInfo culture) => value.ToString("N0", culture);
}
