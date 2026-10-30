using RoG.Core.Auth;
using RoG.Linux.App.Services;
using RoG.Linux.Platform.Secrets;

namespace RoG.Linux.Tests.Support;

/// <summary>App-Verdrahtung mit Fakes: Einstellungen im Temp-Ordner, Schlüsselbund im Speicher, Fake-API und -Bilder.</summary>
public sealed class TestServices : IDisposable
{
    private TestServices(AppServices services, FakeApi api, FakeSecretBackend secrets, RecordingBrowser browser, string folder)
    {
        Services = services;
        Api = api;
        Secrets = secrets;
        Browser = browser;
        Folder = folder;
    }

    public AppServices Services { get; }

    public FakeApi Api { get; }

    public FakeSecretBackend Secrets { get; }

    public RecordingBrowser Browser { get; }

    public string Folder { get; }

    public static async Task<TestServices> CreateAsync(StoredToken? token = null, bool guest = false, string language = "de", bool keyring = true)
    {
        var folder = Path.Combine(Path.GetTempPath(), "rog-linux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var secrets = new FakeSecretBackend { Unavailable = !keyring };
        var tokens = new FallbackTokenStore(new SecretServiceTokenStore(secrets, "production"));
        if (token is not null)
        {
            await tokens.SetAsync(token);
        }

        var api = new FakeApi();
        var browser = new RecordingBrowser();
        var services = await AppServices.CreateAsync(new AppServicesOptions
        {
            SettingsDirectory = folder,
            Tokens = tokens,
            ApiHandler = api,
            ImageHandler = new FakeImages(),
            Browser = browser,
            Notifier = new SilentNotifier(),
            Environment = "production",
            Language = language,
        });
        if (guest)
        {
            await services.UpdateSettingsAsync(s => s with { GuestMode = true });
        }

        return new TestServices(services, api, secrets, browser, folder);
    }

    public void Dispose()
    {
        Services.Dispose();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp-Ordner – egal
        }
    }
}
