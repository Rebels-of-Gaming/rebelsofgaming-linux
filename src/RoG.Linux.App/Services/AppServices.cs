using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Localization;
using RoG.Core.Settings;
using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Platform.Localization;
using RoG.Linux.Platform.Secrets;

namespace RoG.Linux.App.Services;

/// <summary>Austauschbare Teile der Verdrahtung (Tests, Screenshots, später Flatpak-Besonderheiten).</summary>
public sealed record AppServicesOptions
{
    /// <summary>Ordner für <c>settings.json</c>; Standard XDG (<see cref="LinuxPaths"/>).</summary>
    public string? SettingsDirectory { get; init; }

    /// <summary>Token-Speicher; Standard Secret Service mit Rückfall „nur diese Sitzung“.</summary>
    public ITokenStore? Tokens { get; init; }

    /// <summary>Innerer HTTP-Handler der API (Tests: Fake-Server).</summary>
    public HttpMessageHandler? ApiHandler { get; init; }

    /// <summary>HTTP-Handler für Bilder vom CDN der Plattform.</summary>
    public HttpMessageHandler? ImageHandler { get; init; }

    public IBrowserLauncher? Browser { get; init; }

    public IDesktopNotifier? Notifier { get; init; }

    /// <summary>Umgebung erzwingen (Startargument, Tests); sonst Einstellungen.</summary>
    public string? Environment { get; init; }

    /// <summary>Sprache erzwingen (Tests); sonst Einstellungen bzw. Systemsprache.</summary>
    public string? Language { get; init; }
}

/// <summary>
/// Composition Root der Linux-App: Einstellungen (XDG), Sprache, Token im Schlüsselbund, API-Client aus RoG.Core,
/// Übersetzungen aus den .resw-Dateien der Windows-App. Eine Instanz je Prozess. Logik liegt im Kern.
/// </summary>
public sealed class AppServices : IDisposable
{
    private AppServices(
        ISettingsStore settingsStore,
        AppSettings settings,
        string language,
        RoGEnvironment environment,
        ITokenStore tokens,
        RoGApi api,
        ReswLocalizer localizer,
        IBrowserLauncher browser,
        IDesktopNotifier notifier,
        ImageLoader images)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        Language = language;
        Culture = CultureInfo.GetCultureInfo(LanguageResolver.ToResourceTag(language));
        Environment = environment;
        Tokens = tokens;
        Api = api;
        Localizer = localizer;
        Browser = browser;
        Notifier = notifier;
        Images = images;
        Account = new AccountService(api.Client, tokens);
        Devices = new LinuxDeviceRegistration(api.Client, tokens);
        api.Handler.Unauthorized += (_, _) => Unauthorized?.Invoke(this, EventArgs.Empty);
    }

    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    public ISettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Aktive Sprache: de, en oder fr.</summary>
    public string Language { get; }

    /// <summary>Kultur für Datum/Zahlen passend zur App-Sprache.</summary>
    public CultureInfo Culture { get; }

    public RoGEnvironment Environment { get; }

    public ITokenStore Tokens { get; }

    /// <summary>Wird der Token über das Beenden hinaus gespeichert? <c>false</c> = kein Schlüsselbund (Hinweis zeigen).</summary>
    public bool TokensPersistent => Tokens is not FallbackTokenStore fallback || fallback.IsPersistent;

    public RoGApi Api { get; }

    public ILocalizer Localizer { get; }

    public IBrowserLauncher Browser { get; }

    public IDesktopNotifier Notifier { get; }

    public ImageLoader Images { get; }

    public AccountService Account { get; }

    public LinuxDeviceRegistration Devices { get; }

    /// <summary>Angemeldete Person (<c>/v1/me</c>); <c>null</c> im Gastmodus oder solange noch nicht geladen.</summary>
    public UserProfile? CurrentUser
    {
        get;
        set
        {
            field = value;
            CurrentUserChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CurrentUserChanged;

    /// <summary>401 auf eine Anfrage mit Token – die Shell verwirft den Token und zeigt den Login.</summary>
    public event EventHandler? Unauthorized;

    /// <summary>„1.2.3“ aus der Assembly-Version.</summary>
    public static string AppVersion { get; } = ReadVersion();

    public string T(string key) => Localizer.GetString(key);

    public string Format(string key, params object?[] args) => string.Format(Culture, Localizer.GetString(key), args);

    public static async Task<AppServices> CreateAsync(AppServicesOptions? options = null)
    {
        options ??= new AppServicesOptions();
        var paths = new LinuxPaths();
        var settingsStore = new JsonFileSettingsStore(Path.Combine(options.SettingsDirectory ?? paths.ConfigDirectory, "settings.json"));
        var settings = await settingsStore.LoadAsync();

        var language = options.Language ?? LanguageResolver.Resolve(settings.Language, CultureInfo.CurrentUICulture);
        var environment = RoGEnvironment.Get(options.Environment ?? LaunchOptions.Current.Environment ?? settings.Environment);
        var tokens = options.Tokens ?? new FallbackTokenStore(new SecretServiceTokenStore(new SecretServiceBackend(), environment.Name));
        var apiContext = new ApiContext(language, AppVersion);

        // Antwort-Cache vorerst nur im Speicher: ein verschlüsselter Plattencache braucht einen Schlüssel aus dem
        // Schlüsselbund (Bauplan Stufe 4, Annahme L6) – DPAPI wie unter Windows gibt es unter Linux nicht.
        var api = new RoGApi(environment, tokens, apiContext, new InMemoryEtagCache(), options.ApiHandler);
        // RoGHttpHandler setzt „RoG-Windows“ nur, wenn noch kein User-Agent da ist – HttpClient überträgt diesen vorher
        api.Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.UserAgentProduct, AppVersion));

        var images = new ImageLoader(options.ImageHandler);
        return new AppServices(
            settingsStore,
            settings,
            language,
            environment,
            tokens,
            api,
            new ReswLocalizer(language),
            options.Browser ?? new XdgLauncher(),
            options.Notifier ?? new FreedesktopNotifier(),
            images);
    }

    /// <summary>Einstellung ändern und speichern – nacheinander, weil der Speicher über eine feste Temp-Datei schreibt.</summary>
    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _settingsGate.WaitAsync();
        try
        {
            Settings = change(Settings);
            await SettingsStore.SaveAsync(Settings);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    /// <summary>Texte für die Seite, die der Browser nach dem Login-Rücksprung zeigt.</summary>
    public LoopbackPageTexts LoopbackTexts() => new(
        Language,
        T(StringKeys.LoopbackTitle),
        T(StringKeys.LoopbackSuccess),
        T(StringKeys.LoopbackFailure));

    /// <summary>Antwort-Cache leeren (Abmelden: Inhalte gehören zum Konto).</summary>
    public void ClearCaches()
    {
        Api.Handler.ClearCache();
        Images.Clear();
    }

    public void Dispose()
    {
        Api.Dispose();
        Images.Dispose();
        _settingsGate.Dispose();
        (Notifier as IDisposable)?.Dispose();
        (Tokens as IDisposable)?.Dispose();
    }

    private static string ReadVersion()
    {
        var info = typeof(AppServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? info : info[..plus];
    }
}
