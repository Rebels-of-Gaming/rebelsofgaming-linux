using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Account;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung – Übersicht (wie <c>AdminOverviewViewModel</c> der Windows-App): Kennzahlen (<c>/v1/admin/dashboard</c>),
/// Systemstatus als Ampel (<c>/v1/system/status</c>) und Audit-Log mit Suche (<c>/v1/audit-log</c>). Jede Quelle nur mit
/// Recht + Scope; fällt eine aus, zeigen die anderen trotzdem ihren Stand (Fehler je Abschnitt).
/// </summary>
public sealed partial class AdminOverviewViewModel : LoadableViewModel, ILazyTab
{
    private readonly AdminOverviewService _overview;
    private readonly FeedbackViewModel _feedback;
    private string? _auditCursor;
    private bool _loaded;
    private bool _anyLoaded;

    public AdminOverviewViewModel(AppServices services, FeedbackViewModel feedback)
        : base(services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _overview = new AdminOverviewService(services.Api.Http);
        _feedback = feedback;
        var me = services.CurrentUser;
        CanKpis = CanKpisFor(me);
        CanSystem = CanSystemFor(me);
        CanAudit = CanAuditFor(me);
    }

    public static bool CanKpisFor(UserProfile? me) => Allowed(me, "admin.access", "admin:read");

    public static bool CanSystemFor(UserProfile? me) => Allowed(me, "system.view", "system:read");

    public static bool CanAuditFor(UserProfile? me) => Allowed(me, "audit.view", "audit:read");

    public bool CanKpis { get; }

    public bool CanSystem { get; }

    public bool CanAudit { get; }

    public bool Any => CanKpis || CanSystem || CanAudit;

    public ObservableCollection<Kpi> Kpis { get; } = [];

    public ObservableCollection<SystemCheckViewModel> Checks { get; } = [];

    public ObservableCollection<string> Audit { get; } = [];

    public bool HasKpis => Kpis.Count > 0;

    [ObservableProperty]
    public partial string SystemText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush? SystemBrush { get; set; }

