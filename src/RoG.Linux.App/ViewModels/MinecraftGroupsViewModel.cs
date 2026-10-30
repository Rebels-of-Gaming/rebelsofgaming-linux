using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Verwaltung → Minecraft → Gruppen (wie <c>MinecraftGroupsViewModel</c> der Windows-App, Website v0.37.0, Guide 44
/// „Gruppen (LuckPerms)“): Rang/Zusatzrolle → LuckPerms-Gruppe mit Priorität, verwaltete und entfernte Gruppen,
/// „Nicht mehr verwalten“. Rangschutz, geschützte Gruppen und Owner-Pflicht beim Freigeben prüft der Server.
/// </summary>
public sealed partial class MinecraftGroupsViewModel : MinecraftAdminTabBase
{
    private readonly MemberAdminService _roles;

    public MinecraftGroupsViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel confirm)
        : base(services, feedback, confirm)
    {
        _roles = new MemberAdminService(services.Api.Client);
    }

    public override bool CanView => Allowed(MinecraftService.ManagePermission, MinecraftService.ManageScope);

    public ObservableCollection<MinecraftGroupItemViewModel> Mappings { get; } = [];

    public ObservableCollection<MinecraftRetiredItemViewModel> Retired { get; } = [];

    /// <summary>Ränge und Zusatzrollen (Squad-Rollen lassen sich nicht zuordnen).</summary>
    public ObservableCollection<SettingsOption> RoleOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedRole { get; set; }

    [ObservableProperty]
    public partial string NewGroup { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? NewPriority { get; set; } = 0;

    [ObservableProperty]
    public partial string ManagedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasRetired { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    protected override Task LoadFirstAsync() => Task.WhenAll(LoadRolesAsync(), LoadAsync());

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    private async Task LoadRolesAsync()
    {
        try
        {
            var roles = await _roles.RolesAsync();
            RoleOptions.Clear();
            foreach (var r in roles.Where(r => r.Type != "squad"))
            {
                RoleOptions.Add(new SettingsOption(r.Id, r.Label));
            }
        }
        catch (Exception ex) when (IsExpected(ex) || ex is RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Ohne roles:read bleibt nur das Bearbeiten vorhandener Zuordnungen.
        }
    }

    private async Task LoadAsync()
    {
        if (!CanView || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await Minecraft.GroupMappingsAsync());
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

    private void Apply(MinecraftGroupOverview overview)
    {
        Mappings.Clear();
        foreach (var m in overview.Mappings)
        {
            Mappings.Add(new MinecraftGroupItemViewModel(m, this, Services));
        }

        Retired.Clear();
        foreach (var r in overview.RetiredGroups)
        {
            Retired.Add(new MinecraftRetiredItemViewModel(r, this, Services));
        }

        IsEmpty = Mappings.Count == 0;
        HasRetired = Retired.Count > 0;
        ManagedText = overview.ManagedGroups.Count == 0 ? L(StringKeys.MinecraftGroupsManagedEmpty) : string.Join(", ", overview.ManagedGroups);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedRole is not { } role)
        {
            Show(L(StringKeys.MinecraftGroupsPickRole), MessageTone.Info);
            return;
        }

        if (!MinecraftService.IsValidGroup(NewGroup))
        {
            Show(L(StringKeys.MinecraftGroupsFormat), MessageTone.Info);
            return;
        }

        var priority = ToInt(NewPriority);
        await RunAndReloadAsync(async () =>
        {
            await Minecraft.SetGroupAsync(role.Value, NewGroup, priority);
            NewGroup = string.Empty;
            NewPriority = 0;
            SelectedRole = null;
            Show(L(StringKeys.MinecraftGroupsSaved), MessageTone.Success);
        });
    }

    /// <summary>Vorhandene Zuordnung ins Formular übernehmen.</summary>
    internal void Edit(MinecraftGroupItemViewModel item)
    {
        var option = RoleOptions.FirstOrDefault(o => o.Value == item.RoleId);
        if (option is null)
        {
            option = new SettingsOption(item.RoleId, item.RoleLabel);
            RoleOptions.Add(option);
        }

        SelectedRole = option;
        NewGroup = item.Group;
        NewPriority = item.Priority;
    }

    internal async Task RemoveAsync(MinecraftGroupItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.MinecraftGroupsRemoveTitle), Format(StringKeys.MinecraftGroupsRemoveText, item.RoleLabel)))
        {
            await RunAndReloadAsync(async () =>
            {
                await Minecraft.RemoveGroupAsync(item.RoleId);
                Show(L(StringKeys.MinecraftGroupsRemoved), MessageTone.Success);
            });
        }
    }

    internal async Task ReleaseAsync(MinecraftRetiredItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.MinecraftGroupsReleaseTitle), Format(StringKeys.MinecraftGroupsReleaseText, item.Group)))
        {
            await RunAndReloadAsync(async () =>
            {
                await Minecraft.ReleaseGroupAsync(item.Group);
                Show(L(StringKeys.MinecraftGroupsReleased), MessageTone.Success);
            });
        }
    }

    /// <summary>Aktion ausführen, danach neu laden – der Server liefert verwaltete/entfernte Gruppen neu.</summary>
    private async Task RunAndReloadAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunAsync(action);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }
}

public sealed partial class MinecraftGroupItemViewModel(MinecraftGroupMapping mapping, MinecraftGroupsViewModel owner, AppServices services)
{
    public string RoleId => mapping.RoleId;

    public string RoleLabel => mapping.RoleLabel;

    public string Group => mapping.Group;

    public int Priority => mapping.Priority;

    public string Meta { get; } = services.Format(StringKeys.MinecraftGroupsMeta, mapping.Group, mapping.Priority);

    [RelayCommand]
    private void Edit() => owner.Edit(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

public sealed partial class MinecraftRetiredItemViewModel(MinecraftRetiredGroup group, MinecraftGroupsViewModel owner, AppServices services)
{
    public string Group => group.Group;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        group.RetiredAt is { } at ? services.Format(StringKeys.MinecraftGroupsRetiredSince, at.ToLocalTime().ToString("d", services.Culture)) : null,
        group.ReleasableAt is { } from ? services.Format(StringKeys.MinecraftGroupsReleasableFrom, from.ToLocalTime().ToString("d", services.Culture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Vor <c>releasable_at</c> deaktiviert; ob der Nutzer Owner ist, entscheidet der Server (403).</summary>
    public bool CanRelease { get; } = group.IsReleasable(DateTimeOffset.UtcNow);

    [RelayCommand]
    private Task ReleaseAsync() => owner.ReleaseAsync(this);
}
