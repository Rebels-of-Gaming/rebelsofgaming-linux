using System.Runtime.InteropServices;
using RoG.Linux.Platform.Secrets;
using RoG.Mac.Platform.Interop;
using CF = RoG.Mac.Platform.Interop.CoreFoundation;

namespace RoG.Mac.Platform.Secrets;

/// <summary>
/// Schlüsselbund von macOS (Security.framework, <c>SecItem*</c>) als <see cref="ISecretBackend"/>: Geräte-Token,
/// Schlüssel für die verschlüsselten Caches und die PIN der App-Sperre – je ein „generisches Passwort“ mit
/// Dienst = Bundle-ID und Konto = Attribute (sortiert, <c>kind=…;environment=…</c>).
/// Bevorzugt der Datenschutz-Schlüsselbund (<c>kSecUseDataProtectionKeychain</c>, wie iOS, nur dieses Gerät, braucht
/// eine signierte App mit Zugriffsgruppe – im Mac App Store gegeben); ohne Berechtigung (<c>errSecMissingEntitlement</c>,
/// unsignierte Entwicklungs-Builds) der klassische Anmelde-Schlüsselbund.
/// </summary>
public sealed partial class KeychainSecretBackend(string? service = null) : ISecretBackend
{
    internal const int Success = 0;
    internal const int ItemNotFound = -25300;
    internal const int DuplicateItem = -25299;
    internal const int MissingEntitlement = -34018;

    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";

    private static readonly Lazy<IntPtr> s_security = new(() => NativeLibrary.Load(SecurityLibrary));

    /// <summary>Datenschutz-Schlüsselbund nutzbar? Wird beim ersten <c>errSecMissingEntitlement</c> abgeschaltet.</summary>
    private static volatile bool s_dataProtection = true;

    private readonly string _service = service ?? MacIdentity.BundleId;

    /// <summary>Wird der Datenschutz-Schlüsselbund benutzt (Diagnose/Tests)?</summary>
    public static bool UsesDataProtectionKeychain => s_dataProtection;

    /// <summary>Konto-Name aus den Attributen: sortiert, <c>schlüssel=wert</c> mit <c>;</c> getrennt (stabil, eindeutig).</summary>
    public static string AccountFor(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return string.Join(';', attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + a.Value));
    }

    public Task<byte[]?> LookupAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        var account = AccountFor(attributes);
        return Task.Run(() => WithFallback(dp => Lookup(account, dp)), cancellationToken);
    }

    public Task StoreAsync(string label, IReadOnlyDictionary<string, string> attributes, byte[] secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var account = AccountFor(attributes);
        return Task.Run(() => WithFallback(dp => Store(label, account, secret, dp)), cancellationToken);
    }

    public Task ClearAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        var account = AccountFor(attributes);
        return Task.Run(() => WithFallback(dp => Clear(account, dp)), cancellationToken);
    }

    private static T WithFallback<T>(Func<bool, (int Status, T Value)> call)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new SecretStoreUnavailableException("Keychain only exists on macOS");
        }

        try
        {
            var dataProtection = s_dataProtection;
            var (status, value) = call(dataProtection);
            if (status == MissingEntitlement && dataProtection)
            {
                s_dataProtection = false;
                (status, value) = call(false);
            }

            return status == Success ? value : throw new SecretStoreUnavailableException($"Keychain status {status}");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreUnavailableException("Security framework not available", ex);
        }
    }

    private static bool WithFallback(Func<bool, int> call) => WithFallback(dp => (call(dp), true));

    private (int, byte[]?) Lookup(string account, bool dataProtection)
    {
        var query = Query(account, dataProtection, [(Sec("kSecReturnData"), CF.True), (Sec("kSecMatchLimit"), Sec("kSecMatchLimitOne"))]);
        try
        {
            var status = SecItemCopyMatching(query, out var result);
            if (status == ItemNotFound)
            {
                return (Success, null);
            }

            if (status != Success)
            {
                return (status, null);
            }

            try
            {
                return (Success, CF.ReadData(result));
            }
            finally
            {
                CF.CFRelease(result);
            }
        }
        finally
        {
            CF.CFRelease(query);
        }
    }

    private int Store(string label, string account, byte[] secret, bool dataProtection)
    {
        var status = Clear(account, dataProtection);
        if (status != Success)
        {
            return status;
        }

        var labelRef = CF.CreateString(label);
        var data = CF.CreateData(secret);
        List<(IntPtr, IntPtr)> extra = [(Sec("kSecAttrLabel"), labelRef), (Sec("kSecValueData"), data)];
        if (dataProtection)
        {
            // nur dieses Gerät, nach dem ersten Entsperren (Hintergrund-Abfragen) – kein iCloud-Schlüsselbund
            extra.Add((Sec("kSecAttrAccessible"), Sec("kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly")));
        }

        var item = Query(account, dataProtection, extra);
        try
        {
            status = SecItemAdd(item, IntPtr.Zero);
            return status == DuplicateItem ? Success : status;
        }
        finally
        {
            CF.CFRelease(item);
            CF.CFRelease(data);
            CF.CFRelease(labelRef);
        }
    }

    private int Clear(string account, bool dataProtection)
    {
        // Der klassische Schlüsselbund löscht je Aufruf nur einen Treffer – wiederholen, bis nichts mehr da ist
        var query = Query(account, dataProtection, []);
        try
        {
            for (var i = 0; i < 16; i++)
            {
                var status = SecItemDelete(query);
                if (status == ItemNotFound)
                {
                    return Success;
                }

                if (status != Success)
                {
                    return status;
                }
            }

            return Success;
        }
        finally
        {
            CF.CFRelease(query);
        }
    }

    private IntPtr Query(string account, bool dataProtection, IEnumerable<(IntPtr Key, IntPtr Value)> extra)
    {
        var service = CF.CreateString(_service);
        var accountRef = CF.CreateString(account);
        try
        {
            List<KeyValuePair<IntPtr, IntPtr>> entries =
            [
                new(Sec("kSecClass"), Sec("kSecClassGenericPassword")),
                new(Sec("kSecAttrService"), service),
                new(Sec("kSecAttrAccount"), accountRef),
            ];
            if (dataProtection)
            {
                entries.Add(new(Sec("kSecUseDataProtectionKeychain"), CF.True));
            }

            entries.AddRange(extra.Select(e => new KeyValuePair<IntPtr, IntPtr>(e.Key, e.Value)));
            return CF.CreateDictionary(entries);
        }
        finally
        {
            // das Wörterbuch hält eigene Referenzen
            CF.CFRelease(accountRef);
            CF.CFRelease(service);
        }
    }

    private static IntPtr Sec(string name) => CF.Constant(s_security.Value, name);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemDelete(IntPtr query);
}
