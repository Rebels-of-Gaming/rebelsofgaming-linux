using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Members;
using RoG.Core.Features.Squads;
using RoG.Core.Features.Uploads;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

public enum SquadAdminTab
{
    Profile,
    Members,
    Archive,
}

/// <summary>
/// Squad verwalten im eigenen Fenster (wie <c>SquadAdminViewModel</c> der Windows-App): Reiter Profil (Texte in der
/// App-Sprache, Rekrutierung, Clan, Logo/Header), Line-up (Mitglied hinzufügen, Squad-Rolle und Ersatz ändern,
/// entfernen) und Archiv. Was sichtbar ist, kommt aus <see cref="SquadAdminRights"/> (Rechte im Scope
/// <c>squad:{id}</c> + Token-Scope <c>squads:write</c>); den Einzelfall (Hierarchie: nur Rollen unter der eigenen)
/// entscheidet der Server – seine Ablehnung erscheint als Rückmeldung. Logik aus <see cref="SquadAdminService"/>.
/// </summary>
public sealed partial class SquadAdminViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SquadService _squads;
    private readonly SquadAdminService _admin;
    private readonly MemberService _members;
    private readonly UploadService _uploads;
    private SquadDetail _current;
    private bool _membersLoaded;
    private int _searchGeneration;

    public SquadAdminViewModel(AppServices services, SquadDetail current, SquadAdminRights rights)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(rights);
        _services = services;
        _current = current;
        Rights = rights;
        _squads = new SquadService(services.Api.Client);
        _admin = new SquadAdminService(services.Api.Http);
        _members = new MemberService(services.Api.Client);
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        RecruitingOptions = SquadAdminService.RecruitingValues
            .Select(v => new SettingsOption(v, services.T("SquadAdmin_Recruiting_" + v))).ToList();
        FillForm();
        Tab = rights.Edit ? SquadAdminTab.Profile : rights.Members ? SquadAdminTab.Members : SquadAdminTab.Archive;
    }

    public SquadAdminRights Rights { get; }

    public string Slug => _current.Summary.Slug;

    public string Title => _current.Summary.Name;

    public bool CanEdit => Rights.Edit;

    public bool CanMembers => Rights.Members;

    public bool CanArchive => Rights.Archive;

    /// <summary>Reiterleiste nur, wenn mehr als ein Bereich erlaubt ist.</summary>
    public bool ShowTabs => new[] { CanEdit, CanMembers, CanArchive }.Count(x => x) > 1;

    /// <summary>Etwas wurde geändert – die Squad-Seite lädt nach dem Schließen neu.</summary>
    public bool Changed { get; private set; }

    /// <summary>Nach dem Schließen des Fensters (Parameter: geändert) – setzt die Squad-Seite.</summary>
    public Action<bool>? Closed { get; set; }

    public FeedbackViewModel Feedback { get; } = new();

    public InlineConfirmViewModel Confirm { get; } = new();

    public IReadOnlyList<SettingsOption> RecruitingOptions { get; }

    /// <summary>Squad-Rollen (Wert = Rollen-ID); nur vergebbare zur Auswahl.</summary>
    public ObservableCollection<SettingsOption> Roles { get; } = [];

    public ObservableCollection<SquadAdminMemberViewModel> Lineup { get; } = [];

    public ObservableCollection<SquadMemberSuggestionViewModel> Suggestions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileTab), nameof(IsMembersTab), nameof(IsArchiveTab))]
    public partial SquadAdminTab Tab { get; set; }

    public bool IsProfileTab
    {
        get => Tab == SquadAdminTab.Profile;
        set => SetTab(SquadAdminTab.Profile, value);
    }

    public bool IsMembersTab
    {
        get => Tab == SquadAdminTab.Members;
        set => SetTab(SquadAdminTab.Members, value);
    }

    public bool IsArchiveTab
    {
        get => Tab == SquadAdminTab.Archive;
        set => SetTab(SquadAdminTab.Archive, value);
    }

    [ObservableProperty]
    public partial string Tagline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrainingTimes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanRequirements { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? ClanLevel { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedRecruiting { get; set; }

    [ObservableProperty]
    public partial string MemberSearch { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewMember))]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    public partial SquadMemberSuggestionViewModel? NewMember { get; set; }

    public bool HasNewMember => NewMember is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    public partial SettingsOption? NewMemberRole { get; set; }

    [ObservableProperty]
    public partial bool NewMemberSubstitute { get; set; }

    [ObservableProperty]
    public partial bool HasSuggestions { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLineupEmpty))]
    public partial bool IsLoadingMembers { get; set; }

    public bool IsLineupEmpty => !IsLoadingMembers && _membersLoaded && Lineup.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand), nameof(AddMemberCommand), nameof(ToggleArchiveCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsArchived { get; set; }

    public string ArchiveLabel => L(IsArchived ? StringKeys.SquadAdminRestore : StringKeys.SquadAdminArchive);

    public string ArchiveStateText => L(IsArchived ? "Linux_SquadAdminIsArchived" : "Linux_SquadAdminIsActive");

    partial void OnIsArchivedChanged(bool value)
    {
        OnPropertyChanged(nameof(ArchiveLabel));
        OnPropertyChanged(nameof(ArchiveStateText));
    }

    private string L(string key) => _services.T(key);

    private void SetTab(SquadAdminTab tab, bool value)
    {
        if (!value)
        {
            OnPropertyChanged(tab switch
            {
                SquadAdminTab.Profile => nameof(IsProfileTab),
                SquadAdminTab.Members => nameof(IsMembersTab),
                _ => nameof(IsArchiveTab),
            });
            return;
        }

        Tab = tab;
    }

    partial void OnTabChanged(SquadAdminTab value)
    {
        Confirm.Cancel();
        if (value == SquadAdminTab.Members)
        {
            _ = LoadMembersAsync();
        }
    }

    /// <summary>Fenster geöffnet: Line-up laden, falls es der erste Reiter ist.</summary>
    public Task StartAsync() => Tab == SquadAdminTab.Members ? LoadMembersAsync() : Task.CompletedTask;

    private void FillForm()
    {
        Tagline = _current.Summary.Tagline ?? string.Empty;
        Description = _current.Description ?? string.Empty;
        TrainingTimes = _current.TrainingTimes ?? string.Empty;
        ClanRequirements = _current.ClanRequirements ?? string.Empty;
        ClanTag = _current.Summary.ClanTag ?? string.Empty;
        ClanName = _current.ClanName ?? string.Empty;
        ClanLevel = _current.ClanLevel;
        IsArchived = _current.Archived;
        var value = SquadAdminService.ValueOf(_current.Summary.Recruiting);
        SelectedRecruiting = RecruitingOptions.FirstOrDefault(o => o.Value == value);
    }

    /// <summary>Line-up und Squad-Rollen laden (beim ersten Öffnen des Reiters, nach Änderungen erneut).</summary>
    public async Task LoadMembersAsync(bool force = false)
    {
        if (!CanMembers || (_membersLoaded && !force) || IsLoadingMembers)
        {
            return;
        }

        IsLoadingMembers = true;
        try
        {
            await RunAsync(async () =>
            {
                var roles = await _admin.RolesAsync(Slug);
                var all = roles.Select(r => new SquadRoleOption(r.Id, r.Label, r.Assignable)).ToList();
                Roles.Clear();
                foreach (var r in all.Where(r => r.Assignable))
                {
                    Roles.Add(new SettingsOption(r.Id, r.Label));
                }

                NewMemberRole = Roles.LastOrDefault();
                var lineup = await _squads.GetMembersAsync(Slug);
                Lineup.Clear();
                foreach (var m in lineup)
                {
                    Lineup.Add(new SquadAdminMemberViewModel(m, all, this, _services));
                }

                _membersLoaded = true;
            });
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task SaveProfileAsync()
    {
        var edit = new SquadEdit(
            Tagline,
            Description,
            TrainingTimes,
            ClanRequirements,
            SelectedRecruiting?.Value,
            ClanTag,
            ClanName,
            ClanLevel is { } level ? (int)Math.Clamp(level, 0, 9999) : null);
        IsBusy = true;
        try
        {
            var saved = false;
            if (await RunAsync(async () => saved = await _admin.UpdateAsync(_current, edit, _services.Language)))
            {
                Feedback.Show(L(saved ? StringKeys.SquadAdminSaved : StringKeys.SquadAdminNothing), saved ? MessageTone.Success : MessageTone.Info);
                if (saved)
                {
                    Changed = true;
                    if (await RunAsync(async () => _current = await _squads.GetAsync(Slug)))
                    {
                        FillForm();
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Logo oder Header: Bild öffentlich hochladen, dann am Squad setzen (Verarbeitung dauert einen Moment).</summary>
    public async Task SetImageAsync(SquadImage kind, UploadSource file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (UploadService.MimeFor(file.Name) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Feedback.Show(L(StringKeys.WikiImageOnly), MessageTone.Info);
            return;
        }

        IsBusy = true;
        try
        {
            if (await RunAsync(async () =>
            {
                await using var stream = await file.OpenAsync();
                var upload = await _uploads.UploadAsync(stream, file.Name, file.Size, UploadVisibility.Public);
                await _admin.SetImageAsync(Slug, kind, upload.Id);
            }))
            {
                Changed = true;
                Feedback.Show(L(StringKeys.SquadAdminImageQueued), MessageTone.Success);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveImageAsync(SquadImage kind)
    {
        if (await Confirm.AskAsync(L(kind == SquadImage.Logo ? StringKeys.SquadAdminRemoveLogo : StringKeys.SquadAdminRemoveHeader), Title,
                L(kind == SquadImage.Logo ? "SquadAdmin_RemoveLogoButton.Content" : "SquadAdmin_RemoveHeaderButton.Content"), L(StringKeys.CommonCancel))
            && await RunAsync(() => _admin.RemoveImageAsync(Slug, kind)))
        {
            Changed = true;
            Feedback.Show(L(StringKeys.SquadAdminSaved), MessageTone.Success);
        }
    }

    partial void OnMemberSearchChanged(string value)
    {
        if (NewMember is { } chosen && chosen.Name == value)
        {
            return; // Text kommt von der Auswahl
        }

        NewMember = null;
        _ = SearchMembersAsync(value);
    }

    /// <summary>Vorschläge für „Mitglied hinzufügen“ (ab 2 Zeichen; nur die letzte Suche zählt).</summary>
    public async Task SearchMembersAsync(string text)
    {
        var generation = ++_searchGeneration;
        if (text.Trim().Length < 2)
        {
            Suggestions.Clear();
            HasSuggestions = false;
            return;
        }

        await RunAsync(async () =>
        {
            var page = await _members.ListAsync(text.Trim(), onlineOnly: false);
            if (generation != _searchGeneration)
            {
                return;
            }

            var inSquad = Lineup.Select(m => m.Slug).ToHashSet(StringComparer.Ordinal);
            Suggestions.Clear();
            foreach (var m in page.Items.Where(m => !inSquad.Contains(m.Slug)).Take(8))
            {
                Suggestions.Add(new SquadMemberSuggestionViewModel(m, this));
            }

            HasSuggestions = Suggestions.Count > 0;
        });
    }

    internal void Choose(SquadMemberSuggestionViewModel member)
    {
        NewMember = member;
        MemberSearch = member.Name;
        Suggestions.Clear();
        HasSuggestions = false;
    }

    private bool CanAdd() => !IsBusy && NewMember is not null && NewMemberRole is not null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddMemberAsync()
    {
        if (NewMember is not { } member || NewMemberRole is not { } role)
        {
            Feedback.Show(L(StringKeys.SquadAdminPickMember), MessageTone.Info);
            return;
        }

        IsBusy = true;
        try
        {
            if (await RunAsync(() => _admin.PutMemberAsync(Slug, member.Slug, role.Value, NewMemberSubstitute)))
            {
                Changed = true;
                NewMember = null;
                MemberSearch = string.Empty;
                NewMemberSubstitute = false;
                Feedback.Show(_services.Format(StringKeys.SquadAdminMemberAdded, member.Name), MessageTone.Success);
                await LoadMembersAsync(force: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task SaveMemberAsync(SquadAdminMemberViewModel item)
    {
        if (item.SelectedRole is not { } role)
        {
            Feedback.Show(L(StringKeys.SquadAdminPickMember), MessageTone.Info);
            return;
        }

        if (await RunAsync(() => _admin.PutMemberAsync(Slug, item.Slug, role.Value, item.Substitute)))
        {
            Changed = true;
            item.MarkSaved();
            Feedback.Show(_services.Format(StringKeys.SquadAdminMemberSaved, item.Name), MessageTone.Success);
        }
    }

    internal async Task RemoveMemberAsync(SquadAdminMemberViewModel item)
    {
        if (await Confirm.AskAsync(L(StringKeys.SquadAdminRemoveMemberTitle), item.Name, L("SquadAdmin_RemoveMember.Content"), L(StringKeys.CommonCancel))
            && await RunAsync(() => _admin.RemoveMemberAsync(Slug, item.Slug)))
        {
            Changed = true;
            Lineup.Remove(item);
            OnPropertyChanged(nameof(IsLineupEmpty));
            Feedback.Show(_services.Format("Linux_SquadAdminMemberRemoved", item.Name), MessageTone.Success);
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ToggleArchiveAsync()
    {
        var archive = !IsArchived;
        if (!await Confirm.AskAsync(ArchiveLabel, L(archive ? StringKeys.SquadAdminArchiveText : StringKeys.SquadAdminRestoreText), L(StringKeys.CommonConfirm), L(StringKeys.CommonCancel)))
        {
            return;
        }

        if (await RunAsync(() => _admin.SetArchivedAsync(Slug, archive)))
        {
            Changed = true;
            _current = _current with { Archived = archive };
            IsArchived = archive;
            Feedback.Show(L(archive ? StringKeys.SquadAdminArchived : StringKeys.SquadAdminRestored), MessageTone.Success);
        }
    }

    /// <summary>Escape: Vorschläge bzw. Rückfrage schließen, sonst Meldung. <c>false</c> = nichts offen (Fenster schließt).</summary>
    public bool HandleEscape()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        if (HasSuggestions)
        {
            Suggestions.Clear();
            HasSuggestions = false;
            return true;
        }

        return false;
    }

    /// <summary>Fenster geschlossen: Squad-Seite informieren (neu laden, wenn geändert).</summary>
    public void NotifyClosed()
    {
        Confirm.Cancel();
        Closed?.Invoke(Changed);
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
            Feedback.Show(AdminErrors.Text(_services, ex.Problem), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Feedback.Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Squad-Rolle aus der API (<c>assignable</c>: darf ich grundsätzlich vergeben).</summary>
public sealed record SquadRoleOption(string Id, string Label, bool Assignable);

/// <summary>
/// Zeile im Line-up: Squad-Rolle wählen (nur vergebbare), Ersatz an/aus, speichern, entfernen. Eine nicht vergebbare
/// Rolle (z. B. Squad-Leader) bleibt als Text sichtbar und lässt sich nicht ändern.
/// </summary>
public sealed partial class SquadAdminMemberViewModel : ObservableObject
{
    private readonly SquadAdminViewModel _owner;
    private SettingsOption? _savedRole;
    private bool _savedSubstitute;

    public SquadAdminMemberViewModel(SquadMember member, IReadOnlyList<SquadRoleOption> roles, SquadAdminViewModel owner, AppServices services)
    {
        _owner = owner;
        Slug = member.Slug;
        Name = member.Name;

        // Die Line-up-Antwort des Kerns hat nur die Bezeichnung der Rolle (Wunsch an den Kern: Rollen-ID)
        var current = roles.FirstOrDefault(r => string.Equals(r.Label, member.RoleLabel, StringComparison.OrdinalIgnoreCase));
        Locked = current is { Assignable: false };
        Roles = roles.Where(r => r.Assignable).Select(r => new SettingsOption(r.Id, r.Label)).ToList();
        SelectedRole = current is null ? null : Roles.FirstOrDefault(r => r.Value == current.Id);
        RoleText = member.RoleLabel ?? string.Empty;
        Substitute = member.Substitute;
        Meta = member.Substitute ? services.Format(StringKeys.SquadsSubstitute, member.RoleLabel ?? string.Empty).Trim() : member.RoleLabel ?? string.Empty;
        MarkSaved();
    }

    public string Slug { get; }

    public string Name { get; }

    public string Meta { get; }

    public string RoleText { get; }

    /// <summary>Rolle über der eigenen (nicht vergebbar) – nur anzeigen.</summary>
    public bool Locked { get; }

    public bool Editable => !Locked;

    public IReadOnlyList<SettingsOption> Roles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial SettingsOption? SelectedRole { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial bool Substitute { get; set; }

    /// <summary>Ungespeicherte Änderung („Speichern“ hervorheben).</summary>
    public bool IsDirty => SelectedRole != _savedRole || Substitute != _savedSubstitute;

    internal void MarkSaved()
    {
        _savedRole = SelectedRole;
        _savedSubstitute = Substitute;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand]
    private Task SaveAsync() => _owner.SaveMemberAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveMemberAsync(this);
}

/// <summary>Vorschlag beim Hinzufügen: Name, Rang; Klick wählt aus.</summary>
public sealed partial class SquadMemberSuggestionViewModel(MemberSummary member, SquadAdminViewModel owner)
{
    public string Slug => member.Slug;

    public string Name => member.Name;

    public string Meta { get; } = string.Join(" · ", new[] { member.RankLabel, string.Join(", ", member.Squads) }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private void Choose() => owner.Choose(this);
}
