using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoG.Linux.App.ViewModels;

/// <summary>Spielmodus und Controller: Bereich wechseln (LB/RB) und Hinweis auf die Bildschirmtastatur (Steam+X).</summary>
public sealed partial class ShellViewModel
{
    /// <summary>Wie lange der Tastatur-Hinweis stehen bleibt.</summary>
    public static readonly TimeSpan KeyboardHintDuration = TimeSpan.FromSeconds(8);

    private bool _keyboardHintShown;
    private Avalonia.Threading.DispatcherTimer? _keyboardHintTimer;

    /// <summary>Spielmodus (Gamescope/Steam Deck oder <c>--gamemode</c>).</summary>
    public bool IsGameMode => _services.GameMode;

    /// <summary>„Mit Steam + X öffnest du die Bildschirmtastatur“ – nur im Spielmodus, einmal je Sitzung.</summary>
    [ObservableProperty]
    public partial bool IsKeyboardHintVisible { get; set; }

    public string KeyboardHintText => _services.T("Linux_KeyboardHint");

    public string KeyboardHintCloseText => _services.T("Linux_KeyboardHintClose");

    /// <summary>Ein Eingabefeld hat den Fokus bekommen (vom Fenster gemeldet).</summary>
    public void OnTextInputFocused()
    {
        if (!IsGameMode || _keyboardHintShown)
        {
            return;
        }

        _keyboardHintShown = true;
        IsKeyboardHintVisible = true;
        _keyboardHintTimer = new Avalonia.Threading.DispatcherTimer { Interval = KeyboardHintDuration };
        _keyboardHintTimer.Tick += (_, _) => CloseKeyboardHint();
        _keyboardHintTimer.Start();
    }

    [RelayCommand]
    private void CloseKeyboardHint()
    {
        _keyboardHintTimer?.Stop();
        _keyboardHintTimer = null;
        IsKeyboardHintVisible = false;
    }

    /// <summary>Bereiche der Seitenleiste in ihrer Reihenfolge (nur sichtbare) – für LB/RB.</summary>
    public IReadOnlyList<NavItem> SectionOrder => [.. MainItems.Concat(FooterItems).Where(i => i.IsVisible)];

    /// <summary>LB/RB: vorheriger bzw. nächster Bereich der Seitenleiste (ringsum). <c>false</c> = Shell nicht sichtbar.</summary>
    public bool StepSection(int step)
    {
        var order = SectionOrder;
        if (IsLoginVisible || order.Count == 0)
        {
            return false;
        }

        var index = CurrentItem is null ? -1 : order.ToList().FindIndex(i => i.Key == CurrentItem.Key);
        var next = index < 0
            ? (step > 0 ? 0 : order.Count - 1)
            : ((index + step) % order.Count + order.Count) % order.Count;
        Navigate(order[next]);
        return true;
    }
}
