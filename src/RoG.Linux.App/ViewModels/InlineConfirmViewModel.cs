using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Rückfrage im Inhalt statt ContentDialog (Avalonia hat keinen, Annahme L31): Titel, Text, „Bestätigen“ und
/// „Abbrechen“ (Standard). <see cref="AskAsync"/> wartet auf die Antwort; Escape bzw. eine neue Frage gilt als Nein.
/// Angezeigt von <c>Controls/ConfirmBar</c>.
/// </summary>
public sealed partial class InlineConfirmViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _pending;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTitle))]
    public partial string? Title { get; set; }

    public bool HasTitle => !string.IsNullOrEmpty(Title);

    [ObservableProperty]
    public partial string? Text { get; set; }

    [ObservableProperty]
    public partial string ConfirmLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CancelLabel { get; set; } = string.Empty;

    /// <summary>Fragt nach; <c>true</c> = bestätigt. Eine noch offene Frage wird dabei mit Nein beendet.</summary>
    public Task<bool> AskAsync(string? title, string text, string confirmLabel, string cancelLabel)
    {
        _pending?.TrySetResult(false);
        var pending = new TaskCompletionSource<bool>();
        _pending = pending;
        Title = title;
        Text = text;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        IsOpen = true;
        return pending.Task;
    }

    [RelayCommand]
    private void Accept() => Finish(true);

    /// <summary>Abbrechen (auch Escape).</summary>
    [RelayCommand]
    public void Cancel() => Finish(false);

    private void Finish(bool result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}
