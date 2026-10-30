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
/// Reiter „Configs“ (wie <c>ServerConfigsViewModel</c> der Windows-App): Datei der Whitelist wählen, im Editor
/// bearbeiten, vor dem Speichern prüfen (Diff + Validierung), speichern (ggf. als Antrag), Versionen zurückspielen,
/// Formular-Vorlage. Linux zusätzlich: Inhalt aus einer lokalen Datei laden (Datei-Dialog des Systems) und die Datei in
/// den Downloads-Ordner speichern. Logik aus <see cref="ServerConfigService"/>/<see cref="ConfigTemplateService"/> des Kerns.
/// </summary>
public sealed partial class ServerConfigsViewModel : ServerDataTabViewModel
{
    /// <summary>Größer als die API liest (512 KB) wird nichts übernommen.</summary>
    public const long MaxBytes = 512 * 1024;

    private readonly ServerConfigService _configs;
    private readonly ConfigTemplateService _templates;
    private string _original = string.Empty;
    private int _fileLoad;
    private bool _reverting;

    public ServerConfigsViewModel(AppServices services, string slug, ServerAbilities abilities, FeedbackViewModel? feedback = null)
        : base(services, slug, abilities, feedback)
    {
        _configs = new ServerConfigService(services.Api.Client, services.Api.Http);
        _templates = new ConfigTemplateService(services.Api.Http);
        var after = new List<ConfigAfterOption> { new(ConfigAfter.Save, L(StringKeys.ConfigAfterSave)) };
        if (abilities.ConsoleCommand)
        {
            after.Add(new(ConfigAfter.Exec, L(StringKeys.ConfigAfterExec)));
        }

        if (abilities.Power)
        {
            after.Add(new(ConfigAfter.Restart, L(StringKeys.ConfigAfterRestart)));
        }

        AfterOptions = after;
        SelectedAfter = after[0];
        Versions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowVersionsEmpty));
    }

    public ObservableCollection<ConfigFile> Files { get; } = [];

    public ObservableCollection<ConfigVersionItemViewModel> Versions { get; } = [];

    public ObservableCollection<DiffLineViewModel> DiffLines { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Was nach dem Speichern passiert – nur, was die Rechte erlauben (exec: Konsole, restart: Power).</summary>
    public IReadOnlyList<ConfigAfterOption> AfterOptions { get; }

    public bool CanWrite => Abilities.FilesWrite && HasScope("servers:files");

    public bool IsReadOnly => !CanWrite;

    [ObservableProperty]
    public partial ConfigAfterOption? SelectedAfter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(NeedsApproval), nameof(HasTemplate), nameof(ShowVersionsEmpty))]
    public partial ConfigFile? SelectedFile { get; set; }

    public bool HasFile => SelectedFile is not null;

    /// <summary>Speichern wird zum Antrag (Vier-Augen-Prinzip am Server oder an der Datei, ohne Freigaberecht).</summary>
    public bool NeedsApproval => CanWrite && (SelectedFile?.RequiresApproval == true || Abilities.ConfigApproval) && !Abilities.FilesApprove;

    public bool HasTemplate => SelectedFile?.HasTemplate == true && CanWrite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(LineNumbers))]
    public partial string Content { get; set; } = string.Empty;

    /// <summary>Zeilennummern passend zum Editor (eine je Zeile).</summary>
    public string LineNumbers
    {
        get
        {
            var count = 1 + Content.Count(c => c == '\n');
            var sb = new StringBuilder(count * 4);
            for (var i = 1; i <= count; i++)
            {
                sb.Append(i).Append('\n');
            }

            return sb.ToString(0, sb.Length - 1);
        }
    }

    /// <summary>Inhalt weicht vom Stand des Servers ab.</summary>
    public bool IsDirty => HasFile && !string.Equals(Content, _original, StringComparison.Ordinal);

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVersionsEmpty))]
    public partial bool IsFileLoading { get; set; }

    public bool ShowVersionsEmpty => HasFile && !IsFileLoading && Versions.Count == 0;

    /// <summary>Prüfung ist da (Diff oder „keine Änderungen“).</summary>
    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial bool IsUnchanged { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateOpen))]
    public partial ConfigTemplateFormViewModel? Template { get; set; }

    public bool IsTemplateOpen => Template is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedPath))]
    public partial string? SavedPath { get; set; }

    public bool HasSavedPath => SavedPath is not null;

    protected override bool HasData => Files.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var files = await _configs.ListAsync(Slug, cancellationToken);
        var keep = SelectedFile?.Id;
        Files.Clear();
        foreach (var f in files)
        {
            Files.Add(f);
        }

        var next = Files.FirstOrDefault(f => f.Id == keep) ?? Files.FirstOrDefault();
        if (next is null || next.Id != keep)
        {
            _reverting = true;
            SelectedFile = next;
            _reverting = false;
            await LoadFileAsync(next);
        }
        else
        {
            _reverting = true;
            SelectedFile = next; // gleiche Datei (neue Instanz) – Inhalt bleibt
            _reverting = false;
        }
    }

    partial void OnSelectedFileChanged(ConfigFile? oldValue, ConfigFile? newValue)
    {
        if (!_reverting)
        {
            _ = SwitchFileAsync(oldValue, newValue);
        }
    }

    partial void OnContentChanged(string value)
    {
        // Geänderter Text → alte Prüfung gilt nicht mehr
        if (HasDiff)
        {
            ClearPreview();
        }

        UpdateCommands();
    }

    private async Task SwitchFileAsync(ConfigFile? oldValue, ConfigFile? newValue)
    {
        if (oldValue is not null && !string.Equals(Content, _original, StringComparison.Ordinal)
            && !await AskAsync(L("Linux_ConfigDiscardTitle"), oldValue.DisplayName))
        {
            _reverting = true;
            SelectedFile = oldValue;
            _reverting = false;
            return;
        }

        await LoadFileAsync(newValue);
    }

    private async Task LoadFileAsync(ConfigFile? file)
    {
        var load = ++_fileLoad;
        Template = null;
        ClearPreview();
        Versions.Clear();
        _original = string.Empty;
        Content = string.Empty;
        if (file is null)
        {
            UpdateCommands();
            return;
        }

        IsFileLoading = true;
        try
        {
            string? content = null;
            await RunAsync(async () => content = await _configs.ReadAsync(Slug, file.Path));
            if (load != _fileLoad)
            {
                return;
            }

            _original = Normalize(content ?? string.Empty);
            Content = _original;
            OnPropertyChanged(nameof(IsDirty));
            IReadOnlyList<ConfigVersion> versions = [];
            await RunAsync(async () => versions = await _configs.VersionsAsync(Slug, file.Id));
            if (load != _fileLoad)
            {
                return;
            }

            foreach (var v in versions)
            {
                Versions.Add(new ConfigVersionItemViewModel(v, this, Services));
            }
        }
        finally
        {
            if (load == _fileLoad)
            {
                IsFileLoading = false;
                UpdateCommands();
            }
        }
    }

    private bool CanEdit() => CanWrite && HasFile && !IsFileLoading;

    private bool CanSave() => CanEdit() && IsDirty;

    /// <summary>Prüfen: Diff gegen den Stand des Servers und Validierung (ohne zu speichern).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task PreviewAsync() => SelectedFile is { } file ? RunAsync(() => PreviewCoreAsync(file)) : Task.CompletedTask;

    private async Task<ConfigPreview> PreviewCoreAsync(ConfigFile file)
    {
        var preview = await _configs.PreviewAsync(Slug, file.Id, Content);
        DiffLines.Clear();
        foreach (var line in preview.Changed ? preview.Diff : [])
        {
            DiffLines.Add(new DiffLineViewModel(line));
        }

        Errors.Clear();
        foreach (var e in preview.Errors)
        {
            Errors.Add(e);
        }

        IsUnchanged = !preview.Changed;
        HasErrors = Errors.Count > 0;
        HasDiff = true;
        return preview;
    }

    /// <summary>Speichern: erst prüfen und den Diff zeigen, dann im Inhalt bestätigen lassen.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        ConfigPreview? preview = null;
        if (!await RunAsync(async () => preview = await PreviewCoreAsync(file)) || preview is null)
        {
            return;
        }

        if (!preview.Changed)
        {
            Show(L(StringKeys.ConfigUnchanged), MessageTone.Info);
            return;
        }

        if (preview.Errors.Count > 0)
        {
            Show(L("Linux_ConfigHasErrors"), MessageTone.Error);
            return;
        }

        var after = SelectedAfter ?? AfterOptions[0];
        var text = Services.Format("Linux_ConfigSaveText", file.Path, after.Label);
        if (NeedsApproval)
        {
            text += "\n" + L("Linux_ConfigApprovalHint");
        }

        if (!await AskAsync(L("Linux_ConfigSaveTitle"), text))
        {
            return;
        }

        var outcome = ConfigSaveOutcome.Saved;
        if (await RunAsync(async () => outcome = await _configs.SaveAsync(Slug, file.Id, Content, after.Value, Note)))
        {
            Show(L(outcome == ConfigSaveOutcome.Pending ? StringKeys.ConfigPending : StringKeys.ConfigSaved), MessageTone.Success);
            Note = string.Empty;
            await LoadFileAsync(file);
        }
    }

    /// <summary>Datei neu vom Server laden (verwirft Änderungen nach Rückfrage).</summary>
    [RelayCommand]
    private async Task ReloadFileAsync()
    {
        if (SelectedFile is not { } file || (IsDirty && !await AskAsync(L("Linux_ConfigDiscardTitle"), file.DisplayName)))
        {
            return;
        }

        await LoadFileAsync(file);
    }

    internal async Task RollbackAsync(ConfigVersionItemViewModel version)
    {
        if (SelectedFile is not { } file || !CanWrite || !await AskAsync(L(StringKeys.ConfigRollbackTitle), version.Text))
        {
            return;
        }

        var after = (SelectedAfter ?? AfterOptions[0]).Value;
        if (await RunAsync(() => _configs.RollbackAsync(Slug, version.Id, after)))
        {
            Show(L(StringKeys.ConfigSaved), MessageTone.Success);
            await LoadFileAsync(file);
        }
    }

    /// <summary>
    /// Inhalt aus einer lokalen Datei übernehmen (Datei-Dialog des Systems). Gespeichert wird erst nach Prüfung und
    /// Bestätigung – wie beim Tippen im Editor. Nur UTF-8-Text bis 512 KB.
    /// </summary>
    public async Task ImportAsync(string name, long size, Func<Task<Stream>> open)
    {
        if (!CanEdit())
        {
            return;
        }

        if (size > MaxBytes)
        {
            Show(L("Linux_ConfigImportTooLarge"), MessageTone.Error);
            return;
        }

        try
        {
            await using var stream = await open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length > MaxBytes)
            {
                Show(L("Linux_ConfigImportTooLarge"), MessageTone.Error);
                return;
            }

            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (text.Contains('\0', StringComparison.Ordinal))
            {
                throw new DecoderFallbackException();
            }

            Content = Normalize(text.TrimStart('﻿'));
            Show(Services.Format("Linux_ConfigImported", name), MessageTone.Info);
        }
        catch (DecoderFallbackException)
        {
            Show(L("Linux_ConfigImportNotText"), MessageTone.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show(Services.Localizer.GetError(RoG.Core.Api.ApiProblem.Codes.Unknown), MessageTone.Error);
        }
    }

    /// <summary>Stand des Servers (nicht der bearbeitete Text) in den Downloads-Ordner speichern.</summary>
    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedFile is not { } file || IsFileLoading)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Directory.CreateDirectory(Services.DownloadDirectory);
            var path = FreePath(Services.DownloadDirectory, file.Path);
            await File.WriteAllTextAsync(path, _original, new UTF8Encoding(false));
            SavedPath = path;
            Show(Services.Format("Linux_ExportSaved", Path.GetFileName(path)), MessageTone.Success);
        });
    }

    [RelayCommand]
    private Task ShowInFolderAsync() => SavedPath is { } path && File.Exists(path) ? Services.FileManager.ShowAsync(path) : Task.CompletedTask;

    /// <summary>Formular-Vorlage der Datei öffnen (z. B. Servername, Slots, Map-Rotation).</summary>
    [RelayCommand]
    private async Task OpenTemplateAsync()
    {
        if (SelectedFile is not { HasTemplate: true } file || !CanWrite)
        {
            return;
        }

        ConfigTemplateForm? form = null;
        if (await RunAsync(async () => form = await _templates.GetAsync(Slug, file.Id)) && form is not null)
        {
            Template = new ConfigTemplateFormViewModel(form, this, Services);
        }
    }

    internal void CloseTemplate() => Template = null;

    internal async Task ApplyTemplateAsync(ConfigTemplateFormViewModel form)
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var (values, errors) = ConfigTemplateService.Build(form.Form.Fields, form.Inputs());
        form.MarkErrors(errors);
        if (errors.Count > 0)
        {
            var labels = form.Form.Fields.Where(f => errors.Contains(f.Name)).Select(f => f.Label);
            form.Error = Services.Format(StringKeys.TemplateInvalid, string.Join(", ", labels));
            return;
        }

        form.Error = null;
        form.IsBusy = true;
        var outcome = ConfigSaveOutcome.Saved;
        try
        {
            if (!await RunAsync(async () => outcome = await _templates.ApplyAsync(Slug, file.Id, values, (SelectedAfter ?? AfterOptions[0]).Value, form.Note)))
            {
                form.Error = Feedback.Message;
                return;
            }
        }
        finally
        {
            form.IsBusy = false;
        }

        Show(L(outcome == ConfigSaveOutcome.Pending ? StringKeys.ConfigPending : StringKeys.ConfigSaved), MessageTone.Success);
        await LoadFileAsync(file);
    }

    public override bool CloseOverlay()
    {
        if (base.CloseOverlay())
        {
            return true;
        }

        if (Template is not null)
        {
            Template = null;
            return true;
        }

        return false;
    }

    private void ClearPreview()
    {
        DiffLines.Clear();
        Errors.Clear();
        HasDiff = false;
        IsUnchanged = false;
        HasErrors = false;
    }

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(IsDirty));
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Zeilenenden wie der Server (\n) – sonst stimmen Diff und „geändert“ nicht.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

