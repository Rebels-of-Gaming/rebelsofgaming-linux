using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.Forum;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Offline;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Forum (wie <c>ForumViewModel</c> der Windows-App): links Bereiche bzw. Themen eines Bereichs, rechts das Thema mit
/// Beiträgen (BBCode als HTML der API, nativ gezeichnet), Umfrage, Reaktionen und Antwort mit BBCode-Leiste und
/// Vorschau; „Neues Thema“ im rechten Bereich. Neue Beiträge kommen live (<c>private-forum.threads.{id}</c>).
/// </summary>
public sealed partial class ForumViewModel : LoadableViewModel, IDetailPage, IRoutablePage, IDisposable
{
    private readonly ForumService _forum;
    private string? _threadCursor;
    private string? _pendingKey;

    public ForumViewModel(AppServices services)
        : base(services)
    {
        _forum = new ForumService(services.Api.Client, services.Environment.WebBaseUrl);
        Thread = new ThreadViewModel(services, _forum);
        NewThread = new NewThreadViewModel(services, this);
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = [];

    public ObservableCollection<ThreadItemViewModel> Threads { get; } = [];

    public ThreadViewModel Thread { get; }

    public NewThreadViewModel NewThread { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCategories), nameof(ShowThreads), nameof(CanCreateThread), nameof(ListTitle))]
    public partial CategoryItemViewModel? Category { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowThread))]
    public partial ThreadItemViewModel? SelectedThread { get; set; }

    /// <summary>Thema per Link geöffnet, das nicht in der Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowThread))]
    public partial bool ThreadForced { get; set; }

    /// <summary>Rechts steht das Formular „Neues Thema“.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(ShowThread))]
    public partial bool IsComposing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowThreadsEmpty))]
    public partial bool IsLoadingThreads { get; set; }

    [ObservableProperty]
    public partial bool HasMoreThreads { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateThread))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowThreadsEmpty))]
    public partial string? ThreadsError { get; set; }

    public bool ShowCategories => Category is null;

    public bool ShowThreads => Category is not null;

    public bool ShowThreadsEmpty => ShowThreads && !IsLoadingThreads && ThreadsError is null && Threads.Count == 0;

    public string ListTitle => Category?.Name ?? Services.T("ForumPage_Title.Text");

    public bool CanCreateThread => IsSignedIn && Category is { CanWrite: true };

    public bool IsDetailOpen => SelectedThread is not null || ThreadForced || IsComposing;

    public bool ShowThread => !IsComposing && (SelectedThread is not null || ThreadForced);

    public bool ShowSelectHint => !IsDetailOpen;

    protected override bool HasData => Categories.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = await Services.Tokens.GetAsync(cancellationToken) is not null;
        var categories = await _forum.GetCategoriesAsync(cancellationToken);
        var selected = Category?.Slug;
        Categories.Clear();
        foreach (var c in categories.Where(c => c.ParentId is null))
        {
            Categories.Add(new CategoryItemViewModel(c, 0, Services));
            foreach (var child in categories.Where(x => x.ParentId == c.Id))
            {
                Categories.Add(new CategoryItemViewModel(child, 1, Services));
            }
        }

        if (selected is not null && Categories.FirstOrDefault(c => c.Slug == selected) is { } again)
        {
            _keepThreads = true;
            Category = again;
            _keepThreads = false;
        }

        if (_pendingKey is { } pending)
        {
            _pendingKey = null;
            Open(pending);
        }
    }

    /// <summary>
    /// <c>rog://forum/{bereich}</c> zeigt den Bereich, sonst ist der Schlüssel eine Themen-ID (<c>rog://forum/{id}</c>,
    /// Website <c>/forum/t/{id}/{slug}</c>) und das Thema öffnet sich.
    /// </summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Categories.Count == 0)
        {
            _pendingKey = key;
            if (!IsLoading)
            {
                _ = LoadAsync();
            }

            return;
        }

        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts[0] == "t")
        {
            parts = parts[1..];
        }

        if (parts.Length == 0)
        {
            return;
        }

        if (Categories.FirstOrDefault(c => c.Slug == parts[0]) is { } category)
        {
            Category = category;
            return;
        }

        var id = parts[0];
        IsComposing = false;
        SelectedThread = Threads.FirstOrDefault(t => t.Id == id);
        if (SelectedThread is null)
        {
            ThreadForced = true;
            _ = Thread.ShowAsync(id);
        }
    }

    private bool _keepThreads;

    partial void OnCategoryChanged(CategoryItemViewModel? value)
    {
        if (_keepThreads)
        {
            return;
        }

        Threads.Clear();
        ThreadsError = null;
        OnPropertyChanged(nameof(ShowThreadsEmpty));
        if (value is not null)
        {
            _ = LoadThreadsAsync(value, null);
        }
    }

    private async Task LoadThreadsAsync(CategoryItemViewModel category, string? cursor)
    {
        IsLoadingThreads = cursor is null;
        try
        {
            var page = await _forum.GetThreadsAsync(category.Slug, cursor);
            if (Category != category)
            {
                return;
            }

            foreach (var t in page.Items)
            {
                Threads.Add(new ThreadItemViewModel(t, Services));
            }

            _threadCursor = page.NextCursor;
            HasMoreThreads = page.HasMore;
        }
        catch (ApiProblemException ex)
        {
            ThreadsError = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            ThreadsError = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingThreads = false;
            OnPropertyChanged(nameof(ShowThreadsEmpty));
        }
    }

    [RelayCommand]
    private Task LoadMoreThreadsAsync() => Category is { } c && _threadCursor is { } cursor ? LoadThreadsAsync(c, cursor) : Task.CompletedTask;

    [RelayCommand]
    private Task RetryThreadsAsync()
    {
        if (Category is not { } c)
        {
            return Task.CompletedTask;
        }

        Threads.Clear();
        ThreadsError = null;
        return LoadThreadsAsync(c, null);
    }

    [RelayCommand]
    private void OpenCategory(CategoryItemViewModel? category) => Category = category;

    [RelayCommand]
    private void BackToCategories()
    {
        Category = null;
        CloseDetail();
    }

    partial void OnSelectedThreadChanged(ThreadItemViewModel? value)
    {
        if (value is not null)
        {
            ThreadForced = false;
            IsComposing = false;
            value.MarkRead();
            _ = Thread.ShowAsync(value.Id);
        }
    }

    [RelayCommand]
    private void StartNewThread()
    {
        if (!CanCreateThread)
        {
            return;
        }

        SelectedThread = null;
        ThreadForced = false;
        NewThread.Reset();
        IsComposing = true;
    }

    /// <summary>Neues Thema anlegen; öffnet es danach (vom Formular aufgerufen).</summary>
    internal async Task CreateThreadAsync(string title, string body)
    {
        if (Category is not { } category)
        {
            return;
        }

        var id = await _forum.CreateThreadAsync(category.Slug, title, body);
        IsComposing = false;
        Threads.Clear();
        await LoadThreadsAsync(category, null);
        SelectedThread = Threads.FirstOrDefault(t => t.Id == id);
        if (SelectedThread is null)
        {
            ThreadForced = true;
            await Thread.ShowAsync(id);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        SelectedThread = null;
        ThreadForced = false;
        IsComposing = false;
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, Category is { } c ? "forum/" + Uri.EscapeDataString(c.Slug) : "forum"));

    public void Dispose() => Thread.Dispose();
}

