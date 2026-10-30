using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Moderation;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung – Reiter „Mod-Log“ (wie <c>ModerationViewModel.Log</c> der Windows-App): wer wann wen verwarnt, gesperrt
/// oder entsperrt hat (<c>GET /v1/moderation/log</c>, Recht <c>moderation.log</c> + Scope <c>moderation:read</c>), je
/// Eintrag eine Zeile „Zeit · Wer · Aktion · Wen“; zusätzlich „Mehr laden“ über den Cursor.
/// </summary>
public sealed partial class ModerationLogViewModel : LoadableViewModel, ILazyTab
{
    private readonly ModerationService _moderation;
    private readonly FeedbackViewModel _feedback;
    private string? _cursor;
    private bool _loaded;

    public ModerationLogViewModel(AppServices services, FeedbackViewModel feedback)
        : base(services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _moderation = new ModerationService(services.Api.Client);
        _feedback = feedback;
    }

    public ObservableCollection<string> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    protected override bool HasData => Items.Count > 0;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _moderation.LogAsync(null, cancellationToken);
        Items.Clear();
        Add(page);
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
            Add(await _moderation.LogAsync(_cursor));
        }
        catch (ApiProblemException ex)
        {
            _feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            _feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void Add(Page<ModLogEntry> page)
    {
        foreach (var e in page.Items)
        {
            Items.Add(string.Join(" · ", new[]
            {
                e.CreatedAt?.ToLocalTime().ToString("g", Services.Culture),
                e.Actor,
                e.Action,
                e.Target,
            }.Where(s => !string.IsNullOrEmpty(s))));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        DataChanged();
    }
}
