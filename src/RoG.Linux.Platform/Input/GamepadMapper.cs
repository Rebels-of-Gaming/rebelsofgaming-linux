namespace RoG.Linux.Platform.Input;

/// <summary>
/// Übersetzt Controller-Zustände in <see cref="GamepadAction"/>s – ohne SDL, mit eigener Zeitbasis (testbar).
/// <list type="bullet">
/// <item>Steuerkreuz und linker Stick → Richtung; Stick mit Totzone und Hysterese (an ab ~49 %, aus unter ~37 %),
///   nur die stärkere Achse zählt (keine Diagonalen).</item>
/// <item>Halten wiederholt Richtungen nach <see cref="InitialDelay"/> alle <see cref="RepeatInterval"/>; rechter Stick
///   scrollt schneller.</item>
/// <item>Alle anderen Tasten lösen einmal beim Drücken aus.</item>
/// <item>Nach <see cref="Reset"/> (Fokus zurück, neuer Controller) zählen schon gehaltene Tasten erst nach dem Loslassen –
///   kein „Phantom-A“ beim Zurückwechseln.</item>
/// </list>
/// </summary>
public sealed class GamepadMapper
{
    /// <summary>Stick-Ausschlag, ab dem eine Richtung gilt (~49 %).</summary>
    public const short PressThreshold = 16000;

    /// <summary>Stick-Ausschlag, unter dem die Richtung wieder losgelassen ist (~37 %) – Hysterese gegen Flattern.</summary>
    public const short ReleaseThreshold = 12000;

    public static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);
    public static readonly TimeSpan ScrollInitialDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ScrollRepeatInterval = TimeSpan.FromMilliseconds(60);

    private static readonly GamepadAction[] s_actions = Enum.GetValues<GamepadAction>();

    private readonly bool[] _active = new bool[s_actions.Length];
    private readonly bool[] _suppressed = new bool[s_actions.Length];
    private readonly TimeSpan[] _nextRepeat = new TimeSpan[s_actions.Length];
    private GamepadAction? _leftStick;
    private GamepadAction? _rightStick;
    private bool _primed = true;

    /// <summary>Alles loslassen; gerade gehaltene Eingaben lösen erst nach dem Loslassen wieder aus.</summary>
    public void Reset()
    {
        for (var i = 0; i < _active.Length; i++)
        {
            _suppressed[i] = _active[i] || _suppressed[i];
            _active[i] = false;
        }

        _primed = false;
    }

    /// <summary>Neuen Zustand verarbeiten (<c>null</c> = kein Controller); liefert die auszulösenden Aktionen.</summary>
    public IReadOnlyList<GamepadAction> Update(GamepadState? state, TimeSpan now)
    {
        var pressed = new bool[s_actions.Length];
        if (state is { } s)
        {
            _leftStick = StickDirection(s.LeftX, s.LeftY, _leftStick, vertical: true, horizontal: true);
            _rightStick = StickDirection(s.RightX, s.RightY, _rightStick, vertical: true, horizontal: false);
            Set(pressed, GamepadAction.Up, s.IsPressed(GamepadButton.DPadUp) || _leftStick == GamepadAction.Up);
            Set(pressed, GamepadAction.Down, s.IsPressed(GamepadButton.DPadDown) || _leftStick == GamepadAction.Down);
            Set(pressed, GamepadAction.Left, s.IsPressed(GamepadButton.DPadLeft) || _leftStick == GamepadAction.Left);
            Set(pressed, GamepadAction.Right, s.IsPressed(GamepadButton.DPadRight) || _leftStick == GamepadAction.Right);
            Set(pressed, GamepadAction.Activate, s.IsPressed(GamepadButton.A));
            Set(pressed, GamepadAction.Back, s.IsPressed(GamepadButton.B));
            Set(pressed, GamepadAction.PreviousSection, s.IsPressed(GamepadButton.LeftShoulder));
            Set(pressed, GamepadAction.NextSection, s.IsPressed(GamepadButton.RightShoulder));
            Set(pressed, GamepadAction.Menu, s.IsPressed(GamepadButton.Start));
            Set(pressed, GamepadAction.Search, s.IsPressed(GamepadButton.Back));
            Set(pressed, GamepadAction.Refresh, s.IsPressed(GamepadButton.Y));
            Set(pressed, GamepadAction.ScrollUp, _rightStick == GamepadAction.Up);
            Set(pressed, GamepadAction.ScrollDown, _rightStick == GamepadAction.Down);
        }
        else
        {
            _leftStick = _rightStick = null;
        }

        var result = new List<GamepadAction>();
        for (var i = 0; i < s_actions.Length; i++)
        {
            var action = s_actions[i];
            if (!pressed[i])
            {
                _active[i] = false;
                _suppressed[i] = false;
                continue;
            }

            if (!_primed || _suppressed[i])
            {
                // nach Reset: gehaltene Eingabe merken, aber erst nach dem Loslassen wieder auslösen
                _suppressed[i] = true;
                continue;
            }

            if (!_active[i])
            {
                _active[i] = true;
                _nextRepeat[i] = now + (IsScroll(action) ? ScrollInitialDelay : InitialDelay);
                result.Add(action);
            }
            else if (IsRepeating(action) && now >= _nextRepeat[i])
            {
                _nextRepeat[i] = now + (IsScroll(action) ? ScrollRepeatInterval : RepeatInterval);
                result.Add(action);
            }
        }

        _primed = true;
        return result;
    }

    private static void Set(bool[] pressed, GamepadAction action, bool value) => pressed[(int)action] = value;

    private static bool IsScroll(GamepadAction action) => action is GamepadAction.ScrollUp or GamepadAction.ScrollDown;

    private static bool IsRepeating(GamepadAction action) =>
        action is GamepadAction.Up or GamepadAction.Down or GamepadAction.Left or GamepadAction.Right || IsScroll(action);

    /// <summary>Richtung eines Sticks mit Totzone, Hysterese und stärkerer Achse.</summary>
    internal static GamepadAction? StickDirection(short x, short y, GamepadAction? previous, bool vertical, bool horizontal)
    {
        int ax = horizontal ? Math.Abs((int)x) : 0;
        int ay = vertical ? Math.Abs((int)y) : 0;

        // Bisherige Richtung bleibt, solange ihre Achse über der Loslass-Schwelle liegt und klar die stärkere ist
        if (previous is { } p)
        {
            var still = p switch
            {
                GamepadAction.Up => y <= -ReleaseThreshold && ay >= ax,
                GamepadAction.Down => y >= ReleaseThreshold && ay >= ax,
                GamepadAction.Left => x <= -ReleaseThreshold && ax >= ay,
                GamepadAction.Right => x >= ReleaseThreshold && ax >= ay,
                _ => false,
            };
            if (still)
            {
                return p;
            }
        }

        if (Math.Max(ax, ay) < PressThreshold)
        {
            return null;
        }

        return ay >= ax
            ? (y < 0 ? GamepadAction.Up : GamepadAction.Down)
            : (x < 0 ? GamepadAction.Left : GamepadAction.Right);
    }
}
