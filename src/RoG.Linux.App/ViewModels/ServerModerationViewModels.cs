using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Gemeinsame Fehlerbehandlung der Reiter im Steuer-Fenster (wie <c>ServerTabViewModel</c> der Windows-App).</summary>
public static class ServerTabActions
{
    /// <summary>Führt die Aktion aus; Fehler erscheinen übersetzt in der Rückmeldung. <c>true</c> = geklappt.</summary>
    public static async Task<bool> RunAsync(AppServices services, FeedbackViewModel feedback, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            feedback.Show(services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or ArgumentException)
        {
            feedback.Show(services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }

        return false;
    }

    /// <summary>Rückfrage im Inhalt mit „Bestätigen“/„Abbrechen“ (Texte wie die Dialoge der Windows-App).</summary>
    public static Task<bool> AskAsync(AppServices services, InlineConfirmViewModel confirm, string title, string text)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(confirm);
        return confirm.AskAsync(title, text, services.T(StringKeys.CommonConfirm).ToUpper(services.Culture), services.T(StringKeys.CommonCancel).ToUpper(services.Culture));
    }
}

/// <summary>Liste in einem Reiter des Steuer-Fensters: lädt beim ersten Öffnen, Lade-/Leer-/Fehlerzustand.</summary>
public abstract partial class ServerListTabViewModel(AppServices services, string slug, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
    : LoadableViewModel(services), ILazyTab
{
    private bool _loaded;

    protected string Slug { get; } = slug;

    protected FeedbackViewModel Feedback { get; } = feedback;

    protected InlineConfirmViewModel Confirm { get; } = confirm;

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await LoadAsync();
        }
    }

    protected string L(string key) => Services.T(key);

    protected Task<bool> RunAsync(Func<Task> action) => ServerTabActions.RunAsync(Services, Feedback, action);

    protected Task<bool> AskAsync(string title, string text) => ServerTabActions.AskAsync(Services, Confirm, title, text);
}

/// <summary>RCON: freier Befehl mit ↑/↓-Verlauf, Ausgabe oben die neueste – wird nicht gespeichert (kann Spieler-IPs enthalten).</summary>
public sealed partial class ServerRconViewModel(AppServices services, string slug, FeedbackViewModel feedback) : ObservableObject
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);

    public CommandHistory History { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Command { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    public partial string Output { get; set; } = string.Empty;

    public bool HasOutput => Output.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Command) && Command.Trim().Length <= 500;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var command = Command.Trim();
        History.Add(command);
        Command = string.Empty;
        IsBusy = true;
        try
        {
            await ServerTabActions.RunAsync(services, feedback, async () =>
            {
                var output = await _moderation.RconAsync(slug, command);
                Output = $"> {command}\n{output}\n\n{Output}".TrimEnd();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string? PreviousCommand() => History.Previous() is { } text ? Command = text : null;

    public string NextCommand() => Command = History.Next();
}

/// <summary>Aktive Sperren der Website: Gast sperren (Spieler-ID oder Ingame-Name), Sperre aufheben (mit Rückfrage).</summary>
public sealed partial class ServerBansViewModel(AppServices services, string slug, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
    : ServerListTabViewModel(services, slug, feedback, confirm)
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<BanItemViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    [ObservableProperty]
    public partial string PlayerId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    /// <summary>Minuten, 0 = dauerhaft.</summary>
    [ObservableProperty]
    public partial decimal? Minutes { get; set; } = 60;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var bans = await _moderation.BansAsync(Slug, cancellationToken);
        Items.Clear();
        foreach (var b in bans)
        {
            Items.Add(new BanItemViewModel(b, this, Services));
        }
    }

    [RelayCommand]
    private async Task BanAsync()
    {
        if (string.IsNullOrWhiteSpace(PlayerId) && string.IsNullOrWhiteSpace(PlayerName))
        {
            Feedback.Show(L(StringKeys.BanPlayerMissing), MessageTone.Info);
            return;
        }

        var minutes = (int)Math.Clamp(Minutes ?? 0, 0, int.MaxValue);
        if (await RunAsync(() => _moderation.BanAsync(Slug, PlayerId, PlayerName, minutes, Reason)))
        {
            PlayerId = PlayerName = Reason = string.Empty;
            Feedback.Show(L(StringKeys.BanDone), MessageTone.Success);
            await LoadAsync(silent: true);
        }
    }

    internal async Task LiftAsync(BanItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.BanLiftTitle), item.Player) && await RunAsync(() => _moderation.LiftAsync(Slug, item.Id)))
        {
            Items.Remove(item);
            DataChanged();
        }
    }
}

