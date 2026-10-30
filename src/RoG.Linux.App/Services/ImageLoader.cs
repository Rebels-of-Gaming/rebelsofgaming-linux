using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace RoG.Linux.App.Services;

/// <summary>
/// Lädt Bilder (Titelbilder, Avatare) von den Servern/dem CDN der Plattform und dekodiert sie auf die angezeigte Breite.
/// Nur http(s); Fehler ergeben <c>null</c> (Platzhalter statt kaputter Seite). Cache nur im Speicher, je Adresse + Breite.
/// </summary>
public sealed class ImageLoader : IDisposable
{
    private const int MaxBytes = 15 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<(Uri, int), Task<Bitmap?>> _cache = new();

    public ImageLoader(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public Task<Bitmap?> LoadAsync(Uri? uri, int decodeWidth)
    {
        if (uri is null || !uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        return _cache.GetOrAdd((uri, decodeWidth), key => LoadCoreAsync(key.Item1, key.Item2));
    }

    public void Clear() => _cache.Clear();

    /// <summary>Ohne den Cache dieser Klasse laden (Kartenkacheln: eigener, begrenzter Cache in <see cref="MapTileCache"/>).</summary>
    public Task<Bitmap?> LoadUncachedAsync(Uri? uri, int decodeWidth) =>
        uri is null || !uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            ? Task.FromResult<Bitmap?>(null)
            : FetchAsync(uri, decodeWidth, cached: false);

    private Task<Bitmap?> LoadCoreAsync(Uri uri, int decodeWidth) => FetchAsync(uri, decodeWidth, cached: true);

    private async Task<Bitmap?> FetchAsync(Uri uri, int decodeWidth, bool cached)
    {
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException or IOException)
        {
            if (cached)
            {
                _cache.TryRemove((uri, decodeWidth), out _);
            }

            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
