using Avalonia.Threading;
using RoG.Core.Localization;
using RoG.Core.Offline;

namespace RoG.Linux.App.Services;

/// <summary>
/// Offline-Warteschlange abarbeiten (wie die Windows-App): nach dem Anmelden bzw. Start, nach einer Neuverbindung der
/// Echtzeit und jede Minute, solange etwas wartet. Endgültig abgelehnte Aktionen meldet eine Desktop-Benachrichtigung.
/// </summary>
public sealed class OutboxRunner : IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;

    public OutboxRunner(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) =>
        {
            if (services.Outbox.Count > 0)
            {
                _ = FlushAsync();
            }
        };
        services.Outbox.Failed += OnFailed;
    }

    /// <summary>Nach dem Anmelden: senden, was wartet, und den Takt starten.</summary>
    public Task StartAsync()
    {
        _timer.Start();
        return FlushAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task FlushAsync()
    {
        if (await _services.Tokens.GetAsync() is null)
        {
            return;
        }

        await _services.Outbox.FlushAsync();
    }

    private void OnFailed(object? sender, OutboxFailure failure) => Dispatcher.UIThread.Post(() =>
        _ = _services.Notifier.ShowAsync(
            _services.T(StringKeys.OutboxFailedTitle),
            $"{failure.Item.Label}: {_services.Localizer.GetError(failure.Problem.Code)}"));

    public void Dispose()
    {
        _timer.Stop();
        _services.Outbox.Failed -= OnFailed;
    }
}
