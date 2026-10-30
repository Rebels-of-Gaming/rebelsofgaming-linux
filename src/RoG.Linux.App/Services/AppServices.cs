using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Localization;
using RoG.Core.Navigation;
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

    /// <summary>HTTP-Handler für Uploads in den Speicher der Plattform (vorab signierte Adressen, ohne Token).</summary>
    public HttpMessageHandler? StorageHandler { get; init; }

    /// <summary>HTTP-Handler für Bilder vom CDN der Plattform.</summary>
    public HttpMessageHandler? ImageHandler { get; init; }

    public IBrowserLauncher? Browser { get; init; }

    public IDesktopNotifier? Notifier { get; init; }

    /// <summary>
    /// Verschlüsselung für Plattencaches (Antworten, Warteschlange). Standard: Schlüssel aus dem Schlüsselbund; ohne
    /// Schlüsselbund bzw. mit <see cref="MemoryOnly"/> bleibt alles im Speicher.
    /// </summary>
    public RoG.Core.Security.IPayloadProtector? Protector { get; init; }

    /// <summary>Nichts auf die Platte schreiben (Tests ohne eigenen Schutz).</summary>
    public bool MemoryOnly { get; init; }

    /// <summary>Ordner für Caches/Warteschlange; Standard XDG (<see cref="LinuxPaths"/>).</summary>
    public string? CacheDirectory { get; init; }

    public string? DataDirectory { get; init; }

    /// <summary>„Beim Anmelden starten“; Standard: Background-Portal in Flatpak, sonst XDG-Autostart-Datei.</summary>
    public IAutostart? Autostart { get; init; }

    /// <summary>Spiele-Links (<c>steam://</c>, <c>et://</c> …); Standard <c>xdg-open</c>.</summary>
    public RoG.Core.Features.Servers.IExternalLauncher? Launcher { get; init; }

    /// <summary>Zwischenablage; Standard die des Hauptfensters.</summary>
    public RoG.Core.Features.Servers.IClipboardService? Clipboard { get; init; }

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
        LinuxSettingsStore linuxStore,
        string language,
        RoGEnvironment environment,
        ITokenStore tokens,
        RoGApi api,
        ReswLocalizer localizer,
        IBrowserLauncher browser,
        IDesktopNotifier notifier,
        ImageLoader images,
        RoG.Core.Offline.Outbox outbox,
        IAutostart autostart,
        RoG.Core.Features.Servers.IExternalLauncher launcher,
        RoG.Core.Features.Servers.ServerConnector serverConnector)
    {
        Launcher = launcher;
        Autostart = autostart;
        Outbox = outbox;
        SettingsStore = settingsStore;
        Settings = settings;
        _linuxStore = linuxStore;
        LinuxSettings = linuxStore.Load();
        Language = language;
        Culture = CultureInfo.GetCultureInfo(LanguageResolver.ToResourceTag(language));
        Environment = environment;
        Tokens = tokens;
        Api = api;
        Localizer = localizer;
        Browser = browser;
        Notifier = notifier;
        Images = images;
        ServerConnector = serverConnector;
        Account = new AccountService(api.Client, tokens);
        Devices = new LinuxDeviceRegistration(api.Client, tokens);
        api.Handler.Unauthorized += (_, _) => Unauthorized?.Invoke(this, EventArgs.Empty);
        Realtime = new RealtimeService(this);
        OutboxRunner = new OutboxRunner(this);
    }

    /// <summary>Sendet die Offline-Warteschlange (Start, Neuverbindung, jede Minute).</summary>
    public OutboxRunner OutboxRunner { get; }

    /// <summary>Tray-Symbol (nur in der laufenden App, nicht in Tests); <c>null</c> = keins.</summary>
    public TrayService? Tray { get; set; }

    /// <summary>Echtzeit (Reverb), Zähler für Glocke/Nachrichten, Desktop-Benachrichtigungen.</summary>
    public RealtimeService Realtime { get; }

    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly LinuxSettingsStore _linuxStore;

    public ISettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Linux-eigene Einstellungen (Design) aus <c>linux.json</c>.</summary>
    public LinuxSettings LinuxSettings { get; private set; }

    /// <summary>Design gewechselt (Einstellungen) – die App setzt die Theme-Variante.</summary>
    public event EventHandler? LinuxSettingsChanged;

    /// <summary>Bereich öffnen (von der Shell gesetzt): Startseite → News, Deep Links, Benachrichtigungen.</summary>
    public Func<AppRoute, bool>? Navigator { get; set; }

    public bool Navigate(AppRoute route) => Navigator?.Invoke(route) ?? false;

    /// <summary>Zeigt die App dieses Ziel selbst an (Bereich mit eigener Seite)? Von der Shell gesetzt.</summary>
    public Func<AppRoute, bool>? IsNative { get; set; }

    /// <summary>
    /// Adresse der Website/des Wikis öffnen: als Seite der App, wenn es sie nativ gibt (<see cref="AppRoute.FromWebUrl"/>),
    /// sonst genau diese Adresse im Browser (z. B. ein Forum-Thema, solange das Forum noch nicht in der App ist).
    /// </summary>
    public Task OpenLinkAsync(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return RouteFor(url) is { } route && Navigate(route) ? Task.CompletedTask : Browser.OpenAsync(url);
    }

    /// <summary>Route in der App für eine Adresse – nur, wenn die App sie nativ zeigt; sonst <c>null</c>.</summary>
    public AppRoute? RouteFor(Uri url) =>
        AppRoute.FromWebUrl(url, Environment) is { } route && (IsNative?.Invoke(route) ?? true) ? route : null;

    /// <summary>Läuft die App als Flatpak? (Über-Bereich, Hinweise)</summary>
    public bool IsFlatpak { get; } = new LinuxPaths().IsFlatpak;

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

    /// <summary>
    /// Offline-Warteschlange (Kern): DMs nur im Speicher, Forenantworten und Zusagen verschlüsselt in
    /// <c>$XDG_DATA_HOME/rebelsofgaming/outbox.dat</c> (nur mit Schlüsselbund).
    /// </summary>
    public RoG.Core.Offline.Outbox Outbox { get; }

    /// <summary>Werden Antworten/Warteschlange verschlüsselt auf der Platte gehalten? (Über-Bereich, Diagnose)</summary>
    public bool PersistentCache { get; private init; }

    /// <summary>Cache-Dateien älter als das werden beim Start gelöscht (zusätzlich zur Obergrenze von 400 Einträgen).</summary>
    public static readonly TimeSpan CacheRetention = TimeSpan.FromDays(30);

    /// <summary>„Beim Anmelden starten“ (Portal in Flatpak, sonst <c>~/.config/autostart</c>).</summary>
    public IAutostart Autostart { get; }

    /// <summary>
    /// Direkter Upload in den Speicher der Plattform (vorab signierte Adresse aus <c>POST /v1/uploads</c>) – eigener
    /// Client ohne Token und ohne RoG-Pipeline, lange Zeitgrenze für Videos.
    /// </summary>
    public HttpClient StorageHttp { get; private init; } = new() { Timeout = TimeSpan.FromMinutes(15) };

    /// <summary>Spiele- und Voice-Links (<c>steam://</c>, <c>ts3server://</c> …) über <c>xdg-open</c>.</summary>
    public RoG.Core.Features.Servers.IExternalLauncher Launcher { get; }

    /// <summary>„Verbinden“ und „Adresse kopieren“ für Gameserver (Kern) mit <c>xdg-open</c> und Zwischenablage.</summary>
    public RoG.Core.Features.Servers.ServerConnector ServerConnector { get; }

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
        var settingsDirectory = options.SettingsDirectory ?? paths.ConfigDirectory;
        var settingsStore = new JsonFileSettingsStore(Path.Combine(settingsDirectory, "settings.json"));
        var settings = await settingsStore.LoadAsync();

        var language = options.Language ?? LanguageResolver.Resolve(settings.Language, CultureInfo.CurrentUICulture);
        var environment = RoGEnvironment.Get(options.Environment ?? LaunchOptions.Current.Environment ?? settings.Environment);
        var secrets = options.Tokens is null ? new SecretServiceBackend() : null;
        var tokens = options.Tokens ?? new FallbackTokenStore(new SecretServiceTokenStore(secrets!, environment.Name));
        var apiContext = new ApiContext(language, AppVersion);

        // Antwort-Cache und Warteschlange verschlüsselt auf der Platte (AES-GCM, Schlüssel im Schlüsselbund – statt DPAPI
        // wie unter Windows). Ohne Schlüsselbund bleibt alles im Speicher (Annahme L6/L30).
        var protector = options.MemoryOnly
            ? null
            : options.Protector ?? (secrets is null ? null : await KeyringPayloadProtector.TryCreateAsync(secrets, environment.Name));
        var cacheDirectory = Path.Combine(options.CacheDirectory ?? paths.CacheDirectory, "http", environment.Name);
        IEtagCache cache = new InMemoryEtagCache();
        if (protector is not null)
        {
            PruneOld(cacheDirectory, CacheRetention);
            var persistent = new PersistentEtagCache(cacheDirectory, protector);
            await persistent.LoadAsync();
            cache = persistent;
        }

        var api = new RoGApi(environment, tokens, apiContext, cache, options.ApiHandler);
        var persistentCache = protector is not null;
        var outbox = new RoG.Core.Offline.Outbox(
            api.Http,
            protector is null ? null : Path.Combine(options.DataDirectory ?? paths.DataDirectory, $"outbox-{environment.Name}.dat"),
            protector ?? (RoG.Core.Security.IPayloadProtector)RoG.Core.Security.NoPayloadProtection.Instance);
        await outbox.LoadAsync();
        // RoGHttpHandler setzt „RoG-Windows“ nur, wenn noch kein User-Agent da ist – HttpClient überträgt diesen vorher
        api.Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.UserAgentProduct, AppVersion));

        var images = new ImageLoader(options.ImageHandler);
        var xdg = new XdgLauncher();
        var launcher = options.Launcher ?? xdg;
        var connector = new RoG.Core.Features.Servers.ServerConnector(launcher, options.Clipboard ?? new AvaloniaClipboard());
        return new AppServices(
            settingsStore,
            settings,
            new LinuxSettingsStore(Path.Combine(settingsDirectory, "linux.json")),
            language,
            environment,
            tokens,
            api,
            new ReswLocalizer(language),
            options.Browser ?? xdg,
            options.Notifier ?? (paths.IsFlatpak ? new PortalNotifier() : new FreedesktopNotifier()),
            images,
            outbox,
            options.Autostart ?? (paths.IsFlatpak
                ? new PortalAutostart()
                : new XdgAutostart(paths.ConfigHome, AutostartExecutable())),
            launcher,
            connector)
        {
            PersistentCache = persistentCache,
            StorageHttp = options.StorageHandler is null
                ? new HttpClient { Timeout = TimeSpan.FromMinutes(15) }
                : new HttpClient(options.StorageHandler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(15) },
        };
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

    /// <summary>
    /// Wie <see cref="UpdateSettingsAsync"/>, aber wartet auf das Speichern – für das Schließen des Fensters, wenn der
    /// Prozess gleich endet. Läuft auf dem Thread-Pool, damit das Warten den UI-Thread nicht verklemmt.
    /// </summary>
    public void UpdateSettingsNow(Func<AppSettings, AppSettings> change) =>
        Task.Run(() => UpdateSettingsAsync(change)).GetAwaiter().GetResult();

    public async Task UpdateLinuxSettingsAsync(Func<LinuxSettings, LinuxSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _settingsGate.WaitAsync();
        try
        {
            LinuxSettings = change(LinuxSettings);
            await _linuxStore.SaveAsync(LinuxSettings);
        }
        finally
        {
            _settingsGate.Release();
        }

        LinuxSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Texte für die Seite, die der Browser nach dem Login-Rücksprung zeigt.</summary>
    public LoopbackPageTexts LoopbackTexts() => new(
        Language,
        T(StringKeys.LoopbackTitle),
        T(StringKeys.LoopbackSuccess),
        T(StringKeys.LoopbackFailure));

    /// <summary>Antwort-Cache und Warteschlange leeren (Abmelden: Inhalte gehören zum Konto) – auch die Dateien.</summary>
    public void ClearCaches()
    {
        Api.Handler.ClearCache();
        Outbox.Clear();
        Images.Clear();
    }

    /// <summary>Cache-Dateien, die länger nicht geschrieben wurden, löschen (Speicherdauer wie im Bauplan).</summary>
    internal static void PruneOld(string folder, TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var limit = DateTime.UtcNow - maxAge;
            foreach (var file in new DirectoryInfo(folder).GetFiles("*.dat").Where(f => f.LastWriteTimeUtc < limit))
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache ist Beiwerk
        }
    }

    public void Dispose()
    {
        // Reverb-Verbindung schließen, ohne den UI-Thread zu blockieren (höchstens 3 s)
        Task.Run(() => Realtime.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
        Api.Dispose();
        Images.Dispose();
        _settingsGate.Dispose();
        (Notifier as IDisposable)?.Dispose();
        OutboxRunner.Dispose();
        Outbox.Dispose();
        StorageHttp.Dispose();
        (Tokens as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Programm für den Autostart-Eintrag: das eigene Startprogramm (Paket: <c>rebelsofgaming</c>); läuft die App über
    /// <c>dotnet rebelsofgaming.dll</c> (Entwicklung), das daneben liegende Startprogramm.
    /// </summary>
    private static string AutostartExecutable()
    {
        var process = System.Environment.ProcessPath;
        return process is not null && !Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.Ordinal)
            ? process
            : Path.Combine(AppContext.BaseDirectory, AppIdentity.DataFolderName);
    }

    private static string ReadVersion()
    {
        var info = typeof(AppServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? info : info[..plus];
    }
}
