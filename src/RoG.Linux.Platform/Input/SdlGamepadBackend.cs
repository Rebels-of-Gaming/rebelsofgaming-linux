using System.Runtime.InteropServices;

namespace RoG.Linux.Platform.Input;

/// <summary>
/// Controller über SDL – zur Laufzeit geladen, keine Abhängigkeit beim Bauen: zuerst <c>libSDL3.so.0</c> (Flatpak-Runtime
/// freedesktop 25.08 bringt SDL3 und sdl2-compat mit), sonst <c>libSDL2-2.0.so.0</c> (Distributionen). Nur das
/// Gamepad-Subsystem, kein Video/Audio; SDL setzt keine eigenen Signal-Handler (SIGTERM gehört der App).
/// Fehlt die Bibliothek oder schlägt die Initialisierung fehl, liefert <see cref="TryCreate"/> <c>null</c>.
/// Alle Aufrufe vom Abfrage-Thread des <see cref="GamepadService"/> (SDL ist nicht threadsicher).
/// </summary>
public sealed unsafe class SdlGamepadBackend : IGamepadBackend
{
    public const string Sdl3Library = "libSDL3.so.0";
    public const string Sdl2Library = "libSDL2-2.0.so.0";

    private const uint InitGamepad = 0x2000; // SDL_INIT_GAMECONTROLLER (SDL2) = SDL_INIT_GAMEPAD (SDL3)
    private const int ButtonCount = 15;

    private readonly IntPtr _library;
    private readonly bool _sdl3;
    private IntPtr _pad;
    private bool _initialized;

    // gemeinsam
    private delegate* unmanaged<byte*, byte*, int> _setHint; // SDL2: SDL_bool (int); SDL3: bool – nur Wert != 0 zählt
    private delegate* unmanaged<void> _quit;
    private delegate* unmanaged<uint, uint, void> _flushEvents;

    // SDL2
    private delegate* unmanaged<uint, int> _init2;
    private delegate* unmanaged<int> _numJoysticks;
    private delegate* unmanaged<int, int> _isGameController;
    private delegate* unmanaged<int, IntPtr> _open2;
    private delegate* unmanaged<IntPtr, IntPtr> _name2;
    private delegate* unmanaged<IntPtr, int> _attached2;
    private delegate* unmanaged<IntPtr, int, byte> _button2;
    private delegate* unmanaged<IntPtr, int, short> _axis2;
    private delegate* unmanaged<void> _update2;
    private delegate* unmanaged<IntPtr, void> _close2;

    // SDL3
    private delegate* unmanaged<uint, byte> _init3;
    private delegate* unmanaged<int*, uint*> _getGamepads;
    private delegate* unmanaged<void*, void> _free;
    private delegate* unmanaged<uint, IntPtr> _open3;
    private delegate* unmanaged<IntPtr, IntPtr> _name3;
    private delegate* unmanaged<IntPtr, byte> _connected3;
    private delegate* unmanaged<IntPtr, int, byte> _button3;
    private delegate* unmanaged<IntPtr, int, short> _axis3;
    private delegate* unmanaged<void> _update3;
    private delegate* unmanaged<IntPtr, void> _close3;

    private SdlGamepadBackend(IntPtr library, bool sdl3)
    {
        _library = library;
        _sdl3 = sdl3;
        Library = sdl3 ? "SDL3" : "SDL2";
    }

    public string Library { get; }

    public string? ControllerName { get; private set; }

    /// <summary>SDL laden (SDL3, sonst SDL2); <c>null</c>, wenn keine passende Bibliothek da ist.</summary>
    public static SdlGamepadBackend? TryCreate() => TryCreate([Sdl3Library, Sdl2Library]);

    internal static SdlGamepadBackend? TryCreate(IEnumerable<string> libraries)
    {
        foreach (var name in libraries)
        {
            if (!NativeLibrary.TryLoad(name, out var handle))
            {
                continue;
            }

            var backend = new SdlGamepadBackend(handle, name.Contains("SDL3", StringComparison.Ordinal));
            if (backend.Bind())
            {
                return backend;
            }

            NativeLibrary.Free(handle);
        }

        return null;
    }

