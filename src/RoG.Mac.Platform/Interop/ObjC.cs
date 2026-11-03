using System.Runtime.InteropServices;

namespace RoG.Mac.Platform.Interop;

/// <summary>
/// Kleinster nötiger Objective-C-Zugriff (libobjc): Klassen, Selektoren, <c>objc_msgSend</c> mit festen Signaturen
/// über Funktionszeiger (auf arm64 Pflicht – <c>objc_msgSend</c> darf nicht variadisch aufgerufen werden).
/// Rückgaben von Klassenmethoden sind „autoreleased“ – Aufrufe laufen deshalb in <see cref="AutoreleasePool"/>.
/// </summary>
internal static unsafe partial class ObjC
{
    public const string Library = "/usr/lib/libobjc.A.dylib";

    private static readonly Lazy<IntPtr> s_msgSend = new(() => NativeLibrary.GetExport(NativeLibrary.Load(Library), "objc_msgSend"));

    [LibraryImport(Library, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetClass(string name);

    [LibraryImport(Library, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Selector(string name);

    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPush")]
    public static partial IntPtr AutoreleasePoolPush();

    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPop")]
    public static partial void AutoreleasePoolPop(IntPtr pool);

    /// <summary>Framework laden (Klassen sind danach über <see cref="GetClass"/> erreichbar); <c>false</c>, wenn es fehlt.</summary>
    public static bool LoadFramework(string name) =>
        NativeLibrary.TryLoad($"/System/Library/Frameworks/{name}.framework/{name}", out _);

    public static IntPtr Send(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)s_msgSend.Value)(receiver, Selector(selector));

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend.Value)(receiver, Selector(selector), a);

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend.Value)(receiver, Selector(selector), a, b);

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b, IntPtr c) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend.Value)(receiver, Selector(selector), a, b, c);

    /// <summary>Methode mit <c>NSUInteger</c> und einem Objekt (z. B. <c>requestAuthorizationWithOptions:completionHandler:</c>).</summary>
    public static void Send(IntPtr receiver, string selector, nuint a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr, void>)s_msgSend.Value)(receiver, Selector(selector), a, b);

    /// <summary>Methode mit <c>BOOL</c>-Rückgabe (arm64: <c>bool</c>, x64: <c>signed char</c> – beides ein Byte).</summary>
    public static bool SendBool(IntPtr receiver, string selector, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)s_msgSend.Value)(receiver, Selector(selector), a) != 0;

    public static bool SendBool(IntPtr receiver, string selector, IntPtr a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, byte>)s_msgSend.Value)(receiver, Selector(selector), a, b) != 0;

    /// <summary><c>[obj release]</c> für Objekte aus <c>alloc</c>/<c>new</c>/<c>copy</c>.</summary>
    public static void Release(IntPtr receiver)
    {
        if (receiver != IntPtr.Zero)
        {
            Send(receiver, "release");
        }
    }

    /// <summary>Hält einen Autorelease-Pool für die Dauer eines <c>using</c>-Blocks (Aufrufe vom Thread-Pool).</summary>
    public readonly struct AutoreleasePool : IDisposable
    {
        private readonly IntPtr _pool;

        private AutoreleasePool(IntPtr pool) => _pool = pool;

        public static AutoreleasePool Create() => new(AutoreleasePoolPush());

        public void Dispose()
        {
            if (_pool != IntPtr.Zero)
            {
                AutoreleasePoolPop(_pool);
            }
        }
    }
}
