using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Datei im Dateimanager zeigen („Im Ordner zeigen“ nach einem Download).</summary>
public interface IFileManager
{
    Task<bool> ShowAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>org.freedesktop.FileManager1.ShowItems</c> (Nautilus, Dolphin, Thunar, Nemo markieren die Datei); ohne diesen
/// Dienst öffnet <c>xdg-open</c> den Ordner.
/// </summary>
public sealed class FreedesktopFileManager(string? address = null, IProcessLauncher? processes = null) : IFileManager
{
    private readonly string? _address = address ?? DBusAddress.Session;
    private readonly IProcessLauncher _processes = processes ?? new ProcessLauncher();

    public async Task<bool> ShowAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!string.IsNullOrEmpty(_address))
        {
            try
            {
                using var connection = new DBusConnection(_address);
                await connection.ConnectAsync().ConfigureAwait(false);
                var writer = connection.GetMessageWriter();
                MessageBuffer message;
                try
                {
                    writer.WriteMethodCallHeader("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1", "ShowItems", "ass", MessageFlags.None);
                    writer.WriteArray(new[] { new Uri(path).AbsoluteUri });
                    writer.WriteString(string.Empty);
                    message = writer.CreateMessage();
                }
                finally
                {
                    writer.Dispose();
                }

                await connection.CallMethodAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is DBusExceptionBase or IOException)
            {
                // kein Dateimanager-Dienst – Ordner öffnen
            }
        }

        var folder = Path.GetDirectoryName(path);
        return folder is not null && await _processes.RunAsync("xdg-open", [folder], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }
}