    [ObservableProperty]
    public partial string AuditSearch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMoreAudit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKpiError))]
    public partial string? KpiError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSystemError))]
    public partial string? SystemError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuditError), nameof(IsAuditEmpty))]
    public partial string? AuditError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuditEmpty))]
    public partial bool IsAuditLoading { get; set; }

    public bool HasKpiError => KpiError is not null;

    public bool HasSystemError => SystemError is not null;

    public bool HasAuditError => AuditError is not null;

    /// <summary>Audit-Log geladen, aber (für die Suche) leer.</summary>
    public bool IsAuditEmpty => CanAudit && !IsAuditLoading && AuditError is null && Audit.Count == 0;

    protected override bool HasData => _anyLoaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    /// <summary>Alle erlaubten Abschnitte laden; scheitern alle, zeigt die Seite den Fehler mit „Erneut versuchen“.</summary>
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        Exception? last = null;
        var ok = false;
        if (CanKpis)
        {
            try
            {
                var kpis = await _overview.KpisAsync(cancellationToken);
                Kpis.Clear();
                foreach (var k in kpis)
                {
                    Kpis.Add(k);
                }

                KpiError = null;
                ok = true;
            }
            catch (Exception ex) when (IsApiFailure(ex))
            {
                KpiError = ErrorText(ex);
                last = ex;
            }

            OnPropertyChanged(nameof(HasKpis));
        }

        if (CanSystem)
        {
            try
            {
                ApplySystem(await _overview.SystemAsync(cancellationToken));
                SystemError = null;
                ok = true;
            }
            catch (Exception ex) when (IsApiFailure(ex))
            {
                SystemError = ErrorText(ex);
                last = ex;
            }
        }

        if (CanAudit)
        {
            if (await LoadAuditCoreAsync(reset: true) is { } ex)
            {
                last = ex;
            }
            else
            {
                ok = true;
            }
        }

        _anyLoaded = ok || !Any;
        if (!ok && last is not null)
        {
            if (last is System.Text.Json.JsonException)
            {
                throw new ApiProblemException(new ApiProblem { Code = ApiProblem.Codes.Unknown });
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(last);
        }
    }

    private void ApplySystem(SystemStatus? system)
    {
        Checks.Clear();
        if (system is null)
        {
            SystemText = Services.T(StringKeys.AdminSystemNever);
            SystemBrush = UiResources.Brush("RoGMutedBrush");
            return;
        }

        SystemText = string.Join(" · ", new[]
        {
            Services.T("Admin_System_" + system.Status),
            system.Version is { } v ? "v" + v : null,
            system.CheckedAt?.ToLocalTime().ToString("g", Services.Culture),
        }.Where(s => !string.IsNullOrEmpty(s)));
        SystemBrush = UiResources.Brush(system.Status switch
        {
            "ok" => "RoGStatusOnlineBrush",
            "down" => "RoGRedBrush",
            _ => "RoGGoldBrush",
        });

        // Rote zuerst, dann grüne, nicht eingerichtete zuletzt (wie Windows)
        foreach (var c in system.Checks.OrderBy(c => c.Ok switch { false => 0, null => 2, _ => 1 }))
        {
            Checks.Add(new SystemCheckViewModel(c));
        }
    }

    private async Task<Exception?> LoadAuditCoreAsync(bool reset)
    {
        if (reset)
        {
            _auditCursor = null;
        }

        IsAuditLoading = true;
        try
        {
            var page = await _overview.AuditAsync(AuditSearch, _auditCursor);
            if (reset)
            {
                Audit.Clear();
            }

            foreach (var e in page.Items)
            {
                Audit.Add(string.Join(" · ", new[]
                {
                    e.CreatedAt?.ToLocalTime().ToString("g", Services.Culture),
                    e.Actor,
                    e.Action,
                    e.Subject,
                }.Where(s => !string.IsNullOrEmpty(s))));
            }

            _auditCursor = page.NextCursor;
            HasMoreAudit = page.HasMore;
            AuditError = null;
            return null;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            AuditError = ErrorText(ex);
            return ex;
        }
        finally
        {
            IsAuditLoading = false;
        }
    }

    /// <summary>Weitere Einträge des Audit-Logs (Cursor).</summary>
    [RelayCommand]
    private async Task MoreAuditAsync()
    {
        if (await LoadAuditCoreAsync(reset: false) is not null)
        {
            _feedback.Show(AuditError!, RoG.Core.Features.Servers.MessageTone.Error);
        }
    }

    /// <summary>Audit-Log mit Suchbegriff neu laden (Enter im Suchfeld).</summary>
    [RelayCommand]
    private Task SearchAuditAsync() => LoadAuditCoreAsync(reset: true);

    private string ErrorText(Exception ex) => ex switch
    {
        ApiProblemException p => Services.Localizer.GetError(p.Problem.Code),
        HttpRequestException => Services.Localizer.GetError("network_error"),
        TaskCanceledException => Services.Localizer.GetError("timeout"),
        _ => Services.Localizer.GetError(ApiProblem.Codes.Unknown),
    };

    private static bool IsApiFailure(Exception ex) =>
        ex is ApiProblemException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    private static bool Allowed(UserProfile? me, string permission, string scope) =>
        me is not null && me.CanAnywhere(permission) && me.HasScope(scope);
}

/// <summary>Eine Prüfung des Systemstatus: ✓ grün, ✗ rot, – nicht eingerichtet; kritische mit „*“.</summary>
public sealed class SystemCheckViewModel(SystemCheck check)
{
    public string Icon { get; } = check.Ok switch { true => "✓", false => "✗", null => "–" };

    public IBrush IconBrush { get; } = UiResources.Brush(check.Ok switch
    {
        true => "RoGStatusOnlineBrush",
        false => "RoGRedBrush",
        null => "RoGMutedBrush",
    });

    public string Label { get; } = check.Critical ? check.Label + " *" : check.Label;

    public string Value { get; } = check.Value ?? string.Empty;

    public string Help { get; } = check.Help ?? string.Empty;

    public bool HasHelp => !string.IsNullOrEmpty(check.Help);

    public string AutomationName => string.Join(" ", new[] { Label, Value }.Where(s => s.Length > 0));
}
