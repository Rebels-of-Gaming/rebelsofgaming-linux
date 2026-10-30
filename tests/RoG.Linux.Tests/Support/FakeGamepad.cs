using RoG.Linux.Platform.Input;

namespace RoG.Linux.Tests.Support;

/// <summary>Controller ohne SDL: Zustand und Name setzt der Test; zählt Initialisierung und Suchläufe.</summary>
internal sealed class FakeGamepadBackend(bool initializes = true) : IGamepadBackend
{
    public string Library => "Fake";

    /// <summary>Angeschlossener Controller; <c>null</c> = keiner.</summary>
    public string? Connected { get; set; } = "Steam Deck Controller";

    public GamepadState State { get; set; }

    public int Rescans { get; private set; }

    public bool Disposed { get; private set; }

    public string? ControllerName { get; private set; }

    public bool Initialize() => initializes;

    public GamepadState? Poll(bool rescan)
    {
        if (rescan)
        {
            Rescans++;
            ControllerName = Connected;
        }
        else if (Connected is null)
        {
            ControllerName = null; // abgezogen: sofort weg
        }

        return ControllerName is null ? null : State;
    }

    public void Dispose() => Disposed = true;
}
