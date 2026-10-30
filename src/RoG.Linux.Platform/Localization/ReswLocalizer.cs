using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using RoG.Core.Localization;

namespace RoG.Linux.Platform.Localization;

/// <summary>
/// Übersetzungen aus den <c>.resw</c>-Dateien der Windows-App (Submodul, DE/EN/FR – dieselben Schlüssel wie
/// <see cref="StringKeys"/> und die <c>x:Uid</c>-Schlüssel wie <c>LoginPage_SignIn.Content</c>) plus Linux-Abweichungen
/// aus <c>Strings/&lt;sprache&gt;/Linux.resw</c> (gehen vor). Fehlt ein Text in EN/FR, gilt Deutsch, fehlt er ganz,
/// der Schlüssel selbst (sichtbar statt leer).
/// <para>
/// WinUI-Schlüssel für angehängte Eigenschaften (<c>X.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name</c>)
/// sind zusätzlich ohne den <c>[using:…]</c>-Teil abrufbar (<c>X.AutomationProperties.Name</c>) – lesbar in AXAML.
/// </para>
/// </summary>
public sealed class ReswLocalizer : ILocalizer
{
    private static readonly Assembly s_assembly = typeof(ReswLocalizer).Assembly;

    private readonly Dictionary<string, string> _texts;

    public ReswLocalizer(string language)
    {
        Language = language is "en" or "fr" ? language : "de";
        _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        // Reihenfolge: DE als Rückfall, dann Sprache, Linux-Texte jeweils danach (überschreiben)
        Merge("de", "Resources");
        Merge("de", "Linux");
        if (Language != "de")
        {
            Merge(Language, "Resources");
            Merge(Language, "Linux");
        }
    }

    public string Language { get; }

    public string GetString(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _texts.TryGetValue(key, out var text) && text.Length > 0 ? text : key;
    }

    /// <summary>Alle Schlüssel einer Datei (für Vollständigkeitstests).</summary>
    public static IReadOnlyDictionary<string, string> Load(string language, string file)
    {
        using var stream = s_assembly.GetManifestResourceStream($"RoG.Strings.{language}.{file}.resw");
        return stream is null ? new Dictionary<string, string>() : Parse(stream);
    }

    internal static Dictionary<string, string> Parse(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var doc = XDocument.Load(reader);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root?.Elements("data") ?? [])
        {
            if (data.Attribute("name")?.Value is { Length: > 0 } name && data.Attribute("type") is null)
            {
                result[name] = data.Element("value")?.Value ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary><c>A.[using:Ns]Prop.Name</c> → <c>A.Prop.Name</c>; sonst <c>null</c>.</summary>
    internal static string? ShortKey(string key)
    {
        var open = key.IndexOf(".[using:", StringComparison.Ordinal);
        var close = open < 0 ? -1 : key.IndexOf(']', open);
        return close < 0 ? null : string.Concat(key.AsSpan(0, open + 1), key.AsSpan(close + 1));
    }

    private void Merge(string language, string file)
    {
        foreach (var (key, value) in Load(language, file))
        {
            if (value.Length > 0)
            {
                _texts[key] = value;
                if (ShortKey(key) is { } alias)
                {
                    _texts[alias] = value;
                }
            }
        }
    }
}
