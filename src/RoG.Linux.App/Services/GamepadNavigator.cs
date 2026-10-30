using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Platform.Input;

namespace RoG.Linux.App.Services;

/// <summary>
/// Controller → Avalonia-Fokus. Richtungen wirken wie Pfeiltasten eines Gamepads (<see cref="KeyDeviceType.Gamepad"/>):
/// Listen, Reiter und Regler behandeln sie selbst (Auswahl wandert mit), sonst verschiebt Avalonias XY-Fokus den Fokus
/// räumlich (Seitenleiste links, Inhalt rechts, Karten im Raster). Findet sich in der Richtung nichts, gilt die
/// Tab-Reihenfolge (↓/→ = Tab, ↑/← = Umschalt+Tab) – so bleibt jedes Element erreichbar. Eingabefelder, geschlossene
/// Auswahllisten und Zahlenfelder bekommen keine Pfeiltasten (sonst würde der Stick den Text-Cursor bzw. den Wert ändern),
/// dort springt der Fokus direkt räumlich weiter.
/// A = Enter (Knöpfe, Schalter, Listen, Auswahl), B = Escape (Detail/Menü/Formular schließen), sonst Alt+← (Verlauf),
/// LB/RB = Bereich wechseln, Menü = Seitenleiste ↔ Inhalt, Ansicht = Suche (Strg+K), Y = F5, rechter Stick scrollt.
/// </summary>
public sealed class GamepadNavigator
{
    private const double ScrollStep = 48;

    private readonly Window _window;
    private readonly AppServices _services;

    public GamepadNavigator(Window window, AppServices services)
    {
        _window = window;
        _services = services;
    }

    private ShellViewModel? Shell => _window.DataContext as ShellViewModel;

    /// <summary>
    /// Controller an das Fenster hängen: Aktionen auf den UI-Thread, Fokus des Fensters an den Dienst, Tastatur-Hinweis
    /// bei Eingabefeldern (Spielmodus). Löst sich beim Schließen des Fensters.
    /// </summary>
    public static GamepadNavigator Attach(Window window, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(services);
        var navigator = new GamepadNavigator(window, services);
        void OnAction(object? sender, GamepadAction action) => Dispatcher.UIThread.Post(() => navigator.Execute(action));
        void OnActivated(object? sender, EventArgs e) => services.Gamepad.WindowFocused = true;
        void OnDeactivated(object? sender, EventArgs e) => services.Gamepad.WindowFocused = false;
        services.Gamepad.ActionTriggered += OnAction;
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.AddHandler(InputElement.GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox)
            {
                (window.DataContext as ShellViewModel)?.OnTextInputFocused();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        window.Closed += (_, _) =>
        {
            services.Gamepad.ActionTriggered -= OnAction;
            services.Gamepad.WindowFocused = false;
        };
        return navigator;
    }

    /// <summary>Eine Aktion ausführen (UI-Thread). <c>true</c>, wenn sie etwas bewirkt hat.</summary>
    public bool Execute(GamepadAction action)
    {
        var shellUsable = Shell is { IsShellVisible: true } && !_services.AppLock.IsLocked;
        return action switch
        {
            GamepadAction.Up => Move(NavigationDirection.Up, Key.Up),
            GamepadAction.Down => Move(NavigationDirection.Down, Key.Down),
            GamepadAction.Left => Move(NavigationDirection.Left, Key.Left),
            GamepadAction.Right => Move(NavigationDirection.Right, Key.Right),
            GamepadAction.Activate => Activate(),
            GamepadAction.Back => Back(shellUsable),
            GamepadAction.PreviousSection => shellUsable && StepSection(-1),
            GamepadAction.NextSection => shellUsable && StepSection(1),
            GamepadAction.Menu => shellUsable && ToggleMenu(),
            GamepadAction.Search => shellUsable && RaiseKey(FocusTarget(), Key.K, KeyModifiers.Control),
            GamepadAction.Refresh => shellUsable && RaiseKey(FocusTarget(), Key.F5),
            GamepadAction.ScrollUp => Scroll(-ScrollStep),
            GamepadAction.ScrollDown => Scroll(ScrollStep),
            _ => false,
        };
    }

    private IFocusManager? Focus => _window.FocusManager;

    private IInputElement? Focused => Focus?.GetFocusedElement() is Visual v && v.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(v), _window)
        ? (IInputElement)v
        : null;

    private IInputElement FocusTarget() => Focused ?? _window;

    /// <summary>Überlagerte Seitenleiste (schmale Ansicht): Fokus bleibt darin.</summary>
    private Control? OverlayRoot => Shell is { IsOverlayOpen: true }
        ? _window.GetVisualDescendants().OfType<NavPane>().FirstOrDefault(p => p.Name == "OverlayPane" && p.IsEffectivelyVisible)
        : null;

    private bool Move(NavigationDirection direction, Key key)
    {
        var focused = Focused;
        if (focused is null)
        {
            return FocusFirst();
        }

        var overlay = OverlayRoot;
        if (overlay is null && WantsArrowKeys(focused, key))
        {
            var before = focused;
            if (RaiseKey(focused, key) || !ReferenceEquals(Focused, before))
            {
                BringFocusIntoView();
                return true;
            }
        }

        var options = new FindNextElementOptions { FocusedElement = focused, SearchRoot = overlay };
        var next = Focus!.FindNextElement(direction, options)
            ?? Focus.FindNextElement(direction is NavigationDirection.Down or NavigationDirection.Right ? NavigationDirection.Next : NavigationDirection.Previous, options);
        if (next is null || (overlay is not null && next is Visual nv && !overlay.IsVisualAncestorOf(nv)))
        {
            return false;
        }

        var moved = next.Focus(NavigationMethod.Directional);
        BringFocusIntoView();
        return moved;
    }

