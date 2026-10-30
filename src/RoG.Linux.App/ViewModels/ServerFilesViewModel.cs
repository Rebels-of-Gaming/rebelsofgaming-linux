using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Reiter „Dateien“ (wie <c>ServerFileBrowserViewModel</c> der Windows-App): Ordner öffnen (Pfadleiste, nach oben),
/// Textdatei ansehen, Sicherungskopie anlegen, Sicherungen umbenennen/löschen. Linux zusätzlich: Textdatei in den
/// Downloads-Ordner speichern. Hochladen gibt es nur über den Config-Editor (Whitelist, Diff, Versionen) – die API hat
/// keinen freien Datei-Upload. Logik aus <see cref="ServerFilesService"/>/<see cref="ServerPaths"/> des Kerns.
/// </summary>
public sealed partial class ServerFilesViewModel : ServerDataTabViewModel
{
    /// <summary>Lesbar sind Textdateien bis 512 KB (Guide 18).</summary>
    public const long MaxBytes = 512 * 1024;

    private readonly ServerFilesService _files;
    private string _requested = string.Empty;

    public ServerFilesViewModel(AppServices services, string slug, ServerAbilities abilities, FeedbackViewModel? feedback = null)
        : base(services, slug, abilities, feedback)
    {
        _files = new ServerFilesService(services.Api.Client, services.Api.Http);
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];

    public ObservableCollection<FileCrumbViewModel> Crumbs { get; } = [];

    /// <summary>Neuer Name (Sicherungskopie, Umbenennen) – Eingabe im Inhalt.</summary>
    public InlinePromptViewModel Prompt { get; } = new();

    public bool CanWrite => Abilities.FilesWrite && HasScope("servers:files");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    public partial string CurrentDirectory { get; set; } = string.Empty;

    public bool CanGoUp => CurrentDirectory.Length > 0;

