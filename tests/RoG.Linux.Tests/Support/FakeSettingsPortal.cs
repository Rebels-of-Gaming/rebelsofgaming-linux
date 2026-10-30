using Tmds.DBus.Protocol;

namespace RoG.Linux.Tests.Support;

/// <summary>
/// <c>org.freedesktop.portal.Settings</c> wie xdg-desktop-portal: <c>ReadOne</c> (Version 2) bzw. nur <c>Read</c> mit
/// doppelt verpacktem Wert (Version 1), unbekannte Schlüssel → <c>NotFound</c>, <c>SettingChanged</c> simulieren.
/// Werte: bool, uint oder string.
/// </summary>
public sealed class FakeSettingsPortal : IPathMethodHandler
{
    public Dictionary<(string Namespace, string Key), object> Values { get; } = [];

    /// <summary>Altes Portal (nur <c>Read</c>).</summary>
    public bool OnlyRead { get; init; }

    public DBusConnection? Connection { get; set; }

    public string Path => "/org/freedesktop/portal/desktop";

    public bool HandlesChildPaths => false;

    public void Change(string ns, string key, object value)
    {
        Values[(ns, key)] = value;
        var writer = Connection!.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, Path, "org.freedesktop.portal.Settings", "SettingChanged", "ssv");
            writer.WriteString(ns);
            writer.WriteString(key);
            WriteVariant(ref writer, value);
            Connection.TrySendMessage(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void WriteVariant(ref MessageWriter writer, object value)
    {
        switch (value)
        {
            case bool b:
                writer.WriteVariantBool(b);
                break;
            case uint u:
                writer.WriteVariantUInt32(u);
                break;
            case double d:
                writer.WriteVariantDouble(d);
                break;
            default:
                writer.WriteVariantString(value.ToString()!);
                break;
        }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        var method = request.MemberAsString;
        if (request.InterfaceAsString != "org.freedesktop.portal.Settings" || method is not ("ReadOne" or "Read") || (OnlyRead && method == "ReadOne"))
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        var ns = reader.ReadString();
        var key = reader.ReadString();
        if (!Values.TryGetValue((ns, key), out var value))
        {
            context.ReplyError("org.freedesktop.portal.Error.NotFound", "Requested setting not found");
            return ValueTask.CompletedTask;
        }

        Replies.Send(context, "v", (ref MessageWriter w) =>
        {
            if (method == "Read")
            {
                // Version 1: Wert in einer weiteren Variante
                w.WriteSignature("v");
                WriteVariant(ref w, value);
            }
            else
            {
                WriteVariant(ref w, value);
            }
        });
        return ValueTask.CompletedTask;
    }
}
