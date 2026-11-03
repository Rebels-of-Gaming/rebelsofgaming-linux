using System.Runtime.InteropServices;

namespace RoG.Mac.Platform.Interop;

/// <summary>
/// Minimaler Zugriff auf CoreFoundation (C-API, kein Objective-C). Wird erst beim ersten Aufruf geladen – unter Linux
/// nie. CF-Objekte sind „toll-free bridged“ (CFString = NSString, CFData = NSData), daher auch für Objective-C nutzbar.
/// </summary>
internal static unsafe partial class CoreFoundation
{
    public const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly Lazy<IntPtr> s_handle = new(() => NativeLibrary.Load(Library));

    [LibraryImport(Library)]
    public static partial void CFRelease(IntPtr cf);

    [LibraryImport(Library)]
    public static partial IntPtr CFStringCreateWithCharacters(IntPtr allocator, char* chars, nint length);

    [LibraryImport(Library)]
    public static partial nint CFStringGetLength(IntPtr value);

    [LibraryImport(Library)]
    public static partial void CFStringGetCharacters(IntPtr value, CFRange range, char* buffer);

    [LibraryImport(Library)]
    public static partial IntPtr CFDataCreate(IntPtr allocator, byte* bytes, nint length);

    [LibraryImport(Library)]
    public static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(Library)]
    public static partial byte* CFDataGetBytePtr(IntPtr data);

    [LibraryImport(Library)]
    public static partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr* keys, IntPtr* values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [LibraryImport(Library)]
    public static partial IntPtr CFBundleGetMainBundle();

    [LibraryImport(Library)]
    public static partial IntPtr CFBundleGetIdentifier(IntPtr bundle);

    /// <summary><c>kCFBooleanTrue</c>.</summary>
    public static IntPtr True => Constant("kCFBooleanTrue");

    /// <summary><c>kCFBooleanFalse</c>.</summary>
    public static IntPtr False => Constant("kCFBooleanFalse");

    /// <summary>Adresse von <c>kCFTypeDictionaryKeyCallBacks</c> (Struktur, nicht Zeiger).</summary>
    public static IntPtr KeyCallBacks => NativeLibrary.GetExport(s_handle.Value, "kCFTypeDictionaryKeyCallBacks");

    /// <summary>Adresse von <c>kCFTypeDictionaryValueCallBacks</c>.</summary>
    public static IntPtr ValueCallBacks => NativeLibrary.GetExport(s_handle.Value, "kCFTypeDictionaryValueCallBacks");

    /// <summary>Exportierte CF-Konstante (z. B. <c>kCFBooleanTrue</c>) – der Export ist ein Zeiger auf den Wert.</summary>
    public static IntPtr Constant(string name) => *(IntPtr*)NativeLibrary.GetExport(s_handle.Value, name);

    /// <summary>Konstante aus einer anderen Bibliothek (z. B. Security: <c>kSecClass</c>).</summary>
    public static IntPtr Constant(IntPtr library, string name) => *(IntPtr*)NativeLibrary.GetExport(library, name);

    /// <summary>Neuer CFString (+1, mit <see cref="CFRelease"/> freigeben).</summary>
    public static IntPtr CreateString(string value)
    {
        fixed (char* chars = value)
        {
            return CFStringCreateWithCharacters(IntPtr.Zero, chars, value.Length);
        }
    }

    /// <summary>CFString/NSString lesen; <c>null</c> bei <see cref="IntPtr.Zero"/>.</summary>
    public static string? ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        var length = CFStringGetLength(value);
        var result = new string('\0', checked((int)length));
        fixed (char* buffer = result)
        {
            CFStringGetCharacters(value, new CFRange(0, length), buffer);
        }

        return result;
    }

    /// <summary>Neues CFData (+1) mit einer Kopie der Bytes.</summary>
    public static IntPtr CreateData(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes)
        {
            return CFDataCreate(IntPtr.Zero, data, bytes.Length);
        }
    }

    public static byte[] ReadData(IntPtr data)
    {
        var length = checked((int)CFDataGetLength(data));
        return new ReadOnlySpan<byte>(CFDataGetBytePtr(data), length).ToArray();
    }

    /// <summary>CFDictionary (+1) aus Schlüssel/Wert-Paaren; Schlüssel und Werte werden vom Wörterbuch gehalten.</summary>
    public static IntPtr CreateDictionary(IReadOnlyList<KeyValuePair<IntPtr, IntPtr>> entries)
    {
        var keys = new IntPtr[entries.Count];
        var values = new IntPtr[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            keys[i] = entries[i].Key;
            values[i] = entries[i].Value;
        }

        fixed (IntPtr* k = keys)
        fixed (IntPtr* v = values)
        {
            return CFDictionaryCreate(IntPtr.Zero, k, v, entries.Count, KeyCallBacks, ValueCallBacks);
        }
    }

    /// <summary>Bundle-ID des laufenden Programms; <c>null</c>, wenn es nicht aus einem .app-Bundle läuft (z. B. <c>dotnet run</c>).</summary>
    public static string? MainBundleIdentifier()
    {
        var bundle = CFBundleGetMainBundle();
        return bundle == IntPtr.Zero ? null : ReadString(CFBundleGetIdentifier(bundle));
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct CFRange(nint Location, nint Length);
}
