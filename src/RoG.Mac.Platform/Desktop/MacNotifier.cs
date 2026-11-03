using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoG.Linux.Platform.Desktop;
using RoG.Mac.Platform.Interop;
using CF = RoG.Mac.Platform.Interop.CoreFoundation;

namespace RoG.Mac.Platform.Desktop;

/// <summary>
/// Mitteilungen über <c>UNUserNotificationCenter</c> (UserNotifications.framework). Beim ersten Mal fragt macOS nach
/// der Erlaubnis (Hinweis, Ton, Kennzeichen); abgelehnt bzw. ausgeschaltet zeigt das System nichts – die App merkt
/// davon nichts und bleibt bedienbar. Nur in einem .app-Bundle (mit Bundle-ID) – <c>dotnet run</c> hätte keine und
/// UserNotifications würde abbrechen, dann liefert <see cref="NotifyAsync"/> <c>null</c>.
/// Stufe 1: Klick auf die Mitteilung holt die App nach vorn (macOS), springt aber noch nicht zum Ziel
/// (<see cref="Activated"/> kommt erst mit einem eigenen Delegate, siehe Bauplan).
/// </summary>
public sealed class MacNotifier : IDesktopNotifier
{
    /// <summary>UNAuthorizationOptionBadge | Sound | Alert.</summary>
    private const nuint AuthorizationOptions = 1 | 2 | 4;

    private static readonly Lock s_gate = new();
    private static bool s_authorizationRequested;
    private static IntPtr s_completionBlock;

    private readonly Func<string?> _bundleIdentifier;
    private uint _nextId;

    public MacNotifier(Func<string?>? bundleIdentifier = null)
    {
        _bundleIdentifier = bundleIdentifier ?? CF.MainBundleIdentifier;
    }

    /// <summary>Stufe 1: nicht ausgelöst (kein Delegate) – Klick bringt nur die App nach vorn.</summary>
    public event EventHandler<uint>? Activated
    {
        add { }
        remove { }
    }

    public async Task<bool> ShowAsync(string title, string body, CancellationToken cancellationToken = default) =>
        await NotifyAsync(new DesktopNotification(title, body), cancellationToken).ConfigureAwait(false) is not null;

    public Task<uint?> NotifyAsync(DesktopNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult<uint?>(null);
        }

        var id = Interlocked.Increment(ref _nextId);
        return Task.Run(() => Post(notification, id) ? id : (uint?)null, cancellationToken);
    }

    private bool Post(DesktopNotification notification, uint id)
    {
        try
        {
            if (string.IsNullOrEmpty(_bundleIdentifier()) || !ObjC.LoadFramework("UserNotifications"))
            {
                return false;
            }

            using var pool = ObjC.AutoreleasePool.Create();
            var center = ObjC.Send(ObjC.GetClass("UNUserNotificationCenter"), "currentNotificationCenter");
            if (center == IntPtr.Zero)
            {
                return false;
            }

            RequestAuthorizationOnce(center);

            var content = ObjC.Send(ObjC.Send(ObjC.GetClass("UNMutableNotificationContent"), "alloc"), "init");
            var title = CF.CreateString(notification.Title);
            var body = CF.CreateString(notification.Body);
            var identifier = CF.CreateString("rog-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try
            {
                ObjC.Send(content, "setTitle:", title);
                ObjC.Send(content, "setBody:", body);
                ObjC.Send(content, "setSound:", ObjC.Send(ObjC.GetClass("UNNotificationSound"), "defaultSound"));
                var request = ObjC.Send(ObjC.GetClass("UNNotificationRequest"), "requestWithIdentifier:content:trigger:", identifier, content, IntPtr.Zero);
                ObjC.Send(center, "addNotificationRequest:withCompletionHandler:", request, IntPtr.Zero);
                return true;
            }
            finally
            {
                CF.CFRelease(identifier);
                CF.CFRelease(body);
                CF.CFRelease(title);
                ObjC.Release(content);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Erlaubnis einmal je Prozess anfragen (macOS fragt nur beim ersten Mal wirklich nach).</summary>
    private static unsafe void RequestAuthorizationOnce(IntPtr center)
    {
        lock (s_gate)
        {
            if (s_authorizationRequested)
            {
                return;
            }

            s_authorizationRequested = true;
            s_completionBlock = CreateGlobalBlock(&OnAuthorization);
        }

        ObjC.Send(center, "requestAuthorizationWithOptions:completionHandler:", AuthorizationOptions, s_completionBlock);
    }

    [UnmanagedCallersOnly]
    private static void OnAuthorization(IntPtr block, byte granted, IntPtr error)
    {
        // Ergebnis egal: abgelehnt = macOS zeigt nichts an; die Einstellung liegt bei der Person (Systemeinstellungen)
    }

    /// <summary>
    /// Globaler Objective-C-Block ohne erfasste Variablen (Block-ABI: isa, flags, reserved, invoke, descriptor). Lebt
    /// bis zum Prozessende; <c>Block_copy</c>/<c>Block_release</c> sind für globale Blöcke wirkungslos.
    /// </summary>
    private static unsafe IntPtr CreateGlobalBlock(delegate* unmanaged<IntPtr, byte, IntPtr, void> invoke)
    {
        const int blockIsGlobal = 1 << 28;
        var isa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (ulong)sizeof(BlockLiteral);
        var literal = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        literal->Isa = isa;
        literal->Flags = blockIsGlobal;
        literal->Invoke = (IntPtr)invoke;
        literal->Descriptor = descriptor;
        return (IntPtr)literal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public ulong Reserved;
        public ulong Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public BlockDescriptor* Descriptor;
    }
}
