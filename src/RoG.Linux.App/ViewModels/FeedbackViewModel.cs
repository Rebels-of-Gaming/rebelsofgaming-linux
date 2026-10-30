using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Servers;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Rückmeldung nach einer Aktion (wie die InfoBar der Windows-App): Text und Ton (Erfolg/Hinweis/Fehler), schließbar.
/// Angezeigt von <c>Controls/FeedbackBar</c>.
/// </summary>
public sealed partial class FeedbackViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess), nameof(IsInfo), nameof(IsError))]
    public partial MessageTone Tone { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public bool IsSuccess => Tone == MessageTone.Success;

    public bool IsInfo => Tone == MessageTone.Info;

    public bool IsError => Tone == MessageTone.Error;

    public void Show(string message, MessageTone tone)
    {
        // Schließen und neu öffnen, damit gleiche Meldungen erneut angesagt werden
        IsOpen = false;
        Message = message;
        Tone = tone;
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;
}

/// <summary>Seite mit Liste und Detail: Escape bzw. „Zurück“ schließt das Detail.</summary>
public interface IDetailPage
{
    bool IsDetailOpen { get; }

    void CloseDetail();
}

/// <summary>Seite, die einen Eintrag per Schlüssel öffnen kann (Deep Link, Suche, Startseite, Benachrichtigung).</summary>
public interface IRoutablePage
{
    void Open(string key);
}

/// <summary>Seite mit eigener Aktualisierung (F5), z. B. je nach offenem Reiter.</summary>
public interface IRefreshablePage
{
    Task RefreshAsync();
}
