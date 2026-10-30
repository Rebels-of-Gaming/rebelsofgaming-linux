using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Social;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Karte „Social Media“ an einer Quelle (News, Termin, Clanwar, Galerie-Album) wie <c>SocialStatusViewModel</c> der
/// Windows-App: Zustand, Kanäle mit Link/Fehler, erneut senden, neu vorbereiten. Sichtbar nur mit Recht
/// <c>social.publish</c> und Scope <c>social:read</c>; Aktionen zusätzlich mit <c>social:write</c>. Rückfrage
/// („bewusst erneut posten“) als Leiste in der Karte (<see cref="Confirmation"/>).
/// </summary>
public sealed partial class SocialStatusViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SocialSourceService _social;
    private string? _source;
    private string? _id;
    private SocialSourceStatus? _status;
    private int _generation;

    public SocialStatusViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _social = new SocialSourceService(services.Api.Http);
        Confirm = (title, text) => Confirmation.AskAsync(title, text, services.T(StringKeys.CommonConfirm), services.T(StringKeys.CommonCancel));
    }

    /// <summary>Rückfrage (Titel, Text) → bestätigt; Standard: Leiste in der Karte.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    public InlineConfirmViewModel Confirmation { get; } = new();

    public FeedbackViewModel Feedback { get; } = new();

    public bool CanRead => _services.CurrentUser is { } me && me.Can(SocialSourceService.Permission) && me.HasScope(SocialSourceService.ReadScope);

    public bool CanWrite => CanRead && _services.CurrentUser!.HasScope(SocialSourceService.WriteScope);

    public ObservableCollection<SocialTargetViewModel> Targets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool HasSource { get; set; }

    /// <summary>Karte sichtbar: gespeicherte Quelle und Recht/Scope.</summary>
    public bool IsShown => HasSource && CanRead;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StateText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial string? DetailText { get; set; }

    public bool HasDetail => !string.IsNullOrEmpty(DetailText);

    [ObservableProperty]
    public partial bool ShowRetryFailed { get; set; }

    [ObservableProperty]
    public partial bool ShowRedraft { get; set; }

    /// <summary>Entwürfe freigeben, Kanäle verwalten: Social-Verwaltung der Website.</summary>
    public bool HasAdminLink => _services.Environment.AdminBaseUrl is not null;

    /// <summary>Quelle setzen (null = noch nicht gespeichert → Karte aus) und Status laden.</summary>
    public Task ShowAsync(string source, string? id)
    {
        _source = source;
        _id = id;
        HasSource = id is not null;
        Targets.Clear();
        _status = null;
        StateText = null;
        DetailText = null;
        ShowRetryFailed = false;
        ShowRedraft = false;
        Feedback.Close();
        Confirmation.Cancel();
        OnPropertyChanged(nameof(IsShown));
        return IsShown ? RefreshAsync() : Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_source is null || _id is null || !CanRead)
        {
            return;
        }

        var generation = ++_generation;
        var source = _source;
        var id = _id;
        await RunAsync(async () =>
        {
            var status = await _social.GetAsync(source, id);
            if (generation == _generation)
            {
                Apply(status);
            }
        }, success: null);
    }

    private void Apply(SocialSourceStatus status)
    {
        _status = status;
        StateText = Label("Social_State_", status.State);
        DetailText = !status.AnyChannel ? _services.T(StringKeys.SocialNoChannels)
            : !status.Eligible && status.PostId is null ? _services.T(StringKeys.SocialNotEligible)
            : status.ScheduledAt is { } at && status.State == "scheduled"
                ? _services.Format(StringKeys.SocialScheduledAt, at.ToLocalTime().ToString("g", _services.Culture))
                : null;
        Targets.Clear();
        foreach (var target in status.Targets)
        {
            Targets.Add(new SocialTargetViewModel(target, this, _services, CanWrite));
        }

        ShowRetryFailed = CanWrite && status.CanRetryFailed;
        ShowRedraft = CanWrite && status.CanRedraft;
    }

    [RelayCommand]
    private Task RetryFailedAsync() => _status?.PostId is not { } post ? Task.CompletedTask : RunAsync(async () =>
    {
        var result = await _social.RetryFailedAsync(post);
        await ReloadQuietAsync();
        var text = _services.Format(StringKeys.SocialRetryStarted, result.Count);
        if (result.Unusable.Count > 0)
        {
            text += " " + _services.Format(StringKeys.SocialUnusable, string.Join(", ", result.Unusable.Select(u => $"{u.Name} ({Label("Social_Unusable_", u.Reason)})")));
        }

        Feedback.Show(text, result.Unusable.Count > 0 ? MessageTone.Info : MessageTone.Success);
    }, success: null);

    internal Task RetryTargetAsync(SocialTargetViewModel target) => _status?.PostId is not { } post ? Task.CompletedTask : RunAsync(async () =>
    {
        await _social.RetryTargetAsync(post, target.Id);
        await ReloadQuietAsync();
    }, success: StringKeys.SocialRetryOne);

    /// <summary>Neu vorbereiten: neuer Entwurf ohne die Kanäle, auf denen es schon steht; sonst bewusst erneut posten.</summary>
    [RelayCommand]
    private async Task RedraftAsync()
    {
        if (_status is not { } status || _source is not { } source || _id is not { } id)
        {
            return;
        }

        var repost = false;
        if (status.State == "published")
        {
            // Alles schon gepostet: nur als bewusst neuer Beitrag
            if (!await Confirm(_services.T(StringKeys.SocialRepostTitle), _services.T(StringKeys.SocialRepostText)))
            {
                return;
            }

            repost = true;
        }

        await RunAsync(async () =>
        {
            await _social.RedraftAsync(source, id, repost);
            await ReloadQuietAsync();
        }, success: StringKeys.SocialRedrafted);
    }

    [RelayCommand]
    private Task OpenInAdminAsync() => _services.Environment.AdminBaseUrl is { } admin ? _services.Browser.OpenAsync(new Uri(admin, "social")) : Task.CompletedTask;

    /// <summary>Escape: Rückfrage bzw. Meldung schließen; <c>false</c> = nichts offen.</summary>
    public bool CloseOverlay()
    {
        if (Confirmation.IsOpen)
        {
            Confirmation.Cancel();
            return true;
        }

        if (Feedback.IsOpen)
        {
            Feedback.Close();
            return true;
        }

        return false;
    }

    private async Task ReloadQuietAsync()
    {
        if (_source is not null && _id is not null)
        {
            Apply(await _social.GetAsync(_source, _id));
        }
    }

    internal string Label(string prefix, string value)
    {
        var text = _services.T(prefix + value);
        return text.StartsWith(prefix, StringComparison.Ordinal) ? value : text;
    }

    internal Task OpenAsync(Uri url) => _services.Browser.OpenAsync(url);

    private async Task RunAsync(Func<Task> action, string? success)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null)
            {
                Feedback.Show(_services.T(success), MessageTone.Success);
            }
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(_services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Ein Kanal des neuesten Posts: Name, Zustand, Fehler, Link, erneut senden.</summary>
public sealed partial class SocialTargetViewModel(SocialTarget target, SocialStatusViewModel owner, AppServices services, bool canWrite)
{
    public string Id => target.Id;

    public string Name { get; } = target.ProviderLabel is { } p && p != target.ChannelName ? $"{target.ChannelName} · {p}" : target.ChannelName;

    public string StatusText { get; } = string.Join(" · ", new[]
    {
        owner.Label("Social_Target_", target.Status),
        target.SkipReason is { } r ? owner.Label("Social_Skip_", r) : null,
        target.PublishedAt?.ToLocalTime().ToString("g", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Error => target.Error ?? string.Empty;

    public bool HasError => !string.IsNullOrEmpty(target.Error);

    public bool HasLink => target.RemoteUrl is not null;

    public bool CanRetry => canWrite && target.Retryable;

    public string AutomationName => $"{Name}: {StatusText}";

    [RelayCommand]
    private Task OpenAsync() => target.RemoteUrl is { } url ? owner.OpenAsync(url) : Task.CompletedTask;

    [RelayCommand]
    private Task RetryAsync() => owner.RetryTargetAsync(this);
}
