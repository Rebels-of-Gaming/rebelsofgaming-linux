using System.Text;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Platform.Security;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

/// <summary>Stufe 7 (App-Sperre): PIN-Hash, Schlüsselbund-Eintrag, Fehlversuche mit Wartezeit, fprintd über D-Bus.</summary>
public class AppLockPlatformTests
{
    private sealed class ListProgress : IProgress<FingerprintPrompt>
    {
        private readonly List<FingerprintPrompt> _items = [];

        public IReadOnlyList<FingerprintPrompt> Items
        {
            get
            {
                lock (_items)
                {
                    return [.. _items];
                }
            }
        }

        public void Report(FingerprintPrompt value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("１２３４", false)] // Vollbreiten-Ziffern zählen nicht
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Pin_format_is_four_to_eight_ascii_digits(string? pin, bool valid) => Assert.Equal(valid, PinHash.IsValidFormat(pin));

    [Fact]
    public void Pin_is_stored_as_salted_pbkdf2_hash_and_verified()
    {
        var first = PinHash.Create("2580", iterations: 1_000);
        var second = PinHash.Create("2580", iterations: 1_000);
        var json = Encoding.UTF8.GetString(first);

        Assert.DoesNotContain("2580", json, StringComparison.Ordinal); // nie Klartext
        Assert.Contains("PBKDF2-SHA256", json, StringComparison.Ordinal);
        Assert.Contains("\"iterations\":1000", json, StringComparison.Ordinal);
        Assert.NotEqual(first, second); // eigenes Salz je Hash

        Assert.True(PinHash.Verify("2580", first));
        Assert.True(PinHash.Verify("2580", second));
        Assert.False(PinHash.Verify("2581", first));
        Assert.False(PinHash.Verify("", first));
        Assert.False(PinHash.Verify("2580", "kaputt"u8.ToArray()));
        Assert.False(PinHash.Verify("2580", Encoding.UTF8.GetBytes(json.Replace("PBKDF2-SHA256", "MD5", StringComparison.Ordinal))));
        Assert.Throws<ArgumentException>(() => PinHash.Create("12"));
        Assert.Equal(210_000, PinHash.DefaultIterations);
    }

    [Fact]
    public async Task Pin_store_uses_one_global_keyring_entry()
    {
        var backend = new FakeSecretBackend();
        var store = new AppLockPinStore(backend, iterations: 1_000);

        Assert.True(await store.IsAvailableAsync());
        Assert.False(await store.HasPinAsync());
        Assert.Equal(PinCheck.Missing, await store.VerifyAsync("2580"));

        await store.SetAsync("2580");
        var (label, secret) = Assert.Single(backend.Items).Value;
        Assert.Equal("Rebels of Gaming (App-Sperre)", label);
        Assert.Equal($"application={AppIdentity.AppId};kind=app-lock-pin", Assert.Single(backend.Items).Key); // ohne Umgebung
        Assert.DoesNotContain("2580", Encoding.UTF8.GetString(secret), StringComparison.Ordinal);
        Assert.Equal(PinCheck.Correct, await store.VerifyAsync("2580"));
        Assert.Equal(PinCheck.Wrong, await store.VerifyAsync("0000"));

        await store.SetAsync("13579");
        Assert.Single(backend.Items);
        Assert.Equal(PinCheck.Wrong, await store.VerifyAsync("2580"));
        Assert.Equal(PinCheck.Correct, await store.VerifyAsync("13579"));

        await store.ClearAsync();
        Assert.Empty(backend.Items);

        backend.Unavailable = true;
        Assert.False(await store.IsAvailableAsync());
        await Assert.ThrowsAsync<SecretStoreUnavailableException>(() => store.VerifyAsync("2580"));
    }

    [Fact]
    public void Failed_attempts_grow_the_wait_time_with_the_clock()
    {
        var clock = new TestClock();
        var throttle = new PinAttemptThrottle(() => clock.Now);

        for (var i = 1; i <= PinAttemptThrottle.FreeAttempts; i++)
        {
            Assert.Equal(TimeSpan.Zero, throttle.RegisterFailure());
            Assert.False(throttle.IsBlocked);
        }

        Assert.Equal(TimeSpan.FromSeconds(30), throttle.RegisterFailure()); // 5. Fehlversuch
        Assert.True(throttle.IsBlocked);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(10), throttle.Remaining);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(throttle.IsBlocked);

        Assert.Equal(TimeSpan.FromSeconds(60), throttle.RegisterFailure());
        Assert.Equal(TimeSpan.FromMinutes(2), PinAttemptThrottle.WaitAfter(7));
        Assert.Equal(TimeSpan.FromMinutes(8), PinAttemptThrottle.WaitAfter(9));
        Assert.Equal(TimeSpan.FromMinutes(15), PinAttemptThrottle.WaitAfter(10));
        Assert.Equal(TimeSpan.FromMinutes(15), PinAttemptThrottle.WaitAfter(500));

