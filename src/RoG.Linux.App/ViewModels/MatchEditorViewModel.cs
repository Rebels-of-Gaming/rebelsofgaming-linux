using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Matches;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Social;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Match eintragen/bearbeiten (eigenes Fenster) wie <c>MatchEditorViewModel</c> der Windows-App: Squad (nur Squads mit
/// <c>matches.manage</c>), Art, Status, Gegner, Zeitpunkt, Maps als Zeilen („supply 2:1“), Gesamtergebnis, Line-up aus
/// dem Squad, Links, Bericht (App-Sprache), veröffentlichen, Anhänge (erst nach dem ersten Speichern), Social-Karte.
/// </summary>
public sealed partial class MatchEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MatchAdminService _admin;
    private readonly SquadService _squads;
    private IReadOnlyList<SquadSummary> _squadList = [];
    private IReadOnlyList<MatchPlayerDraft> _players = [];
    private int _playersGeneration;

    public MatchEditorViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _admin = new MatchAdminService(services.Api.Http);
        _squads = new SquadService(services.Api.Client);
        Social = new SocialStatusViewModel(services);
        Confirm = (title, text) => Confirmation.AskAsync(title, text, services.T(StringKeys.CommonConfirm), services.T(StringKeys.CommonCancel));
        TypeOptions = MatchAdminService.Types.Select(t => new SettingsOption(t, services.T("MatchEdit_Type_" + t))).ToList();
        StatusOptions = MatchAdminService.Statuses.Select(t => new SettingsOption(t, services.T("MatchEdit_Status_" + t))).ToList();
        SelectedType = TypeOptions[0];
        SelectedStatus = StatusOptions[1];
    }

    /// <summary>Etwas wurde gespeichert/gelöscht – die Matches-Seite lädt nach dem Schließen neu.</summary>
    public bool Changed { get; private set; }

    public IReadOnlyList<SettingsOption> TypeOptions { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public ObservableCollection<SettingsOption> SquadOptions { get; } = [];

    public ObservableCollection<MatchPlayerItem> Players { get; } = [];

    public ObservableCollection<MatchAttachmentItem> Attachments { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    public InlineConfirmViewModel Confirmation { get; } = new();

    /// <summary>Rückfrage (Titel, Text) → bestätigt; Standard: Leiste im Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    /// <summary>Karte „Social Media“ (nur mit Recht <c>social.publish</c>).</summary>
    public SocialStatusViewModel Social { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(Heading))]
    public partial string? Id { get; set; }

    public bool IsSaved => Id is not null;

    public string Heading => _services.T(Id is null ? StringKeys.MatchEditNew : StringKeys.MatchEditEdit);

    [ObservableProperty]
    public partial SettingsOption? SelectedSquad { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedType { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string Opponent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpponentTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpponentUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? PlayedDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial TimeSpan? PlayedTime { get; set; } = new TimeSpan(20, 0, 0);

    [ObservableProperty]
    public partial string MapsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? ScoreOwn { get; set; }

    [ObservableProperty]
    public partial decimal? ScoreOpponent { get; set; }

    [ObservableProperty]
    public partial string LinksText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Report { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Published { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayersEmpty))]
    public partial bool IsLoadingPlayers { get; set; }

    public bool ShowPlayersEmpty => !IsLoadingPlayers && Players.Count == 0;

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>Keine Squads mit Recht – Hinweis statt leerem Formular.</summary>
    [ObservableProperty]
    public partial bool NoSquads { get; set; }

    partial void OnIdChanged(string? value) => _ = Social.ShowAsync(SocialSources.Match, value);

    /// <summary>Squads laden (nur die mit Recht) und ggf. ein Match zum Bearbeiten öffnen.</summary>
    public async Task StartAsync(string? matchId)
    {
        IsBusy = true;
        try
        {
            await RunAsync(async () =>
            {
                var me = _services.CurrentUser;
                _squadList = await _squads.ListAsync();
                SquadOptions.Clear();
                foreach (var s in _squadList.Where(s => MatchAdminService.CanManage(me, s.Id)))
                {
                    SquadOptions.Add(new SettingsOption(s.Id, s.Name));
                }

                NoSquads = SquadOptions.Count == 0;
            }, success: null);
            if (matchId is not null)
            {
                await RunAsync(async () => Fill(await _admin.GetAsync(matchId)), success: null);
            }
            else
            {
                SelectedSquad = SquadOptions.FirstOrDefault();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fill(MatchDraft draft)
    {
        Id = draft.Id;
        _players = draft.Players ?? [];
        SelectedSquad = SquadOptions.FirstOrDefault(s => s.Value == draft.SquadId) ?? SelectedSquad;
        SelectedType = TypeOptions.FirstOrDefault(t => t.Value == draft.Type) ?? TypeOptions[0];
        SelectedStatus = StatusOptions.FirstOrDefault(t => t.Value == draft.Status) ?? StatusOptions[1];
        Opponent = draft.Opponent;
        OpponentTag = draft.OpponentTag ?? string.Empty;
        OpponentUrl = draft.OpponentUrl ?? string.Empty;
        var local = draft.PlayedAt.ToLocalTime();
        PlayedDate = local.Date;
        PlayedTime = local.TimeOfDay;
        MapsText = MatchAdminService.FormatMaps(draft.Maps);
        ScoreOwn = draft.ScoreOwn;
        ScoreOpponent = draft.ScoreOpponent;
        LinksText = MatchAdminService.FormatLinks(draft.Links ?? []);
        Report = draft.Report ?? string.Empty;
        Published = draft.Published;
        Attachments.Clear();
        foreach (var a in draft.Attachments ?? [])
        {
            Attachments.Add(new MatchAttachmentItem(a, this));
        }

        OnPropertyChanged(nameof(HasAttachments));
        MarkPlayers();
    }

    partial void OnSelectedSquadChanged(SettingsOption? value)
    {
        if (value is not null)
        {
            _ = LoadPlayersAsync(value.Value);
        }
    }

    /// <summary>Line-up zur Auswahl: Mitglieder des Squads, beim Bearbeiten mit den bisherigen Spielern.</summary>
    private async Task LoadPlayersAsync(string squadId)
    {
        var generation = ++_playersGeneration;
        IsLoadingPlayers = true;
        try
        {
            await RunAsync(async () =>
            {
                var squad = _squadList.FirstOrDefault(s => s.Id == squadId);
                var members = squad is null ? [] : await _squads.GetMembersAsync(squad.Slug);
                if (generation != _playersGeneration)
                {
                    return;
                }

                Players.Clear();
                foreach (var m in members)
                {
                    Players.Add(new MatchPlayerItem(m.Id, m.Name) { Substitute = m.Substitute });
                }

                MarkPlayers();
            }, success: null);
        }
        finally
        {
            if (generation == _playersGeneration)
            {
                IsLoadingPlayers = false;
                OnPropertyChanged(nameof(ShowPlayersEmpty));
            }
        }
    }

    private void MarkPlayers()
    {
        foreach (var p in Players)
        {
            if (_players.FirstOrDefault(x => x.UserId == p.UserId) is { } saved)
            {
                p.Selected = true;
                p.Substitute = saved.Slot == "substitute";
                p.Position = saved.Position ?? string.Empty;
            }
        }
    }

    private MatchDraft Current()
    {
        var local = (PlayedDate ?? DateTime.Today).Date + (PlayedTime ?? TimeSpan.Zero);
        var players = Players.Where(p => p.Selected)
            .Select(p => new MatchPlayerDraft(p.UserId, p.Substitute ? "substitute" : "starter", p.Position)).ToList();
        return new MatchDraft(
            Id,
            SelectedSquad?.Value ?? string.Empty,
            SelectedType?.Value ?? "clanwar",
            SelectedStatus?.Value ?? "finished",
            Opponent,
            new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)),
            MatchAdminService.ParseMaps(MapsText),
            OpponentTag,
            OpponentUrl,
            ScoreOwn is { } own ? (int)own : null,
            ScoreOpponent is { } opp ? (int)opp : null,
            Published,
            Report,
            Players.Count > 0 ? players : null,
            MatchAdminService.ParseLinks(LinksText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = Current();
        var errors = MatchAdminService.Validate(draft);
        if (errors.Count > 0)
        {
            Feedback.Show(string.Join(" ", errors.Select(e => _services.T("MatchEdit_Error_" + e))), MessageTone.Error);
            return;
        }

        string? id = null;
        if (await RunAsync(async () => id = await _admin.SaveAsync(draft, _services.Language), success: null))
        {
            Changed = true;
            var before = Id;
            Id = string.IsNullOrEmpty(id) ? Id : id;
            _players = draft.Players ?? _players;
            if (Id == before)
            {
                // Ergebnis veröffentlicht → Website legt ggf. einen Entwurf an
                _ = Social.ShowAsync(SocialSources.Match, Id);
            }

            Feedback.Show(_services.T(StringKeys.MatchEditSaved), MessageTone.Success);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Id is { } id && await Confirm(_services.T(StringKeys.MatchEditDeleteTitle), Opponent) && await RunAsync(() => _admin.DeleteAsync(id), success: StringKeys.MatchEditDeleted))
        {
            Changed = true;
            Id = null;
            Attachments.Clear();
            OnPropertyChanged(nameof(HasAttachments));
        }
    }

    /// <summary>Screenshots/Dateien anhängen (je Anfrage höchstens 5) und den Stand neu laden.</summary>
    public async Task AttachAsync(IReadOnlyList<UploadSource> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (Id is not { } id || files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var chunk in files.Chunk(5))
            {
                var streams = new List<(Stream Content, string Name)>();
                try
                {
                    var ok = await RunAsync(async () =>
                    {
                        foreach (var file in chunk)
                        {
                            streams.Add((await file.OpenAsync(), file.Name));
                        }

                        await _admin.AttachAsync(id, streams);
                    }, success: null);
                    if (!ok)
                    {
                        return;
                    }
                }
                finally
                {
                    foreach (var (stream, _) in streams)
                    {
                        await stream.DisposeAsync();
                    }
                }
            }

            Changed = true;
            await RunAsync(async () => Fill(await _admin.GetAsync(id)), success: StringKeys.MatchEditAttached);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RemoveAttachmentAsync(MatchAttachmentItem item)
    {
        if (Id is { } id && await Confirm(_services.T(StringKeys.MatchEditRemoveAttachment), item.Name)
            && await RunAsync(() => _admin.RemoveAttachmentAsync(id, item.Attachment.Id), success: null))
        {
            Changed = true;
            Attachments.Remove(item);
            OnPropertyChanged(nameof(HasAttachments));
        }
    }

    internal Task OpenAttachmentAsync(MatchAttachmentItem item) =>
        item.Attachment.Url is { } url ? _services.Browser.OpenAsync(url) : Task.CompletedTask;

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

/// <summary>Mitglied in der Line-up-Auswahl (dabei, Ersatz, Position).</summary>
public sealed partial class MatchPlayerItem(string userId, string name) : ObservableObject
{
    public string UserId => userId;

    public string Name => name;

    [ObservableProperty]
    public partial bool Selected { get; set; }

    [ObservableProperty]
    public partial bool Substitute { get; set; }

    [ObservableProperty]
    public partial string Position { get; set; } = string.Empty;
}

/// <summary>Anhang eines Matches: öffnen (kurzlebiger Link im Browser), entfernen.</summary>
public sealed partial class MatchAttachmentItem(MatchAttachment attachment, MatchEditorViewModel owner)
{
    public MatchAttachment Attachment => attachment;

    public string Name => attachment.Name;

    public bool CanOpen => attachment.Url is not null;

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAttachmentAsync(this);

    [RelayCommand]
    private Task OpenAsync() => owner.OpenAttachmentAsync(this);
}
