using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RoG.Linux.App.Services;

/// <summary>
/// Vorschau für Markdown im News-Editor: wandelt die gängigen Elemente (Überschriften, Absätze, fett/kursiv/
/// durchgestrichen, Code, Links, Bilder, Listen, Zitate, Trennlinie, Codeblöcke) in HTML, das dann wie jeder andere
/// Inhalt nativ gezeichnet wird (<c>HtmlContentParser</c> → <c>RichContentView</c>). Eigenes HTML wird wie auf der
/// Website escaped; Bilder lädt der Parser nur von Servern der Plattform. Maßgeblich bleibt die Darstellung der
/// Website (Einbettungen erscheinen hier als Link) – Wunsch an den Kern: gemeinsamer Markdown-Renderer.
/// </summary>
public static partial class MarkdownPreview
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var paragraph = new List<string>();
        string? list = null;
        var quote = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(string.Join("<br>", paragraph.Select(Inline))).Append("</p>");
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (list is not null)
            {
                html.Append("</").Append(list).Append('>');
                list = null;
            }
        }

        void FlushQuote()
        {
            if (quote.Count > 0)
            {
                html.Append("<blockquote>").Append(ToHtml(string.Join('\n', quote))).Append("</blockquote>");
                quote.Clear();
            }
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushQuote();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushAll();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                html.Append("<pre><code>").Append(WebUtility.HtmlEncode(string.Join('\n', code))).Append("</code></pre>");
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                FlushList();
                quote.Add(trimmed[1..].TrimStart());
                continue;
            }

            FlushQuote();
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (Heading().Match(trimmed) is { Success: true } h)
            {
                FlushParagraph();
                FlushList();
                var level = Math.Min(h.Groups[1].Value.Length + 1, 6); // # → h2 (h1 ist der Titel)
                html.Append("<h").Append(level).Append('>').Append(Inline(h.Groups[2].Value.Trim())).Append("</h").Append(level).Append('>');
                continue;
            }

            if (Rule().IsMatch(trimmed))
            {
                FlushParagraph();
                FlushList();
                html.Append("<hr>");
                continue;
            }

            var bullet = Bullet().Match(trimmed);
            var number = Numbered().Match(trimmed);
            if (bullet.Success || number.Success)
            {
                FlushParagraph();
                var kind = bullet.Success ? "ul" : "ol";
                if (list != kind)
                {
                    FlushList();
                    html.Append('<').Append(kind).Append('>');
                    list = kind;
                }

                html.Append("<li>").Append(Inline((bullet.Success ? bullet : number).Groups[1].Value)).Append("</li>");
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        FlushAll();
        return html.ToString();
    }

    /// <summary>Inline-Formatierung auf escaptem Text; Links nur http(s)/mailto.</summary>
    private static string Inline(string text)
    {
        var codes = new List<string>();
        var escaped = WebUtility.HtmlEncode(text);
        escaped = Code().Replace(escaped, m =>
        {
            codes.Add("<code>" + m.Groups[1].Value + "</code>");
            return "\u0001" + (codes.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0002";
        });
        escaped = Image().Replace(escaped, m => SafeUrl(m.Groups[2].Value) is { } url ? $"<img src=\"{url}\" alt=\"{m.Groups[1].Value}\">" : m.Value);
        escaped = Link().Replace(escaped, m => SafeUrl(m.Groups[2].Value) is { } url ? $"<a href=\"{url}\">{m.Groups[1].Value}</a>" : m.Value);
        escaped = AutoLink().Replace(escaped, m => $"<a href=\"{m.Value}\">{m.Value}</a>");
        escaped = Bold().Replace(escaped, "<strong>$2</strong>");
        escaped = Italic().Replace(escaped, "<em>$2</em>");
        escaped = Strike().Replace(escaped, "<del>$1</del>");
        return Placeholder().Replace(escaped, m => codes[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private static string? SafeUrl(string url)
    {
        var decoded = WebUtility.HtmlDecode(url.Trim());
        return Uri.TryCreate(decoded, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto)
            ? WebUtility.HtmlEncode(uri.AbsoluteUri)
            : null;
    }

    [GeneratedRegex(@"^(#{1,5})\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\*\s*\*\s*\*|-\s*-\s*-|_\s*_\s*_)[\s*_-]*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\d{1,3}[.)]\s+(.+)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)\s]+)\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"(?<!!)\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<![""=>])\bhttps?://[^\s<]+")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])(\*|_)(?=\S)(.+?)(?<=\S)\1(?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~")]
    private static partial Regex Strike();

    [GeneratedRegex("\u0001(\\d+)\u0002")]
    private static partial Regex Placeholder();
}
