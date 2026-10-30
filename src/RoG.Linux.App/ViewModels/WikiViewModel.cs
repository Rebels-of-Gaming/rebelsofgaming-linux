using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Uploads;
using RoG.Core.Features.Wiki;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Wiki wie WikiPage der Windows-App: Bereich wählen, Seitenbaum, Suche oder Kategorien links, Seite nativ rechts
/// (HTML der API → <see cref="RichDocument"/>) mit Inhaltsverzeichnis, Beobachten, Versionen (Vergleich, Wiederherstellen,
/// Freigeben/Ablehnen), Bearbeiten mit Vorschau und Konflikterkennung, neue Seite, Bild einfügen. Gelesene Seiten landen
/// verschlüsselt im <see cref="WikiOfflineStore"/> (nur mit Schlüsselbund) und werden ohne Netz von dort gezeigt.
/// </summary>
public sealed partial class WikiViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    /// <summary>Erste Segmente von Wiki-Adressen, die keine Seiten sind (Datei, Hilfe, Suche …) → Website.</summary>
    private static readonly string[] s_webOnly = ["file", "help", "search", "recent", "special", "templates"];

    private readonly WikiService _wiki;
    private readonly UploadService _uploads;
    private readonly Action _requestSignIn;
    private readonly Dictionary<string, WikiNamespace> _namespaceInfo = new(StringComparer.Ordinal);
    private bool _namespacesLoaded;
    private bool _suppressNamespace;
    private bool _suppressSelection;
    private bool _suppressCategories;
    private string? _categorySlug;
    private string? _baseRevisionId;
    private string? _conflictContent;
    private int _pageVersion;
    private Func<Task<WikiArticle>>? _lastLoad;
    private Func<Task<WikiArticle?>>? _lastOffline;

    public WikiViewModel(AppServices services, Action requestSignIn)
        : base(services)
    {
        _requestSignIn = requestSignIn;
        _wiki = new WikiService(services.Api.Client, services.Api.Http, services.Environment.WikiBaseUrl);
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        Formats =
        [
            new("wikitext", services.T("Wiki_Format_wikitext")),
            new("markdown", services.T("Wiki_Format_markdown")),
        ];
        NewFormat = Formats[0];
        services.CurrentUserChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OnRightsChanged);
    }

    public ObservableCollection<SettingsOption> Namespaces { get; } = [];

    public ObservableCollection<WikiListItem> Items { get; } = [];

    public ObservableCollection<WikiTocItem> Toc { get; } = [];

    public ObservableCollection<WikiRevisionItemViewModel> Revisions { get; } = [];

    public ObservableCollection<WikiDiffLine> DiffLines { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage vor Wiederherstellen, Freigeben und Ablehnen.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    /// <summary>Überschrift aus dem Inhaltsverzeichnis anspringen (die Ansicht sucht sie im gezeichneten Inhalt).</summary>
    public event EventHandler<string>? HeadingRequested;

    // ── Liste ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial SettingsOption? SelectedNamespace { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    public partial string? SearchText { get; set; }

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    /// <summary>Liste zeigt Kategorien statt Seitenbaum.</summary>
    [ObservableProperty]
    public partial bool ShowCategories { get; set; }

    /// <summary>Gewählte Kategorie (Liste zeigt ihre Seiten), sonst <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategory))]
    public partial string? CategoryTitle { get; set; }

    public bool HasCategory => CategoryTitle is not null;

    [ObservableProperty]
    public partial WikiListItem? Selected { get; set; }

    /// <summary>Kein Token: nur öffentliche Bereiche, kein Bearbeiten – Hinweis mit „Anmelden“.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveOffline), nameof(ShowOfflineUnavailable))]
    public partial bool IsGuest { get; set; }

    /// <summary>Liste stammt aus den Offline-Kopien (kein Netz).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveOffline))]
    public partial bool IsOfflineList { get; set; }

    /// <summary>Gibt es Offline-Kopien? Nur mit Schlüsselbund (sonst nichts auf der Platte).</summary>
    public bool IsOfflineAvailable => Services.WikiOffline is not null;

    public bool ShowOfflineUnavailable => !IsOfflineAvailable && !IsGuest;

    /// <summary>„Bereich offline speichern“: alle Seiten des Bereichs (Baum) herunterladen.</summary>
    public bool CanSaveOffline => IsOfflineAvailable && !IsOfflineList && !ShowCategories && !HasSearchText && SelectedNamespace is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSavingOffline))]
    public partial bool IsSavingOffline { get; set; }

    public bool IsNotSavingOffline => !IsSavingOffline;

    [ObservableProperty]
    public partial string? OfflineProgress { get; set; }

    protected override bool HasData => Items.Count > 0;

    // ── Seite ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage), nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowPage), nameof(Title))]
    public partial WikiArticle? Article { get; set; }

    public bool HasPage => Article is not null;

    public string? Title => Article?.Title;

    [ObservableProperty]
    public partial RichDocument? Document { get; set; }

    [ObservableProperty]
    public partial string? NamespaceName { get; set; }

    [ObservableProperty]
    public partial string PageMeta { get; set; } = string.Empty;

    /// <summary>Seite kommt aus der Offline-Kopie (Hinweis „möglicherweise nicht aktuell“).</summary>
    [ObservableProperty]
    public partial bool IsOfflineCopy { get; set; }

    /// <summary>Für diese Seite liegt eine aktuelle Offline-Kopie vor.</summary>
    [ObservableProperty]
    public partial bool IsSavedOffline { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowPage), nameof(ShowPageState))]
    public partial bool IsPageLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowPage), nameof(ShowPageState))]
    public partial string? PageError { get; set; }

    public bool ShowPage => HasPage && !IsPageLoading && PageError is null && !IsCreating;

    /// <summary>Seite lädt bzw. konnte nicht geladen werden (Zustand rechts).</summary>
    public bool ShowPageState => (IsPageLoading || PageError is not null) && !IsCreating;

    public bool IsDetailOpen => HasPage || IsPageLoading || PageError is not null || IsCreating;

    public bool ShowSelectHint => !IsDetailOpen;

    public bool HasToc => Toc.Count > 1;

    /// <summary>Inhaltsverzeichnis nur beim Lesen.</summary>
    public bool ShowToc => HasToc && !IsEditing;

    [ObservableProperty]
    public partial string WatchLabel { get; set; } = string.Empty;

    /// <summary>Bearbeiten nur mit Recht <c>wiki.edit</c> (irgendwo, Squad-Bereiche) und Scope <c>wiki:write</c>; der Server prüft.</summary>
    public bool CanEdit => Services.CurrentUser is { } me && me.CanAnywhere("wiki.edit") && me.HasScope("wiki:write");

    /// <summary>Neue Seite anlegen (gleiche Rechte wie Bearbeiten).</summary>
    public bool CanCreate => CanEdit && !IsOfflineList;

    public bool CanWatch => Services.CurrentUser is not null;

    /// <summary>Vorschläge freigeben/ablehnen (Prüfpflicht) – Recht <c>wiki.approve</c> (global oder im Squad des Bereichs).</summary>
    public bool CanApprove => Services.CurrentUser is { } me && me.CanAnywhere("wiki.approve") && me.HasScope("wiki:write");

    [ObservableProperty]
    public partial bool IsRevisionsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingRevisions { get; set; }

    [ObservableProperty]
    public partial bool HasNoRevisions { get; set; }

    // ── Bearbeiten ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReader), nameof(ShowToc))]
    public partial bool IsEditing { get; set; }

    public bool ShowReader => !IsEditing;

    [ObservableProperty]
    public partial string EditContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsSaving { get; set; }

    public bool IsNotBusy => !IsSaving && !IsUploadingImage;

    [ObservableProperty]
    public partial RichDocument? PreviewDocument { get; set; }

    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    /// <summary>Beim Speichern hat jemand anderes inzwischen gespeichert (409 <c>edit_conflict</c>).</summary>
    [ObservableProperty]
    public partial bool HasConflict { get; set; }

    [ObservableProperty]
    public partial string? ConflictMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsUploadingImage { get; set; }

    // ── Unterschiede ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial string? DiffTitle { get; set; }

    [ObservableProperty]
    public partial bool IsDiffUnchanged { get; set; }

    // ── Neue Seite ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowPage), nameof(ShowPageState))]
    public partial bool IsCreating { get; set; }

    public IReadOnlyList<SettingsOption> Formats { get; }

    public ObservableCollection<SettingsOption> ParentOptions { get; } = [];

    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? NewFormat { get; set; }

    [ObservableProperty]
    public partial SettingsOption? NewParent { get; set; }

    [ObservableProperty]
    public partial string NewContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewPageHint { get; set; }

    // ── Laden ────────────────────────────────────────────────────────────────

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        try
        {
            if (!_namespacesLoaded)
            {
                var namespaces = await _wiki.NamespacesAsync(cancellationToken);
                Namespaces.Clear();
                _namespaceInfo.Clear();
                foreach (var n in namespaces)
                {
                    Namespaces.Add(new SettingsOption(n.Slug, n.IsInternal ? n.Name + " · " + Services.T(StringKeys.WikiInternal) : n.Name));
                    _namespaceInfo[n.Slug] = n;
                }

                _namespacesLoaded = true;
                _suppressNamespace = true;
                SelectedNamespace ??= Namespaces.FirstOrDefault();
                _suppressNamespace = false;
            }

            var items = new List<WikiListItem>();
            if (_categorySlug is { } category)
            {
                items.AddRange((await _wiki.CategoryPagesAsync(category, cancellationToken)).Select(p => new WikiListItem(p.Id, p.Title, NamespaceLabel(p.NamespaceSlug), 0)));
            }
            else if (ShowCategories)
            {
                items.AddRange((await _wiki.CategoriesAsync(cancellationToken))
                    .Select(c => new WikiListItem(c.Slug, c.Name, Services.Format("Linux_WikiCategoryCount", c.Count), 0, IsCategory: true)));
            }
            else if (!string.IsNullOrWhiteSpace(SearchText))
            {
                items.AddRange((await _wiki.SearchAsync(SearchText, cancellationToken))
                    .Select(h => new WikiListItem(h.Id, h.Title, Join(NamespaceLabel(h.NamespaceSlug), StripMarks(h.Snippet)), 0)));
            }
            else if (SelectedNamespace is { } ns)
            {
                items.AddRange((await _wiki.TreeAsync(ns.Value, cancellationToken)).Select(e => new WikiListItem(e.Id, e.Title, null, e.Depth)));
            }

            IsOfflineList = false;
            Replace(items);
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError && Services.WikiOffline is { } store)
        {
            // Ohne Netz: offline gespeicherte Seiten zeigen
            var pages = await store.AllAsync(Services.Language, cancellationToken);
            if (pages.Count == 0)
            {
                throw;
            }

            IsOfflineList = true;
            Replace(pages.Select(p => new WikiListItem(p.Id, p.Title, p.NamespaceName, 0)).ToList());
            Feedback.Show(Services.T(StringKeys.WikiOfflineList), MessageTone.Info);
        }

        OnPropertyChanged(nameof(CanSaveOffline));
        OnPropertyChanged(nameof(CanCreate));
    }

    private void Replace(List<WikiListItem> items)
    {
        _suppressSelection = true;
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        Selected = Article is { } page ? Items.FirstOrDefault(i => !i.IsCategory && i.Id == page.Id) : null;
        _suppressSelection = false;
    }

    private string? NamespaceLabel(string slug) => _namespaceInfo.TryGetValue(slug, out var ns) ? ns.Name : null;

    private void OnRightsChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanWatch));
        OnPropertyChanged(nameof(CanApprove));
    }

    partial void OnSelectedNamespaceChanged(SettingsOption? value)
    {
        OnPropertyChanged(nameof(CanSaveOffline));
        if (!_suppressNamespace && _namespacesLoaded)
        {
            SearchText = null;
            _categorySlug = null;
            CategoryTitle = null;
            if (ShowCategories)
            {
                _suppressCategories = true;
                ShowCategories = false;
                _suppressCategories = false;
            }

            _ = LoadAsync();
        }
    }

    partial void OnShowCategoriesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveOffline));
        if (_suppressCategories)
        {
            return;
        }

        _categorySlug = null;
        CategoryTitle = null;
        if (_namespacesLoaded)
        {
            _ = LoadAsync();
        }
    }

    partial void OnSearchTextChanged(string? value) => OnPropertyChanged(nameof(CanSaveOffline));

    partial void OnSelectedChanged(WikiListItem? value)
    {
        if (_suppressSelection || value is null)
        {
            return;
        }

        if (value.IsCategory)
        {
            OpenCategory(value.Id, value.Title);
        }
        else if (Article?.Id != value.Id || PageError is not null)
        {
            _ = OpenPageAsync(value.Id);
        }
    }

    private void OpenCategory(string slug, string title)
    {
        _categorySlug = slug;
        CategoryTitle = title;
        _suppressSelection = true;
        Selected = null;
        _suppressSelection = false;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task LeaveCategoryAsync()
    {
        _categorySlug = null;
        CategoryTitle = null;
        return LoadAsync();
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        _categorySlug = null;
        CategoryTitle = null;
        if (ShowCategories)
        {
            _suppressCategories = true;
            ShowCategories = false;
            _suppressCategories = false;
        }

        return LoadAsync();
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        SearchText = null;
        return LoadAsync();
    }

    [RelayCommand]
    private void SignIn() => _requestSignIn();

    // ── Deep Links ───────────────────────────────────────────────────────────

    /// <summary>Wiki-Adresse aus <c>rog://wiki/…</c>, einem Link im Inhalt, der Suche oder einer Benachrichtigung.</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _ = OpenPathAsync(key);
    }

    /// <summary>
    /// <c>bereich</c> → Bereich wählen, <c>bereich/…/seite</c> → Seite, <c>category/x</c> → Kategorie; Dateien, Hilfe,
    /// Suche und Spezialseiten gibt es nur auf der Website.
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.Split('#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        if (s_webOnly.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            await Services.Browser.OpenAsync(new Uri(Services.Environment.WikiBaseUrl, string.Join('/', segments.Select(Uri.EscapeDataString))));
            return;
        }

        if (!_namespacesLoaded)
        {
            await LoadAsync();
        }

        if (segments[0] == "category" && segments.Length > 1)
        {
            OpenCategory(segments[1], segments[1].Replace('_', ' '));
            return;
        }

        if (segments.Length == 1)
        {
            if (Namespaces.FirstOrDefault(n => n.Value == segments[0]) is { } ns)
            {
                CloseDetail();
                SelectedNamespace = ns;
            }

            return;
        }

        var (nsSlug, slug) = (segments[0], segments[^1]);
        await LoadPageAsync(
            () => _wiki.PageBySlugAsync(nsSlug, slug, Services.Language),
            async () => Services.WikiOffline is { } store
                ? (await store.AllAsync(Services.Language)).FirstOrDefault(p => p.NamespaceSlug == nsSlug && p.Slug == slug)
                : null);
    }

    private Task OpenPageAsync(string id) => LoadPageAsync(
        () => _wiki.PageAsync(id, Services.Language),
        async () => Services.WikiOffline is { } store ? await store.LoadAsync(id, Services.Language) : null);

    private async Task LoadPageAsync(Func<Task<WikiArticle>> load, Func<Task<WikiArticle?>> offline)
    {
        var version = ++_pageVersion;
        _lastLoad = load;
        _lastOffline = offline;
        IsCreating = false;
        ResetPageState();
        PageError = null;
        IsPageLoading = true;
        try
        {
            var page = await load();
            if (version != _pageVersion)
            {
                return;
            }

            ShowArticle(page, offline: false);
            await SaveOfflineAsync(page);
        }
        catch (ApiProblemException ex)
        {
            if (version != _pageVersion)
            {
                return;
            }

            // Ohne Netz: Offline-Kopie zeigen, falls vorhanden
            if (ex.Problem.IsNetworkError && await offline() is { } cached && version == _pageVersion)
            {
                ShowArticle(cached, offline: true);
            }
            else
            {
                Article = null;
                PageError = Services.Localizer.GetError(ex.Problem.Code);
            }
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            if (version == _pageVersion)
            {
                Article = null;
                PageError = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
            }
        }
        finally
        {
            if (version == _pageVersion)
            {
                IsPageLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task RetryPageAsync() => _lastLoad is { } load && _lastOffline is { } offline ? LoadPageAsync(load, offline) : Task.CompletedTask;

    private void ResetPageState()
    {
        IsEditing = false;
        HasConflict = false;
        HasPreview = false;
        HasDiff = false;
        DiffLines.Clear();
        Revisions.Clear();
        IsRevisionsExpanded = false;
        HasNoRevisions = false;
        Confirm.Cancel();
    }

    private void ShowArticle(WikiArticle page, bool offline)
    {
        Article = page;
        IsOfflineCopy = offline;
        IsSavedOffline = offline;
        NamespaceName = page.NamespaceName.ToUpper(Services.Culture);
        Document = new HtmlContentParser(ContentPolicy.Platform, page.WebUrl ?? Services.Environment.WikiBaseUrl).Parse(page.Html);
        PageMeta = Join(
            page.UpdatedAt?.ToLocalTime().ToString("g", Services.Culture),
            page.Categories.Count > 0 ? string.Join(", ", page.Categories) : null) ?? string.Empty;
        WatchLabel = Services.T(page.Watching == true ? StringKeys.WikiUnwatch : StringKeys.WikiWatch).ToUpper(Services.Culture);
        Toc.Clear();
        foreach (var entry in page.Toc)
        {
            Toc.Add(new WikiTocItem(entry.Text, entry.Anchor, new Thickness(Math.Max(0, entry.Level - 2) * 16, 0, 0, 0)));
        }

        OnPropertyChanged(nameof(HasToc));
        OnPropertyChanged(nameof(ShowToc));

        // Liste: passenden Eintrag markieren, ohne die Seite neu zu laden
        _suppressSelection = true;
        Selected = Items.FirstOrDefault(i => !i.IsCategory && i.Id == page.Id);
        _suppressSelection = false;

        // Aus einem Link in einen anderen Bereich: dessen Baum zeigen
        if (!offline && !IsOfflineList && _categorySlug is null && string.IsNullOrWhiteSpace(SearchText) && !ShowCategories
            && SelectedNamespace?.Value != page.NamespaceSlug && Namespaces.FirstOrDefault(n => n.Value == page.NamespaceSlug) is { } ns)
        {
            SelectedNamespace = ns;
        }
    }

    private async Task SaveOfflineAsync(WikiArticle page)
    {
        if (Services.WikiOffline is not { } store)
        {
            return;
        }

        try
        {
            await store.SaveAsync(page);
            IsSavedOffline = Article?.Id == page.Id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // Offline-Kopie ist Beiwerk
        }
    }

    /// <summary>Alle Seiten des gewählten Bereichs offline speichern (zusätzlich zu den gelesenen).</summary>
    [RelayCommand]
    private async Task SaveNamespaceOfflineAsync()
    {
        if (Services.WikiOffline is not { } store || SelectedNamespace is not { } ns || IsSavingOffline)
        {
            return;
        }

        IsSavingOffline = true;
        var saved = 0;
        try
        {
            var tree = await _wiki.TreeAsync(ns.Value);
            for (var i = 0; i < tree.Count; i++)
            {
                OfflineProgress = Services.Format("Linux_WikiSavingOffline", i + 1, tree.Count);
                await store.SaveAsync(await _wiki.PageAsync(tree[i].Id, Services.Language));
                saved++;
            }

            if (Article is { } page && tree.Any(t => t.Id == page.Id))
            {
                IsSavedOffline = true;
            }

            Feedback.Show(Services.Format("Linux_WikiSavedOffline", saved), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            OfflineProgress = null;
            IsSavingOffline = false;
        }
    }

    [RelayCommand]
    private void GoToHeading(WikiTocItem? item)
    {
        if (item is not null)
        {
            HeadingRequested?.Invoke(this, item.Text);
        }
    }

    [RelayCommand]
    private async Task ToggleWatchAsync()
    {
        if (Article is not { } page)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var watching = await _wiki.ToggleWatchAsync(page.Id);
            Article = page with { Watching = watching };
            WatchLabel = Services.T(watching ? StringKeys.WikiUnwatch : StringKeys.WikiWatch).ToUpper(Services.Culture);
            Feedback.Show(Services.T(watching ? StringKeys.WikiWatching : StringKeys.WikiNotWatching), MessageTone.Success);
        });
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (Article?.WebUrl is { } url)
        {
            await Services.Browser.OpenAsync(url);
        }
    }

    // ── Versionen ────────────────────────────────────────────────────────────

    partial void OnIsRevisionsExpandedChanged(bool value)
    {
        if (value && Revisions.Count == 0)
        {
            _ = LoadRevisionsAsync();
        }
    }

    [RelayCommand]
    private async Task LoadRevisionsAsync()
    {
        if (Article is not { } page || page.Id.Length == 0)
        {
            return;
        }

        IsLoadingRevisions = true;
        try
        {
            await RunAsync(async () =>
            {
                var revisions = await _wiki.RevisionsAsync(page.Id);
                if (Article?.Id != page.Id)
                {
                    return;
                }

                Revisions.Clear();
                foreach (var r in revisions)
                {
                    Revisions.Add(new WikiRevisionItemViewModel(r, this, Services));
                }
            });
        }
        finally
        {
            IsLoadingRevisions = false;
            HasNoRevisions = Revisions.Count == 0;
        }
    }

    internal Task CompareAsync(WikiRevisionItemViewModel revision) => RunAsync(async () =>
    {
        var old = await _wiki.RevisionContentAsync(revision.Id);
        var current = Article?.Content ?? (Article?.RevisionId is { } id ? await _wiki.RevisionContentAsync(id) : string.Empty);
        ShowDiff(old, current, Services.Format("Linux_WikiDiffRevision", revision.When));
    });

    private void ShowDiff(string oldText, string newText, string title)
    {
        DiffLines.Clear();
        foreach (var (kind, text) in LineDiff.Compare(oldText, newText).Where(l => l.Kind != ' ').Take(2000))
        {
            DiffLines.Add(new WikiDiffLine(kind, text));
        }

        DiffTitle = title;
        IsDiffUnchanged = DiffLines.Count == 0;
        HasDiff = true;
    }

    [RelayCommand]
    private void CloseDiff()
    {
        HasDiff = false;
        DiffLines.Clear();
    }

    internal async Task DecideAsync(WikiRevisionItemViewModel revision, bool approve)
    {
        if (Article is not { } page)
        {
            return;
        }

        var confirmed = await Confirm.AskAsync(
            Services.T(approve ? "Approval_ApproveTitle" : "Approval_RejectTitle"),
            revision.Text,
            Services.T(approve ? "Approval_Approve.Content" : "Approval_Reject.Content"),
            Services.T(StringKeys.CommonCancel));
        if (confirmed && await RunAsync(() => approve ? _wiki.ApproveAsync(revision.Id) : _wiki.RejectAsync(revision.Id)))
        {
            await OpenPageAsync(page.Id);
            Feedback.Show(Services.T(approve ? StringKeys.WikiApproved : StringKeys.WikiRejected), MessageTone.Success);
            IsRevisionsExpanded = true;
            await LoadRevisionsAsync();
        }
    }

    internal async Task RestoreAsync(WikiRevisionItemViewModel revision)
    {
        if (Article is not { } page)
        {
            return;
        }

        var confirmed = await Confirm.AskAsync(
            Services.T("Config_RollbackTitle"), revision.Text, Services.T("Config_Rollback.Content"), Services.T(StringKeys.CommonCancel));
        if (confirmed && await RunAsync(() => _wiki.RestoreAsync(revision.Id)))
        {
            await OpenPageAsync(page.Id);
            Feedback.Show(Services.T(StringKeys.WikiRestored), MessageTone.Success);
        }
    }

    // ── Bearbeiten ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Article is not { } page || !CanEdit)
        {
            return;
        }

        // Quelltext: kommt mit der Seite; sonst aus der aktuellen Version
        var content = page.Content;
        if (content is null && page.RevisionId is { } revision && !await RunAsync(async () => content = await _wiki.RevisionContentAsync(revision)))
        {
            return;
        }

        EditContent = content ?? string.Empty;
        EditSummary = string.Empty;
        _baseRevisionId = page.RevisionId;
        _conflictContent = null;
        HasConflict = false;
        HasPreview = false;
        HasDiff = false;
        Feedback.Close();
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        HasConflict = false;
        HasPreview = false;
        HasDiff = false;
    }

    [RelayCommand]
    private Task PreviewAsync() => Article is not { } page ? Task.CompletedTask : RunAsync(async () =>
    {
        var html = await _wiki.RenderAsync(page.Id, EditContent, page.Format, page.Title);
        PreviewDocument = new HtmlContentParser(ContentPolicy.Platform, page.WebUrl ?? Services.Environment.WikiBaseUrl).Parse(html);
        HasPreview = true;
    });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Article is not { } page || IsSaving)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var outcome = await _wiki.SaveAsync(page.Id, page.Locale, page.Title, EditContent, page.Format, EditSummary, _baseRevisionId);
            IsEditing = false;
            HasConflict = false;
            await OpenPageAsync(page.Id);
            Feedback.Show(Services.T(outcome == WikiSaveOutcome.Pending ? StringKeys.WikiPending : StringKeys.WikiSaved), MessageTone.Success);
        }
        catch (WikiEditConflictException conflict)
        {
            // Fremde Änderung: Unterschied zeigen; „Speichern“ übernimmt jetzt bewusst die eigene Fassung
            _baseRevisionId = conflict.CurrentRevisionId;
            _conflictContent = conflict.CurrentContent;
            ConflictMessage = Services.Format(StringKeys.WikiConflict, conflict.UpdatedBy ?? "?");
            HasConflict = true;
            ShowDiff(conflict.CurrentContent, EditContent, Services.T("Linux_WikiDiffConflict"));
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Konflikt: die inzwischen gespeicherte Fassung übernehmen (eigene Änderungen verwerfen).</summary>
    [RelayCommand]
    private void TakeTheirs()
    {
        if (_conflictContent is null)
        {
            return;
        }

        EditContent = _conflictContent;
        HasConflict = false;
        HasDiff = false;
        HasPreview = false;
        Feedback.Show(Services.T("Linux_WikiTookTheirs"), MessageTone.Info);
    }

    /// <summary>
    /// Bild hochladen (Sichtbarkeit passend zum Bereich), im Wiki registrieren und am Ende des Editors einbinden.
    /// </summary>
    public async Task InsertImageAsync(Stream content, string fileName, long size)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Article is not { } page || IsUploadingImage)
        {
            return;
        }

        if (UploadService.MimeFor(fileName) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Feedback.Show(Services.T(StringKeys.WikiImageOnly), MessageTone.Error);
            return;
        }

        IsUploadingImage = true;
        try
        {
            var visibility = WikiService.UploadVisibilityFor(_namespaceInfo.TryGetValue(page.NamespaceSlug, out var ns) ? ns.UploadVisibility : null);
            await RunAsync(async () =>
            {
                var upload = await _uploads.UploadAsync(content, fileName, size, visibility);
                var file = await _wiki.RegisterFileAsync(upload.Id, page.NamespaceSlug, fileName, null);
                var embed = WikiService.FileEmbed(page.Format, file.Name.Length > 0 ? file.Name : fileName, upload.Links.Direct ?? file.Source);
                EditContent = EditContent.TrimEnd() + "\n\n" + embed + "\n";
                Feedback.Show(Services.T(StringKeys.WikiImageInserted), MessageTone.Success);
            });
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    // ── Neue Seite ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartCreate()
    {
        if (!CanCreate || SelectedNamespace is not { } ns)
        {
            return;
        }

        ParentOptions.Clear();
        ParentOptions.Add(new SettingsOption(string.Empty, Services.T(StringKeys.WikiNoParent)));
        if (!ShowCategories && _categorySlug is null && string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var item in Items.Where(i => !i.IsCategory))
            {
                ParentOptions.Add(new SettingsOption(item.Id, new string(' ', item.Depth * 3) + item.Title));
            }
        }

        NewTitle = string.Empty;
        NewContent = string.Empty;
        NewSummary = string.Empty;
        NewFormat = Formats[0];
        NewParent = Article is { } page && ParentOptions.FirstOrDefault(p => p.Value == page.Id) is { } current ? current : ParentOptions[0];
        NewPageHint = Services.Format("Linux_WikiNewPageHint", ns.Label);
        Feedback.Close();
        IsCreating = true;
    }

    [RelayCommand]
    private void CancelCreate() => IsCreating = false;

    /// <summary>Neue Seite im gewählten Bereich anlegen und öffnen; <c>false</c> bei Fehler (Meldung im Feedback).</summary>
    [RelayCommand]
    public async Task<bool> CreateAsync()
    {
        if (SelectedNamespace is not { } ns || IsSaving)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            Feedback.Show(Services.T(StringKeys.WikiNewTitleMissing), MessageTone.Error);
            return false;
        }

        IsSaving = true;
        WikiCreated? created = null;
        try
        {
            var parent = NewParent?.Value;
            if (!await RunAsync(async () => created = await _wiki.CreatePageAsync(
                    ns.Value, Services.Language, NewTitle, NewContent, NewFormat?.Value ?? "wikitext", NewSummary, string.IsNullOrEmpty(parent) ? null : parent)))
            {
                return false;
            }
        }
        finally
        {
            IsSaving = false;
        }

        IsCreating = false;
        await LoadAsync();
        await OpenPageAsync(created!.PageId);
        Feedback.Show(Services.T(created.Status == "pending" ? StringKeys.WikiPending : StringKeys.WikiCreated), MessageTone.Success);
        return true;
    }

    // ── Detail schließen ─────────────────────────────────────────────────────

    /// <summary>Escape/„Zurück“: erst Rückfrage, dann Formular bzw. Editor, dann die Seite schließen.</summary>
    [RelayCommand]
    public void CloseDetail()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return;
        }

        if (IsCreating)
        {
            IsCreating = false;
            return;
        }

        if (IsEditing)
        {
            CancelEdit();
            return;
        }

        _pageVersion++;
        _suppressSelection = true;
        Selected = null;
        _suppressSelection = false;
        ResetPageState();
        Article = null;
        PageError = null;
        IsPageLoading = false;
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
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException or ArgumentException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }

    private static string StripMarks(string snippet) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(snippet, "<[^>]+>", string.Empty));
}

