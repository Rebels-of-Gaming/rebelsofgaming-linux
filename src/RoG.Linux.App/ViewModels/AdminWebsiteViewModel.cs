using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Account;
using RoG.Core.Features.Admin;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung – Reiter „Website“ (wie der Website-Teil von <c>MediaAdminViewModel</c> der Windows-App): Bereiche der
/// Website-Verwaltung, die die App selbst kann (<see cref="AdminAreas"/> aus dem Kern, je Recht + Scope), öffnen den
/// allgemeinen Editor im eigenen Fenster; die übrigen (<see cref="AdminWebLinks"/>) öffnen die Admin-Domain im Browser.
/// </summary>
public sealed class AdminWebsiteViewModel
{
    public AdminWebsiteViewModel(AppServices services, FeedbackViewModel feedback)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        Feedback = feedback;
        var me = services.CurrentUser;
        NativeAreas = AdminAreas.VisibleTo(me)
            .Select(a => new AdminAreaButtonViewModel(a, services.T(a.TitleKey), services.T("AdminGroup_" + a.Group), this))
            .ToList();

        // Browser-Links wie Windows nur zusammen mit der Übersicht (Admin-Zugang)
        var overview = AdminOverviewViewModel.CanKpisFor(me) || AdminOverviewViewModel.CanSystemFor(me) || AdminOverviewViewModel.CanAuditFor(me);
        WebLinks = services.Environment.AdminBaseUrl is { } admin && overview
            ? AdminWebLinks.RemainingFor(me).Select(l => new AdminWebLinkViewModel(services.T("AdminWeb_" + l.Key), new Uri(admin, l.Path), services)).ToList()
            : [];
    }

    public AppServices Services { get; }

    public FeedbackViewModel Feedback { get; }

    /// <summary>Bereiche, die die App selbst bearbeiten kann – je Gruppe (Clan, Inhalte, Technik).</summary>
    public IReadOnlyList<AdminAreaButtonViewModel> NativeAreas { get; }

    public bool HasNativeAreas => NativeAreas.Count > 0;

    /// <summary>Bereiche ohne Gegenstück in der App (Rechte-Matrix, Menüs, Startseite, Integrationen …).</summary>
    public IReadOnlyList<AdminWebLinkViewModel> WebLinks { get; }

    public bool HasWebLinks => WebLinks.Count > 0;

    /// <summary>„Bereich bearbeiten“: die Ansicht öffnet das Editor-Fenster (eins je Bereich).</summary>
    public event EventHandler<AdminArea>? EditorRequested;

    internal void RequestEditor(AdminArea area) => EditorRequested?.Invoke(this, area);

    /// <summary>Sichtbarkeit des Reiters wie Windows: (Übersicht und Browser-Links) oder eigene Bereiche.</summary>
    public static bool IsVisibleTo(UserProfile? me, bool overview, bool hasAdminSite) =>
        (overview && hasAdminSite && AdminWebLinks.RemainingFor(me).Count > 0) || AdminAreas.VisibleTo(me).Count > 0;
}

/// <summary>Knopf für einen Verwaltungsbereich – öffnet den Editor im eigenen Fenster.</summary>
public sealed partial class AdminAreaButtonViewModel(AdminArea area, string label, string group, AdminWebsiteViewModel owner)
{
    public AdminArea Area => area;

    public string Label => label;

    public string Group => group;

    [RelayCommand]
    private void Open() => owner.RequestEditor(area);
}

/// <summary>Bereich der Website-Verwaltung im Browser (die Website prüft Rechte und Zwei-Faktor selbst).</summary>
public sealed partial class AdminWebLinkViewModel(string label, Uri url, AppServices services)
{
    public string Label => label;

    public Uri Url => url;

    public string Address { get; } = url.Host + url.AbsolutePath;

    [RelayCommand]
    private Task OpenAsync() => services.Browser.OpenAsync(url);
}
