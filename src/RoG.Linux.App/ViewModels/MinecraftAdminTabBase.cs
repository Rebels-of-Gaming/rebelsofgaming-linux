using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Gemeinsame Grundlage der Minecraft-Reiter der Verwaltung (Minecraft, Rebel-Coins, Minecraft-Netzwerk): Rückmeldung
/// und Rückfrage kommen von der Seite (eine Leiste für alle Reiter), Laden beim ersten Öffnen (<see cref="ILazyTab"/>),
/// Escape schließt eine offene Rückfrage (<see cref="CloseOverlay"/>). Fehler als übersetzte Rückmeldung.
/// </summary>
public abstract partial class MinecraftAdminTabBase : ObservableObject, ILazyTab
{
    private bool _loaded;

    protected MinecraftAdminTabBase(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
    {
        Services = services;
        Feedback = feedback;
        Confirm = confirm;
        Minecraft = new MinecraftService(services.Api.Http);
    }

    protected AppServices Services { get; }

    protected MinecraftService Minecraft { get; }

    public FeedbackViewModel Feedback { get; }

    public InlineConfirmViewModel Confirm { get; }

    /// <summary>Darf der Reiter gezeigt werden (Recht + Token-Scope aus <c>/v1/me</c>)?</summary>
    public abstract bool CanView { get; }

    /// <summary>Lädt beim ersten Aufruf (Reiter wird sichtbar), danach nur über „Aktualisieren“.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded || !CanView)
        {
            return;
        }

        _loaded = true;
        await LoadFirstAsync();
    }

    protected bool IsLoaded => _loaded;

    protected abstract Task LoadFirstAsync();

    /// <summary>Escape: offene Rückfrage abbrechen. <c>true</c> = etwas geschlossen.</summary>
    public virtual bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>Recht und Scope (Recht ohne Scope zählt nicht – ein Token darf nie mehr als sein Konto).</summary>
    protected bool Allowed(string permission, string scope) =>
        Services.CurrentUser is { } me && me.Can(permission) && me.HasScope(scope);

    internal System.Globalization.CultureInfo Culture => Services.Culture;

    protected string L(string key) => Services.T(key);

    protected string Format(string key, params object?[] args) => Services.Format(key, args);

    /// <summary>Übersetzter Wert, sonst der Rohwert (die Website ist tolerant und kennt evtl. mehr).</summary>
    internal string Label(string prefix, string value)
    {
        var key = prefix + value;
        var text = Services.T(key);
        return string.IsNullOrEmpty(text) || text == key ? value : text;
    }

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
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
            return false;
        }
    }

    protected void ShowError(Exception ex) => Show(ErrorText(Services, ex), MessageTone.Error);

    internal static bool IsExpected(Exception ex) =>
        ex is ApiProblemException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException or ArgumentException or IOException;

    internal static string ErrorText(AppServices services, Exception ex) => ex switch
    {
        ApiProblemException p => services.Localizer.GetError(p.Problem.Code),
        HttpRequestException => services.Localizer.GetError("network_error"),
        TaskCanceledException => services.Localizer.GetError("timeout"),
        _ => services.Localizer.GetError(ApiProblem.Codes.Unknown),
    };

    /// <summary>
    /// Ohne das Zeichen ⛃ der Windows-Texte (fehlt in den üblichen Linux-Systemschriften, stünde als Kästchen da) –
    /// „Coins“ liest sich in allen drei Sprachen.
    /// </summary>
    internal static string WithoutCoinGlyph(string text) => text.Replace("⛃", "Coins", StringComparison.Ordinal);

    internal static int ToInt(decimal? value) => value is { } v ? (int)Math.Clamp(Math.Round(v), 0, int.MaxValue) : 0;
}
