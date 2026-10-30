using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Localization;
using RoG.Linux.App.Services;
using RoG.Linux.Platform;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Login wie in der Windows-App: „Im Browser anmelden“ (PKCE + Loopback, <see cref="AppLoginFlow"/> aus dem Kern,
/// System-Browser über xdg-open/Portal) oder „Ohne Anmeldung umsehen“ (Gastmodus).
/// </summary>
public sealed partial class LoginViewModel(AppServices services) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool IsIdle => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Kein Schlüsselbund: Hinweis, dass die Anmeldung nur bis zum Beenden gilt.</summary>
    public bool ShowKeyringWarning => !services.TokensPersistent;

    public string KeyringWarning => services.T("Linux_TokenNotPersistent");

    /// <summary>Anmeldung oder Gastmodus abgeschlossen – die Shell zeigt die Navigation.</summary>
    public event EventHandler? Completed;

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var http = services.Api.Http;
            var flow = new AppLoginFlow(services.Environment, http, services.Browser, services.Tokens);
            var scopes = await AppScopes.ResolveAsync(http, cts.Token);
            var deviceName = LinuxDeviceName.For(Environment.UserName, LinuxDeviceName.IsSteamDeck());
            await flow.LoginAsync(scopes, deviceName, services.LoopbackTexts(), cancellationToken: cts.Token);
            await services.UpdateSettingsAsync(s => s with { GuestMode = false });
            OnPropertyChanged(nameof(ShowKeyringWarning));
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // vom Nutzer abgebrochen – keine Meldung
        }
        catch (AppLoginException ex)
        {
            var own = services.Localizer.GetError(ex.Code);
            ErrorMessage = ex.Code == "access_denied" && !string.IsNullOrWhiteSpace(ex.Description) ? ex.Description : own;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = services.Localizer.GetError(ex.Problem.Code);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = services.Localizer.GetError("network_error");
        }
        finally
        {
            IsBusy = false;
            cts.Dispose();
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task BrowseAsGuestAsync()
    {
        await services.UpdateSettingsAsync(s => s with { GuestMode = true });
        Completed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