    [ObservableProperty]
    public partial FileItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial string PreviewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    public bool HasPreview => PreviewTitle.Length > 0;

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedPath))]
    public partial string? SavedPath { get; set; }

    public bool HasSavedPath => SavedPath is not null;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var directory = ServerPaths.Normalize(_requested);
        var list = await _files.ListAsync(Slug, directory, cancellationToken);
        CurrentDirectory = directory;
        Crumbs.Clear();
        var crumbs = ServerPaths.Crumbs(directory);
        for (var i = 0; i < crumbs.Count; i++)
        {
            Crumbs.Add(new FileCrumbViewModel(crumbs[i].Name, crumbs[i].Path, i == crumbs.Count - 1, this));
        }

        Items.Clear();
        foreach (var f in list)
        {
            Items.Add(new FileItemViewModel(f, this, Services));
        }

        if (!string.IsNullOrEmpty(PreviewTitle) && ServerPaths.Parent(PreviewTitle) != directory)
        {
            ClosePreview();
        }
    }

    /// <summary>Ordner öffnen (Pfad relativ zum Server-Root).</summary>
    public Task OpenAsync(string directory)
    {
        _requested = ServerPaths.Normalize(directory);
        return LoadAsync();
    }

    private bool CanUp() => CanGoUp;

    [RelayCommand(CanExecute = nameof(CanUp))]
    private Task UpAsync() => OpenAsync(ServerPaths.Parent(CurrentDirectory));

    /// <summary>Ordner öffnen bzw. Datei in der Vorschau zeigen.</summary>
    public async Task ActivateAsync(FileItemViewModel item)
    {
        if (!item.Entry.IsFile)
        {
            await OpenAsync(item.Entry.Path);
            return;
        }

        SelectedItem = item;
        PreviewTitle = item.Entry.Path;
        Preview = string.Empty;
        if (item.Entry.Size > MaxBytes)
        {
            Preview = L(StringKeys.FilesTooLarge);
            return;
        }

        IsPreviewLoading = true;
        try
        {
            string? text = null;
            await RunAsync(async () => text = await _files.ReadAsync(Slug, item.Entry.Path));
            if (PreviewTitle == item.Entry.Path)
            {
                Preview = text ?? string.Empty;
            }
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private void ClosePreview()
    {
        SelectedItem = null;
        PreviewTitle = string.Empty;
        Preview = string.Empty;
    }

    internal async Task CopyAsync(FileItemViewModel item)
    {
        var suggestion = ServerPaths.SuggestBackup(item.Entry.Path, Items.Select(i => i.Entry.Path));
        var target = await Prompt.AskAsync(L(StringKeys.FilesCopyTitle), L(StringKeys.FilesBackupNameHint), suggestion);
        if (target is null)
        {
            return;
        }

        if (!ServerPaths.IsBackupName(target))
        {
            Show(L(StringKeys.FilesBackupNameHint), MessageTone.Info);
            return;
        }

        if (await RunAsync(() => _files.CopyAsync(Slug, item.Entry.Path, target)))
        {
            Show(L(StringKeys.FilesCopied), MessageTone.Success);
            await LoadAsync(silent: true);
        }
    }

    internal async Task RenameAsync(FileItemViewModel item)
    {
        var target = await Prompt.AskAsync(L(StringKeys.FilesRenameTitle), L(StringKeys.FilesBackupNameHint), item.Entry.Path);
        if (target is null || ServerPaths.Normalize(target) == item.Entry.Path)
        {
            return;
        }

        if (!ServerPaths.IsBackupName(target))
        {
            Show(L(StringKeys.FilesBackupNameHint), MessageTone.Info);
            return;
        }

        if (await RunAsync(() => _files.RenameAsync(Slug, item.Entry.Path, target)))
        {
            await LoadAsync(silent: true);
        }
    }

    internal async Task DeleteAsync(FileItemViewModel item)
    {
        if (await AskAsync(L(StringKeys.FilesDeleteTitle), item.Entry.Path) && await RunAsync(() => _files.DeleteAsync(Slug, item.Entry.Path)))
        {
            Items.Remove(item);
            if (PreviewTitle == item.Entry.Path)
            {
                ClosePreview();
            }

            DataChanged();
            Show(L(StringKeys.FilesDeleted), MessageTone.Success);
        }
    }

    /// <summary>Textdatei (bis 512 KB, mehr liest die API nicht) in den Downloads-Ordner speichern.</summary>
    internal async Task DownloadAsync(FileItemViewModel item)
    {
        if (!item.Entry.IsFile)
        {
            return;
        }

        if (item.Entry.Size > MaxBytes)
        {
            Show(L("Linux_FilesDownloadTooLarge"), MessageTone.Info);
            return;
        }

        await RunAsync(async () =>
        {
            var text = await _files.ReadAsync(Slug, item.Entry.Path);
            Directory.CreateDirectory(Services.DownloadDirectory);
            var path = FreePath(Services.DownloadDirectory, item.Entry.Name);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            SavedPath = path;
            Show(Services.Format("Linux_ExportSaved", Path.GetFileName(path)), MessageTone.Success);
        });
    }

    [RelayCommand]
    private Task ShowInFolderAsync() => SavedPath is { } path && File.Exists(path) ? Services.FileManager.ShowAsync(path) : Task.CompletedTask;

    public override bool CloseOverlay()
    {
        if (Prompt.IsOpen)
        {
            Prompt.Cancel();
            return true;
        }

        if (base.CloseOverlay())
        {
            return true;
        }

        if (HasPreview)
        {
            ClosePreview();
            return true;
        }

        return false;
    }
}

/// <summary>Teil der Pfadleiste („/“, „etmain“ …); der letzte ist der offene Ordner.</summary>
public sealed partial class FileCrumbViewModel(string name, string target, bool isCurrent, ServerFilesViewModel owner)
{
    public string Name => name;

    public string Target => target;

    public bool IsCurrent => isCurrent;

    public bool HasSeparator => target.Length > 0;

    [RelayCommand]
    private Task OpenAsync() => owner.OpenAsync(target);
}

public sealed partial class FileItemViewModel(ServerFileEntry entry, ServerFilesViewModel owner, AppServices services)
{
    public ServerFileEntry Entry => entry;

    public string Name => entry.Name;

    public bool IsFolder => !entry.IsFile;

    public bool IsFile => entry.IsFile;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        entry.SizeText,
        entry.ModifiedAt?.ToLocalTime().ToString("g", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasMeta => Meta.Length > 0;

    /// <summary>Gesicherte Kopie (.bak/.old) – gold markiert.</summary>
    public bool IsBackup => entry.IsFile && entry.IsBackup;

    public bool CanCopy => owner.CanWrite && entry.IsFile && entry.Operable;

    public bool CanChange => owner.CanWrite && entry.IsFile && entry.IsBackup;

    /// <summary>Herunterladen geht über den Inhalt der Datei – also nur Text bis 512 KB.</summary>
    public bool CanDownload => entry.IsFile && entry.Size <= ServerFilesViewModel.MaxBytes;

    [RelayCommand]
    private Task OpenAsync() => owner.ActivateAsync(this);

    [RelayCommand]
    private Task CopyAsync() => owner.CopyAsync(this);

    [RelayCommand]
    private Task RenameAsync() => owner.RenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);
}
