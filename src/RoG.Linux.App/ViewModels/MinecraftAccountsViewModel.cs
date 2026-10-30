using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Konto → Minecraft (wie <c>MinecraftAccountsViewModel</c> der Windows-App, Guide 44/46): eigene Java-/Bedrock-Accounts,
/// Code aus <c>/link</c> einlösen (immer erst Vorschau „bist das du?“, dann Bestätigen – auch bei einem Code aus einem
/// Link), trennen (mit Rückfrage), Website-Nachrichten im Spiel, Rebel-Coins (Stand, Serie, letzte Buchungen,
/// Sichtbarkeit) und Sichtbarkeit der Minecraft-Statistiken. Rückfragen im Inhalt statt ContentDialog.
/// Ältere Geräte-Tokens haben <c>minecraft:read</c>/<c>minecraft:write</c> erst nach neuer Anmeldung.
/// </summary>
public sealed partial class MinecraftAccountsViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;
    private bool _visibilityLoaded;
    private bool _settingLoaded;

    public MinecraftAccountsViewModel(AppServices services)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        VisibilityOptions = MinecraftService.CoinsVisibilities.Select(v => new SettingsOption(v, services.T("Coins_Visibility_" + v))).ToList();
        SelectedCoinsVisibility = VisibilityOptions[0];
        SelectedStatsVisibility = VisibilityOptions[0];
        services.CurrentUserChanged += (_, _) => OnScopesChanged();
    }

    public bool CanRead => Services.CurrentUser is { } me && me.HasScope(MinecraftService.ReadScope);

    public bool CanLink => Services.CurrentUser is { } me && me.HasScope(MinecraftService.WriteScope);

    /// <summary>Token ohne Minecraft-Scopes: Hinweis „einmal ab- und wieder anmelden“.</summary>
    public bool NeedsSignIn => Services.CurrentUser is not null && !(CanRead && CanLink);

    public bool CanEditVisibility => Services.CurrentUser is { } me && me.HasScope("profile:read") && me.HasScope("profile:write");

    public ObservableCollection<MinecraftAccountItemViewModel> Items { get; } = [];

    public ObservableCollection<CoinTransactionViewModel> Transactions { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Vorschau „bist das du?“ und „Account trennen?“.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    public IReadOnlyList<SettingsOption> VisibilityOptions { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool HasAccounts { get; set; }

    /// <summary>Geladen und noch kein Account verknüpft.</summary>
    public bool ShowEmpty => !HasAccounts && !IsLoading && !HasError && CanRead;

    /// <summary>Website-/App-Nachrichten von Freunden im Spiel anzeigen (Standard aus).</summary>
    [ObservableProperty]
    public partial bool ShowWebsiteMessages { get; set; }

    /// <summary>Wallet geladen – auch ohne verknüpften Account (die Wallet bleibt beim Konto).</summary>
    [ObservableProperty]
    public partial bool HasWallet { get; set; }

    [ObservableProperty]
    public partial string BalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DailyText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoTransactions))]
    public partial bool HasTransactions { get; set; }

    public bool NoTransactions => !HasTransactions;

    [ObservableProperty]
    public partial SettingsOption SelectedCoinsVisibility { get; set; }

    /// <summary>„Minecraft-Statistiken“ im Profil (Statistiken, Bestenlisten, Awards) – gleiche Werte wie bei den Coins.</summary>
    [ObservableProperty]
    public partial SettingsOption SelectedStatsVisibility { get; set; }

    /// <summary>Die Seite setzt den Fokus ins Code-Feld (Link <c>rog://account/minecraft?code=…</c>).</summary>
    public event EventHandler? FocusCodeRequested;

    protected override bool HasData => Items.Count > 0 || HasWallet || !CanRead;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoading)
        {
            _ = LoadAsync(silent: true);
        }
    }

    private void OnScopesChanged()
    {
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanLink));
        OnPropertyChanged(nameof(NeedsSignIn));
        OnPropertyChanged(nameof(CanEditVisibility));
        OnPropertyChanged(nameof(ShowEmpty));
        CheckCommand.NotifyCanExecuteChanged();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        OnScopesChanged();
        if (!CanRead)
        {
            Items.Clear();
            HasAccounts = false;
            return;
        }

        var list = await _minecraft.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var account in list)
        {
            Items.Add(new MinecraftAccountItemViewModel(account, this, Services));
        }

        HasAccounts = Items.Count > 0;
        await LoadWalletAsync(cancellationToken);
        if (HasAccounts && !_settingLoaded)
        {
            try
            {
                ShowWebsiteMessages = await _minecraft.ShowWebsiteMessagesAsync(cancellationToken);
                _settingLoaded = true;
            }
            catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
            {
                // Einstellung nicht lesbar (z. B. Server noch ohne v0.38.0) – Schalter bleibt aus und ohne Wirkung
            }
        }

        OnPropertyChanged(nameof(ShowEmpty));
    }

    private async Task LoadWalletAsync(CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await _minecraft.WalletAsync(cancellationToken);
            BalanceText = Services.Format(StringKeys.CoinsBalance, wallet.Balance.ToString("N0", Services.Culture));
            DailyText = DailySummary(Services, wallet.Daily.Streak, wallet.Daily.BestStreak)
                + " · " + Services.T(wallet.Daily.ClaimedToday ? StringKeys.CoinsClaimedToday : StringKeys.CoinsNotClaimedToday);
            Transactions.Clear();
            foreach (var tx in wallet.Transactions)
            {
                Transactions.Add(new CoinTransactionViewModel(tx, Services));
            }

            HasTransactions = Transactions.Count > 0;
            HasWallet = true;
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            // Wallet nicht lesbar (z. B. Server noch ohne v0.39.0) – Bereich bleibt ausgeblendet
            HasWallet = false;
        }

        if (CanEditVisibility && !_visibilityLoaded)
        {
            try
            {
                var (coins, stats) = await _minecraft.ProfileVisibilityAsync(cancellationToken);
                SelectedCoinsVisibility = VisibilityOptions.First(o => o.Value == coins);
                SelectedStatsVisibility = VisibilityOptions.First(o => o.Value == stats);
                _visibilityLoaded = true;
            }
            catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
            {
                // Sichtbarkeit nicht lesbar – Auswahl bleibt ohne Wirkung
            }
        }
    }

    /// <summary>„Serie: 5 Tage (beste: 12)“ bzw. „Keine laufende Serie (beste: 12)“.</summary>
    internal static string DailySummary(AppServices services, int streak, int best) =>
        services.Format(streak switch { <= 0 => StringKeys.CoinsNoStreak, 1 => StringKeys.CoinsStreakOne, _ => StringKeys.CoinsStreak }, streak, best);

    partial void OnSelectedCoinsVisibilityChanged(SettingsOption value)
    {
        if (_visibilityLoaded && value is not null)
        {
            _ = SaveVisibilityAsync(() => _minecraft.SetCoinsVisibilityAsync(value.Value), StringKeys.CoinsVisibilitySaved, value.Label);
        }
    }

    partial void OnSelectedStatsVisibilityChanged(SettingsOption value)
    {
        if (_visibilityLoaded && value is not null)
        {
            _ = SaveVisibilityAsync(() => _minecraft.SetStatsVisibilityAsync(value.Value), StringKeys.StatsVisibilitySaved, value.Label);
        }
    }

    private async Task SaveVisibilityAsync(Func<Task<string>> save, string messageKey, string label)
    {
        try
        {
            await save();
            Feedback.Show(Services.Format(messageKey, label), MessageTone.Success);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException or HttpRequestException)
        {
            Feedback.Show(ex is ApiProblemException p ? Services.Localizer.GetError(p.Problem.Code) : Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    partial void OnShowWebsiteMessagesChanged(bool value)
    {
        if (_settingLoaded)
        {
            _ = SaveSettingAsync(value);
        }
    }

    private async Task SaveSettingAsync(bool value)
    {
        try
        {
            await _minecraft.SetShowWebsiteMessagesAsync(value);
            Feedback.Show(Services.T(value ? StringKeys.MinecraftWebsiteMessagesOn : StringKeys.MinecraftWebsiteMessagesOff), MessageTone.Success);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or HttpRequestException)
        {
            Feedback.Show(ex is ApiProblemException p ? Services.Localizer.GetError(p.Problem.Code) : Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
            _settingLoaded = false;
            ShowWebsiteMessages = !value;
            _settingLoaded = true;
        }
    }

    /// <summary>Code aus einem Link vorbefüllen – eingelöst wird er erst nach „Code prüfen“ und Bestätigen.</summary>
    public void Prefill(string code)
    {
        Code = code;
        Feedback.Show(Services.T(StringKeys.MinecraftPrefilled), MessageTone.Info);
    }

    /// <summary>Link <c>…/account/minecraft[?code=…]</c>: Bereich aufklappen, Code nur vorbefüllen, Fokus ins Feld.</summary>
    public void OpenFromLink(string? code)
    {
        IsExpanded = true;
        if (code is not null)
        {
            Prefill(code);
        }

        FocusCodeRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanCheck() => !IsBusy && CanLink;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (!MinecraftService.IsValidCode(Code))
        {
            Feedback.Show(Services.T(StringKeys.MinecraftCodeFormat), MessageTone.Info);
            return;
        }

        var code = MinecraftService.NormalizeCode(Code)!;
        IsBusy = true;
        try
        {
            var preview = await _minecraft.PreviewAsync(code);
            var yes = await Confirm.AskAsync(
                Services.T(StringKeys.MinecraftPreviewTitle),
                PreviewText(preview),
                Services.T(StringKeys.MinecraftConfirm),
                Services.T(StringKeys.MinecraftDecline));
            if (!yes)
            {
                Feedback.Show(Services.T(StringKeys.MinecraftDeclined), MessageTone.Info);
                return;
            }

            var account = await _minecraft.ConfirmAsync(code);
            Code = string.Empty;
            Feedback.Show(Services.Format(StringKeys.MinecraftLinked, account.Name), MessageTone.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task UnlinkAsync(MinecraftAccountItemViewModel item)
    {
        if (!CanLink || !await Confirm.AskAsync(
                Services.T(StringKeys.MinecraftUnlinkTitle),
                Services.Format(StringKeys.MinecraftUnlinkText, item.Name),
                Services.T(StringKeys.MinecraftUnlinkConfirm),
                Services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        try
        {
            await _minecraft.UnlinkAsync(item.Id);
            Items.Remove(item);
            HasAccounts = Items.Count > 0;
            DataChanged();
            Feedback.Show(Services.Format(StringKeys.MinecraftUnlinked, item.Name), MessageTone.Success);
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    [RelayCommand]
    private void OpenLeaderboards() => Services.Navigate(new AppRoute(AppSection.Leaderboards));

    /// <summary>„Code für Notch (Java), angefordert vor 2 Minuten – bist das du?“ (+ Server).</summary>
    private string PreviewText(MinecraftLinkPreview preview)
    {
        var edition = EditionText(Services, preview.Edition, preview.Platform);
        var when = preview.MinutesAgo is { } m && m > 0
            ? Services.Format(StringKeys.MinecraftMinutesAgo, m)
            : Services.T(StringKeys.MinecraftJustNow);
        var text = Services.Format(StringKeys.MinecraftPreview, preview.Name, edition, when);
        return preview.Server is { } server ? text + "\n" + Services.Format(StringKeys.MinecraftPreviewServer, server) : text;
    }

    /// <summary>„Java“ bzw. „Bedrock · Xbox“.</summary>
    internal static string EditionText(AppServices services, string edition, string? platform)
    {
        var text = services.T("Minecraft_Edition_" + edition);
        return edition == MinecraftService.Bedrock && platform is { } p && p != MinecraftService.Java
            ? text + " · " + services.T("Minecraft_Platform_" + p)
            : text;
    }
}

public sealed partial class MinecraftAccountItemViewModel(MinecraftAccount account, MinecraftAccountsViewModel owner, AppServices services)
{
    public string Id => account.Id;

    public string Name => account.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        MinecraftAccountsViewModel.EditionText(services, account.Edition, account.Platform),
        account.LinkedAt is { } linked ? services.Format(StringKeys.MinecraftLinkedAt, linked.ToLocalTime().ToString("d", services.Culture)) : null,
        account.LastSeenAt is { } seen ? services.Format(StringKeys.MinecraftLastSeen, seen.ToLocalTime().ToString("g", services.Culture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Uuid => account.Uuid;

    public bool CanUnlink => owner.CanLink;

    [RelayCommand]
    private Task UnlinkAsync() => owner.UnlinkAsync(this);
}

/// <summary>
/// Eine Buchung: „+120 · Verkauf an den Server-Shop · 2x DIAMOND“ – andere Spieler werden nie genannt. Ohne das Zeichen
/// ⛃ der Windows-App (fehlt in den üblichen Linux-Systemschriften, steht als Kästchen da).
/// </summary>
public sealed class CoinTransactionViewModel(MinecraftTransaction tx, AppServices services)
{
    public string Amount { get; } = (tx.IsIncoming ? "+" : tx.IsOutgoing ? "−" : "±") + tx.Amount.ToString("N0", services.Culture);

    public bool IsIncoming => tx.IsIncoming;

    public string Title { get; } = TypeText(services, tx.Type, tx.Source);

    public string Meta { get; } = string.Join(" · ", new[]
    {
        tx.Reason,
        tx.Server,
        services.T("Coins_Counterparty_" + tx.Counterparty) is { } c && !c.StartsWith("Coins_", StringComparison.Ordinal) ? c : null,
        (tx.PeriodStart ?? tx.CreatedAt)?.ToLocalTime().ToString("g", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Bekannte Typen übersetzt, Berufs-Verdienst mit Beruf („Verdienst: miner“), sonst der Typ selbst.</summary>
    internal static string TypeText(AppServices services, string type, string? source)
    {
        var text = services.T("Coins_Type_" + type);
        if (string.IsNullOrEmpty(text) || text.StartsWith("Coins_", StringComparison.Ordinal))
        {
            text = type;
        }

        return type == "earning" && source is { } s ? text + ": " + (s.StartsWith("job:", StringComparison.Ordinal) ? s[4..] : s) : text;
    }
}
