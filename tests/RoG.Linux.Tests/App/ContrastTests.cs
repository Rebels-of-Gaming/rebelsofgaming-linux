using System.Globalization;
using System.Xml.Linq;
using RoG.Linux.Tests.Platform;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 10 – Kontrast nach WCAG 2.2 AA für beide Paletten aus <c>Themes/RoGTheme.axaml</c> (Dunkel und Hell):
/// Fließtext ≥ 4,5:1, Symbole/Status-Punkte und Bedienelemente ≥ 3:1.
/// Begründete Ausnahmen stehen unten (z. B. Markenrot nur für Kanten/Flächen, als Text gibt es RoGRedText).
/// </summary>
public class ContrastTests
{
    private static readonly Dictionary<string, Dictionary<string, string>> s_palettes = Load();

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var file = Path.Combine(LocalizationTests.RepoRoot(), "src", "RoG.Linux.App", "Themes", "RoGTheme.axaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(file).Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") is "Dark" or "Light")
            .ToDictionary(
                e => (string)e.Attribute(x + "Key")!,
                e => e.Elements().Where(b => b.Name.LocalName == "SolidColorBrush")
                    .ToDictionary(b => ShortName((string)b.Attribute(x + "Key")!), b => (string)b.Attribute("Color")!));
    }

    /// <summary><c>RoGRedTextBrush</c> → <c>RedText</c>; Fluent-Schlüssel (<c>TextControl…</c>) bleiben, wie sie sind.</summary>
    private static string ShortName(string key) =>
        key.StartsWith("RoG", StringComparison.Ordinal) && key.EndsWith("Brush", StringComparison.Ordinal) ? key[3..^5] : key;

    internal static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8)
        {
            h = h[2..]; // #AARRGGBB – nur deckende Farben werden geprüft
        }

        double Channel(int i)
        {
            var c = int.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    internal static double Ratio(string a, string b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_colors_reach_aa_on_every_surface(string theme)
    {
        var p = s_palettes[theme];
        var failures = new List<string>();
        // Text (≥ 4,5:1) auf Seite, Karte und erhöhter Fläche (Auswahl, Chips, Eingabefelder)
        foreach (var fg in new[] { "Ink", "Muted", "RedText", "Gold", "Green" })
        {
            foreach (var bg in new[] { "Bg", "Bg2", "Bg3" })
            {
                Check(failures, p, fg, bg, 4.5);
            }
        }

        // Text auf roten Flächen (Primär-Knopf in Ruhe, Hover, gedrückt; Zähler in der Seitenleiste)
        foreach (var bg in new[] { "ActionRed", "ActionRedHover", "ActionRedPressed" })
        {
            Check(failures, p, "OnAccent", bg, 4.5);
        }

        // Eingabefelder: Text und Platzhalter
        Check(failures, p, "TextControlForeground", "TextControlBackground", 4.5);
        Check(failures, p, "TextControlPlaceholderForeground", "TextControlBackground", 4.5);
        Check(failures, p, "TextControlPlaceholderForeground", "TextControlBackgroundFocused", 4.5);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Icons_status_dots_and_focus_reach_three_to_one(string theme)
    {
        var p = s_palettes[theme];
        var failures = new List<string>();
        // Symbole und Status-Punkte (grün online · blau Voice · rot Server · gold im Spiel · grau offline) auf Seite und Karte
        foreach (var fg in new[] { "Red", "Blue", "Green", "Gold", "StatusOnline", "StatusVoice", "StatusServer", "StatusInGame", "StatusOffline", "Muted" })
        {
            foreach (var bg in new[] { "Bg", "Bg2" })
            {
                Check(failures, p, fg, bg, 3.0);
            }
        }

        // Symbole auf erhöhter Fläche: Symbol-Knopf (Hover), gewählter Eintrag der Seitenleiste (rotes Symbol = RedText)
        foreach (var fg in new[] { "RedText", "Blue", "Green", "Gold", "Muted" })
        {
            Check(failures, p, fg, "Bg3", 3.0);
        }

        // Fokusrahmen (Off-White bzw. Tinte) auf allen Flächen; Fokus-Kante der Eingabefelder (Rot)
        foreach (var bg in new[] { "Bg", "Bg2", "Bg3" })
        {
            Check(failures, p, "Ink", bg, 3.0);
        }

        Check(failures, p, "TextControlBorderBrushFocused", "TextControlBackgroundFocused", 3.0);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Begründete Ausnahme: das Markenrot (<c>RoGRed</c>, Website-Token #E0324A) erreicht im Dunkel-Design als Text nur
    /// 3,7:1 – deshalb gibt es <c>RoGRedText</c> für rote Schrift (Kicker, Links, Fehler, Niederlage), das Markenrot
    /// bleibt für Kanten, Marker und Punkte. Der Test hält fest, dass die Unterscheidung nötig bleibt.
    /// </summary>
    [Fact]
    public void Brand_red_stays_for_accents_red_text_for_type()
    {
        var dark = s_palettes["Dark"];
        Assert.Equal("#E0324A", dark["Red"]);
        Assert.True(Ratio(dark["Red"], dark["Bg"]) < 4.5);
        Assert.True(Ratio(dark["RedText"], dark["Bg3"]) >= 4.5);
    }

    [Fact]
    public void Both_palettes_define_the_same_keys()
    {
        Assert.Equal(s_palettes["Dark"].Keys.Order(), s_palettes["Light"].Keys.Order());
        Assert.Equal(4.5, Math.Round(Ratio("#767676", "#FFFFFF"), 1)); // Referenzwert der WCAG-Formel
    }

    private static void Check(List<string> failures, Dictionary<string, string> p, string fg, string bg, double minimum)
    {
        var ratio = Ratio(p[fg], p[bg]);
        if (ratio < minimum)
        {
            failures.Add($"{fg} {p[fg]} auf {bg} {p[bg]}: {ratio:F2}:1 < {minimum}:1");
        }
    }
}
