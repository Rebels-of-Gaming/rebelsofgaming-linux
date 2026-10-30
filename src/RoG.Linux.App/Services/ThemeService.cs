using Avalonia;
using Avalonia.Styling;
using RoG.Linux.Platform;

namespace RoG.Linux.App.Services;

/// <summary>
/// Design der App: RoG-Dunkel (Standard), Hell oder „wie das System“ – dann folgt Avalonia der Farbschema-Einstellung
/// des Desktops (Settings-Portal <c>org.freedesktop.appearance color-scheme</c>, GNOME/KDE/Steam Deck) auch live.
/// </summary>
public static class ThemeService
{
    public static ThemeVariant ToVariant(string theme) => theme switch
    {
        LinuxSettings.ThemeSystem => ThemeVariant.Default,
        LinuxSettings.ThemeLight => ThemeVariant.Light,
        _ => ThemeVariant.Dark,
    };

    public static void Apply(string theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ToVariant(theme);
        }
    }
}
