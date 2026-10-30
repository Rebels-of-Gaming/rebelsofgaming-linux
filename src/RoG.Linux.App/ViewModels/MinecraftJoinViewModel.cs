using System.Globalization;
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
/// Server → Minecraft (wie <c>MinecraftJoinViewModel</c> der Windows-App): „So kommst du auf den Server“ für Java,
/// Bedrock und Konsole mit Kopier-Knöpfen, Ressourcenpaket zum Herunterladen – auch im Gastmodus. Mit Recht
/// <c>minecraft.manage</c> und Scope <c>minecraft:manage</c>: Beitrittsdaten bearbeiten (Formular lädt erst beim
/// Aufklappen) und Ressourcenpaket hochladen (.zip bis 50 MB), SHA-1 kopieren, löschen (Website v0.43.0/v0.44.0).
/// </summary>
public sealed partial class MinecraftJoinViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;
    private readonly FeedbackViewModel _feedback;
    private bool _editLoaded;
    private bool _hintsKnown; // Hinweise nur senden, wenn sie gelesen wurden – sonst würden leere Felder sie löschen

    /// <param name="confirm">Rückfrage (Paket löschen); ohne eine eigene, die die Ansicht zeigt.</param>
    public MinecraftJoinViewModel(AppServices services, FeedbackViewModel feedback, InlineConfirmViewModel? confirm = null)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        _feedback = feedback;
        Confirm = confirm ?? new InlineConfirmViewModel();
    }

    public InlineConfirmViewModel Confirm { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaAddress), nameof(BedrockAddress), nameof(BedrockPort), nameof(ShowConsole), nameof(Gamertag),
        nameof(HasHint), nameof(Hint), nameof(ShowDefaultPortNote))]
    public partial MinecraftJoin? Join { get; set; }

    // --- Ressourcenpaket (Website v0.44.0) ---------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPack), nameof(PackInfo), nameof(PackSha1), nameof(ShowPackCard), nameof(CanDeletePack))]
    [NotifyCanExecuteChangedFor(nameof(DeletePackCommand))]
    public partial MinecraftResourcePack? Pack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeletePack))]
    [NotifyCanExecuteChangedFor(nameof(DeletePackCommand))]
    public partial bool IsPackBusy { get; set; }

    public bool HasPack => Pack is not null;

    /// <summary>Karte zeigen, wenn es ein Paket gibt – Verwaltende sehen sie immer (zum Hochladen).</summary>
    public bool ShowPackCard => HasPack || CanEdit;

    public string PackInfo => Pack is { } p
        ? Services.Format(
            p.UpdatedAt is null ? StringKeys.ResourcePackInfoNoDate : StringKeys.ResourcePackInfo,
            MinecraftMapGeometry.FormatBytes(p.Size),
            p.UpdatedAt?.ToLocalTime().ToString("g", Services.Culture) ?? string.Empty)
        : Services.T(StringKeys.ResourcePackNone);

    public string PackSha1 => Pack?.Sha1 ?? string.Empty;

    public bool CanDeletePack => CanEdit && HasPack && !IsPackBusy;

    public string JavaAddress => Join?.JavaText ?? string.Empty;

    public string BedrockAddress => Join?.Bedrock.Address ?? string.Empty;

    public string BedrockPort => Join?.Bedrock.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public bool ShowConsole => Join?.ConsoleEnabled == true && !string.IsNullOrEmpty(Join.Gamertag);

    public string Gamertag => Join?.Gamertag ?? string.Empty;

    public bool HasHint => ShowConsole && !string.IsNullOrEmpty(Join?.Hint);

    public string Hint => Join?.Hint ?? string.Empty;

    /// <summary>Hinweis „25565 ist Standard“ nur, wenn der Standard-Port gilt (dann steht kein Port in der Adresse).</summary>
    public bool ShowDefaultPortNote => Join is { } j && !MinecraftService.ShowJavaPort(j.Java.Port);

    /// <summary>„Konto verknüpfen“ nur angemeldet (im Gastmodus gibt es kein Konto).</summary>
    public bool IsSignedIn => Services.CurrentUser is not null;

    /// <summary>Beitrittsdaten bearbeiten und Paket verwalten: Recht <c>minecraft.manage</c> + Scope <c>minecraft:manage</c>.</summary>
    public bool CanEdit => Services.CurrentUser is { } me && me.Can(MinecraftService.ManagePermission) && me.HasScope(MinecraftService.ManageScope);

    // --- Formular ---------------------------------------------------------------------------------------------------

    /// <summary>Formular „Beitrittsdaten bearbeiten“ aufgeklappt – lädt beim ersten Aufklappen.</summary>
    [ObservableProperty]
    public partial bool IsEditExpanded { get; set; }

    partial void OnIsEditExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadEditAsync();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsEditLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    /// <summary>Speichern erst, wenn das Formular mit den gespeicherten Werten gefüllt ist.</summary>
    public bool CanSave => _editLoaded && !IsEditLoading && !IsSaving;

    [ObservableProperty]
    public partial string EditJavaAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? EditJavaPort { get; set; } = MinecraftService.DefaultJavaPort;

    [ObservableProperty]
    public partial string EditBedrockAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? EditBedrockPort { get; set; } = MinecraftService.DefaultBedrockPort;

    [ObservableProperty]
    public partial bool EditConsoleEnabled { get; set; }

    [ObservableProperty]
    public partial string EditGamertag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintDe { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintFr { get; set; } = string.Empty;

    protected override bool HasData => Join is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var pack = LoadPackAsync(cancellationToken);
        try
        {
            Join = await _minecraft.JoinAsync(cancellationToken) ?? throw new ApiProblemException();
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ApiProblemException();
        }

        await pack;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowPackCard));
        OnPropertyChanged(nameof(CanDeletePack));
    }

    private async Task LoadPackAsync(CancellationToken cancellationToken)
    {
        try
        {
            Pack = await _minecraft.ResourcePackAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException)
        {
            Pack = null; // Paket ist Beiwerk – die Anleitung bleibt nutzbar
        }
    }

    /// <summary>Formular füllen (einmal, beim Aufklappen) – mit den Hinweisen aller Sprachen.</summary>
    public async Task LoadEditAsync()
    {
        if (!CanEdit || _editLoaded || IsEditLoading)
        {
            return;
        }

        IsEditLoading = true;
        try
        {
            if (await _minecraft.JoinForEditAsync() is { } join)
            {
                Join = join;
                Fill(join);
                _editLoaded = true;
            }
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsEditLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanEdit || !_editLoaded)
        {
            return;
        }

        if (Invalid() is { } invalid)
        {
            _feedback.Show(Services.T(invalid), MessageTone.Info);
            return;
        }

        IsSaving = true;
        try
        {
            var update = new MinecraftJoinUpdate(
                EditJavaAddress,
                (int)EditJavaPort!.Value,
                EditBedrockAddress,
                (int)EditBedrockPort!.Value,
                EditConsoleEnabled,
                EditConsoleEnabled || !string.IsNullOrWhiteSpace(EditGamertag) ? EditGamertag : null,
                _hintsKnown ? new Dictionary<string, string?> { ["de"] = EditHintDe, ["en"] = EditHintEn, ["fr"] = EditHintFr } : null);
            if (await _minecraft.UpdateJoinAsync(update) is { } join)
            {
                Join = join;
                Fill(join);
            }

            _feedback.Show(Services.T(StringKeys.MinecraftJoinSaved), MessageTone.Success);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void CopyPackSha1() => Copy(PackSha1);

    /// <summary>Neues Paket hochladen (Datei aus dem Dateidialog/Portal der Ansicht); prüft Endung und Größe vorab.</summary>
    public async Task UploadPackAsync(UploadSource file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!CanEdit || IsPackBusy)
        {
            return;
        }

        if (!MinecraftService.IsResourcePackName(file.Name) || file.Size <= 0 || file.Size > MinecraftService.ResourcePackMaxBytes)
        {
            _feedback.Show(Services.T(StringKeys.ResourcePackInvalid), MessageTone.Info);
            return;
        }

        IsPackBusy = true;
        try
        {
            await using var content = await file.OpenAsync();
            Pack = await _minecraft.UploadResourcePackAsync(content, file.Name) ?? await _minecraft.ResourcePackAsync();
            _feedback.Show(Services.T(StringKeys.ResourcePackUploaded), MessageTone.Success);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex) || ex is UnauthorizedAccessException)
        {
            ShowError(ex);
        }
        finally
        {
            IsPackBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeletePack))]
    private async Task DeletePackAsync()
    {
        if (!await Confirm.AskAsync(Services.T(StringKeys.ResourcePackDeleteTitle), Services.T(StringKeys.ResourcePackDeleteText),
                Services.T(StringKeys.CommonConfirm), Services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        IsPackBusy = true;
        try
        {
            await _minecraft.DeleteResourcePackAsync();
            Pack = null;
            _feedback.Show(Services.T(StringKeys.ResourcePackDeleted), MessageTone.Success);
        }
        catch (Exception ex) when (MinecraftAdminTabBase.IsExpected(ex))
        {
            ShowError(ex);
        }
        finally
        {
            IsPackBusy = false;
        }
    }

    /// <summary>Escape: offene Rückfrage schließen. <c>true</c> = etwas geschlossen.</summary>
    public bool CloseOverlay()
    {
        if (Confirm.IsOpen)
        {
            Confirm.Cancel();
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void CopyJava() => Copy(JavaAddress);

    [RelayCommand]
    private void CopyBedrockAddress() => Copy(BedrockAddress);

    [RelayCommand]
    private void CopyBedrockPort() => Copy(BedrockPort);

    [RelayCommand]
    private void CopyGamertag() => Copy(Gamertag);

    [RelayCommand]
    private Task DownloadPackAsync() => Pack is { } p ? Services.Browser.OpenAsync(p.Url) : Task.CompletedTask;

    [RelayCommand]
    private void OpenMap() => Services.Navigate(new AppRoute(AppSection.MinecraftMap));

    [RelayCommand]
    private void OpenLeaderboards() => Services.Navigate(new AppRoute(AppSection.Leaderboards));

    /// <summary>Minecraft-Konto verknüpfen – bis die Kontoseite es nativ kann, auf der Website (Konto → Minecraft).</summary>
    [RelayCommand]
    private Task LinkAccountAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "account/minecraft"));

    private string? Invalid()
    {
        if (!MinecraftService.IsValidAddress(EditJavaAddress) || !MinecraftService.IsValidAddress(EditBedrockAddress))
        {
            return StringKeys.MinecraftJoinInvalidAddress;
        }

        if (EditJavaPort is not { } java || EditBedrockPort is not { } bedrock
            || java != decimal.Truncate(java) || bedrock != decimal.Truncate(bedrock)
            || !MinecraftService.IsValidPort(java is >= 1 and <= 65535 ? (int)java : 0)
            || !MinecraftService.IsValidPort(bedrock is >= 1 and <= 65535 ? (int)bedrock : 0))
        {
            return StringKeys.MinecraftJoinInvalidPort;
        }

        return (EditConsoleEnabled || !string.IsNullOrWhiteSpace(EditGamertag)) && !MinecraftService.IsValidGamertag(EditGamertag)
            ? StringKeys.MinecraftJoinInvalidGamertag
            : null;
    }

    private void Fill(MinecraftJoin join)
    {
        EditJavaAddress = join.Java.Address;
        EditJavaPort = join.Java.Port;
        EditBedrockAddress = join.Bedrock.Address;
        EditBedrockPort = join.Bedrock.Port;
        EditConsoleEnabled = join.ConsoleEnabled;
        // Bei ausgeblendeter Konsole liefert die API keinen Gamertag – Feld bleibt dann wie zuletzt
        if (join.Gamertag is { } gamertag)
        {
            EditGamertag = gamertag;
        }

        if (join.Hints is { } hints)
        {
            _hintsKnown = true;
            EditHintDe = hints.GetValueOrDefault("de") ?? string.Empty;
            EditHintEn = hints.GetValueOrDefault("en") ?? string.Empty;
            EditHintFr = hints.GetValueOrDefault("fr") ?? string.Empty;
        }

        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ShowError(Exception ex) => _feedback.Show(MinecraftAdminTabBase.ErrorText(Services, ex), MessageTone.Error);

    private void Copy(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var ok = Services.ServerConnector.Copy(text);
        _feedback.Show(Services.Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, text), ok ? MessageTone.Success : MessageTone.Error);
    }
}
