using System.Collections.Concurrent;
using System.Net;
using Avalonia.Media.Imaging;

namespace RoG.Linux.App.Services;

/// <summary>
/// Lädt Bilder (Titelbilder, Avatare) von den Servern/dem CDN der Plattform und dekodiert sie auf die angezeigte Breite.
/// Fehler ergeben <c>null</c> (Platzhalter statt kaputter Seite). Cache nur im Speicher, je Adresse + Breite.
/// <para>
/// Datenschutz: nur Medien-Domains der Plattform (<c>RoGEnvironment.IsOwnMedia</c> des Kerns: Website, CDN; https,
/// http nur in der lokalen Umgebung) – auch nach Weiterleitungen. Fremde Bilder liefert die API bereits über den Bild-Cache der
/// Website aus (<c>/media/external?u=…</c>, signiert); was trotzdem fremd ist, lädt die App nicht.
/// Sicherheit: höchstens <see cref="MaxBytes"/> – auch ohne bzw. mit falscher <c>Content-Length</c> (Audit N7).
/// </para>
/// </summary>
public sealed class ImageLoader : IDisposable
{
    /// <summary>Größtes Bild, das gelesen wird (danach Abbruch, Platzhalter).</summary>
    public const int MaxBytes = 15 * 1024 * 1024;

    private const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly Func<Uri, bool> _isOwnMedia;
    private readonly ConcurrentDictionary<(Uri, int), Task<Bitmap?>> _cache = new();

    public ImageLoader(HttpMessageHandler? handler = null, Func<Uri, bool>? isOwnMedia = null)
    {
        // Weiterleitungen selbst folgen, damit jedes Ziel gegen die Host-Liste geprüft wird
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None })
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(20);
        // Ohne Vorgabe (nur Tests/Werkzeuge): Medien-Domains irgendeiner Umgebung; die App übergibt die der aktiven
        _isOwnMedia = isOwnMedia ?? (uri => RoG.Core.Config.RoGEnvironment.All.Values.Any(e => e.IsOwnMedia(uri)));
    }

    public Task<Bitmap?> LoadAsync(Uri? uri, int decodeWidth)
    {
        if (!IsAllowed(uri))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        return _cache.GetOrAdd((uri!, decodeWidth), key => LoadCoreAsync(key.Item1, key.Item2));
    }

    public void Clear() => _cache.Clear();

    /// <summary>Ohne den Cache dieser Klasse laden (Kartenkacheln: eigener, begrenzter Cache in <see cref="MapTileCache"/>).</summary>
    public Task<Bitmap?> LoadUncachedAsync(Uri? uri, int decodeWidth) =>
        IsAllowed(uri) ? FetchAsync(uri!, decodeWidth, cached: false) : Task.FromResult<Bitmap?>(null);

    /// <summary>Absolute http(s)-Adresse auf einem Host der Plattform?</summary>
    public bool IsAllowed(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && _isOwnMedia(uri);

    /// <summary>Antwort lesen, aber nie mehr als <paramref name="maxBytes"/>; <c>null</c> = zu groß.</summary>
    internal static async Task<byte[]?> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(content.Headers.ContentLength is { } length and > 0 ? (int)length : 64 * 1024);
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private Task<Bitmap?> LoadCoreAsync(Uri uri, int decodeWidth) => FetchAsync(uri, decodeWidth, cached: true);

    private async Task<Bitmap?> FetchAsync(Uri uri, int decodeWidth, bool cached)
    {
        try
        {
            var target = uri;
            var response = await _http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            for (var hop = 0; IsRedirect(response.StatusCode) && hop < MaxRedirects; hop++)
            {
                var next = response.Headers.Location is { } location ? new Uri(target, location) : null;
                response.Dispose();
                if (!IsAllowed(next))
                {
                    return null; // Weiterleitung auf einen fremden Host: nicht laden (bleibt als Platzhalter gecacht)
                }

                target = next!;
                response = await _http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            }

            byte[]? bytes;
            using (response)
            {
                bytes = response.IsSuccessStatusCode ? await ReadCappedAsync(response.Content, MaxBytes).ConfigureAwait(false) : null;
            }

            if (bytes is null)
            {
                return null; // Fehlerstatus oder zu groß: Platzhalter
            }

            using var stream = new MemoryStream(bytes);
            return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException or IOException or UriFormatException)
        {
            return Forget(uri, decodeWidth, cached);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => (int)status is 301 or 302 or 303 or 307 or 308;

    /// <summary>Fehlschlag nicht cachen (später erneut versuchen).</summary>
    private Bitmap? Forget(Uri uri, int decodeWidth, bool cached)
    {
        if (cached)
        {
            _cache.TryRemove((uri, decodeWidth), out _);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
