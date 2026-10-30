using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Info;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Offene Pflicht-Bestätigung: Regelwerk (Slug) oder Rechtstext (Typ).</summary>
public sealed record PendingAcceptance(InfoKind Kind, string Key, string Title, bool Enforced);

/// <summary>
/// Offene Pflicht-Bestätigungen nach dem Login (Guides 40 und 27): Regelwerke aus <c>GET /v1/me/rules/pending</c>
/// (<c>requires_acceptance</c>, ggf. <c>enforced</c> = Schreiben gesperrt) und Rechtstexte aus <c>GET /v1/me/legal</c>
/// (<c>pending</c>). Die Shell zeigt daraus einen Hinweisbalken, der sich nicht wegklicken lässt, bis alles bestätigt ist
/// – lesen bleibt möglich (wie auf der Website, die nur das Schreiben sperrt). Bestätigt wird auf der Info-Seite.
/// </summary>
public sealed partial class AcceptanceViewModel : ObservableObject
{
    private readonly AppServices _services;
    private Task? _loading;

    public AcceptanceViewModel(AppServices services)
    {
        _services = services;
        _services.CurrentUserChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    public ObservableCollection<PendingAcceptance> Items { get; } = [];

    /// <summary>Etwas zu bestätigen → Balken sichtbar.</summary>
    [ObservableProperty]
    public partial bool HasPending { get; set; }

    /// <summary>Ein Regelwerk mit <c>enforced</c> ist offen: Schreiben in der Community ist gesperrt.</summary>
    [ObservableProperty]
    public partial bool WriteBlocked { get; set; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    /// <summary>Stand geladen (sonst gelten die Angaben der Info-Liste).</summary>
    public bool IsLoaded { get; private set; }

    public event EventHandler? Changed;

    public bool IsPending(InfoKind kind, string key) => Items.Any(i => i.Kind == kind && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Wartet auf den laufenden bzw. ersten Abruf.</summary>
    public Task EnsureLoadedAsync() => IsLoaded ? Task.CompletedTask : _loading ?? RefreshAsync();

    public Task RefreshAsync() => _loading = LoadAsync();

    private async Task LoadAsync()
    {
        if (await _services.Tokens.GetAsync() is null || _services.CurrentUser is null)
        {
            Apply([], false, loaded: false);
            return;
        }

        var items = new List<PendingAcceptance>();
        var blocked = false;
        try
        {
            var rules = await _services.Api.Client.V1_me_rules_pendingAsync();
            blocked = rules.Meta?.Write_blocked == true;
            items.AddRange(rules.Data
                .Where(r => r.Requires_acceptance && !r.Accepted)
                .OrderByDescending(r => r.Enforced)
                .Select(r => new PendingAcceptance(InfoKind.Rules, r.Slug, r.Title, r.Enforced)));
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            // ohne Scope bzw. offline: kein Hinweis zu Regeln
        }

        try
        {
            var pending = (await _services.Api.Client.V1_me_legalAsync()).Data.Where(l => l.Pending).Select(l => l.Type).ToList();
            if (pending.Count > 0)
            {
                var titles = await LegalTitlesAsync();
                items.AddRange(pending.Select(type => new PendingAcceptance(InfoKind.Legal, type, titles.GetValueOrDefault(type, type), false)));
            }
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            // ohne Scope bzw. offline: kein Hinweis zu Rechtstexten
        }

        Apply(items, blocked, loaded: true);
    }

    private async Task<Dictionary<string, string>> LegalTitlesAsync()
    {
        try
        {
            return (await _services.Api.Client.V1_legal_indexAsync()).Data.ToDictionary(l => l.Type, l => l.Title, StringComparer.Ordinal);
        }
        catch (Exception ex) when (IsQuiet(ex))
        {
            return [];
        }
    }

    /// <summary>Nach dem Bestätigen auf der Info-Seite: Eintrag entfernen, Sperre ggf. aufheben.</summary>
    public void MarkAccepted(InfoKind kind, string key)
    {
        var rest = Items.Where(i => !(i.Kind == kind && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))).ToList();
        Apply(rest, WriteBlocked && rest.Any(i => i.Enforced), IsLoaded);
    }

    private void Apply(IReadOnlyList<PendingAcceptance> items, bool blocked, bool loaded)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        IsLoaded = loaded;
        WriteBlocked = blocked && items.Count > 0;
        HasPending = items.Count > 0;
        var titles = string.Join(", ", items.Select(i => i.Title));
        Text = items.Count == 0 ? null : _services.Format(WriteBlocked ? "Linux_AcceptBannerBlocked" : "Linux_AcceptBanner", titles);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Erstes offenes Dokument auf der Info-Seite öffnen.</summary>
    [RelayCommand]
    private void Open()
    {
        if (Items.FirstOrDefault() is { } first)
        {
            _services.Navigate(new AppRoute(AppSection.Info, first.Key));
        }
    }

    private static bool IsQuiet(Exception ex) =>
        ex is ApiProblemException or GeneratedApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;
}
