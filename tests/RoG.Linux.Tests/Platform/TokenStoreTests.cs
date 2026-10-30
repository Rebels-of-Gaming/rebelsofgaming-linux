using System.Text;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.Platform;

public class TokenStoreTests
{
    [Fact]
    public async Task Token_round_trips_through_the_keyring()
    {
        var backend = new FakeSecretBackend();
        var store = new SecretServiceTokenStore(backend, "staging");

        await store.SetAsync(TestTokens.Wahke);
        var token = await store.GetAsync();

        Assert.NotNull(token);
        Assert.Equal("test-token", token.AccessToken);
        Assert.Equal("Wahke", token.UserName);
        Assert.Equal(TestTokens.Wahke.Scopes, token.Scopes);
        var (label, _) = Assert.Single(backend.Items.Values);
        Assert.Equal("Rebels of Gaming (Wahke)", label);
    }

    [Fact]
    public async Task Entries_are_separated_per_environment()
    {
        var backend = new FakeSecretBackend();
        await new SecretServiceTokenStore(backend, "staging").SetAsync(TestTokens.Wahke);

        Assert.Null(await new SecretServiceTokenStore(backend, "production").GetAsync());
        Assert.Equal(AppIdentity.AppId, new SecretServiceTokenStore(backend, "production").Attributes["application"]);
    }

    [Fact]
    public async Task Clear_removes_the_entry()
    {
        var backend = new FakeSecretBackend();
        var store = new SecretServiceTokenStore(backend, "production");
        await store.SetAsync(TestTokens.Wahke);
        await store.ClearAsync();
        Assert.Null(await store.GetAsync());
        Assert.Empty(backend.Items);
    }

    [Fact]
    public async Task Corrupted_entry_counts_as_signed_out()
    {
        var backend = new FakeSecretBackend();
        var store = new SecretServiceTokenStore(backend, "production");
        await backend.StoreAsync("x", store.Attributes, Encoding.UTF8.GetBytes("kein json"));
        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public void Token_is_never_printed() => Assert.DoesNotContain("test-token", TestTokens.Wahke.ToString(), StringComparison.Ordinal);

    [Fact]
    public async Task Missing_keyring_degrades_to_session_only_storage()
    {
        var backend = new FakeSecretBackend { Unavailable = true };
        var store = new FallbackTokenStore(new SecretServiceTokenStore(backend, "production"));
        var degraded = 0;
        store.Degraded += (_, _) => degraded++;

        await store.SetAsync(TestTokens.Wahke);

        Assert.False(store.IsPersistent);
        Assert.Equal(1, degraded);
        Assert.Equal("test-token", (await store.GetAsync())?.AccessToken);
        await store.ClearAsync();
        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public async Task Working_keyring_stays_persistent()
    {
        var backend = new FakeSecretBackend();
        var store = new FallbackTokenStore(new SecretServiceTokenStore(backend, "production"));
        await store.SetAsync(TestTokens.Wahke);
        Assert.True(store.IsPersistent);
        Assert.Single(backend.Items);
    }
}
