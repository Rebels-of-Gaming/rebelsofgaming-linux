using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using RoG.Core.Features.Servers;

namespace RoG.Linux.App.Services;

/// <summary>
/// Zwischenablage des Hauptfensters (X11-Auswahl bzw. Wayland-Datenangebot über Avalonia). Schreiben ist asynchron;
/// der Kern erwartet einen synchronen Aufruf – ohne Fenster (Hintergrund, Tests) gibt es eine Ausnahme, die der
/// <see cref="ServerConnector"/> als „Kopieren fehlgeschlagen“ meldet.
/// </summary>
public sealed class AvaloniaClipboard : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard
            ?? throw new InvalidOperationException("Keine Zwischenablage verfügbar.");
        _ = clipboard.SetTextAsync(text);
    }
}
