using Avalonia;
using Avalonia.Controls;
using RoG.Core.Settings;

namespace RoG.Linux.App.Services;

/// <summary>Wiederherzustellender Fensterzustand: Position in Bildschirmpixeln (nur X11), Größe in DIP.</summary>
public sealed record WindowRestore(PixelPoint? Position, double Width, double Height, bool Maximized);

/// <summary>
/// Fenstergröße, -position und „maximiert“ merken (<c>settings.json</c> → <c>window</c>, XDG-Konfigordner) und beim
/// Start wiederherstellen – passend zum heutigen Bildschirm (<see cref="WindowPlacement.FitInto"/> aus dem Kern).
/// Gespeichert werden Position in Pixeln und Größe in DIP (unabhängig von der Skalierung). Unter Wayland darf eine App
/// ihre Position weder lesen noch setzen – dort nur Größe und „maximiert“, die Lage bestimmt der Compositor.
/// </summary>
public static class WindowPlacementService
{
    /// <summary>Plan aus gespeichertem Zustand und Arbeitsbereich des Bildschirms; <c>null</c> = Standard (1280×800, mittig).</summary>
    public static WindowRestore? Plan(WindowPlacement? saved, PixelRect workingArea, double scaling, bool canPosition)
    {
        if (saved is null || scaling <= 0)
        {
            return null;
        }

        var inPixels = saved with { Width = (int)Math.Round(saved.Width * scaling), Height = (int)Math.Round(saved.Height * scaling) };
        var fitted = inPixels.FitInto(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height);
        if (fitted is null)
        {
            return null;
        }

        return new WindowRestore(
            canPosition ? new PixelPoint(fitted.X, fitted.Y) : null,
            fitted.Width / scaling,
            fitted.Height / scaling,
            saved.Maximized);
    }

    /// <summary>
    /// Tatsächlich benutztes Fenstersystem (nach einem möglichen Rückfall von Wayland auf X11): „X11“, „Wayland“ oder
    /// der Handle-Typ der Plattform. Vom ersten Fenster gesetzt, für „Über die App“.
    /// </summary>
    public static string ActualBackend { get; private set; } = LaunchOptions.Current.Backend == DisplayBackend.Wayland ? "Wayland" : "X11";

    /// <summary>Fenstersystem am Fenster ablesen (X11-Fenster haben ein „XID“-Handle).</summary>
    public static string Detect(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var descriptor = window.TryGetPlatformHandle()?.HandleDescriptor;
        ActualBackend = descriptor switch
        {
            "XID" => "X11",
            null => ActualBackend,
            _ when descriptor.Contains("wl", StringComparison.OrdinalIgnoreCase) || descriptor.Contains("wayland", StringComparison.OrdinalIgnoreCase) => "Wayland",
            _ => descriptor,
        };
        return ActualBackend;
    }

    /// <summary>Darf die App ihre Fensterposition setzen? Nur unter X11 (Wayland verbietet es).</summary>
    public static bool CanPosition => ActualBackend == "X11";

    /// <summary>Vor <c>Show()</c>: Größe/Lage setzen. Bildschirm = der, auf dem die gespeicherte Position liegt.</summary>
    public static void Restore(Window window, WindowPlacement? saved)
    {
        ArgumentNullException.ThrowIfNull(window);
        Detect(window);
        if (saved is null)
        {
            return;
        }

        var screens = window.Screens;
        var screen = screens.ScreenFromPoint(new PixelPoint(saved.X + 40, saved.Y + 40)) ?? screens.Primary ?? (screens.All.Count > 0 ? screens.All[0] : null);
        if (screen is null || Plan(saved, screen.WorkingArea, screen.Scaling, CanPosition) is not { } plan)
        {
            return;
        }

        window.Width = plan.Width;
        window.Height = plan.Height;
        if (plan.Position is { } position)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = position;
        }

        if (plan.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Zustand zum Speichern; <paramref name="normal"/> = letzte Lage im normalen (nicht maximierten) Zustand.</summary>
    public static WindowPlacement Capture(PixelPoint position, Size normal, bool maximized) =>
        new(position.X, position.Y, (int)Math.Round(normal.Width), (int)Math.Round(normal.Height), maximized);
}
