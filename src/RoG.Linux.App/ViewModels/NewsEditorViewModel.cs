using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.News;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Social;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// News-Redaktion (eigenes Fenster) wie <c>NewsEditorViewModel</c> der Windows-App: Entwürfe und geplante News, Texte je
/// Sprache (DE Pflicht, EN/FR optional), Kategorie, Sichtbarkeit, Status (veröffentlichen/planen nur mit
/// <c>news.publish</c>), Titelbild (öffentlicher Upload, erst nach dem ersten Speichern), löschen, Social-Media-Karte.
/// Linux zusätzlich: Vorschau der gewählten Sprache (Markdown nativ gezeichnet) und Titelbild-Vorschau.
/// </summary>
public sealed partial class NewsEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly NewsAdminService _admin;
    private readonly NewsService _news;
    private readonly UploadService _uploads;
    private readonly Dictionary<string, string> _title = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _teaser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _body = new(StringComparer.Ordinal);
    private IReadOnlyList<NewsCategory> _categories = [];
    private string _language = "de";
    private string? _slug;
    private bool _switching;
    private bool _opening;

    public NewsEditorViewModel(AppServices services, NewsRights rights)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rights);
        _services = services;
        Rights = rights;
        _admin = new NewsAdminService(services.Api.Http);
        _news = new NewsService(services.Api.Client, services.Environment.WebBaseUrl);
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        Social = new SocialStatusViewModel(services);
        Confirm = (title, text) => Confirmation.AskAsync(title, text, services.T(StringKeys.CommonConfirm), services.T(StringKeys.CommonCancel));
        LanguageOptions = NewsAdminService.Languages.Select(l => new SettingsOption(l, services.T("NewsEdit_Lang_" + l))).ToList();
        VisibilityOptions = [new("public", services.T(StringKeys.NewsEditPublic)), new("members", services.T(StringKeys.NewsEditMembers))];
        StatusOptions = rights.Publish
            ? [new("draft", services.T(StringKeys.NewsEditDraft)), new("published", services.T(StringKeys.NewsEditPublished)), new("scheduled", services.T(StringKeys.NewsEditScheduled))]
            : [new("draft", services.T(StringKeys.NewsEditDraft))];
        SelectedLanguage = LanguageOptions[0];
        NewDraft();
    }

    public NewsRights Rights { get; }

    /// <summary>Etwas wurde gespeichert/gelöscht – die News-Seite lädt nach dem Schließen neu.</summary>
    public bool Changed { get; private set; }

    /// <summary>Gespeichert bzw. gelöscht (für die News-Seite, auch bei offenem Fenster).</summary>
    public event EventHandler? Saved;

    public IReadOnlyList<SettingsOption> LanguageOptions { get; }

    public IReadOnlyList<SettingsOption> VisibilityOptions { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public ObservableCollection<SettingsOption> CategoryOptions { get; } = [];

    public ObservableCollection<NewsDraftItem> Drafts { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    public InlineConfirmViewModel Confirmation { get; } = new();

    /// <summary>Rückfrage (Titel, Text) → bestätigt; Standard: Leiste im Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    /// <summary>Karte „Social Media“ (nur mit Recht <c>social.publish</c>).</summary>
    public SocialStatusViewModel Social { get; }

    [ObservableProperty]
    public partial NewsDraftItem? SelectedDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(CanDelete), nameof(Heading))]
    public partial string? Id { get; set; }

    public bool IsSaved => Id is not null;

    public bool CanDelete => Id is not null && (Rights.Manage || SelectedStatus?.Value == "draft");

    public string Heading => _services.T(Id is null ? StringKeys.NewsEditNew : StringKeys.NewsEditEdit);

    /// <summary>Schmales Fenster: Editor statt Entwurfsliste zeigen.</summary>
    [ObservableProperty]
    public partial bool IsEditorOpen { get; set; } = true;

    [ObservableProperty]
    public partial SettingsOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Teaser { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedVisibility { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled), nameof(CanDelete))]
    public partial SettingsOption? SelectedStatus { get; set; }

    public bool IsScheduled => SelectedStatus?.Value == "scheduled";

    [ObservableProperty]
    public partial DateTime? PublishDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? PublishTime { get; set; } = new TimeSpan(18, 0, 0);

    [ObservableProperty]
    public partial bool Pinned { get; set; }

    [ObservableProperty]
    public partial bool CommentsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool PostToDiscord { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Entwurfsliste konnte nicht geladen werden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDraftsEmpty))]
    public partial string? DraftsError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDraftsEmpty))]
    public partial bool IsLoadingDrafts { get; set; }

    public bool ShowDraftsEmpty => !IsLoadingDrafts && DraftsError is null && Drafts.Count == 0;

    // ---- Vorschau ----

    [ObservableProperty]
    public partial bool IsPreviewOpen { get; set; }

    [ObservableProperty]
    public partial RichDocument? PreviewBody { get; set; }

    [ObservableProperty]
    public partial string? PreviewTitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewTeaser))]
    public partial string? PreviewTeaser { get; set; }

    public bool HasPreviewTeaser => !string.IsNullOrWhiteSpace(PreviewTeaser);

    /// <summary>Titelbild der gespeicherten News (für die Vorschau; Verarbeitung im Hintergrund).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasCover => Cover is not null;

    partial void OnIdChanged(string? value) => _ = Social.ShowAsync(SocialSources.News, value);

    /// <summary>Kategorien und Entwürfe laden, optional direkt eine News (Slug) öffnen.</summary>
    public async Task StartAsync(string? slug)
    {
        if (CategoryOptions.Count == 0)
        {
            await RunAsync(async () =>
            {
                _categories = await _admin.CategoriesAsync();
                CategoryOptions.Clear();
                CategoryOptions.Add(new SettingsOption(string.Empty, _services.T(StringKeys.NewsEditNoCategory)));
                foreach (var c in _categories)
                {
                    CategoryOptions.Add(new SettingsOption(c.Id, c.Name));
                }

                SelectedCategory ??= CategoryOptions[0];
            }, success: null);
            await LoadDraftsAsync();
        }

        if (slug is not null)
        {
            await OpenAsync(slug);
        }
    }

    [RelayCommand]
    private async Task LoadDraftsAsync()
    {
        IsLoadingDrafts = true;
        DraftsError = null;
        try
        {
            var drafts = await _admin.ListAsync("draft");
            var scheduled = Rights.Publish ? await _admin.ListAsync("scheduled") : [];
            Drafts.Clear();
            foreach (var n in scheduled.Concat(drafts))
            {
                var when = n.PublishedAt?.ToLocalTime().ToString("g", _services.Culture);
                var status = _services.T(n.Status == "scheduled" ? StringKeys.NewsEditScheduled : StringKeys.NewsEditDraft);
                Drafts.Add(new NewsDraftItem(n.Slug, n.Title, status + (when is null ? string.Empty : " · " + when), n.Status == "scheduled"));
            }
        }
        catch (ApiProblemException ex)
        {
            DraftsError = _services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            DraftsError = _services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingDrafts = false;
            OnPropertyChanged(nameof(ShowDraftsEmpty));
        }
    }

    partial void OnSelectedDraftChanged(NewsDraftItem? value)
    {
        if (value is not null && !_opening)
        {
            _ = OpenAsync(value.Slug);
        }
    }

    /// <summary>News zum Bearbeiten laden (alle Übersetzungen).</summary>
    public async Task OpenAsync(string slug)
    {
        IsBusy = true;
        try
        {
            if (await RunAsync(async () => Fill(await _admin.GetAsync(slug, _categories)), success: null))
            {
                _slug = slug;
                IsEditorOpen = true;
                _ = LoadCoverAsync(slug);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fill(NewsDraft draft)
    {
        Id = draft.Id;
        _slug = draft.Slug;
        Copy(draft.Title, _title);
        Copy(draft.Teaser, _teaser);
        Copy(draft.Body, _body);
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Value == (draft.CategoryId ?? string.Empty)) ?? CategoryOptions.FirstOrDefault();
        SelectedVisibility = VisibilityOptions.FirstOrDefault(v => v.Value == draft.Visibility) ?? VisibilityOptions[0];
        SelectedStatus = StatusOptions.FirstOrDefault(s => s.Value == draft.Status) ?? StatusOptions[0];
        PublishDate = draft.PublishedAt?.ToLocalTime().Date;
        PublishTime = draft.PublishedAt?.ToLocalTime().TimeOfDay ?? new TimeSpan(18, 0, 0);
        Pinned = draft.Pinned;
        CommentsEnabled = draft.CommentsEnabled;
        PostToDiscord = draft.PostToDiscord;
        Cover = null;
        IsPreviewOpen = false;
        ShowLanguage();
    }

    private static void Copy(IReadOnlyDictionary<string, string> from, Dictionary<string, string> to)
    {
        to.Clear();
        foreach (var (k, v) in from)
        {
            to[k] = v;
        }
    }

    [RelayCommand]
    private void NewDraft()
    {
        _opening = true;
        SelectedDraft = null;
        _opening = false;
        Fill(new NewsDraft(null, new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>()));
        _slug = null;
        Feedback.Close();
        IsEditorOpen = true;
    }

    /// <summary>Schmal: zurück zur Entwurfsliste.</summary>
    [RelayCommand]
    private void ShowDrafts() => IsEditorOpen = false;

    partial void OnSelectedLanguageChanged(SettingsOption? oldValue, SettingsOption? newValue)
    {
        if (newValue is null || _switching)
        {
            return;
        }

        Store();
        _language = newValue.Value;
        ShowLanguage();
    }

    /// <summary>Felder der gewählten Sprache in die Wörterbücher übernehmen.</summary>
    private void Store()
    {
        _title[_language] = Title;
        _teaser[_language] = Teaser;
        _body[_language] = Body;
    }

    private void ShowLanguage()
    {
        _switching = true;
        Title = _title.GetValueOrDefault(_language, string.Empty);
        Teaser = _teaser.GetValueOrDefault(_language, string.Empty);
        Body = _body.GetValueOrDefault(_language, string.Empty);
        _switching = false;
        if (IsPreviewOpen)
        {
            UpdatePreview();
        }
    }

    partial void OnTitleChanged(string value) => RefreshPreviewIfOpen();

    partial void OnTeaserChanged(string value) => RefreshPreviewIfOpen();

    partial void OnBodyChanged(string value) => RefreshPreviewIfOpen();

    private void RefreshPreviewIfOpen()
    {
        if (IsPreviewOpen && !_switching)
        {
            UpdatePreview();
        }
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewOpen = !IsPreviewOpen;

    partial void OnIsPreviewOpenChanged(bool value)
    {
        if (value)
        {
            UpdatePreview();
        }
    }

    /// <summary>Vorschau der gewählten Sprache; leere Übersetzung fällt wie auf der Website auf Deutsch zurück.</summary>
    private void UpdatePreview()
    {
        Store();
        string Pick(Dictionary<string, string> texts) =>
            texts.GetValueOrDefault(_language) is { Length: > 0 } t && !string.IsNullOrWhiteSpace(t) ? t : texts.GetValueOrDefault("de", string.Empty);
        PreviewTitle = Pick(_title) is { Length: > 0 } title ? title : Heading;
        PreviewTeaser = Pick(_teaser);
        PreviewBody = new HtmlContentParser(ContentPolicy.Platform, _services.Environment.WebBaseUrl).Parse(MarkdownPreview.ToHtml(Pick(_body)));
    }

    private NewsDraft Current()
    {
        Store();
        DateTimeOffset? at = null;
        if (PublishDate is { } date)
        {
            var local = date.Date + (PublishTime ?? TimeSpan.Zero);
            at = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        return new NewsDraft(
            Id,
            new Dictionary<string, string>(_title),
            new Dictionary<string, string>(_teaser),
            new Dictionary<string, string>(_body),
            string.IsNullOrEmpty(SelectedCategory?.Value) ? null : SelectedCategory!.Value,
            SelectedVisibility?.Value ?? "public",
            SelectedStatus?.Value ?? "draft",
            at,
            Pinned,
            CommentsEnabled,
            PostToDiscord);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = Current();
        var errors = NewsAdminService.Validate(draft, DateTimeOffset.Now);
        if (errors.Count > 0)
        {
            Feedback.Show(string.Join(" ", errors.Select(e => _services.T("NewsEdit_Error_" + e))), MessageTone.Error);
            return;
        }

        NewsSaved? saved = null;
        if (!await RunAsync(async () => saved = await _admin.SaveAsync(draft), success: null))
        {
            return;
        }

        MarkChanged();
        var before = Id;
        Id = saved!.Id.Length > 0 ? saved.Id : Id;
        _slug = saved.Slug.Length > 0 ? saved.Slug : _slug;
        if (Id == before)
        {
            // Veröffentlichen legt ggf. einen Entwurf an – Status neu holen
            _ = Social.ShowAsync(SocialSources.News, Id);
        }

        var text = _services.T(draft.Status switch { "published" => StringKeys.NewsEditPublishedDone, "scheduled" => StringKeys.NewsEditScheduledDone, _ => StringKeys.NewsEditSaved });
        Feedback.Show(saved.Warnings.Count > 0 ? text + " " + string.Join(" ", saved.Warnings) : text, saved.Warnings.Count > 0 ? MessageTone.Info : MessageTone.Success);
        await LoadDraftsAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Id is not { } id || !await Confirm(_services.T(StringKeys.NewsEditDeleteTitle), _title.GetValueOrDefault("de") is { Length: > 0 } t ? t : Title))
        {
            return;
        }

        if (await RunAsync(() => _admin.DeleteAsync(id), success: null))
        {
            MarkChanged();
            NewDraft();
            Feedback.Show(_services.T(StringKeys.NewsEditDeleted), MessageTone.Success);
            await LoadDraftsAsync();
        }
    }

    /// <summary>Titelbild: Bild öffentlich hochladen, dann an der News setzen (erst nach dem ersten Speichern).</summary>
    public async Task SetCoverAsync(UploadSource file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (Id is not { } id)
        {
            return;
        }

        if (UploadService.MimeFor(file.Name) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Feedback.Show(_services.T("Linux_NewsCoverType"), MessageTone.Error);
            return;
        }

        IsBusy = true;
        try
        {
            if (await RunAsync(async () =>
            {
                await using var content = await file.OpenAsync();
                var upload = await _uploads.UploadAsync(content, file.Name, file.Size, UploadVisibility.Public);
                await _admin.SetCoverAsync(id, upload.Id);
            }, success: StringKeys.SquadAdminImageQueued))
            {
                MarkChanged();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveCoverAsync()
    {
        if (Id is { } id && await RunAsync(() => _admin.RemoveCoverAsync(id), success: StringKeys.SquadAdminSaved))
        {
            Cover = null;
            MarkChanged();
        }
    }

    /// <summary>Titelbild der gespeicherten News für die Vorschau (still; fehlt es, bleibt die Fläche leer).</summary>
    private async Task LoadCoverAsync(string slug)
    {
        try
        {
            var article = await _news.GetAsync(slug);
            var bitmap = await _services.Images.LoadAsync(article.Summary.Cover, 960);
            if (_slug == slug)
            {
                Cover = bitmap;
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or TaskCanceledException or RoG.Core.Api.Generated.GeneratedApiException or System.Text.Json.JsonException)
        {
            // Vorschau ist Beiwerk
        }
    }

    /// <summary>Escape: Rückfrage, Social-Karte bzw. Meldung schließen; <c>false</c> = nichts offen (Fenster schließt).</summary>
    public bool CloseOverlay()
    {
        if (Confirmation.IsOpen)
        {
            Confirmation.Cancel();
            return true;
        }

        if (Social.CloseOverlay())
        {
            return true;
        }

        if (Feedback.IsOpen)
        {
            Feedback.Close();
            return true;
        }

        return false;
    }

    private void MarkChanged()
    {
        Changed = true;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> RunAsync(Func<Task> action, string? success)
    {
        try
        {
            await action();
            if (success is not null)
            {
                Feedback.Show(_services.T(success), MessageTone.Success);
            }

            return true;
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            Feedback.Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Entwurf bzw. geplante News in der Liste der Redaktion.</summary>
public sealed record NewsDraftItem(string Slug, string Title, string Meta, bool Scheduled)
{
    public string AutomationName => $"{Title}, {Meta}";
}