/// <summary>Bereich in der Liste (Unterbereiche eingerückt).</summary>
public sealed class CategoryItemViewModel(ForumCategory category, int depth, AppServices services)
{
    public string Slug => category.Slug;

    public string Name => category.Name;

    public bool CanWrite => category.CanWrite && !category.Locked;

    public string Description => category.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrEmpty(category.Description);

    public string Stats { get; } = services.Format(StringKeys.ForumCategoryStats, category.Threads, category.Posts)
        + (category.SquadName is null ? string.Empty : " · " + category.SquadName);

    public Thickness Indent { get; } = new(depth * 20, 0, 0, 0);

    public bool Locked => category.Locked;
}

/// <summary>Thema in der Liste: angeheftet/geschlossen/Umfrage als Symbol, ungelesen fett mit roter Kante.</summary>
public sealed partial class ThreadItemViewModel(ForumThreadSummary thread, AppServices services) : ObservableObject
{
    public string Id => thread.Id;

    public string Title => thread.Title;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        thread.AuthorBlocked ? services.T(StringKeys.ForumBlockedAuthor) : thread.AuthorName,
        services.Format(StringKeys.ForumReplies, thread.Replies),
        thread.LastPostAt?.ToLocalTime().ToString("g", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool Pinned => thread.Pinned;

    public bool Locked => thread.Locked;

    public bool HasPoll => thread.HasPoll;

    public string AutomationName { get; } = string.Join(", ", new[]
    {
        thread.Title,
        thread.Unread ? services.T("Linux_Unread") : null,
        thread.Pinned ? services.T("Linux_ForumPinned") : null,
        thread.Locked ? services.T("Linux_ForumLocked") : null,
    }.Where(s => s is not null));

    [ObservableProperty]
    public partial bool Unread { get; set; } = thread.Unread;

    public void MarkRead() => Unread = false;
}

/// <summary>Ein Thema: Beiträge, Umfrage, Reaktionen, Antwort mit BBCode-Leiste und Vorschau (Strg+Enter sendet).</summary>
public sealed partial class ThreadViewModel(AppServices services, ForumService forum) : LoadableViewModel(services), IDisposable
{
    private string? _threadId;
    private string? _postCursor;
    private IDisposable? _watch;
    private IReadOnlyList<ReactionOption>? _reactions;