    private bool Bind()
    {
        IntPtr Get(string name) => NativeLibrary.TryGetExport(_library, name, out var p) ? p : IntPtr.Zero;
        _setHint = (delegate* unmanaged<byte*, byte*, int>)Get("SDL_SetHint");
        _quit = (delegate* unmanaged<void>)Get("SDL_Quit");
        _flushEvents = (delegate* unmanaged<uint, uint, void>)Get("SDL_FlushEvents");
        if (_sdl3)
        {
            _init3 = (delegate* unmanaged<uint, byte>)Get("SDL_Init");
            _getGamepads = (delegate* unmanaged<int*, uint*>)Get("SDL_GetGamepads");
            _free = (delegate* unmanaged<void*, void>)Get("SDL_free");
            _open3 = (delegate* unmanaged<uint, IntPtr>)Get("SDL_OpenGamepad");
            _name3 = (delegate* unmanaged<IntPtr, IntPtr>)Get("SDL_GetGamepadName");
            _connected3 = (delegate* unmanaged<IntPtr, byte>)Get("SDL_GamepadConnected");
            _button3 = (delegate* unmanaged<IntPtr, int, byte>)Get("SDL_GetGamepadButton");
            _axis3 = (delegate* unmanaged<IntPtr, int, short>)Get("SDL_GetGamepadAxis");
            _update3 = (delegate* unmanaged<void>)Get("SDL_UpdateGamepads");
            _close3 = (delegate* unmanaged<IntPtr, void>)Get("SDL_CloseGamepad");
            return _setHint != null && _quit != null && _flushEvents != null && _init3 != null && _getGamepads != null
                && _free != null && _open3 != null && _name3 != null && _connected3 != null && _button3 != null
                && _axis3 != null && _update3 != null && _close3 != null;
        }

        _init2 = (delegate* unmanaged<uint, int>)Get("SDL_Init");
        _numJoysticks = (delegate* unmanaged<int>)Get("SDL_NumJoysticks");
        _isGameController = (delegate* unmanaged<int, int>)Get("SDL_IsGameController");
        _open2 = (delegate* unmanaged<int, IntPtr>)Get("SDL_GameControllerOpen");
        _name2 = (delegate* unmanaged<IntPtr, IntPtr>)Get("SDL_GameControllerName");
        _attached2 = (delegate* unmanaged<IntPtr, int>)Get("SDL_GameControllerGetAttached");
        _button2 = (delegate* unmanaged<IntPtr, int, byte>)Get("SDL_GameControllerGetButton");
        _axis2 = (delegate* unmanaged<IntPtr, int, short>)Get("SDL_GameControllerGetAxis");
        _update2 = (delegate* unmanaged<void>)Get("SDL_GameControllerUpdate");
        _close2 = (delegate* unmanaged<IntPtr, void>)Get("SDL_GameControllerClose");
        return _setHint != null && _quit != null && _flushEvents != null && _init2 != null && _numJoysticks != null
            && _isGameController != null && _open2 != null && _name2 != null && _attached2 != null && _button2 != null
            && _axis2 != null && _update2 != null && _close2 != null;
    }

    public bool Initialize()
    {
        if (_initialized)
        {
            return true;
        }

        // Keine SIGINT/SIGTERM-Handler von SDL (die App beendet sich selbst ordentlich); Eingaben auch ohne SDL-Fenster
        SetHint("SDL_NO_SIGNAL_HANDLERS", "1");
        SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        _initialized = _sdl3 ? _init3(InitGamepad) != 0 : _init2(InitGamepad) == 0;
        return _initialized;
    }

    private void SetHint(string name, string value)
    {
        var n = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        var v = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* pn = n)
        fixed (byte* pv = v)
        {
            _ = _setHint(pn, pv);
        }
    }

    public GamepadState? Poll(bool rescan)
    {
        if (!_initialized)
        {
            return null;
        }

        // Zustand und Hot-Plug aktualisieren; SDL-Ereignisse braucht die App nicht (Warteschlange leeren)
        if (_sdl3)
        {
            _update3();
        }
        else
        {
            _update2();
        }

        _flushEvents(0, uint.MaxValue);

        if (_pad != IntPtr.Zero && !IsConnected(_pad))
        {
            Close();
        }

        if (_pad == IntPtr.Zero && rescan)
        {
            OpenFirst();
        }

        if (_pad == IntPtr.Zero)
        {
            return null;
        }

        var buttons = 0;
        for (var i = 0; i < ButtonCount; i++)
        {
            if ((_sdl3 ? _button3(_pad, i) : _button2(_pad, i)) != 0)
            {
                buttons |= 1 << i;
            }
        }

        return new GamepadState(buttons, Axis(0), Axis(1), Axis(2), Axis(3));
    }

    private short Axis(int axis) => _sdl3 ? _axis3(_pad, axis) : _axis2(_pad, axis);

    private bool IsConnected(IntPtr pad) => _sdl3 ? _connected3(pad) != 0 : _attached2(pad) != 0;

    private void OpenFirst()
    {
        if (_sdl3)
        {
            int count;
            var ids = _getGamepads(&count);
            if (ids == null)
            {
                return;
            }

            try
            {
                for (var i = 0; i < count && _pad == IntPtr.Zero; i++)
                {
                    _pad = _open3(ids[i]);
                }
            }
            finally
            {
                _free(ids);
            }
        }
        else
        {
            var count = _numJoysticks();
            for (var i = 0; i < count && _pad == IntPtr.Zero; i++)
            {
                if (_isGameController(i) != 0)
                {
                    _pad = _open2(i);
                }
            }
        }

        if (_pad != IntPtr.Zero)
        {
            var name = _sdl3 ? _name3(_pad) : _name2(_pad);
            ControllerName = name == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(name);
        }
    }

    private void Close()
    {
        if (_pad == IntPtr.Zero)
        {
            return;
        }

        if (_sdl3)
        {
            _close3(_pad);
        }
        else
        {
            _close2(_pad);
        }

        _pad = IntPtr.Zero;
        ControllerName = null;
    }

    public void Dispose()
    {
        Close();
        if (_initialized)
        {
            _quit();
            _initialized = false;
        }

        // Bibliothek bleibt geladen: SDL kann eigene Threads (Hot-Plug) erst beim Prozessende ganz abbauen
    }
}
