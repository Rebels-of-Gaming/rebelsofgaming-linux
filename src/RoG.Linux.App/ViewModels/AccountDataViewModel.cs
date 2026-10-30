using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Account;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Konto → Geräte &amp; Daten (wie <c>AccountDataViewModel</c> der Windows-App): angemeldete Geräte (dieses markiert,
/// abmelden), Datenexport anfordern und in den Downloads-Ordner laden, Konto löschen. Das Löschen ist zweistufig wie in
/// Windows (<see cref="AccountDeletionFlow"/>: aktive Mitglieder treten erst aus dem Clan aus, Bestätigung mit Passwort
/// bzw. Anzeigename) – als Bereich im Inhalt statt ContentDialog. Nach dem Löschen bzw. dem Abmelden dieses Geräts
/// meldet die App ab wie „Abmelden“.
/// </summary>
public sealed partial class AccountDataViewModel(AppServices services, Func<Task> signedOut) : LoadableViewModel(services)
{
    private readonly AccountDataService _data = new(services.Api.Client, services.Tokens);

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public ObservableCollection<ExportItemViewModel> Exports { get; } = [];

    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>Rückfrage „Dieses Gerät abmelden?“.</summary>
    public InlineConfirmViewModel Confirm { get; } = new();

    /// <summary>Hinweis im Löschen-Bereich (Fehler, Austritt nötig, ausgetreten …).</summary>
    public FeedbackViewModel DeleteHint { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoExports))]
    public partial bool HasExports { get; set; }

    public bool HasNoExports => !HasExports;

    [ObservableProperty]
    public partial bool IsWorking { get; set; }

    // --- Konto löschen --------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsDeleteOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteAccountCommand), nameof(LeaveClanCommand))]
    public partial string DeleteConfirmation { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLeave))]
    [NotifyCanExecuteChangedFor(nameof(DeleteAccountCommand), nameof(LeaveClanCommand))]
    public partial AccountDeletionFlow Flow { get; set; } = AccountDeletionFlow.Start;

    /// <summary>Austritt anbieten (409 <c>leave_clan_first</c>).</summary>
    public bool CanLeave => Flow.CanLeave;

    /// <summary>Austritt ist endgültig wie das Löschen: erster Klick fragt nach, zweiter tritt aus.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeaveLabel))]
    public partial bool LeaveArmed { get; set; }

    public string LeaveLabel => Services.T(LeaveArmed ? StringKeys.AccountDeleteLeaveConfirm : StringKeys.AccountDeleteLeaveButton);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteAccountCommand), nameof(LeaveClanCommand))]
    public partial bool IsDeleting { get; set; }

    /// <summary>Die Seite setzt den Fokus ins Bestätigungsfeld (Öffnen, leeres Feld).</summary>
    public event EventHandler? FocusConfirmationRequested;

    protected override bool HasData => Devices.Count > 0;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoading)
        {
            _ = LoadAsync(silent: HasData);
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var devices = await _data.GetDevicesAsync(cancellationToken);
        Devices.Clear();
        foreach (var d in devices)
        {
            Devices.Add(new DeviceItemViewModel(d, this, Services));
        }

        try
        {
            var exports = await _data.GetExportsAsync(cancellationToken);
            Exports.Clear();
            foreach (var e in exports)
            {
                Exports.Add(new ExportItemViewModel(e, this, Services));
            }

            HasExports = Exports.Count > 0;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Exporte sind Beiwerk
        }
    }

    internal async Task RemoveAsync(DeviceItemViewModel item)
    {
        // Das eigene Gerät abmelden heißt: App abgemeldet – deshalb nur hier nachfragen
        if (item.Device.Current && !await Confirm.AskAsync(
                Services.T("Linux_DeviceRemoveTitle"),
                Services.T("Linux_DeviceRemoveText"),
                Services.T("AccountPage_DeviceRemove.Content"),
                Services.T(StringKeys.CommonCancel)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _data.RemoveDeviceAsync(item.Device);
            if (item.Device.Current)
            {
                await signedOut();
                return;
            }

            Devices.Remove(item);
            Feedback.Show(Services.T(StringKeys.AccountDeviceRemoved), MessageTone.Success);
        });
    }

    [RelayCommand]
    private Task RequestExportAsync() => RunAsync(async () =>
    {
        await _data.RequestExportAsync();
        Feedback.Show(Services.T(StringKeys.AccountExportRequested), MessageTone.Success);
        await LoadAsync(silent: true);
    });

    /// <summary>Fertigen Export als ZIP in den Downloads-Ordner (XDG, wie die Downloads-Seite) laden.</summary>
    internal Task DownloadAsync(ExportItemViewModel item) => RunAsync(async () =>
    {
        item.IsBusy = true;
        try
        {
            item.SavedPath = await _data.DownloadExportAsync(item.Id, Services.DownloadDirectory);
            Feedback.Show(Services.Format("Linux_ExportSaved", Path.GetFileName(item.SavedPath)), MessageTone.Success);
        }
        finally
        {
            item.IsBusy = false;
        }
    });

    internal Task ShowInFolderAsync(string path) => Services.FileManager.ShowAsync(path);

    [RelayCommand]
    private void OpenDelete()
    {
        Flow = AccountDeletionFlow.Start;
        DeleteConfirmation = string.Empty;
        LeaveArmed = false;
        DeleteHint.Close();
        IsDeleteOpen = true;
        FocusConfirmationRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Abbrechen (auch Escape): Bereich schließen, Eingabe verwerfen.</summary>
    [RelayCommand]
    public void CancelDelete()
    {
        IsDeleteOpen = false;
        DeleteConfirmation = string.Empty;
        LeaveArmed = false;
        DeleteHint.Close();
    }

    private bool CanDeleteAccount() => Flow.CanDelete && !IsDeleting && !string.IsNullOrWhiteSpace(DeleteConfirmation);

    [RelayCommand(CanExecute = nameof(CanDeleteAccount))]
    private async Task DeleteAccountAsync()
    {
        IsDeleting = true;
        try
        {
            var next = await DeleteAccountAsync(Flow, DeleteConfirmation);
            if (next is null)
            {
                IsDeleteOpen = false;
                DeleteConfirmation = string.Empty;
                await signedOut();
                return;
            }

            Flow = next;
            LeaveArmed = false;
            RenderHint();
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private bool CanLeaveClan() => Flow.CanLeave && !IsDeleting;

    [RelayCommand(CanExecute = nameof(CanLeaveClan))]
    private async Task LeaveClanAsync()
    {
        if (string.IsNullOrWhiteSpace(DeleteConfirmation))
        {
            FocusConfirmationRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!LeaveArmed)
        {
            LeaveArmed = true;
            return;
        }

        IsDeleting = true;
        try
        {
            Flow = await LeaveClanAsync(Flow, DeleteConfirmation);
            LeaveArmed = false;
            RenderHint();
        }
        finally
        {
            IsDeleting = false;
        }
    }

    /// <summary>
    /// Konto löschen (<c>DELETE /v1/me</c>). Rückgabe = neuer Zustand, <c>null</c> = gelöscht (Token ist dann schon weg).
    /// Aktive Mitglieder bekommen <c>leave_clan_first</c> → Austritt anbieten.
    /// </summary>
    public async Task<AccountDeletionFlow?> DeleteAccountAsync(AccountDeletionFlow flow, string confirmation)
    {
        ArgumentNullException.ThrowIfNull(flow);
        try
        {
            await _data.DeleteAccountAsync(confirmation);
            return null;
        }
        catch (ApiProblemException ex)
        {
            return flow.AfterDeleteFailed(ex.Problem);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            return flow with { ErrorCode = ApiProblem.Codes.Unknown, FieldError = null, RetryAfter = null };
        }
    }

    /// <summary>Aus dem Clan austreten (gleiche Bestätigung wie das Löschen) – danach ist das Löschen frei.</summary>
    public async Task<AccountDeletionFlow> LeaveClanAsync(AccountDeletionFlow flow, string confirmation)
    {
        ArgumentNullException.ThrowIfNull(flow);
        try
        {
            await _data.LeaveClanAsync(confirmation);
            return AccountDeletionFlow.AfterLeft();
        }
        catch (ApiProblemException ex)
        {
            return flow.AfterLeaveFailed(ex.Problem);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            return flow with { ErrorCode = ApiProblem.Codes.Unknown, FieldError = null, RetryAfter = null };
        }
    }

    /// <summary>Hinweis zum Zustand: Fehler zuerst, sonst der Text zum Zustand (Austritt nötig, ausgetreten …).</summary>
    public (string? Message, MessageTone Tone) DeletionHint(AccountDeletionFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var l = Services.Localizer;
        if (flow.HasError && flow.Stage != AccountDeletionStage.Blocked)
        {
            var message = flow.FieldError ?? l.GetError(flow.ErrorCode!);
            if (flow.RetryAfter is { } wait)
            {
                message += " " + Services.Format(StringKeys.CommonRetryAfter, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
            }

            return (message, MessageTone.Error);
        }

        return flow.Stage switch
        {
            AccountDeletionStage.LeaveRequired => (l.GetString(StringKeys.AccountDeleteLeaveText), MessageTone.Info),
            AccountDeletionStage.LeaveUnavailable => (l.GetString(StringKeys.AccountDeleteLeaveUnavailable), MessageTone.Info),
            AccountDeletionStage.Left => (l.GetString(StringKeys.AccountDeleteLeft), MessageTone.Success),
            AccountDeletionStage.Blocked => (l.GetError(flow.ErrorCode ?? "owner_cannot_delete"), MessageTone.Error),
            _ => (null, MessageTone.Info),
        };
    }

    private void RenderHint()
    {
        var (message, tone) = DeletionHint(Flow);
        if (message is null)
        {
            DeleteHint.Close();
        }
        else
        {
            DeleteHint.Show(message, tone);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsWorking = true;
        Feedback.Close();
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Feedback.Show(Services.Localizer.GetError(ex.Problem.Code), MessageTone.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            Feedback.Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), MessageTone.Error);
        }
        finally
        {
            IsWorking = false;
        }
    }
}

public sealed partial class DeviceItemViewModel(DeviceInfo device, AccountDataViewModel owner, AppServices services)
{
    public DeviceInfo Device => device;

    public string Name => device.Name;

    public bool IsCurrent => device.Current;

    public string ThisDevice { get; } = services.T(StringKeys.AccountThisDevice);

    public string Meta { get; } = string.Join(" · ", new[]
    {
        device.Platform,
        device.AppVersion is null ? null : "v" + device.AppVersion,
        device.LastActiveAt?.ToLocalTime().ToString("g", services.Culture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

public sealed partial class ExportItemViewModel(DataExport export, AccountDataViewModel owner, AppServices services) : ObservableObject
{
    public string Id => export.Id;

    public string Text { get; } = string.Join(" · ", new[]
    {
        export.CreatedAt.ToLocalTime().ToString("g", services.Culture),
        export.IsReady ? services.T(StringKeys.AccountExportReady) : services.T(StringKeys.AccountExportPending),
        export.Size is { } s ? (s / 1048576.0).ToString("0.0", services.Culture) + " MB" : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public bool IsReady => export.IsReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    public partial string? SavedPath { get; set; }

    public bool IsSaved => SavedPath is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanDownload() => IsReady && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => owner.DownloadAsync(this);

    [RelayCommand]
    private Task ShowInFolderAsync() => SavedPath is { } p ? owner.ShowInFolderAsync(p) : Task.CompletedTask;
}