    public ObservableCollection<PostViewModel> Posts { get; } = [];

    public ObservableCollection<PollOptionViewModel> PollOptions { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    public string? ThreadId => _threadId;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Meta { get; set; }

    [ObservableProperty]
    public partial bool CanReply { get; set; }

    /// <summary>Angemeldet, aber Thema geschlossen bzw. keine Schreibrechte – Hinweis statt Eingabe.</summary>
    [ObservableProperty]
    public partial bool ShowReadOnlyHint { get; set; }

    [ObservableProperty]
    public partial bool HasMorePosts { get; set; }

    [ObservableProperty]
    public partial string? PollQuestion { get; set; }

    [ObservableProperty]
    public partial string? PollMeta { get; set; }

    [ObservableProperty]
    public partial bool HasPoll { get; set; }

    [ObservableProperty]
    public partial bool CanVote { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial RichDocument? PreviewDocument { get; set; }

    [ObservableProperty]
    public partial bool ShowPreview { get; set; }

    /// <summary>Text im Antwortfeld ersetzt (Zitat) – die Ansicht setzt die Eingabemarke ans Ende.</summary>
    public event EventHandler? DraftReplaced;

    protected override bool HasData => Title is not null;

    public IReadOnlyList<ReactionOption> ReactionOptions => _reactions ?? [];

    public Task ShowAsync(string threadId)
    {
        if (threadId == _threadId && Title is not null)
        {
            return LoadAsync(silent: true);
        }

        _watch?.Dispose();
        _watch = null;
        _threadId = threadId;
        Title = null;
        Posts.Clear();
        PollOptions.Clear();
        HasPoll = false;
        Draft = string.Empty;
        ShowPreview = false;
        Feedback.Close();
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_threadId is null)
        {
            return;
        }

        var id = _threadId;
        var threadTask = forum.GetThreadAsync(id, cancellationToken);
        var postsTask = forum.GetPostsAsync(id, null, cancellationToken);
        _reactions ??= await TryReactionsAsync(cancellationToken);
        var thread = await threadTask;
        var posts = await postsTask;
        if (id != _threadId)
        {
            return;
        }

        var signedIn = await Services.Tokens.GetAsync(cancellationToken) is not null;
        Meta = string.Join(" · ", new[] { thread.CategoryName, thread.Summary.AuthorBlocked ? Services.T(StringKeys.ForumBlockedAuthor) : thread.Summary.AuthorName }
            .Where(s => !string.IsNullOrEmpty(s)));
        CanReply = signedIn && thread.CanReply;
        ShowReadOnlyHint = signedIn && !thread.CanReply;
        ApplyPoll(thread.Poll, signedIn);

        Posts.Clear();
        foreach (var p in posts.Items)
        {
            Posts.Add(new PostViewModel(p, this, signedIn, Services));
        }

        _postCursor = posts.NextCursor;
        HasMorePosts = posts.HasMore;
        Title = thread.Summary.Title;
        _watch ??= Services.Realtime.WatchChannel($"private-forum.threads.{id}", (name, payload) =>
        {
            if (name == "post.published")
            {
                _ = LoadAsync(silent: true);
            }
        });
    }

    private async Task<IReadOnlyList<ReactionOption>?> TryReactionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await forum.GetReactionsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            return null;
        }
    }

    private void ApplyPoll(ForumPoll? poll, bool signedIn)
    {
        PollOptions.Clear();
        HasPoll = poll is not null;
        if (poll is null)
        {
            return;
        }

        PollQuestion = poll.Question;
        PollMeta = Services.Format(StringKeys.ForumPollVoters, poll.Voters) + (poll.Closed ? " · " + Services.T(StringKeys.ForumPollClosed) : string.Empty);
        var total = Math.Max(1, poll.Options.Sum(o => o.Votes));
        CanVote = signedIn && !poll.Closed;
        foreach (var o in poll.Options)
        {
            PollOptions.Add(new PollOptionViewModel(poll.Id, o, (double)o.Votes / total, this, Services));
        }
    }

