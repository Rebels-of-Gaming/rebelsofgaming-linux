using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Reiter „Backups“ (wie <c>ServerBackupsViewModel</c> der Windows-App): Liste, anlegen (läuft im Hintergrund),
/// herunterladen (signierter, kurzlebiger Link vom Node im Browser), sperren/entsperren, wiederherstellen, löschen.
/// Logik aus <see cref="ServerBackupService"/> des Kerns.
/// </summary>
public sealed partial class ServerBackupsViewModel : ServerDataTabViewModel
{
    private readonly ServerBackupService _backups;

    public ServerBackupsViewModel(AppServices services, string slug, ServerAbilities abilities, FeedbackViewModel? feedback = null)
        : base(services, slug, abilities, feedback)
    {
        _backups = new ServerBackupService(services.Api.Client);
    }

    public ObservableCollection<BackupItemViewModel> Items { get; } = [];

    public bool CanManage => Abilities.Backups && HasScope("servers:backups");

    /// <summary>Wiederherstellen braucht zusätzlich <c>servers.power</c> (der Server wird gestoppt).</summary>
    public bool CanRestore => CanManage && Abilities.Power;

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsCreating { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await _backups.ListAsync(Slug, cancellationToken);
        Items.Clear();
        foreach (var b in list)
        {
            Items.Add(new BackupItemViewModel(b, this, Services));
        }
    }

    private bool CanCreate() => CanManage && !IsCreating;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsCreating = true;
        try
        {
            if (await RunAsync(() => _backups.CreateAsync(Slug, NewName)))
            {
                NewName = string.Empty;
                Show(L(StringKeys.BackupCreated), MessageTone.Success);
                await LoadAsync(silent: true);
            }
        }
        finally
        {
            IsCreating = false;
        }
    }

    internal async Task DownloadAsync(BackupItemViewModel item)
    {
        if (await RunAsync(async () => await Services.Browser.OpenAsync(await _backups.DownloadUrlAsync(Slug, item.Uuid))))
        {
            Show(L("Linux_BackupDownloadOpened"), MessageTone.Info);
        }
    }

    internal async Task RestoreAsync(BackupItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.BackupRestoreTitle), Services.Format(StringKeys.BackupRestoreText, item.Name))
            && await RunAsync(() => _backups.RestoreAsync(Slug, item.Uuid, truncate: false)))
        {
            Show(L(StringKeys.BackupRestoreStarted), MessageTone.Success);
        }
    }

    internal async Task ToggleLockAsync(BackupItemViewModel item)
    {
        if (await RunAsync(() => _backups.SetLockedAsync(Slug, item.Uuid, !item.Locked)))
        {
            await LoadAsync(silent: true);
        }
    }

    internal async Task DeleteAsync(BackupItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.BackupDeleteTitle), item.Name) && await RunAsync(() => _backups.DeleteAsync(Slug, item.Uuid)))
        {
            Items.Remove(item);
            DataChanged();
        }
    }
}

public sealed partial class BackupItemViewModel(ServerBackup backup, ServerBackupsViewModel owner, AppServices services)
{
    public string Uuid => backup.Uuid;

    public string Name => backup.Name;

    public bool Locked => backup.Locked;

    public bool IsReady => backup.Completed && backup.Successful;

    public bool IsRunning => !backup.Completed;

    public bool IsFailed => backup.Completed && !backup.Successful;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        backup.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
        backup.Completed
            ? (backup.Successful ? ServerPaths.FormatSize(backup.Bytes) : services.T(StringKeys.BackupFailed))
            : services.T(StringKeys.BackupRunning),
        backup.Locked ? services.T(StringKeys.BackupLocked) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string LockLabel { get; } = services.T(backup.Locked ? StringKeys.BackupUnlock : StringKeys.BackupLock);

    public bool CanManage => owner.CanManage;

    public bool CanDownload => owner.CanManage && IsReady;

    public bool CanRestore => owner.CanRestore && IsReady;

    /// <summary>Gesperrte Backups erst entsperren (sonst <c>409 backup_locked</c>).</summary>
    public bool CanDelete => owner.CanManage && !backup.Locked;

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(this);

    [RelayCommand]
    private Task ToggleLockAsync() => owner.ToggleLockAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}
