using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RoG.Linux.App.Services;

/// <summary>
/// Kacheln der Mitgliederkarte – nur im Speicher und begrenzt (zuletzt benutzte zuerst behalten), geladen über den
/// <see cref="ImageLoader"/> (gleicher HTTP-Weg wie Bilder, in Tests Fake-Kacheln). Nur auf dem UI-Thread benutzen.
/// Fehlgeschlagene Kacheln (z. B. abgelaufene Signatur) bleiben leer, bis die Vorlage neu geholt wird (neue Adressen).
/// </summary>
public sealed class MapTileCache(ImageLoader loader, int capacity = MapTileCache.DefaultCapacity)
{
    public const int DefaultCapacity = 384;

    private readonly Dictionary<Uri, LinkedListNode<(Uri Url, Bitmap? Bitmap)>> _entries = [];
    private readonly LinkedList<(Uri Url, Bitmap? Bitmap)> _recent = new();
    private readonly HashSet<Uri> _loading = [];
    private int _generation;

    /// <summary>Zahl der Kacheln im Speicher (Tests).</summary>
    public int Count => _entries.Count;

    /// <summary>Schon geladen? <paramref name="bitmap"/> ist <c>null</c>, wenn die Kachel nicht zu bekommen war.</summary>
    public bool TryGet(Uri url, out Bitmap? bitmap)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (_entries.TryGetValue(url, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }

        bitmap = null;
        return false;
    }

    /// <summary>Kachel laden (falls nicht schon da oder unterwegs); <paramref name="loaded"/> danach auf dem UI-Thread.</summary>
    public void Request(Uri url, Action loaded)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(loaded);
        if (_entries.ContainsKey(url) || !_loading.Add(url))
        {
            return;
        }

        _ = LoadAsync(url, loaded, _generation);
    }

    public void Clear()
    {
        _generation++;
        _entries.Clear();
        _recent.Clear();
        _loading.Clear();
    }

    private async Task LoadAsync(Uri url, Action loaded, int generation)
    {
        // Anfrage kommt aus dem Zeichnen (ohne SynchronizationContext) – Ergebnis immer auf dem UI-Thread eintragen
        var bitmap = await loader.LoadUncachedAsync(url, 0).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => Complete(url, bitmap, loaded, generation));
    }

    private void Complete(Uri url, Bitmap? bitmap, Action loaded, int generation)
    {
        if (generation != _generation)
        {
            return;
        }

        _loading.Remove(url);
        _entries[url] = _recent.AddFirst((url, bitmap));
        while (_entries.Count > capacity && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _entries.Remove(oldest.Value.Url);
        }

        loaded();
    }
}
