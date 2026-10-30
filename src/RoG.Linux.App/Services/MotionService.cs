using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RoG.Linux.Platform.Desktop;

namespace RoG.Linux.App.Services;

/// <summary>
/// „Bewegung reduzieren“ wie der Desktop (Annahme: keine eigene Einstellung in der App – GNOME, KDE und
/// xdg-desktop-portal ≥ 1.20 liefern den Wunsch über das Settings-Portal, ohne Flatpak-Zusatzrecht; auch live).
/// Setzt die Klasse <c>reduce-motion</c> an allen offenen und neuen Fenstern (Stile in <c>Themes/ReducedMotion.axaml</c>).
/// </summary>
public static class MotionService
{
    public const string ReduceClass = "reduce-motion";

    private static readonly List<WeakReference<Window>> s_windows = [];
    private static bool s_attached;

    /// <summary>Gerade reduziert?</summary>
    public static bool IsReduced { get; private set; }

    /// <summary>Jedes Fenster beim Öffnen erfassen (einmal, aus <c>App.Initialize</c>).</summary>
    public static void Attach()
    {
        if (s_attached)
        {
            return;
        }

        s_attached = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, args) =>
        {
            lock (s_windows)
            {
                s_windows.RemoveAll(w => !w.TryGetTarget(out _));
                s_windows.Add(new WeakReference<Window>(window));
            }

            window.Classes.Set(ReduceClass, IsReduced);
        }, RoutingStrategies.Direct);
    }

    /// <summary>Klasse an allen offenen Fenstern setzen bzw. entfernen (UI-Thread).</summary>
    public static void Apply(bool reduce)
    {
        IsReduced = reduce;
        List<Window> open;
        lock (s_windows)
        {
            open = [.. s_windows.Select(w => w.TryGetTarget(out var window) ? window : null).OfType<Window>()];
        }

        foreach (var window in open)
        {
            window.Classes.Set(ReduceClass, reduce);
        }
    }

    /// <summary>Wunsch des Desktops lesen und verfolgen – im Hintergrund, der Start wartet nicht darauf.</summary>
    public static async Task StartAsync(MotionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Changed += (_, reduce) => Dispatcher.UIThread.Post(() => Apply(reduce));
        var reduce = await settings.StartAsync().ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => Apply(reduce));
    }
}
