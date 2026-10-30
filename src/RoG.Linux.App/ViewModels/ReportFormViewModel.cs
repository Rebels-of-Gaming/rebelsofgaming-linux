using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Moderation;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// „Inhalt melden“ (Guide 28) als Formular im Inhalt statt ContentDialog (wie <c>ReportDialog</c> der Windows-App):
/// Grund aus den Meldegründen der API (Kinderschutz zuerst), optionaler Kommentar, danach Dank. Die Meldung geht an die
/// Moderation, nie an die gemeldete Person. Eine Seite hält eine Instanz und öffnet sie je Inhalt mit <see cref="Open"/>;
/// angezeigt von <c>Controls/ReportForm</c> (Escape schließt).
/// </summary>
public sealed partial class ReportFormViewModel : ObservableObject
{
    /// <summary>Token-Scope zum Melden (<c>POST /v1/reports</c>).</summary>
    public const string Scope = "reports:write";

    public const int MaxComment = 1000;

    private readonly AppServices _services;
    private string? _type;
    private string? _id;
    private string? _locale;

    public ReportFormViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Reasons = ModerationService.ReportReasons.Select(r => new SettingsOption(r, services.T("Report_Reason_" + r))).ToList();
    }

    /// <summary>Darf die angemeldete Person melden? (angemeldet + Scope <c>reports:write</c>; was sie sehen darf, prüft der Server)</summary>
    public static bool IsAllowed(AppServices services) => services?.CurrentUser is { } me && me.HasScope(Scope);

    /// <summary>Gründe in der Reihenfolge der API (<c>child_safety</c> zuerst).</summary>
    public IReadOnlyList<SettingsOption> Reasons { get; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Was gemeldet wird („Forenbeitrag von Nyx“) – als Unterzeile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubject))]
    public partial string? Subject { get; set; }

    public bool HasSubject => !string.IsNullOrEmpty(Subject);

    [ObservableProperty]
    public partial SettingsOption? SelectedReason { get; set; }

    [ObservableProperty]
    public partial string Comment { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    /// <summary>Gesendet: Dank statt Formular.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    public partial bool IsSent { get; set; }

    public bool ShowForm => !IsSent;

    /// <summary>Nach erfolgreichem Senden (Typ, ID) – z. B. für Tests oder eine Rückmeldung der Seite.</summary>
    public event EventHandler<(string Type, string Id)>? Reported;

    /// <summary>
    /// Formular für einen Inhalt öffnen. <paramref name="type"/>: <c>message</c>, <c>profile</c>, <c>gallery_item</c>,
    /// <c>news_comment</c>, <c>forum_post</c>, <c>wiki_page</c> (mit <paramref name="locale"/>) oder <c>wiki_revision</c>.
    /// </summary>
    public void Open(string type, string id, string? subject = null, string? locale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _type = type;
        _id = id;
        _locale = locale;
        Subject = subject;
        SelectedReason = null;
        Comment = string.Empty;
        Error = null;
        IsSent = false;
        IsOpen = true;
    }

    /// <summary>Schließen (auch Escape); <c>true</c>, wenn es offen war.</summary>
    public bool Close()
    {
        if (!IsOpen)
        {
            return false;
        }

        IsOpen = false;
        return true;
    }

    [RelayCommand]
    private void Cancel() => Close();

    private bool CanSend() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (_type is null || _id is null)
        {
            return;
        }

        if (SelectedReason is not { } reason)
        {
            Error = _services.T(StringKeys.ReportReasonMissing);
            return;
        }

        IsSending = true;
        Error = null;
        try
        {
            await new ModerationService(_services.Api.Client).ReportAsync(_type, _id, reason.Value, Comment, _locale);
            IsSent = true;
            Reported?.Invoke(this, (_type, _id));
        }
        catch (ApiProblemException ex)
        {
            // 429 too_many_reports (z. B. mehrere Kinderschutz-Meldungen) mit Wartezeit aus Retry-After
            Error = ex.Problem.RetryAfter is { } wait && ex.Problem.IsRateLimited
                ? _services.Localizer.GetError(ex.Problem.Code) + " " + _services.Format(StringKeys.CommonRetryAfter, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)))
                : _services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException or ArgumentException)
        {
            Error = _services.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsSending = false;
        }
    }
}
