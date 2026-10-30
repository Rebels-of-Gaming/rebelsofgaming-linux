namespace RoG.Linux.Platform.Input;

/// <summary>Stand der Controller-Unterstützung (Einstellungen → Controller).</summary>
public enum GamepadAvailability
{
    /// <summary>Noch nicht geprüft (Dienst nicht gestartet).</summary>
    Unknown,

    /// <summary>SDL geladen und initialisiert.</summary>
    Ready,

    /// <summary>Keine SDL-Bibliothek bzw. Initialisierung fehlgeschlagen – die App läuft ohne Controller.</summary>
    Unavailable,
}

/// <summary>
/// Controller-Eingabe: fragt die Quelle (SDL) auf einem eigenen Hintergrund-Thread ab – alle 16 ms, solange das Fenster
/// den Fokus hat, sonst nur alle 500 ms für Hot-Plug und Namen (ohne Aktionen). Neue Controller werden jede Sekunde
/// gesucht. Aktionen kommen über <see cref="ActionTriggered"/> auf dem Abfrage-Thread (die App reicht sie an den
/// UI-Thread weiter). Ausgeschaltet oder ohne SDL: stiller No-Op. Nichts verlässt den Rechner.
/// </summary>
public sealed class GamepadService : IDisposable
{
    public static readonly TimeSpan FocusedInterval = TimeSpan.FromMilliseconds(16);
    public static readonly TimeSpan BackgroundInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private readonly Func<IGamepadBackend?> _factory;
    private readonly Func<TimeSpan> _clock;
    private readonly GamepadMapper _mapper = new();
    private readonly Lock _gate = new();
    private IGamepadBackend? _backend;
    private Thread? _thread;
    private volatile bool _stopped;
    private volatile bool _enabled = true;
    private volatile bool _focused;
    private TimeSpan _lastScan = TimeSpan.MinValue;
    private bool _hadController;

    /// <param name="factory">Quelle (Standard SDL: <see cref="SdlGamepadBackend.TryCreate()"/>); <c>null</c> = nicht verfügbar.</param>
    /// <param name="clock">Zeitbasis (Tests); Standard monotone Uhr.</param>
    public GamepadService(Func<IGamepadBackend?>? factory = null, Func<TimeSpan>? clock = null)
    {
        _factory = factory ?? SdlGamepadBackend.TryCreate;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _clock = clock ?? (() => watch.Elapsed);
    }

    /// <summary>Aktion ausgelöst (auf dem Abfrage-Thread bzw. dem Thread, der <see cref="Tick"/> aufruft).</summary>
    public event EventHandler<GamepadAction>? ActionTriggered;

    /// <summary>Controller angeschlossen/getrennt oder Verfügbarkeit geändert.</summary>
    public event EventHandler? ControllerChanged;

    public GamepadAvailability Availability { get; private set; }

    /// <summary>„SDL3“/„SDL2“, sobald geladen.</summary>
    public string? Library { get; private set; }

    /// <summary>Name des angeschlossenen Controllers; <c>null</c> = keiner.</summary>
    public string? ControllerName { get; private set; }

    /// <summary>Einstellung „Controller verwenden“ (Standard an). Aus: keine Abfrage, keine Aktionen.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                lock (_gate)
                {
                    _mapper.Reset();
                }
            }
        }
    }

    /// <summary>Hat das App-Fenster den Fokus? Nur dann werden Aktionen ausgelöst.</summary>
    public bool WindowFocused
    {
        get => _focused;
        set
        {
            if (_focused == value)
            {
                return;
            }

            _focused = value;
            lock (_gate)
            {
                _mapper.Reset(); // gehaltene Tasten beim Fokuswechsel nicht auslösen
            }
        }
    }

    /// <summary>Andere Quelle verwenden (Tests, Fake-Controller) – ersetzt eine geladene Quelle.</summary>
    public void UseBackend(IGamepadBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            _backend?.Dispose();
            _backend = null;
            Availability = GamepadAvailability.Unknown;
            InitializeBackend(backend);
        }
    }

    /// <summary>Abfrage-Thread starten (einmal; die App beim Start, Tests rufen <see cref="Tick"/> selbst).</summary>
    public void Start()
    {
        if (_thread is not null || _stopped)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "RoG gamepad" };
        _thread.Start();
    }

    private void Run()
    {
        while (!_stopped)
        {
            try
            {
                Tick();
            }
#pragma warning disable CA1031 // Controller sind Beiwerk: ein Fehler der Quelle darf die App nie beenden
            catch (Exception)
#pragma warning restore CA1031
            {
                lock (_gate)
                {
                    _backend?.Dispose();
                    _backend = null;
                    Availability = GamepadAvailability.Unavailable;
                    ControllerName = null;
                }

                ControllerChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (Availability == GamepadAvailability.Unavailable)
            {
                return; // keine SDL: Thread endet, App läuft normal
            }

            Thread.Sleep(_enabled && _focused ? FocusedInterval : BackgroundInterval);
        }
    }

    /// <summary>Ein Abfrage-Schritt: Quelle lesen, Hot-Plug, Aktionen auslösen.</summary>
    public void Tick()
    {
        if (!_enabled || _stopped)
        {
            return;
        }

        IReadOnlyList<GamepadAction> actions;
        bool changed;
        lock (_gate)
        {
            if (_backend is null && Availability == GamepadAvailability.Unknown)
            {
                InitializeBackend(_factory());
            }

            if (_backend is null)
            {
                return;
            }

            var now = _clock();
            var rescan = _lastScan == TimeSpan.MinValue || now - _lastScan >= RescanInterval;
            if (rescan)
            {
                _lastScan = now;
            }

            var state = _backend.Poll(rescan);
            var name = state is null ? null : _backend.ControllerName ?? string.Empty;
            changed = name != ControllerName || (state is not null) != _hadController;
            if (changed)
            {
                ControllerName = name;
                _hadController = state is not null;
                _mapper.Reset(); // neuer Controller: gehaltene Tasten erst nach dem Loslassen
            }

            actions = _focused ? _mapper.Update(state, now) : [];
            if (!_focused)
            {
                _mapper.Reset();
            }
        }

        if (changed)
        {
            ControllerChanged?.Invoke(this, EventArgs.Empty);
        }

        foreach (var action in actions)
        {
            ActionTriggered?.Invoke(this, action);
        }
    }

    private void InitializeBackend(IGamepadBackend? backend)
    {
        if (backend is not null && backend.Initialize())
        {
            _backend = backend;
            Library = backend.Library;
            Availability = GamepadAvailability.Ready;
        }
        else
        {
            backend?.Dispose();
            Availability = GamepadAvailability.Unavailable;
        }

        _lastScan = TimeSpan.MinValue;
    }

    public void Dispose()
    {
        _stopped = true;
        _thread?.Join(TimeSpan.FromSeconds(1));
        lock (_gate)
        {
            _backend?.Dispose();
            _backend = null;
        }
    }
}
