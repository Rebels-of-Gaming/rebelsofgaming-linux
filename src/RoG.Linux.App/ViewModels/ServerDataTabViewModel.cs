using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Gemeinsame Grundlage der Reiter Configs, Dateien und Backups im Steuer-Fenster (wie <c>ServerTabViewModel</c> der
/// Windows-App): Server-Slug, wirksame Rechte, Rückmeldung, Rückfrage im Inhalt, Fehlertexte über <c>Error_&lt;code&gt;</c>.
/// Lädt beim ersten Anzeigen (<see cref="EnsureLoadedAsync"/>, ruft die View selbst auf).
/// </summary>
public abstract partial class ServerDataTabViewModel : LoadableViewModel
{
    private bool _loaded;

    /// <param name="feedback">Gemeinsame Rückmeldung des Fensters; ohne zeigt der Reiter eine eigene.</param>
    protected ServerDataTabViewModel(AppServices services, string slug, ServerAbilities abilities, FeedbackViewModel? feedback)
        : base(services)
    {
        Slug = slug;
        Abilities = abilities;
        ShowOwnFeedback = feedback is null;
        Feedback = feedback ?? new FeedbackViewModel();
    }

    public string Slug { get; }

    public ServerAbilities Abilities { get; }

    public FeedbackViewModel Feedback { get; }

    /// <summary>Eigene Rückmeldung im Reiter zeigen (keine gemeinsame vom Fenster übergeben).</summary>
    public bool ShowOwnFeedback { get; }

    /// <summary>Rückfrage vor Gefährlichem (Löschen, Zurückspielen, Wiederherstellen, Speichern).</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    /// <summary>Lädt beim ersten Aufruf (Reiter wird sichtbar), danach nur noch über „Aktualisieren“.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    /// <summary>Escape: offene Rückfrage bzw. Eingabe schließen. <c>true</c> = etwas geschlossen.</summary>
    public virtual bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>Scope des Tokens zusätzlich zum Recht (ohne bekanntes Profil entscheidet der Server).</summary>
    protected bool HasScope(string scope) => Services.CurrentUser?.HasScope(scope) != false;

    protected string L(string key) => Services.T(key);

    protected void Show(string text, MessageTone tone) => Feedback.Show(text, tone);

    protected Task<bool> AskAsync(string title, string text) =>
        Confirm.AskAsync(title, text, L(StringKeys.CommonConfirm), L(StringKeys.CommonCancel));

    /// <summary>Aktion ausführen; Fehler als übersetzte Rückmeldung. <c>true</c> = geklappt.</summary>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (HttpRequestException)
        {
            Show(Services.Localizer.GetError("network_error"), MessageTone.Error);
        }
        catch (TaskCanceledException)
        {
            Show(Services.Localizer.GetError("timeout"), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }

    /// <summary>Freier Dateiname im Ordner: „name.ext“, sonst „name (2).ext“ … (Name kommt vom Server → bereinigen).</summary>
    internal static string FreePath(string folder, string name)
    {
        var file = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']))
        {
            file = file.Replace(c, '_');
        }

        file = file.Trim().TrimStart('.');
        if (file.Length == 0)
        {
            file = "datei.txt";
        }

        var path = Path.Combine(folder, file);
        var stem = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
        }

        return path;
    }
}

/// <summary>
/// Eingabe im Inhalt statt Dialog (Annahme L31): Titel, Hinweis, Textfeld, „Bestätigen“/„Abbrechen“.
/// <see cref="AskAsync"/> liefert den Text oder <c>null</c> (abgebrochen, Escape, neue Frage).
/// </summary>
public sealed partial class InlinePromptViewModel : ObservableObject
{
    private TaskCompletionSource<string?>? _pending;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    public Task<string?> AskAsync(string title, string? hint, string suggestion)
    {
        _pending?.TrySetResult(null);
        var pending = new TaskCompletionSource<string?>();
        _pending = pending;
        Title = title;
        Hint = hint;
        Text = suggestion;
        IsOpen = true;
        return pending.Task;
    }

    [RelayCommand]
    private void Accept() => Finish(string.IsNullOrWhiteSpace(Text) ? null : Text.Trim());

    [RelayCommand]
    public void Cancel() => Finish(null);

    private void Finish(string? result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}