/// <summary>Eintrag der Liste: Seite (eingerückt nach Tiefe im Baum), Suchtreffer oder Kategorie.</summary>
public sealed record WikiListItem(string Id, string Title, string? Subtitle, int Depth, bool IsCategory = false)
{
    public Thickness Indent => new(Math.Min(Depth, 6) * 16, 0, 0, 0);

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public bool IsPage => !IsCategory;
}

public sealed record WikiTocItem(string Text, string Anchor, Thickness Indent);

/// <summary>Geänderte Zeile im Vergleich: <c>+</c> neu (grün), <c>-</c> entfernt (rot).</summary>
public sealed record WikiDiffLine(char Kind, string Text)
{
    public bool IsAdded => Kind == '+';

    public bool IsRemoved => Kind == '-';

    public string Display => Kind + " " + Text;
}

public sealed partial class WikiRevisionItemViewModel(WikiRevision revision, WikiViewModel owner, AppServices services)
{
    public string Id => revision.Id;

    public string When { get; } = revision.CreatedAt?.ToLocalTime().ToString("g", services.Culture) ?? string.Empty;

    public string Text { get; } = string.Join(" · ", new[]
    {
        revision.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
        revision.Author,
        revision.Summary,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>„Ausstehend“/„Abgelehnt“ als Kennzeichen; freigegebene Versionen ohne.</summary>
    public string? Status { get; } = revision.Status switch
    {
        "approved" => null,
        "pending" => services.T("Linux_WikiStatusPending"),
        "rejected" => services.T("Linux_WikiStatusRejected"),
        var other => other,
    };

    public bool HasStatus => Status is not null;

    public bool IsPending => revision.Status == "pending";

    public bool CanRestore => owner.CanEdit && revision.Status == "approved";

    public bool CanDecide => owner.CanApprove && revision.Status == "pending";

    [RelayCommand]
    private Task CompareAsync() => owner.CompareAsync(this);

    [RelayCommand]
    private Task ApproveAsync() => owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, approve: false);

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(this);
}
