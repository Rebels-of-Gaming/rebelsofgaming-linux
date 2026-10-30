using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Öffentliche Ban-Liste (wie <c>BansPage</c> der Windows-App; Website v0.34.0, Guide 43): aktive bzw. kürzlich beendete
/// Bans mit gekürzter Kennung, Detail mit Einspruchs-Status und Einspruch (Scope <c>gamebans:appeal</c>; Nachweis prüft
/// der Server). Auch im Gastmodus lesbar. Links <c>rog://bans</c> und <c>rog://bans/{id}</c>.
/// </summary>
public sealed partial class BansViewModel : LoadableViewModel, IDetailPage, IRoutablePage, IRefreshablePage
{
    private readonly PublicBanService _bans;
    private string? _cursor;

    public BansViewModel(AppServices services)
        : base(services)
    {
        _bans = new PublicBanService(services.Api.Http);
        StatusOptions = PublicBanService.Statuses.Select(s => new SettingsOption(s, services.T("Bans_Status_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
        Detail = new BanDetailViewModel(services, _bans);
    }

    public ObservableCollection<PublicBanItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public BanDetailViewModel Detail { get; }

    protected override bool HasData => Items.Count > 0;

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value) => _ = LoadAsync();

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial PublicBanItemViewModel? Selected { get; set; }

    /// <summary>Detail per Link geöffnet (ohne Auswahl in der Liste).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    partial void OnSelectedChanged(PublicBanItemViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
            _ = Detail.OpenAsync(value.Id);
        }
    }

    /// <summary>Link auf einen Ban (<c>/bans/{id}</c>, <c>rog://bans/{id}</c>): Liste laden und Detail zeigen.</summary>
    public async Task OpenAsync(string id)
    {
        DetailForced = true;
        await Task.WhenAll(ShowContent || IsLoading ? Task.CompletedTask : LoadAsync(), Detail.OpenAsync(id));
    }

    public void Open(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = OpenAsync(key);
    }

    public Task RefreshAsync() => IsDetailOpen ? Task.WhenAll(LoadAsync(silent: true), Detail.LoadAsync()) : LoadAsync();

