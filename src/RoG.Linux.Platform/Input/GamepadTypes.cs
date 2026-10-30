namespace RoG.Linux.Platform.Input;

/// <summary>
/// Tasten eines Controllers in der Xbox-Belegung (Werte wie <c>SDL_GameControllerButton</c> in SDL2 bzw.
/// <c>SDL_GamepadButton</c> in SDL3 – beide nummerieren gleich; A = unten/„South“, B = rechts/„East“).
/// Steam Deck: Ansicht (⧉) = <see cref="Back"/>, Menü (≡) = <see cref="Start"/>, L1/R1 = Schultertasten.
/// </summary>
public enum GamepadButton
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    Back = 4,
    Guide = 5,
    Start = 6,
    LeftStick = 7,
    RightStick = 8,
    LeftShoulder = 9,
    RightShoulder = 10,
    DPadUp = 11,
    DPadDown = 12,
    DPadLeft = 13,
    DPadRight = 14,
}

/// <summary>Momentaufnahme eines Controllers: gedrückte Tasten als Bitmaske, Sticks −32768…32767 (Y nach unten positiv).</summary>
public readonly record struct GamepadState(int Buttons, short LeftX = 0, short LeftY = 0, short RightX = 0, short RightY = 0)
{
    public bool IsPressed(GamepadButton button) => (Buttons & (1 << (int)button)) != 0;

    /// <summary>Für Tests und Fake-Quellen: Zustand aus gedrückten Tasten.</summary>
    public static GamepadState Of(params GamepadButton[] buttons) => new(buttons.Aggregate(0, (mask, b) => mask | (1 << (int)b)));
}

/// <summary>Was die App aus einer Controller-Eingabe macht (siehe <see cref="GamepadMapper"/>).</summary>
public enum GamepadAction
{
    Up,
    Down,
    Left,
    Right,

    /// <summary>A: Aktivieren (Enter).</summary>
    Activate,

    /// <summary>B: Zurück (Escape, sonst Alt+←).</summary>
    Back,

    /// <summary>LB: vorheriger Bereich der Seitenleiste.</summary>
    PreviousSection,

    /// <summary>RB: nächster Bereich der Seitenleiste.</summary>
    NextSection,

    /// <summary>Menü/Start (≡): Seitenleiste öffnen bzw. Fokus hinein.</summary>
    Menu,

    /// <summary>Ansicht/Back (⧉): Suche (Strg+K).</summary>
    Search,

    /// <summary>Y: Aktualisieren (F5).</summary>
    Refresh,

    /// <summary>Rechter Stick: Seite scrollen.</summary>
    ScrollUp,
    ScrollDown,
}

/// <summary>
/// Quelle für Controller-Zustände (SDL oder Fake). Alle Aufrufe kommen vom selben Thread (dem Abfrage-Thread des
/// <see cref="GamepadService"/>).
/// </summary>
public interface IGamepadBackend : IDisposable
{
    /// <summary>Name der Bibliothek (z. B. „SDL3“, „SDL2“) – für die Diagnose in den Einstellungen.</summary>
    string Library { get; }

    /// <summary>Initialisieren; <c>false</c> = nicht nutzbar (die App läuft ohne Controller weiter).</summary>
    bool Initialize();

    /// <summary>
    /// Zustand des ersten angeschlossenen Controllers; <c>null</c> = keiner. Mit <paramref name="rescan"/> wird nach neu
    /// angeschlossenen Geräten gesucht (Hot-Plug), sonst nur der offene Controller gelesen.
    /// </summary>
    GamepadState? Poll(bool rescan);

    /// <summary>Name des offenen Controllers (z. B. „Steam Deck“, „Xbox Wireless Controller“).</summary>
    string? ControllerName { get; }
}