    /// <summary>Darf das Element die Pfeiltaste selbst behandeln? Nicht bei Text, geschlossenen Auswahllisten, Zahlen.</summary>
    private static bool WantsArrowKeys(IInputElement element, Key key) => element switch
    {
        TextBox => false,
        ComboBox combo => combo.IsDropDownOpen,
        NumericUpDown => false,
        Slider slider => slider.Orientation == Avalonia.Layout.Orientation.Horizontal ? key is Key.Left or Key.Right : key is Key.Up or Key.Down,
        _ => true,
    };

    private void BringFocusIntoView() => (Focused as Control)?.BringIntoView();

    /// <summary>Kein Fokus: erstes bedienbares Element der Seite (bzw. der überlagerten Seitenleiste).</summary>
    public bool FocusFirst()
    {
        var root = OverlayRoot ?? PageHost() as Control;
        var first = root is not null ? FocusManager.FindFirstFocusableElement(root) : null;
        first ??= Focus?.FindFirstFocusableElement();
        if (first is null)
        {
            return false;
        }

        var done = first.Focus(NavigationMethod.Directional);
        BringFocusIntoView();
        return done;
    }

    /// <summary>Der Platz, an dem die aktuelle Seite steht.</summary>
    private ContentControl? PageHost() => Shell?.CurrentPage is { } page
        ? _window.GetVisualDescendants().OfType<ContentControl>().FirstOrDefault(c => ReferenceEquals(c.Content, page) && c.IsEffectivelyVisible)
        : null;

    private bool Activate()
    {
        var focused = Focused;
        if (focused is null)
        {
            return FocusFirst();
        }

        if (RaiseKey(focused, Key.Enter))
        {
            return true;
        }

        // Listeneintrag, der per XY-Fokus erreicht wurde, aber noch nicht ausgewählt ist
        if (focused is ListBoxItem { IsSelected: false } item)
        {
            item.IsSelected = true;
            return true;
        }

        return false;
    }

    private bool Back(bool shellUsable)
    {
        if (RaiseKey(FocusTarget(), Key.Escape))
        {
            return true;
        }

        return shellUsable && Shell!.GoBack();
    }

    private bool StepSection(int step)
    {
        if (!Shell!.StepSection(step))
        {
            return false;
        }

        // Fokus auf den neuen Eintrag der Seitenleiste – → führt in den Inhalt (nach dem Layout der neuen Seite)
        Dispatcher.UIThread.Post(() =>
        {
            if (!FocusNavEntry())
            {
                FocusFirst();
            }
        }, DispatcherPriority.Background);
        return true;
    }

    /// <summary>Menü: Fokus in die Seitenleiste (schmal: Überlagerung öffnen) – noch einmal: zurück in den Inhalt.</summary>
    private bool ToggleMenu()
    {
        var shell = Shell!;
        if (shell.IsNarrow)
        {
            shell.IsOverlayOpen = !shell.IsOverlayOpen;
            Dispatcher.UIThread.Post(() =>
            {
                if (!shell.IsOverlayOpen || !FocusNavEntry())
                {
                    FocusFirst();
                }
            }, DispatcherPriority.Background);
            return true;
        }

        var inPane = Focused is Visual v && v.FindAncestorOfType<NavPane>(includeSelf: true) is not null;
        return inPane ? FocusFirst() : FocusNavEntry();
    }

    /// <summary>Den Eintrag der aktuellen Seite in der sichtbaren Seitenleiste fokussieren.</summary>
    private bool FocusNavEntry()
    {
        var current = Shell?.CurrentItem;
        var button = _window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("nav") && b.IsEffectivelyVisible && (OverlayRoot is not { } o || o.IsVisualAncestorOf(b)))
            .FirstOrDefault(b => current is not null && ReferenceEquals(b.DataContext, current));
        if (button is null)
        {
            return false;
        }

        var done = button.Focus(NavigationMethod.Directional);
        button.BringIntoView();
        return done;
    }

    private bool Scroll(double delta)
    {
        var viewer = (Focused as Visual)?.FindAncestorOfType<ScrollViewer>()
            ?? PageHost()?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height);
        if (viewer is null)
        {
            return false;
        }

        var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var y = Math.Clamp(viewer.Offset.Y + delta, 0, max);
        if (Math.Abs(y - viewer.Offset.Y) < 0.5)
        {
            return false;
        }

        viewer.Offset = viewer.Offset.WithY(y);
        return true;
    }

    /// <summary>
    /// Taste wie von einem Gamepad auslösen (KeyDown/KeyUp am fokussierten Element, mit Tunnel und Bubble bis zum
    /// Fenster). Unbehandelt → <see cref="InputElement.KeyBindings"/> der Vorfahren (z. B. Escape/Enter in Formularen).
    /// </summary>
    internal static bool RaiseKey(IInputElement target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (target is not Interactive interactive)
        {
            return false;
        }

        var down = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            KeyDeviceType = KeyDeviceType.Gamepad,
            Source = interactive,
        };
        interactive.RaiseEvent(down);
        var handled = down.Handled || InvokeKeyBindings(interactive, down);
        interactive.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = modifiers,
            KeyDeviceType = KeyDeviceType.Gamepad,
            Source = interactive,
        });
        return handled;
    }

    private static bool InvokeKeyBindings(Interactive target, KeyEventArgs e)
    {
        for (Visual? v = target as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v is not InputElement element)
            {
                continue;
            }

            foreach (var binding in element.KeyBindings)
            {
                if (binding.Gesture?.Matches(e) == true && binding.Command?.CanExecute(binding.CommandParameter) == true)
                {
                    binding.Command.Execute(binding.CommandParameter);
                    return true;
                }
            }
        }

        return false;
    }
}
