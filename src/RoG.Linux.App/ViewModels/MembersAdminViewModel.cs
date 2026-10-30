using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung – Reiter „Mitglieder“ (wie <c>MembersAdminViewModel</c> der Windows-App): Liste mit Status-Filter und
/// Suche, Konto mit Rollen; Rolle vergeben (global oder im Squad) und entziehen – nur mit <c>roles.assign</c> und Scope
/// <c>roles:write</c>. Die Hierarchie („nur unterhalb der eigenen Position“) prüft der Server; seine Ablehnung erscheint
/// als Rückmeldung. Logik aus <see cref="MemberAdminService"/> des Kerns.
/// </summary>
public sealed partial class MembersAdminViewModel : LoadableViewModel, ILazyTab, IDetailPage
{
    private static readonly string[] s_statuses = ["active", "suspended", "former"];
    private readonly MemberAdminService _admin;
    private string? _cursor;
    private int _generation;
    private int _detailGeneration;
    private bool _loaded;
    private bool _rolesLoaded;
    private AdminMemberDetail? _detail;

    public MembersAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services)
    {
        _admin = new MemberAdminService(services.Api.Client);
        Feedback = feedback;
        Confirm = confirm;
        StatusOptions = s_statuses
            .Select(s => new SettingsOption(s, services.T("MemberAdmin_Status_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
    }

    public FeedbackViewModel Feedback { get; }

    public InlineConfirmViewModel Confirm { get; }

    /// <summary>Rückmeldung und Rückfrage im Reiter zeigen (Standard). <c>false</c>, wenn die Seite die gemeinsamen selbst zeigt.</summary>
    public bool ShowBars { get; set; } = true;

    /// <summary>Reiter sichtbar: <c>members.view</c> (global oder in einem Squad) und Scope <c>members:read</c>.</summary>
    public bool CanView => Services.CurrentUser is { } me && me.CanAnywhere("members.view") && me.HasScope("members:read");

    /// <summary>Rollen vergeben/entziehen: <c>roles.assign</c> und Scope <c>roles:write</c>.</summary>
    public bool CanAssign => Services.CurrentUser is { } me && me.CanAnywhere("roles.assign") && me.HasScope("roles:write");

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public ObservableCollection<MemberAdminRowViewModel> Items { get; } = [];

    public ObservableCollection<RoleAssignmentViewModel> Roles { get; } = [];

    public ObservableCollection<SettingsOption> RoleOptions { get; } = [];

    public ObservableCollection<SettingsOption> ScopeOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial MemberAdminRowViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    public partial bool IsDetailLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    public partial bool HasDetail { get; set; }

    public bool ShowDetail => HasDetail && !IsDetailLoading;

    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRoles { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    public partial SettingsOption? SelectedRole { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedScope { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Mitglied moderieren (verwarnen/sperren) – setzt die Verwaltungsseite (Teil Moderation); Parameter = Konto-ID.</summary>
    public Func<string, Task>? Moderate { get; set; }

    /// <summary>„Mitglied moderieren …“ zeigen – setzt die Verwaltungsseite (Rechte der Moderation).</summary>
    [ObservableProperty]
    public partial bool CanModerate { get; set; }

    protected override bool HasData => Items.Count > 0;

    /// <summary>Lädt beim ersten Öffnen des Reiters.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    partial void OnSelectedStatusChanged(SettingsOption? value)
    {
        if (_loaded)
        {
            Selected = null;
            _ = LoadAsync();
        }
    }

    partial void OnSelectedChanged(MemberAdminRowViewModel? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Slug);
        }
        else
        {
            HasDetail = false;
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanView)
        {
            Items.Clear();
            HasMore = false;
            throw new ApiProblemException(new ApiProblem { Code = "forbidden", Status = 403 });
        }

        var generation = ++_generation;
        var page = await _admin.ListAsync(Search, SelectedStatus?.Value, null, cancellationToken);
        if (generation != _generation)
        {
            return; // inzwischen neu gefiltert
        }

        Items.Clear();
        foreach (var m in page.Items)
        {
            Items.Add(new MemberAdminRowViewModel(m, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private Task SearchNowAsync()
    {
        _loaded = true;
        Selected = null;
        return LoadAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is not { } cursor || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        var generation = _generation;
        try
        {
            await RunAsync(async () =>
            {
                var page = await _admin.ListAsync(Search, SelectedStatus?.Value, cursor);
                if (generation != _generation)
                {
                    return;
                }

                foreach (var m in page.Items)
                {
                    Items.Add(new MemberAdminRowViewModel(m, Services));
                }

                _cursor = page.NextCursor;
                HasMore = page.HasMore;
            });
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task OpenAsync(string slug)
    {
        var generation = ++_detailGeneration;
        IsDetailLoading = true;
        try
        {
            if (CanAssign && !_rolesLoaded)
            {
                _rolesLoaded = await RunAsync(async () =>
                {
                    RoleOptions.Clear();
                    foreach (var r in await _admin.RolesAsync())
                    {
                        RoleOptions.Add(new SettingsOption(r.Id, r.Label));
                    }
                });
            }

            AdminMemberDetail? loaded = null;
            if (await RunAsync(async () => loaded = await _admin.DetailAsync(slug)) && generation == _detailGeneration && loaded is { } d)
            {
                Show(d);
            }
        }
        finally
        {
            if (generation == _detailGeneration)
            {
                IsDetailLoading = false;
            }
        }
    }

    private void Show(AdminMemberDetail d)
    {
        _detail = d;
        Header = string.Join(" · ", new[] { d.Name, d.Rank }.Where(s => !string.IsNullOrEmpty(s)));
        AccountText = string.Join("\n", new[]
        {
            Services.T("MemberAdmin_Status_" + d.AccountStatus),
            d.Email,
            d.TwoFactor is { } tf ? Services.T(tf ? StringKeys.AccountTwoFactorOn : StringKeys.AccountTwoFactorOff) : null,
            d.JoinedAt is { } j ? Services.Format(StringKeys.MemberAdminJoined, j.ToLocalTime().ToString("d", Services.Culture)) : null,
            d.LastSeenAt is { } s ? Services.Format(StringKeys.MemberAdminLastSeen, s.ToLocalTime().ToString("g", Services.Culture)) : null,
            d.Squads.Count > 0 ? string.Join(", ", d.Squads.Select(q => q.Name)) : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        Roles.Clear();
        foreach (var r in d.Roles)
        {
            Roles.Add(new RoleAssignmentViewModel(r, d.Squads, this, Services));
        }

        HasRoles = Roles.Count > 0;
        ScopeOptions.Clear();
        ScopeOptions.Add(new SettingsOption(string.Empty, Services.T(StringKeys.MemberAdminScopeGlobal)));
        foreach (var (id, name) in d.Squads)
        {
            ScopeOptions.Add(new SettingsOption(id, name));
        }

        SelectedScope = ScopeOptions[0];
        HasDetail = true;
    }

    private bool CanAssignNow() => CanAssign && SelectedRole is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAssignNow))]
    private async Task AssignAsync()
    {
        if (_detail is not { } d || SelectedRole is not { } role)
        {
            return;
        }

        var squad = string.IsNullOrEmpty(SelectedScope?.Value) ? null : SelectedScope.Value;
        IsBusy = true;
        try
        {
            if (await RunAsync(() => _admin.AssignAsync(d.Slug, role.Value, squad, null)))
            {
                Feedback.Show(Services.T(StringKeys.MemberAdminAssigned), MessageTone.Success);
                await OpenAsync(d.Slug);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RevokeAsync(RoleAssignmentViewModel role)
    {
        if (_detail is not { } d
            || !await Confirm.AskAsync(Services.T("Linux_MemberAdminRevokeTitle"), Services.Format("Linux_MemberAdminRevokeText", role.Text, d.Name),
                Services.T("AdminPage_RevokeRole.Content"), Services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        if (await RunAsync(() => _admin.RevokeAsync(d.Slug, role.Id)))
        {
            Roles.Remove(role);
            HasRoles = Roles.Count > 0;
            Feedback.Show(Services.T(StringKeys.MemberAdminRevoked), MessageTone.Success);
        }
    }

    [RelayCommand]
    private void OpenProfile()
    {
        if (_detail is { } d)
        {
            Services.Navigate(new AppRoute(AppSection.Members, d.Slug));
        }
    }

    [RelayCommand]
    private Task ModerateMemberAsync() => _detail is { } d && Moderate is not null ? Moderate(d.Id) : Task.CompletedTask;

    [RelayCommand]
    public void CloseDetail() => Selected = null;

    /// <summary>Escape: Rückfrage abbrechen, sonst Detail schließen. <c>true</c> = etwas geschlossen.</summary>
    public bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        if (Selected is not null)
        {
            Selected = null;
            return true;
        }

        return false;
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(AdminErrors.Text(Services, ex.Problem), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException
            or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Fehlertexte der Verwaltungs-Reiter: Validierung mit Text des Servers, Hierarchie („über deiner Position“) mit Begründung.</summary>
internal static class AdminErrors
{
    public static string Text(AppServices services, ApiProblem problem)
    {
        if (problem.Code == "validation_failed" && !string.IsNullOrWhiteSpace(problem.Detail))
        {
            return problem.Detail;
        }

        var text = services.Localizer.GetError(problem.Code);

        // Hierarchie-Ablehnungen (403/409) begründet der Server in „detail“ – mit anzeigen, damit klar ist, warum
        return problem.Status is 403 or 409 && !string.IsNullOrWhiteSpace(problem.Detail) && !text.Contains(problem.Detail, StringComparison.Ordinal)
            ? text + " " + problem.Detail
            : text;
    }
}

/// <summary>Zeile der Mitgliederliste: Name, Rang · Squads.</summary>
public sealed class MemberAdminRowViewModel(AdminMemberRow row, AppServices services)
{
    public string Slug => row.Slug;

    public string Name => row.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        row.Rank,
        string.Join(", ", row.Squads),
        row.Status is { Length: > 0 } s && s != "active" ? services.T("MemberAdmin_Status_" + s) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasMeta => Meta.Length > 0;
}

/// <summary>Rollenzuweisung im Konto: Rolle · Geltung · befristet; „Entziehen“ nur mit Recht.</summary>
public sealed partial class RoleAssignmentViewModel(RoleAssignment role, IReadOnlyList<(string Id, string Name)> squads, MembersAdminViewModel owner, AppServices services)
{
    public string Id => role.AssignmentId;

    public string Text { get; } = string.Join(" · ", new[]
    {
        role.RoleLabel,
        role.ScopeType switch
        {
            "squad" => squads.FirstOrDefault(s => s.Id == role.ScopeId).Name ?? services.T(StringKeys.MemberAdminScopeSquad),
            null or "" or "global" => services.T(StringKeys.MemberAdminScopeGlobal),
            var other => other,
        },
        role.ExpiresAt is { } e ? services.Format(StringKeys.BanUntil, e.ToLocalTime().ToString("d", services.Culture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsTemporary => role.ExpiresAt is not null;

    public bool CanRevoke => owner.CanAssign;

    [RelayCommand]
    private Task RevokeAsync() => owner.RevokeAsync(this);
}