/// <summary>Auswahl „nach dem Speichern“.</summary>
public sealed record ConfigAfterOption(ConfigAfter Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Zeile im Diff: hinzugefügt (grün), entfernt (rot) oder Kontext, mit Zeilennummern alt/neu.</summary>
public sealed class DiffLineViewModel(DiffLine line)
{
    public bool IsAdded => line.Type == "add";

    public bool IsRemoved => line.Type is "remove" or "del" or "delete";

    public string Marker => IsAdded ? "+" : IsRemoved ? "−" : " ";

    public string Text => line.Text;

    public string OldNumber => line.Old?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string NewNumber => line.New?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string Display => line.Display;
}

public sealed partial class ConfigVersionItemViewModel(ConfigVersion version, ServerConfigsViewModel owner, AppServices services)
{
    public string Id => version.Id;

    public string Text { get; } = string.Join(" · ", new[]
    {
        version.CreatedAt?.ToLocalTime().ToString("g", services.Culture),
        version.Author,
        version.Note,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool CanWrite => owner.CanWrite;

    [RelayCommand]
    private Task RollbackAsync() => owner.RollbackAsync(this);
}

/// <summary>Formular einer Config-Datei (Felder je Typ wie <c>ConfigTemplateDialog</c> der Windows-App), im Inhalt.</summary>
public sealed partial class ConfigTemplateFormViewModel : ObservableObject
{
    private readonly ServerConfigsViewModel _owner;

    public ConfigTemplateFormViewModel(ConfigTemplateForm form, ServerConfigsViewModel owner, AppServices services)
    {
        Form = form;
        _owner = owner;
        Fields = form.Fields.Select(f => new TemplateFieldViewModel(f, form, services)).ToList();
    }

    public ConfigTemplateForm Form { get; }

    public string Title => Form.Label;

    public string? Description => Form.Description;

    public bool HasDescription => !string.IsNullOrEmpty(Form.Description);

    public IReadOnlyList<TemplateFieldViewModel> Fields { get; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    internal IReadOnlyDictionary<string, TemplateInput> Inputs() => Fields.ToDictionary(f => f.Name, f => f.ToInput(), StringComparer.Ordinal);

    internal void MarkErrors(IReadOnlyList<string> errors)
    {
        foreach (var f in Fields)
        {
            f.HasError = errors.Contains(f.Name);
        }
    }

    [RelayCommand]
    private Task ApplyAsync() => _owner.ApplyTemplateAsync(this);

    [RelayCommand]
    private void Cancel() => _owner.CloseTemplate();
}

public sealed partial class TemplateFieldViewModel : ObservableObject
{
    private readonly TemplateField _field;

    public TemplateFieldViewModel(TemplateField field, ConfigTemplateForm form, AppServices services)
    {
        _field = field;
        Text = field.Type is "password" ? string.Empty : form.TextOf(field.Name);
        Checked = form.BoolOf(field.Name);
        SelectedOption = field.Options.FirstOrDefault(o => o == form.TextOf(field.Name));
        CanClearPassword = field.Type == "password" && form.PasswordSet.Contains(field.Name);
        Placeholder = field.Type switch
        {
            "password" => services.T(CanClearPassword ? StringKeys.TemplatePasswordKeep : StringKeys.TemplatePasswordNone),
            "list" => services.T(StringKeys.TemplateListHint),
            _ => string.Empty,
        };
    }

    public string Name => _field.Name;

    public string Label => _field.Label;

    public bool IsBool => _field.Type == "bool";

    public bool IsSelect => _field.Type == "select";

    public bool IsList => _field.Type == "list";

    public bool IsPassword => _field.Type == "password";

    /// <summary>Einzeiliges Textfeld (Text und Zahl).</summary>
    public bool IsText => !IsBool && !IsSelect && !IsList && !IsPassword;

    public bool ShowLabel => !IsBool;

    public int MaxLength => _field.Max is { } max && _field.Type is "text" or "password" ? max : 0;

    public IReadOnlyList<string> Options => _field.Options;

    public string Placeholder { get; }

    public bool CanClearPassword { get; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool Checked { get; set; }

    [ObservableProperty]
    public partial string? SelectedOption { get; set; }

    [ObservableProperty]
    public partial bool ClearPassword { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    internal TemplateInput ToInput() => _field.Type switch
    {
        "select" => new TemplateInput(SelectedOption ?? string.Empty),
        "bool" => new TemplateInput(string.Empty, Checked: Checked),
        "password" => new TemplateInput(Text, ClearPassword: ClearPassword),
        _ => new TemplateInput(Text.Replace("\r", string.Empty, StringComparison.Ordinal)),
    };
}
