using System.Diagnostics;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Startet ein Hilfsprogramm ohne Shell (Argumente einzeln, keine Interpolation) – austauschbar für Tests.</summary>
public interface IProcessLauncher
{
    /// <returns><c>true</c>, wenn das Programm gestartet wurde und mit 0 endete (bzw. nach <paramref name="wait"/> noch läuft).</returns>
    Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan wait, CancellationToken cancellationToken = default);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public async Task<bool> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            // Ausgaben verwerfen, damit volle Puffer das Programm nicht blockieren
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(wait);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // xdg-open kann blockieren, bis der Browser beendet ist – gestartet ist gestartet
                return true;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // Programm fehlt
        }
    }
}
