using RoG.Linux.Platform.Security;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Tests.Support;

/// <summary>Verstellbare Uhr für die App-Sperre (Abwesenheit, Wartezeit nach Fehlversuchen).</summary>
public sealed class TestClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 20, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Fingerabdruck ohne fprintd: Verfügbarkeit und Ergebnis vorgeben, Aufrufe zählen.</summary>
public sealed class FakeFingerprint : IFingerprintVerifier
{
    public bool Available { get; set; }

    public FingerprintResult Result { get; set; } = FingerprintResult.Match;

    /// <summary>Vor dem Ergebnis gemeldete Zwischenstände.</summary>
    public List<FingerprintPrompt> Prompts { get; } = [FingerprintPrompt.Scan];

    public int Verifications { get; private set; }

    /// <summary>Auf Abbruch warten statt sofort zu antworten.</summary>
    public bool WaitForCancel { get; set; }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);

    public async Task<FingerprintResult> VerifyAsync(IProgress<FingerprintPrompt>? progress, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Verifications++;
        foreach (var prompt in Prompts)
        {
            progress?.Report(prompt);
        }

        if (WaitForCancel)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return FingerprintResult.Cancelled;
            }
        }

        return Available ? Result : FingerprintResult.Unavailable;
    }
}