public sealed partial class BanItemViewModel(ServerBan ban, ServerBansViewModel owner, AppServices services)
{
    public string Id => ban.Id;

    public string Player => ban.Player;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.Reason,
        ban.EndsAt is { } e
            ? services.Format(StringKeys.BanUntil, e.ToLocalTime().ToString("g", services.Culture))
            : services.T(StringKeys.BanPermanent),
        ban.IssuedBy,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string LiftAutomationName => $"{services.T("Ban_Lift.Content")}: {Player}";

    [RelayCommand]
    private Task LiftAsync() => owner.LiftAsync(this);
}

/// <summary>Whitelist des Servers: Einträge mit erkanntem Mitglied, Spielername hinzufügen, Eintrag entfernen (mit Rückfrage).</summary>
public sealed partial class ServerWhitelistViewModel(AppServices services, string slug, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
    : ServerListTabViewModel(services, slug, feedback, confirm)
{
    private readonly ServerWhitelistService _whitelist = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<WhitelistItemViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await _whitelist.ListAsync(Slug, cancellationToken);
        Items.Clear();
        foreach (var e in list)
        {
            Items.Add(new WhitelistItemViewModel(e, this, Services));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(PlayerName))
        {
            Feedback.Show(L(StringKeys.BanPlayerMissing), MessageTone.Info);
            return;
        }

        if (await RunAsync(() => _whitelist.AddAsync(Slug, null, PlayerName.Trim())))
        {
            PlayerName = string.Empty;
            Feedback.Show(L(StringKeys.WhitelistAdded), MessageTone.Success);
            await LoadAsync(silent: true);
        }
    }

    internal async Task RemoveAsync(WhitelistItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.WhitelistRemoveTitle), item.Display)
            && await RunAsync(() => _whitelist.RemoveAsync(Slug, item.Entry.MemberId, item.Entry.Line)))
        {
            Items.Remove(item);
            DataChanged();
        }
    }
}

public sealed partial class WhitelistItemViewModel(WhitelistEntry entry, ServerWhitelistViewModel owner, AppServices services)
{
    public WhitelistEntry Entry => entry;

    public string Display => entry.Display;

    public bool IsMember => entry.MemberId is not null;

    public string RemoveAutomationName => $"{services.T("Whitelist_Remove.Content")}: {Display}";

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

/// <summary>Offene Config-Anträge (Vier-Augen-Prinzip) mit Diff freigeben oder ablehnen – je mit Rückfrage und optionaler Begründung.</summary>
public sealed partial class ServerApprovalsViewModel(AppServices services, string slug, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
    : ServerListTabViewModel(services, slug, feedback, confirm)
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<ChangeItemViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var changes = await _moderation.PendingChangesAsync(Slug, cancellationToken);
        Items.Clear();
        foreach (var c in changes)
        {
            Items.Add(new ChangeItemViewModel(c, this, Services));
        }
    }

    internal async Task DecideAsync(ChangeItemViewModel item, bool approve)
    {
        if (!await AskAsync(L(approve ? StringKeys.ApprovalApproveTitle : StringKeys.ApprovalRejectTitle), item.Title))
        {
            return;
        }

        var ok = await RunAsync(() => approve
            ? _moderation.ApproveAsync(Slug, item.Id, item.Note)
            : _moderation.RejectAsync(Slug, item.Id, item.Note));
        if (ok)
        {
            Items.Remove(item);
            DataChanged();
            Feedback.Show(L(approve ? StringKeys.ApprovalApproved : StringKeys.ApprovalRejected), MessageTone.Success);
        }
    }
}

public sealed partial class ChangeItemViewModel(ConfigChange change, ServerApprovalsViewModel owner, AppServices services) : ObservableObject
{
    public string Id => change.Id;

    public string Title { get; } = change.Path;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        change.Author,
        change.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
        change.Note,
        change.After,
        change.Outdated ? services.T(StringKeys.ApprovalOutdated) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsOutdated => change.Outdated;

    public string Diff => change.Diff;

    public bool HasDiff => Diff.Length > 0;

    /// <summary>Begründung zur Entscheidung (optional).</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [RelayCommand]
    private Task ApproveAsync() => owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, approve: false);
}
