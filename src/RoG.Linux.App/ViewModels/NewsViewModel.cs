using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Content;
using RoG.Core.Features.News;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>News: Liste (Cursor, „Mehr laden“) und Artikel daneben bzw. darüber – öffentlich, auch im Gastmodus.</summary>
public sealed partial class NewsViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    private readonly NewsService _news;
    private string? _cursor;
    private string? _pendingSlug;

    public NewsViewModel(AppServices services)
        : base(services)
    {
        _news = new NewsService(services.Api.Client, services.Environment.WebBaseUrl);
        Detail = new NewsDetailViewModel(services, _news);
    }

    public ObservableCollection<NewsItemViewModel> Items { get; } = [];

    public NewsDetailViewModel Detail { get; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial NewsItemViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null || IsLinkedDetailOpen;

    /// <summary>Artikel aus einem Link (Startseite, <c>rog://news/…</c>), der nicht in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool IsLinkedDetailOpen { get; set; }

    public bool ShowSelectHint => !IsDetailOpen;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _news.ListAsync(null, cancellationToken);
        var selectedSlug = Selected?.Slug;
        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(new NewsItemViewModel(item, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        if (selectedSlug is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Slug == selectedSlug);
        }

        if (_pendingSlug is { } pending)
        {
            _pendingSlug = null;
            Open(pending);
        }
    }

    /// <summary>Artikel per Slug öffnen – aus der Liste, sonst direkt (älterer Artikel); vor dem Laden gemerkt.</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Items.FirstOrDefault(i => i.Slug == key) is { } item)
        {
            IsLinkedDetailOpen = false;
            Selected = item;
        }
        else if (Items.Count == 0 && (IsLoading || !ShowContent))
        {
            _pendingSlug = key;
        }
        else
        {
            Selected = null;
            IsLinkedDetailOpen = true;
            _ = Detail.ShowAsync(key);
        }
    }

    partial void OnSelectedChanged(NewsItemViewModel? value)
    {
        if (value is not null)
        {
            IsLinkedDetailOpen = false;
        }

        if (value is not null && Detail.Slug != value.Slug)
        {
            _ = Detail.ShowAsync(value.Slug);
        }
    }

    /// <summary>Zurück zur Liste (schmales Fenster, Escape).</summary>
    [RelayCommand]
    public void CloseDetail()
    {
        Selected = null;
        IsLinkedDetailOpen = false;
    }

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
            var page = await _news.ListAsync(_cursor);
            foreach (var item in page.Items)
            {
                Items.Add(new NewsItemViewModel(item, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or HttpRequestException)
        {
            // „Mehr laden“ bleibt sichtbar – erneut versuchen genügt
        }
        finally
        {
            IsLoadingMore = false;
        }
    }
}

/// <summary>Listeneintrag mit Titelbild (asynchron geladen, Platzhalter = RoG-Abzeichen).</summary>
public sealed partial class NewsItemViewModel : ObservableObject
{
    public NewsItemViewModel(NewsSummary summary, AppServices services)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(services);
        Summary = summary;
        Category = summary.CategoryName?.ToUpper(services.Culture);
        Meta = NewsText.Meta(summary, services);
        _ = LoadCoverAsync(services);
    }

    public NewsSummary Summary { get; }

    public string Slug => Summary.Slug;

    public string Title => Summary.Title;

    public string Teaser => Summary.Teaser;

    public bool Pinned => Summary.Pinned;

    public string? Category { get; }

    public bool HasCategory => !string.IsNullOrEmpty(Category);

    public string Meta { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasNoCover => Cover is null;

    private async Task LoadCoverAsync(AppServices services) =>
        Cover = await services.Images.LoadAsync(Summary.Cover, 192);
}

/// <summary>Artikel: Kopf, Titelbild, nativ gerenderter Text (<see cref="RichDocument"/>), Kommentare (lesen).</summary>
public sealed partial class NewsDetailViewModel(AppServices services, NewsService news) : LoadableViewModel(services)
{
    private NewsArticle? _article;

    public string? Slug { get; private set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Category { get; set; }

    [ObservableProperty]
    public partial string? Meta { get; set; }

    [ObservableProperty]
    public partial RichDocument? Body { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasCover => Cover is not null;

    [ObservableProperty]
    public partial string? CoverAlt { get; set; }

    public ObservableCollection<NewsCommentViewModel> Comments { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComments))]
    public partial string? CommentsHeader { get; set; }

    public bool HasComments => Comments.Count > 0;

    protected override bool HasData => _article is not null;

    public async Task ShowAsync(string slug)
    {
        Slug = slug;
        _article = null;
        Cover = null;
        await LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (Slug is null)
        {
            return;
        }

        var slug = Slug;
        var article = await news.GetAsync(slug, cancellationToken);
        if (slug != Slug)
        {
            return; // inzwischen anderer Artikel gewählt
        }

        _article = article;
        var s = article.Summary;
        Title = s.Title;
        Category = s.CategoryName?.ToUpper(Services.Culture);
        Meta = NewsText.Meta(s, Services);
        CoverAlt = s.CoverAlt ?? s.Title;
        Body = article.Body;
        _ = LoadCoverAsync(s.Cover, slug);
        Comments.Clear();
        if (article.CommentsEnabled)
        {
            try
            {
                foreach (var c in await news.GetCommentsAsync(slug, cancellationToken: cancellationToken))
                {
                    Comments.Add(new NewsCommentViewModel(c, Services));
                }
            }
            catch (RoG.Core.Api.ApiProblemException)
            {
                // Kommentare sind Beiwerk – Artikel bleibt lesbar
            }
        }

        CommentsHeader = Services.Format(StringKeys.NewsComments, Comments.Count);
        OnPropertyChanged(nameof(HasComments));
    }

    private async Task LoadCoverAsync(Uri? cover, string slug)
    {
        var bitmap = await Services.Images.LoadAsync(cover, 1280);
        if (slug == Slug)
        {
            Cover = bitmap;
        }
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (_article?.Summary.WebUrl is { } url)
        {
            await Services.Browser.OpenAsync(url);
        }
    }
}

public sealed class NewsCommentViewModel(NewsComment comment, AppServices services)
{
    public string Author { get; } = comment.AuthorName;

    public string Time { get; } = comment.CreatedAt?.ToLocalTime().ToString("g", services.Culture) ?? string.Empty;

    public string Body { get; } = comment.AuthorBlocked && comment.Body is null
        ? services.T("News_CommentBlocked")
        : comment.Body ?? string.Empty;
}

internal static class NewsText
{
    /// <summary>„16.10.2026 · Wahke · 3 Min. Lesezeit“ – nur, was da ist.</summary>
    public static string Meta(NewsSummary s, AppServices services)
    {
        var parts = new List<string>();
        if (s.PublishedAt is { } at)
        {
            parts.Add(at.ToLocalTime().ToString("d", services.Culture));
        }

        if (!string.IsNullOrEmpty(s.AuthorName))
        {
            parts.Add(s.AuthorName);
        }

        if (s.ReadingMinutes > 0)
        {
            parts.Add(services.Format(StringKeys.NewsReadingMinutes, s.ReadingMinutes));
        }

        return string.Join(" · ", parts);
    }
}
