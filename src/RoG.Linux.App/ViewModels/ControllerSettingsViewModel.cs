using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Linux.App.Services;
using RoG.Linux.Platform.Input;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Einstellungen → Controller: an/aus (Standard an, <c>linux.json</c>), angeschlossener Controller, Belegung.
/// Der Zustand kommt vom <see cref="GamepadService"/> (Hot-Plug) und wird auf dem UI-Thread aktualisiert.
/// </summary>
public sealed partial class ControllerSettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly bool _loaded;

    public ControllerSettingsViewModel(AppServices services)
    {
        _services = services;
        Enabled = services.LinuxSettings.Controller;
        Mapping = BuildMapping(services);
        UpdateStatus();
        services.Gamepad.ControllerChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatus);
        _loaded = true;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>„Angeschlossen: Steam Deck“, „Kein Controller angeschlossen“, „nicht verfügbar“ …</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Kein SDL → Hinweis statt Status.</summary>
    [ObservableProperty]
    public partial bool IsUnavailable { get; set; }

    /// <summary>Spielmodus aktiv (Vollbild, große Ziele).</summary>
    public bool IsGameMode => _services.GameMode;

    /// <summary>Belegung: Taste → Wirkung.</summary>
    public IReadOnlyList<ShortcutRow> Mapping { get; }

    partial void OnEnabledChanged(bool value)
    {
        if (!_loaded)
        {
            return;
        }

        _services.Gamepad.Enabled = value;
        UpdateStatus();
        if (_services.LinuxSettings.Controller != value)
        {
            _ = _services.UpdateLinuxSettingsAsync(s => s with { Controller = value });
        }
    }

    /// <summary>Stand neu lesen (Hot-Plug, an/aus).</summary>
    public void UpdateStatus()
    {
        var pad = _services.Gamepad;
        IsUnavailable = pad.Availability == GamepadAvailability.Unavailable;
        StatusText = IsUnavailable
            ? _services.T("Linux_ControllerUnavailable")
            : !Enabled
                ? _services.T("Linux_ControllerOff")
                : pad.ControllerName is { } name
                    ? _services.Format("Linux_ControllerConnected", name.Length > 0 ? name : _services.T("Linux_ControllerUnknownName"))
                        + (pad.Library is { } lib ? " · " + _services.Format("Linux_ControllerLibrary", lib) : string.Empty)
                    : _services.T("Linux_ControllerNone");
    }

    private static IReadOnlyList<ShortcutRow> BuildMapping(AppServices s) =>
    [
        new(s.T("Linux_PadDirectionsKey"), s.T("Linux_PadMove")),
        new("A", s.T("Linux_PadActivate")),
        new("B", s.T("Linux_PadBack")),
        new("LB · RB", s.T("Linux_PadSections")),
        new(s.T("Linux_PadMenuKey"), s.T("Linux_PadMenu")),
        new(s.T("Linux_PadViewKey"), s.T("Linux_PadSearch")),
        new("Y", s.T("Linux_PadRefresh")),
        new(s.T("Linux_PadRightStickKey"), s.T("Linux_PadScroll")),
        new("Steam + X", s.T("Linux_PadKeyboard")),
    ];
}
