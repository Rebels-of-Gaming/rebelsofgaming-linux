using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RoG.Core.Localization;
using RoG.Linux.Platform.Desktop;

namespace RoG.Linux.App.Services;

/// <summary>Was im Tray-Menü gewählt wurde.</summary>
public enum TrayCommand
{
    Open = 1,
    Notifications = 2,
    Messages = 3,
    Servers = 4,
    Exit = 9,
}

/// <summary>
/// Tray-Symbol der App (wie <c>TrayIcon</c> der Windows-App): Linksklick öffnet das Fenster, Menü mit Öffnen,
/// Benachrichtigungen, Nachrichten, Server und Beenden; Tooltip mit der Zahl ungelesener Einträge. Ereignisse kommen im
/// UI-Thread. Zeigt kein Panel das Symbol (GNOME ohne AppIndicator-Erweiterung), ist <see cref="IsVisible"/>
/// <c>false</c> – dann schließt das Fenster die App wie gewohnt.
/// </summary>
public sealed class TrayService : IAsyncDisposable
{
    /// <summary>Größen für <c>IconPixmap</c> – das Panel nimmt die passende (HiDPI: 48/64).</summary>
    private static readonly int[] s_sizes = [16, 22, 24, 32, 48, 64];

    private readonly AppServices _services;
    private readonly ITrayIcon _tray;

    public TrayService(AppServices services, ITrayIcon? tray = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _tray = tray ?? new StatusNotifierTray(services.T(StringKeys.AppDisplayName), RenderIcons());
        _tray.SetMenu(
        [
            new((int)TrayCommand.Open, services.T(StringKeys.TrayOpen)),
            new((int)TrayCommand.Notifications, services.T("Shell_NavNotifications.Content")),
            new((int)TrayCommand.Messages, services.T("Shell_NavMessages.Content")),
            new((int)TrayCommand.Servers, services.T("Shell_NavServers.Content")),
            new(5, null),
            new((int)TrayCommand.Exit, services.T(StringKeys.TrayExit)),
        ]);
        _tray.Activated += (_, _) => Dispatcher.UIThread.Post(() => Invoked?.Invoke(this, TrayCommand.Open));
        _tray.MenuItemInvoked += (_, id) => Dispatcher.UIThread.Post(() => Invoked?.Invoke(this, (TrayCommand)id));
        _tray.VisibilityChanged += (_, _) => Dispatcher.UIThread.Post(() => VisibilityChanged?.Invoke(this, EventArgs.Empty));
        services.Realtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RealtimeService.UnreadNotifications) or nameof(RealtimeService.UnreadMessages))
            {
                UpdateToolTip();
            }
        };
        UpdateToolTip();
    }

    /// <summary>Zeigt ein Panel das Symbol? Nur dann geht „Schließen“ in den Infobereich.</summary>
    public bool IsVisible => _tray.IsVisible;

    public event EventHandler<TrayCommand>? Invoked;

    public event EventHandler? VisibilityChanged;

    public Task<bool> StartAsync() => _tray.StartAsync();

    private void UpdateToolTip()
    {
        var unread = _services.Realtime.UnreadNotifications + _services.Realtime.UnreadMessages;
        _tray.SetToolTip(
            _services.T(StringKeys.AppDisplayName),
            unread > 0 ? _services.Format(StringKeys.TrayTooltipUnread, unread) : string.Empty);
    }

    /// <summary>RoG-Abzeichen als ARGB32 (Netzwerk-Byte-Reihenfolge, nicht vormultipliziert) in mehreren Größen.</summary>
    internal static IReadOnlyList<TrayIconPixmap> RenderIcons()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://rebelsofgaming/Assets/rog-badge-512.png"));
            using var source = new Bitmap(stream);
            return s_sizes.Select(size => Render(source, size)).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            return []; // dann zeigt das Panel das Symbol aus dem Icon-Theme (IconName)
        }
    }

    private static TrayIconPixmap Render(Bitmap source, int size)
    {
        using var scaled = source.CreateScaledBitmap(new PixelSize(size, size), BitmapInterpolationMode.HighQuality);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            scaled.CopyPixels(new PixelRect(0, 0, size, size), pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }

        var bgra = scaled.Format == PixelFormat.Bgra8888;
        var premultiplied = scaled.AlphaFormat != AlphaFormat.Unpremul;
        var argb = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            byte r = bgra ? pixels[i + 2] : pixels[i], g = pixels[i + 1], b = bgra ? pixels[i] : pixels[i + 2], a = pixels[i + 3];
            if (premultiplied && a is > 0 and < 255)
            {
                r = (byte)Math.Min(255, r * 255 / a);
                g = (byte)Math.Min(255, g * 255 / a);
                b = (byte)Math.Min(255, b * 255 / a);
            }

            argb[i] = a;
            argb[i + 1] = r;
            argb[i + 2] = g;
            argb[i + 3] = b;
        }

        return new TrayIconPixmap(size, size, argb);
    }

    public ValueTask DisposeAsync() => _tray.DisposeAsync();
}
