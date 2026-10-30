using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung → Reiter „Rebel-Coins“ (wie <c>CoinsAdminViewModel</c> der Windows-App, Website v0.39.0, Guide 46):
/// Wallets (Konten und Gäste) links, gewählte Wallet mit Buchungen rechts, Gutschrift/Abzug mit Pflicht-Grund und
/// Rückfrage, Tagesbonus und Grenzen gegen Missbrauch. Sichtbar mit Scope <c>minecraft:economy</c> und Recht
/// <c>minecraft.economy</c>; Rangschutz, eigene Wallet und Tageslimit je Admin prüft der Server.
/// </summary>
public sealed partial class CoinsAdminViewModel : MinecraftAdminTabBase
{
    private string? _cursor;
    private string? _txCursor;

    public CoinsAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        KindOptions =
        [
            new SettingsOption(string.Empty, L(StringKeys.CoinsAdminKindAll)),
            .. MinecraftService.WalletKinds.Select(k => new SettingsOption(k, L("CoinsAdmin_Kind_" + k))),
        ];
        SelectedKind = KindOptions[0];
        DirectionOptions = [new SettingsOption(MinecraftService.Credit, L(StringKeys.CoinsAdminCredit)), new SettingsOption(MinecraftService.Debit, L(StringKeys.CoinsAdminDebit))];
        SelectedDirection = DirectionOptions[0];
    }

    public override bool CanView => Allowed(MinecraftService.EconomyPermission, MinecraftService.EconomyScope);

    public ObservableCollection<CoinWalletItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> KindOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedKind { get; set; }

    partial void OnSelectedKindChanged(SettingsOption value)
    {
        if (IsLoaded)
        {
            _ = LoadAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsEmpty { get; set; }

    public bool ShowEmpty => IsEmpty && !IsBusy;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    // --- Einstellungen ---------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool HasSettings { get; set; }

    /// <summary>Einstellungen aufgeklappt (schmal: öffnet die rechte Spalte ohne gewählte Wallet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen))]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial decimal? DailyBase { get; set; }

    [ObservableProperty]
    public partial string Milestones { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? SystemDailyLimit { get; set; }

    [ObservableProperty]
    public partial decimal? KeySystemDailyLimit { get; set; }

    [ObservableProperty]
    public partial decimal? KeyEarningsHourlyLimit { get; set; }

    [ObservableProperty]
    public partial decimal? PayDailyLimit { get; set; }

    [ObservableProperty]
    public partial decimal? AdminDailyLimit { get; set; }

    // --- Gewählte Wallet -------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsDetailOpen))]
    public partial CoinWalletItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>Rechte Spalte (schmal: statt der Liste) – gewählte Wallet oder Einstellungen.</summary>
    public bool IsDetailOpen => Selected is not null || IsSettingsOpen;

    public ObservableCollection<CoinTransactionViewModel> Transactions { get; } = [];

    [ObservableProperty]
    public partial bool HasMoreTransactions { get; set; }

    [ObservableProperty]
    public partial bool NoTransactions { get; set; }

    public IReadOnlyList<SettingsOption> DirectionOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedDirection { get; set; }

    [ObservableProperty]
    public partial decimal? Amount { get; set; }

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    partial void OnSelectedChanged(CoinWalletItemViewModel? value)
    {
        Transactions.Clear();
        NoTransactions = false;
        HasMoreTransactions = false;
        _txCursor = null;
        Amount = null;
        Reason = string.Empty;
        if (value is not null)
        {
            _ = LoadTransactionsAsync(reset: true);
        }
    }

    protected override Task LoadFirstAsync() => Task.WhenAll(LoadAsync(reset: true), LoadSettingsAsync());

    /// <summary>Escape: Rückfrage, sonst gewählte Wallet bzw. Einstellungen schließen.</summary>
    public override bool CloseOverlay()
    {
        if (base.CloseOverlay())
        {
            return true;
        }

        if (IsDetailOpen)
        {
            CloseDetail();
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        Selected = null;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(LoadAsync(reset: true), LoadSettingsAsync());

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private Task MoreTransactionsAsync() => LoadTransactionsAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        if (!CanView || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var kind = string.IsNullOrEmpty(SelectedKind.Value) ? null : SelectedKind.Value;
            var page = await Minecraft.WalletsAsync(Search, kind, reset ? null : _cursor);
            if (reset)
            {
                Selected = null;
                Items.Clear();
            }

            foreach (var wallet in page.Items)
            {
                Items.Add(new CoinWalletItemViewModel(wallet, this, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTransactionsAsync(bool reset)
    {
        if (Selected is not { } wallet || (!reset && _txCursor is null))
        {
            return;
        }

        try
        {
            var page = await Minecraft.WalletTransactionsAsync(wallet.Id, reset ? null : _txCursor);
            if (Selected != wallet)
            {
                return; // inzwischen andere Wallet gewählt
            }

            if (reset)
            {
                Transactions.Clear();
            }

            foreach (var tx in page.Items)
            {
                Transactions.Add(new CoinTransactionViewModel(tx, Services));
            }

            _txCursor = page.NextCursor;
            HasMoreTransactions = page.HasMore;
            NoTransactions = Transactions.Count == 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            Apply(await Minecraft.EconomyAsync());
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
        }
    }

    private void Apply(EconomySettings s)
    {
        DailyBase = s.DailyBase;
        Milestones = MinecraftService.FormatMilestones(s.Milestones);
        SystemDailyLimit = s.SystemDailyLimit;
        KeySystemDailyLimit = s.KeySystemDailyLimit;
        KeyEarningsHourlyLimit = s.KeyEarningsHourlyLimit;
        PayDailyLimit = s.PayDailyLimit;
        AdminDailyLimit = s.AdminDailyLimit;
        HasSettings = true;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!HasSettings)
        {
            return;
        }

        if (MinecraftService.ParseMilestones(Milestones) is not { } milestones)
        {
            Show(L(StringKeys.CoinsAdminMilestonesFormat), MessageTone.Info);
            return;
        }

        await RunAsync(async () =>
        {
            Apply(await Minecraft.SaveEconomyAsync(new EconomySettings(
                ToInt(DailyBase), milestones, ToInt(SystemDailyLimit), ToInt(KeySystemDailyLimit), ToInt(KeyEarningsHourlyLimit), ToInt(PayDailyLimit), ToInt(AdminDailyLimit))));
            Show(L(StringKeys.CoinsAdminSaved), MessageTone.Success);
        });
    }

    [RelayCommand]
    private async Task AdjustAsync()
    {
        if (Selected is not { } wallet)
        {
            return;
        }

        var amount = ToInt(Amount);
        if (!MinecraftService.IsValidAmount(amount))
        {
            Show(L(StringKeys.CoinsAdminAmountRange), MessageTone.Info);
            return;
        }

        if (!MinecraftService.IsValidReason(Reason))
        {
            Show(L(StringKeys.CoinsAdminReasonRequired), MessageTone.Info);
            return;
        }

        var direction = SelectedDirection.Value;
        var text = WithoutCoinGlyph(Format(StringKeys.CoinsAdminConfirmText, SelectedDirection.Label, amount.ToString("N0", Services.Culture), wallet.Title));
        if (!await AskAsync(L(StringKeys.CoinsAdminConfirmTitle), text))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var balance = await Minecraft.AdjustAsync(wallet.Id, direction, amount, Reason);
            wallet.Balance = balance;
            Amount = null;
            Reason = string.Empty;
            Show(WithoutCoinGlyph(Format(StringKeys.CoinsAdminAdjusted, balance.ToString("N0", Services.Culture))), MessageTone.Success);
            await LoadTransactionsAsync(reset: true);
        });
    }

    internal void OpenProfile(string slug) => Services.Navigate(new AppRoute(AppSection.Members, slug));
}

/// <summary>Eine Wallet in der Liste: Spieler- bzw. Kontoname, Art, verknüpfte Accounts, Kontostand, Serie.</summary>
public sealed partial class CoinWalletItemViewModel : ObservableObject
{
    private readonly AdminWallet _wallet;
    private readonly CoinsAdminViewModel _owner;
    private readonly AppServices _services;

    public CoinWalletItemViewModel(AdminWallet wallet, CoinsAdminViewModel owner, AppServices services)
    {
        _wallet = wallet;
        _owner = owner;
        _services = services;
        Balance = wallet.Balance;
        Title = wallet.UserName ?? wallet.Name ?? services.T(StringKeys.CoinsAdminUnknownName);
        Meta = string.Join(" · ", new[]
        {
            services.T("CoinsAdmin_Kind_" + wallet.Kind),
            wallet.Accounts.Count > 0 ? string.Join(", ", wallet.Accounts.Select(a => a.Name)) : null,
            MinecraftAccountsViewModel.DailySummary(services, wallet.Daily.Streak, wallet.Daily.BestStreak),
            wallet.LastActivityAt is { } at ? services.Format(StringKeys.CoinsAdminLastActivity, at.ToLocalTime().ToString("g", services.Culture)) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public string Id => _wallet.Id;

    public string Title { get; }

    public string Meta { get; }

    /// <summary>UUID nur bei Gast-Wallets.</summary>
    public string? Uuid => _wallet.Uuid;

    public bool HasUuid => _wallet.Uuid is not null;

    public bool HasProfile => _wallet.UserSlug is not null;

    public bool IsGuest => _wallet.IsGuest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceText))]
    public partial int Balance { get; set; }

    public string BalanceText => Balance.ToString("N0", _services.Culture) + " Coins";

    [RelayCommand]
    private void OpenProfile()
    {
        if (_wallet.UserSlug is { } slug)
        {
            _owner.OpenProfile(slug);
        }
    }
}
