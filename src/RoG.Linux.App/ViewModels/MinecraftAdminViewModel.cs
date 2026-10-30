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
/// Verwaltung → Reiter „Minecraft“ (wie der Minecraft-Bereich der Windows-<c>AdminPage</c>): oben die Gruppen
/// (<see cref="Groups"/>, LuckPerms), darunter alle verknüpften Accounts mit Konto und Server der Verknüpfung, Suche nach
/// Name/UUID, Filter Edition, trennen (Website v0.36.0, Guide 44). Sichtbar mit Scope <c>minecraft:manage</c> und Recht
/// <c>minecraft.manage</c>; Rangschutz beim Trennen prüft der Server (<c>403 forbidden</c>).
/// </summary>
public sealed partial class MinecraftAdminViewModel : MinecraftAdminTabBase
{
    private string? _cursor;

    public MinecraftAdminViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        Groups = new MinecraftGroupsViewModel(services, feedback, confirm);
        EditionOptions =
        [
            new SettingsOption(string.Empty, L(StringKeys.MinecraftAdminEditionAll)),
            .. MinecraftService.Editions.Select(e => new SettingsOption(e, L("Minecraft_Edition_" + e))),
        ];
        SelectedEdition = EditionOptions[0];
    }

    public override bool CanView => Allowed(MinecraftService.ManagePermission, MinecraftService.ManageScope);

    /// <summary>Gruppen (LuckPerms) im selben Reiter.</summary>
    public MinecraftGroupsViewModel Groups { get; }

    public ObservableCollection<MinecraftAdminItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> EditionOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedEdition { get; set; }

    partial void OnSelectedEditionChanged(SettingsOption value)
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

    /// <summary>Leerzustand erst nach dem Laden (nicht während).</summary>
    public bool ShowEmpty => IsEmpty && !IsBusy;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    protected override Task LoadFirstAsync() => Task.WhenAll(Groups.EnsureLoadedAsync(), LoadAsync(reset: true));

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(Groups.ReloadCommand.ExecuteAsync(null), LoadAsync(reset: true));

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        if (!CanView || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var edition = string.IsNullOrEmpty(SelectedEdition.Value) ? null : SelectedEdition.Value;
            var page = await Minecraft.ListAsync(Search, edition, null, reset ? null : _cursor);
            if (reset)
            {
                Items.Clear();
            }

            foreach (var account in page.Items)
            {
                Items.Add(new MinecraftAdminItemViewModel(account, this, Services));
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

    internal async Task UnlinkAsync(MinecraftAdminItemViewModel item)
    {
        var text = Format(StringKeys.MinecraftAdminUnlinkText, item.Name, item.UserName ?? "—");
        if (!await AskAsync(L(StringKeys.MinecraftUnlinkTitle), text))
        {
            return;
        }

        if (await RunAsync(() => Minecraft.AdminUnlinkAsync(item.Id)))
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
            Show(Format(StringKeys.MinecraftUnlinked, item.Name), MessageTone.Success);
        }
    }

    internal void OpenProfile(string slug) => Services.Navigate(new AppRoute(AppSection.Members, slug));
}

/// <summary>Ein verknüpfter Account: Name, Edition/Konto/Server/Datum, UUID, trennen, Profil öffnen.</summary>
public sealed partial class MinecraftAdminItemViewModel(MinecraftAccount account, MinecraftAdminViewModel owner, AppServices services)
{
    public string Id => account.Id;

    public string Name => account.Name;

    public string? UserName => account.UserName;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        MinecraftAccountsViewModel.EditionText(services, account.Edition, account.Platform),
        account.UserName is { } user ? services.Format(StringKeys.MinecraftAdminUser, user) : null,
        account.ServerName is { } server ? services.Format(StringKeys.MinecraftAdminVia, server) : null,
        account.LinkedAt is { } linked ? services.Format(StringKeys.MinecraftLinkedAt, linked.ToLocalTime().ToString("d", services.Culture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Uuid => account.Uuid;

    public bool HasProfile => account.UserSlug is not null;

    [RelayCommand]
    private Task UnlinkAsync() => owner.UnlinkAsync(this);

    [RelayCommand]
    private void OpenProfile()
    {
        if (account.UserSlug is { } slug)
        {
            owner.OpenProfile(slug);
        }
    }
}