        // Neustart: gespeicherter Stand gilt weiter; zurückgestellte Uhr verlängert nie über das Maximum
        var restored = new PinAttemptThrottle(() => clock.Now);
        restored.Restore(throttle.Failures, throttle.BlockedUntil);
        Assert.Equal(6, restored.Failures);
        Assert.True(restored.IsBlocked);
        clock.Advance(TimeSpan.FromDays(-3));
        Assert.Equal(PinAttemptThrottle.MaxWait, restored.Remaining);

        restored.Reset();
        Assert.False(restored.IsBlocked);
        Assert.Equal(0, restored.Failures);
    }

    // ---- fprintd gegen den privaten Bus ----

    [Fact]
    [Trait("Category", "DBus")]
    public async Task Fprintd_is_available_only_with_a_device_and_an_enrolled_finger()
    {
        await using var bus = await PrivateBus.StartAsync();
        var verifier = new FprintdVerifier(bus.Address);
        Assert.False(await verifier.IsAvailableAsync()); // kein fprintd

        var fprintd = new FakeFprintd();
        await bus.ServeAsync("net.reactivated.Fprint", fprintd);
        Assert.True(await verifier.IsAvailableAsync());
        Assert.Contains("ListEnrolledFingers:", fprintd.Calls); // "" = angemeldete Person
        Assert.DoesNotContain(fprintd.Calls, c => c.StartsWith("Claim", StringComparison.Ordinal)); // Prüfen belegt das Gerät nicht

        fprintd.Fingers = [];
        Assert.False(await verifier.IsAvailableAsync());

        fprintd.HasDevice = false;
        Assert.False(await verifier.IsAvailableAsync());
    }

    [Fact]
    [Trait("Category", "DBus")]
    public async Task Fprintd_match_claims_verifies_stops_and_releases()
    {
        await using var bus = await PrivateBus.StartAsync();
        var fprintd = new FakeFprintd();
        fprintd.Statuses.Clear();
        fprintd.Statuses.Add(("verify-retry-scan", false));
        fprintd.Statuses.Add(("verify-match", true));
        await bus.ServeAsync("net.reactivated.Fprint", fprintd);
        var progress = new ListProgress();

        var result = await new FprintdVerifier(bus.Address).VerifyAsync(progress, TimeSpan.FromSeconds(10));

        Assert.Equal(FingerprintResult.Match, result);
        Assert.Equal(["GetDefaultDevice", "ListEnrolledFingers:", "Claim:", "VerifyStart:any", "VerifyStop", "Release"], fprintd.Calls);
        Assert.Equal([FingerprintPrompt.Scan, FingerprintPrompt.Retry], progress.Items.Order());
    }

    [Fact]
    [Trait("Category", "DBus")]
    public async Task Fprintd_no_match_timeout_cancel_and_busy_device()
    {
        await using var bus = await PrivateBus.StartAsync();
        var fprintd = new FakeFprintd();
        await bus.ServeAsync("net.reactivated.Fprint", fprintd);
        var verifier = new FprintdVerifier(bus.Address);

        fprintd.Statuses.Clear();
        fprintd.Statuses.Add(("verify-no-match", true));
        Assert.Equal(FingerprintResult.NoMatch, await verifier.VerifyAsync(null, TimeSpan.FromSeconds(10)));

        fprintd.Statuses.Clear();
        fprintd.Statuses.Add(("verify-disconnected", true));
        Assert.Equal(FingerprintResult.Failed, await verifier.VerifyAsync(null, TimeSpan.FromSeconds(10)));

        // Kein Finger aufgelegt → Zeitgrenze, Gerät trotzdem freigegeben
        fprintd.Statuses.Clear();
        fprintd.Calls.Clear();
        Assert.Equal(FingerprintResult.TimedOut, await verifier.VerifyAsync(null, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(["VerifyStop", "Release"], fprintd.Calls[^2..]);

        // Abbruch (PIN eingegeben, Abmelden)
        fprintd.Calls.Clear();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        Assert.Equal(FingerprintResult.Cancelled, await verifier.VerifyAsync(null, TimeSpan.FromSeconds(30), cts.Token));
        Assert.Equal(["VerifyStop", "Release"], fprintd.Calls[^2..]);

        // Anderes Programm (z. B. sudo mit pam_fprintd) hat das Gerät
        fprintd.InUse = true;
        fprintd.Calls.Clear();
        Assert.Equal(FingerprintResult.Unavailable, await verifier.VerifyAsync(null, TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain("Release", fprintd.Calls);

        fprintd.InUse = false;
        fprintd.Fingers = [];
        Assert.Equal(FingerprintResult.Unavailable, await verifier.VerifyAsync(null, TimeSpan.FromSeconds(10)));
    }
}