    [RelayCommand]
    private async Task LoadMorePostsAsync()
    {
        if (_threadId is null || _postCursor is null)
        {
            return;
        }

        try
        {
            var page = await forum.GetPostsAsync(_threadId, _postCursor);
            var signedIn = await Services.Tokens.GetAsync() is not null;
            foreach (var p in page.Items)
            {
                Posts.Add(new PostViewModel(p, this, signedIn, Services));
            }

            _postCursor = page.NextCursor;
            HasMorePosts = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // „Mehr laden“ erneut
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft) && Draft.Trim().Length <= ForumService.MaxBodyLength;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (_threadId is null)
        {
            return;
        }

        IsSending = true;
        var text = Draft.Trim();
        try
        {
            await forum.ReplyAsync(_threadId, text);
            Draft = string.Empty;
            ShowPreview = false;
            Feedback.Show(Services.T(StringKeys.ForumReplySent), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError)
        {
            await Services.Outbox.EnqueueAsync(OutboxKind.ForumReply, _threadId, new { body = text }, Services.T(StringKeys.OutboxLabelForum));
            Draft = string.Empty;
            ShowPreview = false;
            Feedback.Show(Services.T(StringKeys.OutboxQueued), MessageTone.Info);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentOutOfRangeException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task TogglePreviewAsync()
    {
        if (ShowPreview)
        {
            ShowPreview = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Draft))
        {
            return;
        }

        PreviewDocument = await PreviewAsync(forum, Draft, Feedback, Services);
        ShowPreview = PreviewDocument is not null;
    }

    /// <summary>Vorschau über die API (gleiche Darstellung wie nach dem Absenden); Fehler als Rückmeldung.</summary>
    internal static async Task<RichDocument?> PreviewAsync(ForumService forum, string draft, FeedbackViewModel feedback, AppServices services)
    {
        try
        {
            return await forum.PreviewAsync(draft);
        }
        catch (ApiProblemException ex)
        {
            feedback.Show(services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            feedback.Show(services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return null;
    }

    internal void QuotePost(PostViewModel post)
    {
        Draft = Draft + BbCode.Quote(post.Author, post.PlainText);
        DraftReplaced?.Invoke(this, EventArgs.Empty);
    }

    internal async Task ReactAsync(PostViewModel post, string key)
    {
        try
        {
            await forum.ToggleReactionAsync(post.Id, key);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    internal async Task VoteAsync(string pollId, string optionId)
    {
        try
        {
            await forum.VoteAsync(pollId, [optionId]);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    internal async Task<RichDocument?> RevealAsync(string postId)
    {
        try
        {
            return (await forum.GetPostAsync(postId)).Content;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            return null;
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() => _threadId is null
        ? Task.CompletedTask
        : Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "forum/t/" + Uri.EscapeDataString(_threadId)));

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
    }
}

/// <summary>Beitrag: Autor, Zeit, Inhalt (eingeklappt bei blockierten Personen), Reaktionen, Zitieren.</summary>
public sealed partial class PostViewModel : ObservableObject
{
    private readonly ForumPost _post;
    private readonly ThreadViewModel _owner;

    public PostViewModel(ForumPost post, ThreadViewModel owner, bool signedIn, AppServices services)
    {
        _post = post;
        _owner = owner;
        Content = post.Content;
        CanInteract = signedIn && !post.Pending && !post.Hidden;
        Author = post.AuthorBlocked ? services.T(StringKeys.ForumBlockedAuthor) : post.AuthorName;
        Meta = string.Join(" · ", new[]
        {
            post.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
            post.EditedAt is not null ? services.T(StringKeys.ForumEdited) : null,
            post.Pending ? services.T(StringKeys.ForumPending) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
        Reactions = post.Reactions.Select(r => new ReactionChipViewModel(r, this, services)).ToList();
        ReactionChoices = owner.ReactionOptions.Select(o => new ReactionChoiceViewModel(o, this)).ToList();
        Initial = string.IsNullOrEmpty(post.AuthorName) ? "?" : post.AuthorName[..1].ToUpper(services.Culture);
        AutomationName = $"{Author}, {Meta}";
    }

    public string Id => _post.Id;

    public string Author { get; }

    public string Initial { get; }

    public string Meta { get; }

    public string AutomationName { get; }

    public bool IsPending => _post.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed), nameof(PlainText))]
    public partial RichDocument? Content { get; set; }

    /// <summary>Beitrag einer blockierten Person: eingeklappt mit „Trotzdem anzeigen“.</summary>
    public bool IsCollapsed => _post.AuthorBlocked && Content is not { Blocks.Count: > 0 };

    public bool CanInteract { get; }

    public bool CanQuote => CanInteract && !IsCollapsed;

    public bool CanReact => CanInteract && ReactionChoices.Count > 0;

    public string PlainText => Content?.ToPlainText() ?? string.Empty;

    public IReadOnlyList<ReactionChipViewModel> Reactions { get; }

    public bool HasReactions => Reactions.Count > 0;

    public IReadOnlyList<ReactionChoiceViewModel> ReactionChoices { get; }

    public string Attachments => string.Join(", ", _post.Attachments);

    public bool HasAttachments => _post.Attachments.Count > 0;

    [RelayCommand]
    private void Quote() => _owner.QuotePost(this);

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (await _owner.RevealAsync(Id) is { } doc)
        {
            Content = doc;
            OnPropertyChanged(nameof(CanQuote));
        }
    }

    internal Task ReactAsync(string key) => _owner.ReactAsync(this, key);
}

/// <summary>Reaktion unter einem Beitrag („👍 3“); eigene mit roter Kante, Klick schaltet um.</summary>
public sealed partial class ReactionChipViewModel(PostReaction reaction, PostViewModel post, AppServices services)
{
    public string Text { get; } = $"{reaction.Emoji} {reaction.Count}";

    public bool Mine => reaction.Mine;

    public string AutomationName { get; } = services.Format("Linux_ReactionAutomation", reaction.Emoji, reaction.Count)
        + (reaction.Mine ? ", " + services.T("Linux_ReactionMine") : string.Empty);

    [RelayCommand]
    private Task ToggleAsync() => post.ReactAsync(reaction.Key);
}

public sealed partial class ReactionChoiceViewModel(ReactionOption option, PostViewModel post)
{
    public string Text { get; } = $"{option.Emoji} {option.Label}";

    [RelayCommand]
    private Task ChooseAsync() => post.ReactAsync(option.Key);
}

/// <summary>Antwort einer Umfrage mit Balken (Anteil) und „Abstimmen“.</summary>
public sealed partial class PollOptionViewModel(string pollId, PollOption option, double share, ThreadViewModel owner, AppServices services)
{
    public string Label { get; } = option.Mine ? $"✓ {option.Label}" : option.Label;

    public bool Mine => option.Mine;

    public double Percent { get; } = Math.Round(share * 100);

    public string Votes { get; } = $"{option.Votes} ({Math.Round(share * 100).ToString(services.Culture)} %)";

    public string VoteAutomationName { get; } = services.Format("Linux_PollVoteAutomation", option.Label);

    [RelayCommand]
    private Task VoteAsync() => owner.VoteAsync(pollId, option.Id);
}

/// <summary>Formular „Neues Thema“: Titel, Text (BBCode-Leiste wie bei Antworten), Vorschau, Anlegen.</summary>
public sealed partial class NewThreadViewModel(AppServices services, ForumViewModel owner) : ObservableObject
{
    public const int MaxTitleLength = 120;

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    public partial RichDocument? PreviewDocument { get; set; }

    [ObservableProperty]
    public partial bool ShowPreview { get; set; }

    public string CategoryName => owner.Category?.Name ?? string.Empty;

    public void Reset()
    {
        Title = string.Empty;
        Body = string.Empty;
        ShowPreview = false;
        Feedback.Close();
        OnPropertyChanged(nameof(CategoryName));
    }

    private bool CanCreate() => !IsCreating && !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Body)
        && Title.Trim().Length <= MaxTitleLength && Body.Trim().Length <= ForumService.MaxBodyLength;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsCreating = true;
        Feedback.Close();
        try
        {
            await owner.CreateThreadAsync(Title, Body);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private async Task TogglePreviewAsync()
    {
        if (ShowPreview)
        {
            ShowPreview = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            return;
        }

        PreviewDocument = await ThreadViewModel.PreviewAsync(new ForumService(services.Api.Client, services.Environment.WebBaseUrl), Body, Feedback, services);
        ShowPreview = PreviewDocument is not null;
    }

    [RelayCommand]
    private void Cancel() => owner.CloseDetail();
}