    [RelayCommand]
    public void CloseDetail()
    {
        Selected = null;
        DetailForced = false;
    }

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync();

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _bans.ListAsync(SelectedStatus.Value, Search, null, cancellationToken);
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var ban in page.Items)
        {
            Items.Add(new PublicBanItemViewModel(ban, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        if (selectedId is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Id == selectedId);
        }
    }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            var page = await _bans.ListAsync(SelectedStatus.Value, Search, _cursor);
            foreach (var ban in page.Items)
            {
                Items.Add(new PublicBanItemViewModel(ban, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ErrorMessage = Services.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    internal static IBrush StatusBrush(string status) => UiResources.Brush(status == PublicBanService.Active ? "RoGRedBrush" : "RoGMutedBrush");

    /// <summary>„bis …“, „dauerhaft“ oder „beendet am …“.</summary>
    internal static string Period(PublicBan ban, AppServices s) =>
        ban.EndedAt is { } ended
            ? s.Format(StringKeys.BansEndedAt, ended.ToLocalTime().ToString("g", s.Culture))
            : ban.ExpiresAt is { } until && !ban.Permanent
                ? s.Format(StringKeys.BanUntil, until.ToLocalTime().ToString("g", s.Culture))
                : s.T(StringKeys.BanPermanent);
}

/// <summary>Ein Ban in der öffentlichen Liste.</summary>
public sealed class PublicBanItemViewModel(PublicBan ban, AppServices services)
{
    public string Id => ban.Id;

    public string Title => ban.Title;

    public IBrush StatusBrush => BansViewModel.StatusBrush(ban.Status);

    public string Identity => $"{ban.IdentifierType} · {ban.IdentifierMasked}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.ServerName ?? ban.GameName ?? ban.ScopeLabel,
        ban.Reason,
        BansViewModel.Period(ban, services),
        ban.Appeal is { } a ? services.T("Bans_Appeal_" + a.Status) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string AutomationName => $"{Title}, {Meta}";
}

/// <summary>Ein öffentlicher Ban mit Einspruchs-Status und Formular.</summary>
public sealed partial class BanDetailViewModel(AppServices services, PublicBanService bans) : LoadableViewModel(services)
{
    private string? _id;
    private PublicBan? _ban;

    protected override bool HasData => _ban is not null;

    public FeedbackViewModel Feedback { get; } = new();

    public async Task OpenAsync(string id)
    {
        _id = id;
        Statement = Code = string.Empty;
        Feedback.Close();
        _ban = null;
        DataChanged();
        await LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_id is null)
        {
            return;
        }

        try
        {
            Apply(await bans.GetAsync(_id, cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        {
            // ungültige ID im Link bzw. Antwort ohne Daten: wie „nicht gefunden“
            throw new ApiProblemException(new ApiProblem { Status = 404, Code = "not_found" });
        }
    }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush? StatusBrushValue { get; set; }

    [ObservableProperty]
    public partial string Identity { get; set; } = string.Empty;

    /// <summary>Spiel/Server, Geltung, Beginn, Ende.</summary>
    [ObservableProperty]
    public partial string Facts { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReason))]
    public partial string Reason { get; set; } = string.Empty;

    public bool HasReason => Reason.Length > 0;

    [ObservableProperty]
    public partial string AppealText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasThread { get; set; }

    /// <summary>Formular zeigen: Einspruch möglich, angemeldet, Scope vorhanden.</summary>
    [ObservableProperty]
    public partial bool CanAppeal { get; set; }

    [ObservableProperty]
    public partial bool NeedsCode { get; set; }

    /// <summary>Warum gerade kein Einspruch geht (oder dass der Code nötig ist).</summary>
    [ObservableProperty]
    public partial string AppealHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAppealHint { get; set; }

    [ObservableProperty]
    public partial string Statement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    private void Apply(PublicBan ban)
    {
        var s = Services;
        _ban = ban;
        Title = ban.Title;
        StatusText = s.T("Bans_Status_" + (ban.IsActive ? PublicBanService.Active : PublicBanService.Ended));
        StatusBrushValue = BansViewModel.StatusBrush(ban.Status);
        Identity = $"{ban.IdentifierType} · {ban.IdentifierMasked}";
        Facts = string.Join(Environment.NewLine, new[]
        {
            ban.GameName,
            ban.ServerName,
            ban.ScopeLabel,
            ban.CreatedAt is { } from ? s.Format(StringKeys.BansSince, from.ToLocalTime().ToString("g", s.Culture)) : null,
            BansViewModel.Period(ban, s),
        }.Where(t => !string.IsNullOrEmpty(t)));
        Reason = ban.Reason ?? string.Empty;

        var info = ban.AppealInfo;
        var status = info?.Status ?? ban.Appeal?.Status;
        AppealText = status is null ? s.T(StringKeys.BansAppealNone) : s.T("Bans_Appeal_" + status);
        HasThread = info?.ThreadUrl is not null;

        var me = s.CurrentUser;
        var hasScope = me is not null && me.HasScope(PublicBanService.AppealScope);
        CanAppeal = info is { Available: true } && hasScope;
        NeedsCode = CanAppeal && info!.NeedsCode;
        AppealHint = info switch
        {
            null => string.Empty,
            { Available: false, Blocked: "cooldown", CooldownUntil: { } until } => s.Format(StringKeys.BansCooldownUntil, until.ToLocalTime().ToString("g", s.Culture)),
            { Available: false, Blocked: { } blocked } => s.T("Bans_Blocked_" + blocked),
            { Available: true } when me is null => s.T(StringKeys.BansSignInToAppeal),
            { Available: true } when !hasScope => s.T(StringKeys.BansScopeMissing),
            { Available: true, NeedsCode: true } => s.T(StringKeys.BansCodeHint),
            _ => s.T(StringKeys.BansProofHint),
        };
        HasAppealHint = AppealHint.Length > 0;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_ban is null || IsSending)
        {
            return;
        }

        var s = Services;
        if (PublicBanService.ValidateAppeal(Statement, Code, NeedsCode) is { Count: > 0 } errors)
        {
            Feedback.Show(string.Join(" ", errors.Select(s.T)), MessageTone.Error);
            return;
        }

        IsSending = true;
        try
        {
            var appeal = await bans.AppealAsync(_ban.Id, Statement, Code);
            Statement = Code = string.Empty;
            await LoadAsync(silent: true);
            Feedback.Show(s.T(StringKeys.BansAppealSent), MessageTone.Success);
            if (appeal.ThreadUrl is { } thread)
            {
                OpenUrl(thread);
            }
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && !string.IsNullOrWhiteSpace(Code)
                ? s.T(StringKeys.BansCodeWrong)
                : s.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException)
        {
            Feedback.Show(s.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void OpenThread()
    {
        if (_ban?.AppealInfo?.ThreadUrl is { } thread)
        {
            OpenUrl(thread);
        }
    }

    [RelayCommand]
    private void OpenWeb()
    {
        if (_ban?.Url is { } url)
        {
            _ = Services.Browser.OpenAsync(url);
        }
    }

    /// <summary>Forum-Thema in der App öffnen, sonst im Browser.</summary>
    private void OpenUrl(Uri url)
    {
        if (Services.RouteFor(url) is not { } route || !Services.Navigate(route))
        {
            _ = Services.Browser.OpenAsync(url);
        }
    }
}
