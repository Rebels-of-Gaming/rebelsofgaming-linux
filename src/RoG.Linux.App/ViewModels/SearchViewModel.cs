using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Search;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Globale Suche (Strg+K): Treffer nach Bereich gruppiert; ein Treffer öffnet den Bereich in der App
/// (<see cref="AppRoute.FromWebUrl"/>), sonst den Browser. Suche mit kurzer Verzögerung ab 2 Zeichen.
/// </summary>
public sealed partial class SearchViewModel(AppServices services) : LoadableViewModel(services), IRoutablePage
{
    private readonly SearchService _search = new(services.Api.Client);
    private int _version;
    private bool _searched;

    public ObservableCollection<SearchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Header { get; set; }

    /// <summary>Noch nichts gesucht (zu kurz) – Hinweis statt Leerzustand.</summary>
    public bool ShowHint => !_searched && !IsLoading && !HasError;

    protected override bool HasData => Rows.Count > 0;

    /// <summary>Leer nur nach einer echten Suche.</summary>
    public bool ShowEmpty => _searched && IsEmpty;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var query = Query.Trim();
        if (query.Length < SearchService.MinLength)
        {
            Rows.Clear();
            Header = null;
            _searched = false;
            Refresh();
            return;
        }

        var groups = await _search.SearchAsync(query, cancellationToken);
        if (query != Query.Trim())
        {
            return; // inzwischen weitergetippt
        }

        Header = Services.Format(StringKeys.SearchHeader, query);
        Rows.Clear();
        foreach (var group in groups)
        {
            Rows.Add(SearchRowViewModel.Group(group.Label.ToUpper(Services.Culture)));
            foreach (var hit in group.Hits)
            {
                Rows.Add(SearchRowViewModel.Hit(hit, Open));
            }
        }

        _searched = true;
        Refresh();
    }

    /// <summary><c>rog://search?q=…</c>: Suchbegriff übernehmen und sofort suchen.</summary>
    public void Open(string key)
    {
        Query = key;
        _version++;
        _ = LoadAsync();
    }

    partial void OnQueryChanged(string value) => _ = SearchDelayedAsync();

    [RelayCommand]
    private Task SearchNowAsync()
    {
        _version++;
        return LoadAsync();
    }

    private async Task SearchDelayedAsync()
    {
        var version = ++_version;
        await Task.Delay(400);
        if (version == _version)
        {
            await LoadAsync();
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsLoading) or nameof(ErrorMessage))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ShowHint));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void Open(SearchHit hit)
    {
        if (hit.Url is null)
        {
            return;
        }

        if (AppRoute.FromWebUrl(hit.Url, Services.Environment) is not { } route || !Services.Navigate(route))
        {
            _ = Services.Browser.OpenAsync(hit.Url);
        }
    }
}

/// <summary>Zeile: Gruppenüberschrift oder Treffer (Knopf: Titel, Auszug, Meta).</summary>
public sealed partial class SearchRowViewModel
{
    private SearchHit? _hit;
    private Action<SearchHit>? _open;

    public required string Title { get; init; }

    public string Excerpt { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    public bool IsGroup { get; init; }

    public bool IsHit => !IsGroup;

    public bool HasExcerpt => Excerpt.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public static SearchRowViewModel Group(string label) => new() { Title = label, IsGroup = true };

    public static SearchRowViewModel Hit(SearchHit hit, Action<SearchHit> open) => new()
    {
        Title = hit.Title,
        Excerpt = hit.Excerpt ?? string.Empty,
        Meta = hit.Meta ?? string.Empty,
        _hit = hit,
        _open = open,
    };

    [RelayCommand]
    private void Open()
    {
        if (_hit is not null)
        {
            _open?.Invoke(_hit);
        }
    }
}
