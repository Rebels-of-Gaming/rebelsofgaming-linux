using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Reiter „Spiel-Bans“ im Steuer-Fenster (wie <c>ServerGameBansViewModel</c> der Windows-App, Website v0.33.0, Guide 43):
/// zentrale Bans, die auf diesem Server gelten (Filter, anlegen, Dauer ändern, aufheben, öffentlich zeigen/ausblenden)
/// und der Plugin-Schlüssel des Servers (Status/Heartbeat, Rechte, erstellen/rotieren, widerrufen, Vorfall).
/// Sichtbarkeit nur aus Rechten + Scopes; was der Server ablehnt, meldet er selbst. Rückfragen (Grund, neue Dauer,
/// Schlüssel einmalig zeigen, Bestätigung) stehen im Inhalt (<see cref="Prompt"/>, <see cref="Confirmation"/>,
/// <see cref="Token"/>) statt in Dialogen; das Fenster kann <see cref="Confirm"/>, <see cref="AskReason"/>,
/// <see cref="AskDuration"/> und <see cref="ShowToken"/> wie in Windows ersetzen.
/// </summary>
public sealed partial class ServerGameBansViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly GameBanService _bans;
    private readonly Action<string, MessageTone> _show;
    private readonly string _serverId;
    private string? _cursor;
    private bool _loaded;

    /// <param name="services">App-Dienste.</param>
    /// <param name="slug">Slug des Servers (Plugin-Schlüssel, Vorfall).</param>
    /// <param name="serverId">ID des Servers (<c>filter[server]</c>, neue Bans).</param>
    /// <param name="abilities">Rechte am Server (<c>control.abilities</c>).</param>
    /// <param name="show">Rückmeldung im Fenster; <c>null</c> = eigene Leiste (<see cref="Feedback"/>) im Reiter.</param>
    public ServerGameBansViewModel(AppServices services, string slug, string serverId, ServerAbilities abilities, Action<string, MessageTone>? show = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(abilities);
        _services = services;
        _bans = new GameBanService(services.Api.Http);
        Slug = slug;
        _serverId = serverId;
        HasOwnFeedback = show is null;
        _show = show ?? Feedback.Show;
        Confirm = (title, text) => Confirmation.AskAsync(title, text, L(StringKeys.CommonConfirm), L(StringKeys.CommonCancel));
        AskReason = Prompt.AskReasonAsync;
        AskDuration = item => Prompt.AskDurationAsync(L(StringKeys.GameBansEditTitle), item.Title, item.Ban.Reason);
        ShowToken = Token.ShowAsync;

        var me = services.CurrentUser;
        IsManager = me is not null && me.Can(GameBanService.ManagePermission) && me.HasScope(GameBanService.BanScope);
        CanBans = me is not null && me.HasScope(GameBanService.BanScope) && (abilities.Bans || IsManager);
        MayTryKey = me is not null && me.HasScope(GameBanService.KeyScope);
        StatusOptions = GameBanService.Statuses.Select(s => new SettingsOption(s, L("GameBans_Status_" + s))).ToList();
        ScopeOptions = GameBanService.Scopes.Where(s => s != GameBanService.ScopeGlobal || IsManager).Select(s => new SettingsOption(s, L("GameBans_Scope_" + s))).ToList();
        MaxScopeOptions = GameBanService.Scopes.Where(s => s == GameBanService.ScopeServer || IsManager).Select(s => new SettingsOption(s, L("GameBans_Scope_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
        SelectedScope = ScopeOptions.FirstOrDefault(o => o.Value == (IsManager ? GameBanService.ScopeGame : GameBanService.ScopeServer)) ?? ScopeOptions[0];
        SelectedMaxScope = MaxScopeOptions[0];
        Token.Copied += (_, ok) => _show(L(ok ? StringKeys.GameBansTokenCopied : "Linux_CopyFailed"), ok ? MessageTone.Success : MessageTone.Error);
        Token.Copy = token => services.ServerConnector.Copy(token);
    }

    private string Slug { get; }

    internal AppServices Services => _services;

    /// <summary>Reiter zeigen: Bans (Scope + Recht) oder Plugin-Schlüssel versuchen (Scope; Recht prüft der Server).</summary>
    public bool IsAvailable => CanBans || MayTryKey;

    /// <summary><c>gamebans.manage</c>: alle Geltungen, IP, Plugin-Rechte über „nur anlegen, nur dieser Server“, Vorfall.</summary>
    public bool IsManager { get; }

    public bool CanBans { get; }

    private bool MayTryKey { get; }

    /// <summary>Eigene Rückmeldung, wenn das Fenster keine liefert (Tests, eigenständige Nutzung).</summary>
    public FeedbackViewModel Feedback { get; } = new();

    public bool HasOwnFeedback { get; }

    /// <summary>Rückfrage vor Gefährlichem (Rotieren, Widerrufen, Vorfall) – Leiste im Inhalt.</summary>
    public InlineConfirmViewModel Confirmation { get; } = new();

    /// <summary>Grund bzw. neue Dauer abfragen – Bereich im Inhalt.</summary>
    public GameBanPromptViewModel Prompt { get; } = new();

    /// <summary>Neuer Plugin-Schlüssel, einmalig sichtbar.</summary>
    public PluginTokenViewModel Token { get; } = new();

    /// <summary>Rückfrage (Titel, Text) → bestätigt.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    /// <summary>Rückfrage mit optionalem Grund (Titel, Text) → Grund bzw. <c>null</c> = abgebrochen.</summary>
    public Func<string, string, Task<string?>> AskReason { get; set; }

    /// <summary>Neue Dauer und Grund für einen Ban → <c>null</c> = abgebrochen.</summary>
    public Func<GameBanItemViewModel, Task<(int Minutes, string? Reason)?>> AskDuration { get; set; }

    /// <summary>Neuen Schlüssel einmalig zeigen (kopierbar); der Wert wird nirgends gespeichert.</summary>
    public Func<string, Task> ShowToken { get; set; }

    /// <summary>Escape: offene Rückfrage schließen; <c>true</c>, wenn etwas geschlossen wurde.</summary>
    public bool CloseOverlay()
    {
        if (Confirmation.IsOpen)
        {
            Confirmation.Cancel();
            return true;
        }

        if (Prompt.IsOpen)
        {
            Prompt.Cancel();
            return true;
        }

        if (Token.IsOpen)
        {
            Token.Done();
            return true;
        }

        return false;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(CanBans ? LoadBansAsync(reset: true) : Task.CompletedTask, MayTryKey ? LoadKeyAsync() : Task.CompletedTask);
    }

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(CanBans ? LoadBansAsync(reset: true) : Task.CompletedTask, MayTryKey ? LoadKeyAsync() : Task.CompletedTask);

    // --- Bans ------------------------------------------------------------------------------------------------------

    public ObservableCollection<GameBanItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public IReadOnlyList<SettingsOption> ScopeOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value)
    {
        if (_loaded)
        {
            _ = LoadBansAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Liste konnte nicht geladen werden (Text für den Fehlerzustand).</summary>
    [ObservableProperty]
    public partial string? ListError { get; set; }

    [RelayCommand]
    private Task SearchAsync() => LoadBansAsync(reset: true);

    [RelayCommand]
    private Task LoadMoreAsync() => LoadBansAsync(reset: false);

    private async Task LoadBansAsync(bool reset)
    {
        if (!CanBans || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ok = await RunAsync(async () =>
            {
                var page = await _bans.ListAsync(_serverId, SelectedStatus.Value, Search, reset ? null : _cursor);
                if (reset)
                {
                    Items.Clear();
                }

                foreach (var ban in page.Items)
                {
                    Items.Add(new GameBanItemViewModel(ban, this));
                }

                _cursor = page.NextCursor;
                HasMore = page.HasMore;
                IsEmpty = Items.Count == 0;
            }, report: !reset);
            if (reset)
            {
                ListError = ok ? null : _lastError;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Neuer Ban

    [ObservableProperty]
    public partial string Identifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IdentifierType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? Minutes { get; set; } = 1440;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Ip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption SelectedScope { get; set; }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var minutes = Minutes is { } m ? (int)Math.Clamp(m, 0, int.MaxValue) : 0;
        var draft = new GameBanDraft(SelectedScope.Value, _serverId, IdentifierType, Identifier, PlayerName, IsManager ? Ip : null, minutes, Reason);
        if (GameBanService.Validate(draft, IsManager) is { Count: > 0 } errors)
        {
            _show(string.Join(" ", errors.Select(L)), MessageTone.Error);
            return;
        }

        GameBan? created = null;
        if (await RunAsync(async () => created = await _bans.CreateAsync(draft)))
        {
            Identifier = IdentifierType = PlayerName = Reason = Ip = string.Empty;
            _show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.GameBansCreated), created!.Title), MessageTone.Success);
            await LoadBansAsync(reset: true);
        }
    }

    internal async Task EditAsync(GameBanItemViewModel item)
    {
        if (await AskDuration(item) is not { } input)
        {
            return;
        }

        GameBan? updated = null;
        if (await RunAsync(async () => updated = await _bans.UpdateAsync(item.Id, input.Minutes, input.Reason)))
        {
            Replace(item, updated!);
            _show(L(StringKeys.GameBansUpdated), MessageTone.Success);
        }
    }

    internal async Task LiftAsync(GameBanItemViewModel item)
    {
        if (await AskReason(L(StringKeys.GameBansLiftTitle), item.Title) is not { } reason)
        {
            return;
        }

        GameBan? lifted = null;
        if (await RunAsync(async () => lifted = await _bans.LiftAsync(item.Id, reason)))
        {
            if (SelectedStatus.Value == GameBanService.Active)
            {
                Items.Remove(item);
                IsEmpty = Items.Count == 0;
            }
            else
            {
                Replace(item, lifted!);
            }

            _show(L(StringKeys.GameBansLifted), MessageTone.Success);
        }
    }

    /// <summary>In der öffentlichen Ban-Liste zeigen bzw. ausblenden (ändert für Plugins nichts).</summary>
    internal async Task TogglePublicAsync(GameBanItemViewModel item)
    {
        GameBan? updated = null;
        var show = !item.Ban.IsPublic;
        if (await RunAsync(async () => updated = await _bans.SetPublicAsync(item.Id, show)))
        {
            Replace(item, updated!);
            _show(L(show ? StringKeys.GameBansShown : StringKeys.GameBansHidden), MessageTone.Success);
        }
    }

    /// <summary>Öffentliche Seite eines Bans: in der App (Ban-Liste), sonst im Browser.</summary>
    internal void OpenPublic(Uri url)
    {
        if (_services.RouteFor(url) is not { } route || !_services.Navigate(route))
        {
            _ = _services.Browser.OpenAsync(url);
        }
    }

    internal async Task LoadDetailsAsync(GameBanItemViewModel item)
    {
        GameBan? full = null;
        if (await RunAsync(async () => full = await _bans.GetAsync(item.Id)))
        {
            item.SetDeliveries(full!.Deliveries);
        }
    }

    private void Replace(GameBanItemViewModel item, GameBan ban)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items[index] = new GameBanItemViewModel(ban, this);
        }
    }

    // --- Plugin-Schlüssel ------------------------------------------------------------------------------------------

    public IReadOnlyList<SettingsOption> MaxScopeOptions { get; }

    /// <summary>Schlüssel-Bereich: nur, wenn der Server den Status liefert (403 = kein <c>servers.manage</c>).</summary>
    [ObservableProperty]
    public partial bool HasKeySection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyActionText))]
    public partial bool KeyExists { get; set; }

    [ObservableProperty]
    public partial string KeyStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyDetails))]
    public partial string KeyDetails { get; set; } = string.Empty;

    public bool HasKeyDetails => KeyDetails.Length > 0;

    [ObservableProperty]
    public partial IBrush? KeyBrush { get; set; }

    [ObservableProperty]
    public partial bool MayIssue { get; set; } = true;

    [ObservableProperty]
    public partial bool MayLift { get; set; }

    [ObservableProperty]
    public partial bool MayLiftAny { get; set; }

    [ObservableProperty]
    public partial bool MayIp { get; set; }

    [ObservableProperty]
    public partial bool MayBanMembers { get; set; }

    /// <summary>Minecraft-Konten verknüpfen – nur am Schlüssel des Proxys (Codes für beliebige UUIDs).</summary>
    [ObservableProperty]
    public partial bool MayLink { get; set; }

    /// <summary>Rebel-Coins buchen – nur für Server mit Shop oder Berufen.</summary>
    [ObservableProperty]
    public partial bool MayEconomy { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedMaxScope { get; set; }

    public string KeyActionText => L(KeyExists ? StringKeys.GameBansKeyRotate : StringKeys.GameBansKeyCreate);

    [ObservableProperty]
    public partial string IncidentReason { get; set; } = string.Empty;

    private PluginKeyRights Rights => new(MayIssue, MayLift, MayLiftAny, MayIp, MayBanMembers, SelectedMaxScope.Value, MayLink, MayEconomy);

    private async Task LoadKeyAsync()
    {
        try
        {
            ApplyKey(await _bans.KeyAsync(Slug));
            HasKeySection = true;
        }
        catch (ApiProblemException ex) when (ex.Problem.Status is 403 or 404)
        {
            HasKeySection = false;
        }
        catch (ApiProblemException ex)
        {
            _show(_services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    private void ApplyKey(PluginKeyStatus key)
    {
        KeyExists = key.Exists && key.Active;
        var rights = key.Exists ? PluginKeyRights.From(key) : PluginKeyRights.Minimal;
        (MayIssue, MayLift, MayLiftAny, MayIp, MayBanMembers, MayLink, MayEconomy) = (rights.MayIssue, rights.MayLift, rights.MayLiftAny, rights.MayIp, rights.MayBanMembers, rights.MayLink, rights.MayEconomy);
        SelectedMaxScope = MaxScopeOptions.FirstOrDefault(o => o.Value == rights.MaxScope) ?? MaxScopeOptions[0];
        KeyStatus = !KeyExists
            ? L(key.RevokedAt is null ? StringKeys.GameBansKeyNone : StringKeys.GameBansKeyRevoked)
            : L(key.PluginActive ? StringKeys.GameBansPluginActive : StringKeys.GameBansPluginInactive);
        KeyBrush = UiResources.Brush(!KeyExists ? "RoGMutedBrush" : key.PluginActive ? "RoGGreenBrush" : "RoGGoldBrush");
        KeyDetails = KeyExists ? string.Join(" · ", new[]
        {
            key.Hint,
            key.Plugin is { } p ? p + (key.PluginVersion is { } v ? " " + v : string.Empty) : null,
            key.PluginOutdated ? L(StringKeys.GameBansPluginOutdated) : null,
            key.GameVersion,
            key.PlayersOnline is { } n ? _services.Format(StringKeys.GameBansPlayersOnline, n) : null,
            key.LastHeartbeatAt is { } hb ? _services.Format(StringKeys.GameBansLastHeartbeat, hb.ToLocalTime().ToString("g", _services.Culture)) : null,
        }.Where(s => !string.IsNullOrEmpty(s))) : string.Empty;
    }

    [RelayCommand]
    private async Task CreateKeyAsync()
    {
        if (KeyExists && !await Confirm(L(StringKeys.GameBansKeyRotate), L(StringKeys.GameBansKeyRotateText)))
        {
            return;
        }

        var rights = IsManager ? Rights : PluginKeyRights.Minimal with { MayIssue = MayIssue };
        PluginKeyCreated? created = null;
        if (await RunAsync(async () => created = await _bans.CreateKeyAsync(Slug, rights)))
        {
            ApplyKey(created!.Status);
            await ShowToken(created.Token);
        }
    }

    [RelayCommand]
    private async Task SaveRightsAsync()
    {
        var rights = Rights;
        if (rights.NeedsManage && !IsManager)
        {
            _show(L(StringKeys.GameBansRightsNeedManage), MessageTone.Error);
            return;
        }

        PluginKeyStatus? key = null;
        if (await RunAsync(async () => key = await _bans.UpdateKeyAsync(Slug, rights)))
        {
            ApplyKey(key!);
            _show(L(StringKeys.GameBansRightsSaved), MessageTone.Success);
        }
    }

    [RelayCommand]
    private async Task RevokeKeyAsync()
    {
        if (await Confirm(L(StringKeys.GameBansKeyRevoke), L(StringKeys.GameBansKeyRevokeText))
            && await RunAsync(() => _bans.RevokeKeyAsync(Slug)))
        {
            _show(L(StringKeys.GameBansKeyRevokedDone), MessageTone.Success);
            await LoadKeyAsync();
        }
    }

    /// <summary>Vorfall: alle Bans aus dem Plugin dieses Servers aufheben (nur <c>gamebans.manage</c>).</summary>
    [RelayCommand]
    private async Task LiftPluginBansAsync()
    {
        if (!IsManager || !await Confirm(L(StringKeys.GameBansIncidentTitle), L(StringKeys.GameBansIncidentText)))
        {
            return;
        }

        var count = 0;
        if (await RunAsync(async () => count = await _bans.LiftPluginBansAsync(Slug, IncidentReason, null)))
        {
            IncidentReason = string.Empty;
            _show(_services.Format(StringKeys.GameBansIncidentDone, count), MessageTone.Success);
            await LoadBansAsync(reset: true);
        }
    }

    // --- Hilfen ----------------------------------------------------------------------------------------------------

    private string? _lastError;

    internal string L(string key) => _services.T(key);

    /// <summary>Führt einen Aufruf aus; Fehler als Rückmeldung (bzw. nur gemerkt, wenn <paramref name="report"/> false).</summary>
    private async Task<bool> RunAsync(Func<Task> action, bool report = true)
    {
        string? error;
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            error = _services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException)
        {
            error = _services.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown);
        }

        _lastError = error;
        if (report)
        {
            _show(error, MessageTone.Error);
        }

        return false;
    }
}

/// <summary>Ein zentraler Ban in der Liste des Reiters.</summary>
public sealed partial class GameBanItemViewModel : ObservableObject
{
    private readonly GameBan _ban;
    private readonly ServerGameBansViewModel _owner;

    public GameBanItemViewModel(GameBan ban, ServerGameBansViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(ban);
        ArgumentNullException.ThrowIfNull(owner);
        _ban = ban;
        _owner = owner;
        var s = owner.Services;
        string Time(DateTimeOffset t) => t.ToLocalTime().ToString("g", s.Culture);
        Meta = string.Join(" · ", new[]
        {
            ban.Scope switch
            {
                GameBanService.ScopeGlobal => s.T("GameBans_Scope_global"),
                GameBanService.ScopeServer => ban.ServerName ?? s.T("GameBans_Scope_server"),
                _ => ban.GameName ?? s.T("GameBans_Scope_game"),
            },
            ban.Reason,
            ban.LiftedAt is { } lifted
                ? s.Format(StringKeys.GameBansLiftedAt, Time(lifted), ban.LiftedBy ?? "—")
                : ban.ExpiresAt is { } e
                    ? s.Format(StringKeys.BanUntil, Time(e))
                    : s.T(StringKeys.BanPermanent),
            ban.IssuedBy is { } issued
                ? s.Format(StringKeys.GameBansIssuedIngame, issued, ban.SourceServer ?? "—")
                : ban.CreatedBy is { } by ? s.Format(StringKeys.GameBansCreatedBy, by) : null,
            ban.UserName is { } user ? s.Format(StringKeys.GameBansMember, user) : null,
        }.Where(t => !string.IsNullOrEmpty(t)));
        PublicInfo = string.Join(" · ", new[]
        {
            ban.IsPublic ? null : s.T(StringKeys.GameBansHiddenBadge),
            ban.AppealCode is { } code ? s.Format(StringKeys.GameBansAppealCode, code) : null,
        }.Where(t => !string.IsNullOrEmpty(t)));
        StatusText = s.T("GameBans_Status_" + ban.Status);
        ToggleText = s.T(ban.IsPublic ? StringKeys.GameBansHide : StringKeys.GameBansShow);
    }

    public string Id => _ban.Id;

    public string Title => _ban.Title;

    public GameBan Ban => _ban;

    public string Identity => $"{_ban.IdentifierType}: {_ban.Identifier}" + (_ban.Ip is { } ip ? " · " + ip : string.Empty);

    public bool IsActive => _ban.IsActive;

    public string StatusText { get; }

    public IBrush StatusBrush => UiResources.Brush(_ban.Status switch
    {
        GameBanService.Active => "RoGRedBrush",
        GameBanService.Lifted => "RoGGreenBrush",
        _ => "RoGMutedBrush",
    });

    public string Meta { get; }

    /// <summary>Öffentliche Ban-Liste: „ausgeblendet“ bzw. Einspruchs-Code (nur für zuständige Admins).</summary>
    public string PublicInfo { get; }

    public bool HasPublicInfo => PublicInfo.Length > 0;

    public string ToggleText { get; }

    public bool HasPublicUrl => _ban.PublicUrl is not null && _ban.IsPublic;

    public string AutomationName => $"{Title}, {StatusText}";

    /// <summary>RCON-Zustellungen (aus dem Einzelabruf).</summary>
    [ObservableProperty]
    public partial string Deliveries { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDeliveries { get; set; }

    internal void SetDeliveries(IReadOnlyList<GameBanDelivery> deliveries)
    {
        var s = _owner.Services;
        Deliveries = deliveries.Count == 0
            ? s.T(StringKeys.GameBansNoDeliveries)
            : string.Join(Environment.NewLine, deliveries.Select(d => string.Join(" · ", new[]
            {
                d.ServerName ?? "—",
                s.T("GameBans_Delivery_" + d.Status),
                d.Error,
            }.Where(t => !string.IsNullOrEmpty(t)))));
        HasDeliveries = true;
    }

    [RelayCommand]
    private Task EditAsync() => _owner.EditAsync(this);

    [RelayCommand]
    private Task LiftAsync() => _owner.LiftAsync(this);

    [RelayCommand]
    private Task DetailsAsync() => _owner.LoadDetailsAsync(this);

    [RelayCommand]
    private Task TogglePublicAsync() => _owner.TogglePublicAsync(this);

    [RelayCommand]
    private void OpenPublic()
    {
        if (_ban.PublicUrl is { } url)
        {
            _owner.OpenPublic(url);
        }
    }
}

/// <summary>Rückfrage im Inhalt: Grund (Aufheben) bzw. neue Dauer + Grund (Ändern) – statt ContentDialog.</summary>
public sealed partial class GameBanPromptViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _pending;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Dauer abfragen (Ändern) – sonst nur der Grund (Aufheben).</summary>
    [ObservableProperty]
    public partial bool AsksDuration { get; set; }

    [ObservableProperty]
    public partial decimal? Minutes { get; set; } = 1440;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Aufheben mit optionalem Grund; <c>null</c> = abgebrochen, sonst der Grund (ggf. leer).</summary>
    public async Task<string?> AskReasonAsync(string title, string text)
    {
        AsksDuration = false;
        Reason = string.Empty;
        return await OpenAsync(title, text) ? Reason.Trim() : null;
    }

    /// <summary>Neue Dauer ab jetzt (0 = dauerhaft) und optional neuer Grund (unverändert → <c>null</c>).</summary>
    public async Task<(int Minutes, string? Reason)?> AskDurationAsync(string title, string banTitle, string? currentReason)
    {
        AsksDuration = true;
        Minutes = 1440;
        Reason = currentReason ?? string.Empty;
        if (!await OpenAsync(title, banTitle))
        {
            return null;
        }

        var value = Minutes is { } m ? (int)Math.Clamp(m, 0, int.MaxValue) : 0;
        var text = Reason.Trim();
        return (value, text.Length == 0 || text == currentReason ? null : text);
    }

    private Task<bool> OpenAsync(string title, string text)
    {
        _pending?.TrySetResult(false);
        var pending = new TaskCompletionSource<bool>();
        _pending = pending;
        Title = title;
        Text = text;
        IsOpen = true;
        return pending.Task;
    }

    [RelayCommand]
    private void Accept() => Finish(true);

    [RelayCommand]
    public void Cancel() => Finish(false);

    private void Finish(bool result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}

/// <summary>Neuer Plugin-Schlüssel: einmal im Inhalt zeigen, kopierbar; „Fertig“ vergisst ihn.</summary>
public sealed partial class PluginTokenViewModel : ObservableObject
{
    private TaskCompletionSource? _pending;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>Kopieren (Zwischenablage) – <c>false</c>, wenn sie gesperrt ist.</summary>
    public Func<string, bool> Copy { get; set; } = _ => false;

    public event EventHandler<bool>? Copied;

    public Task ShowAsync(string token)
    {
        _pending?.TrySetResult();
        _pending = new TaskCompletionSource();
        Value = token;
        IsOpen = true;
        return _pending.Task;
    }

    [RelayCommand]
    private void CopyToken()
    {
        if (Value.Length > 0)
        {
            Copied?.Invoke(this, Copy(Value));
        }
    }

    [RelayCommand]
    public void Done()
    {
        IsOpen = false;
        Value = string.Empty;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult();
    }
}
