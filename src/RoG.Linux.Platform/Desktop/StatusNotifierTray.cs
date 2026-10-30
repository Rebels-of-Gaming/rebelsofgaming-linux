using System.Text;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>Eintrag im Menü des Tray-Symbols; <see cref="Label"/> <c>null</c> = Trennlinie.</summary>
public sealed record TrayMenuEntry(int Id, string? Label)
{
    public bool IsSeparator => Label is null;
}

/// <summary>Symbol in einer Größe, Pixel als ARGB32 in Netzwerk-Byte-Reihenfolge (wie StatusNotifierItem <c>IconPixmap</c>).</summary>
public sealed record TrayIconPixmap(int Width, int Height, byte[] Argb);

/// <summary>Symbol im Infobereich (Tray) – Gegenstück zum <c>Shell_NotifyIcon</c> der Windows-App.</summary>
public interface ITrayIcon : IAsyncDisposable
{
    /// <summary>Zeigt ein Panel das Symbol gerade an? Nur dann darf „Schließen“ in den Infobereich gehen.</summary>
    bool IsVisible { get; }

    /// <summary><see cref="IsVisible"/> hat sich geändert (Panel neu gestartet, Erweiterung an/aus).</summary>
    event EventHandler? VisibilityChanged;

    /// <summary>Linksklick auf das Symbol.</summary>
    event EventHandler? Activated;

    /// <summary>Menüeintrag gewählt (ID aus <see cref="TrayMenuEntry"/>).</summary>
    event EventHandler<int>? MenuItemInvoked;

    /// <returns><see cref="IsVisible"/> nach dem Anmelden – nie eine Ausnahme ohne Session-Bus oder Panel.</returns>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    void SetToolTip(string title, string body);

    void SetMenu(IReadOnlyList<TrayMenuEntry> entries);
}

/// <summary>
/// Tray über <c>org.kde.StatusNotifierItem</c> + Menü über <c>com.canonical.dbusmenu</c> (KDE Plasma, XFCE, Cinnamon,
/// GNOME mit AppIndicator-Erweiterung, Steam-Deck-Desktopmodus). Eigene Umsetzung statt Avalonias <c>TrayIcon</c>:
/// meldet sich mit dem eindeutigen Busnamen an (kein eigener Name nötig – in Flatpak genügt
/// <c>--talk-name=org.kde.StatusNotifierWatcher</c>) und weiß, ob ein Panel das Symbol wirklich zeigt
/// (<c>IsStatusNotifierHostRegistered</c>). Ohne Panel bleibt alles still; die App schließt dann normal.
/// Ereignisse kommen auf einem Hintergrund-Thread.
/// </summary>
public sealed class StatusNotifierTray : ITrayIcon
{
    internal const string WatcherName = "org.kde.StatusNotifierWatcher";
    internal const string WatcherPath = "/StatusNotifierWatcher";
    internal const string ItemInterface = "org.kde.StatusNotifierItem";
    internal const string ItemPath = "/StatusNotifierItem";
    internal const string MenuInterface = "com.canonical.dbusmenu";
    internal const string MenuPath = "/MenuBar";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    private readonly string? _address;
    private readonly string _title;
    private readonly IReadOnlyList<TrayIconPixmap> _pixmaps;
    private readonly Lock _lock = new();
    private DBusConnection? _connection;
    private IDisposable? _watcherOwner;
    private IDisposable? _hostRegistered;
    private string _toolTipTitle;
    private string _toolTipBody = string.Empty;
    private IReadOnlyList<TrayMenuEntry> _menu = [];
    private uint _revision = 1;
    private bool _visible;

    public StatusNotifierTray(string title, IReadOnlyList<TrayIconPixmap> pixmaps, string? address = null)
    {
        _title = title;
        _toolTipTitle = title;
        _pixmaps = pixmaps;
        _address = address ?? DBusAddress.Session;
    }

    public bool IsVisible => _visible;

    public event EventHandler? VisibilityChanged;

    public event EventHandler? Activated;

    public event EventHandler<int>? MenuItemInvoked;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_address) || _connection is not null)
        {
            return _visible;
        }

        try
        {
            var connection = new DBusConnection(_address);
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandlers([new ItemHandler(this), new MenuHandler(this)]);
            _connection = connection;

            // Panel neu gestartet (plasmashell, Erweiterung aus/an): neu anmelden
            _watcherOwner = await connection.WatchSignalAsync(
                "org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus",
                "NameOwnerChanged",
                static (m, _) =>
                {
                    var r = m.GetBodyReader();
                    return (r.ReadString(), r.ReadString(), r.ReadString());
                },
                (Notification<(string Name, string Old, string New)> n) =>
                {
                    if (n.HasValue && n.Value.Name == WatcherName)
                    {
                        _ = RegisterAsync();
                    }
                },
                ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);
            _hostRegistered = await connection.WatchSignalAsync(
                null,
                WatcherPath,
                WatcherName,
                "StatusNotifierHostRegistered",
                (Notification n) => _ = RegisterAsync(),
                ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);
            await RegisterAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException)
        {
            SetVisible(false);
        }

        return _visible;
    }

    public void SetToolTip(string title, string body)
    {
        lock (_lock)
        {
            if (_toolTipTitle == title && _toolTipBody == body)
            {
                return;
            }

            _toolTipTitle = title;
            _toolTipBody = body;
        }

        EmitSignal(ItemPath, ItemInterface, "NewToolTip");
    }

    public void SetMenu(IReadOnlyList<TrayMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        uint revision;
        lock (_lock)
        {
            _menu = entries;
            revision = ++_revision;
        }

        if (_connection is not { } connection)
        {
            return;
        }

        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, MenuPath, MenuInterface, "LayoutUpdated", "ui");
            writer.WriteUInt32(revision);
            writer.WriteInt32(0);
            connection.TrySendMessage(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private async Task RegisterAsync()
    {
        if (_connection is not { } connection || connection.UniqueName is not { } unique)
        {
            return;
        }

        try
        {
            var writer = connection.GetMessageWriter();
            MessageBuffer register;
            try
            {
                writer.WriteMethodCallHeader(WatcherName, WatcherPath, WatcherName, "RegisterStatusNotifierItem", "s", MessageFlags.None);
                writer.WriteString(unique);
                register = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            await connection.CallMethodAsync(register).ConfigureAwait(false);

            writer = connection.GetMessageWriter();
            MessageBuffer get;
            try
            {
                writer.WriteMethodCallHeader(WatcherName, WatcherPath, PropertiesInterface, "Get", "ss", MessageFlags.None);
                writer.WriteString(WatcherName);
                writer.WriteString("IsStatusNotifierHostRegistered");
                get = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            var host = await connection.CallMethodAsync(get, static (m, _) => m.GetBodyReader().ReadVariantValue().GetBool(), null).ConfigureAwait(false);
            SetVisible(host);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or InvalidCastException or InvalidOperationException)
        {
            // kein Watcher (GNOME ohne Erweiterung) bzw. Panel weg
            SetVisible(false);
        }
    }

    private void SetVisible(bool visible)
    {
        if (_visible != visible)
        {
            _visible = visible;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EmitSignal(string path, string @interface, string member)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, path, @interface, member, null);
            connection.TrySendMessage(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _watcherOwner?.Dispose();
        _hostRegistered?.Dispose();
        _connection?.Dispose();
        _connection = null;
        return ValueTask.CompletedTask;
    }

    // --- StatusNotifierItem --------------------------------------------------------------------------------------------

    private sealed class ItemHandler(StatusNotifierTray owner) : IPathMethodHandler
    {
        private static readonly ReadOnlyMemory<byte> s_introspection = Encoding.UTF8.GetBytes(
            """
            <interface name="org.kde.StatusNotifierItem">
              <property name="Category" type="s" access="read"/>
              <property name="Id" type="s" access="read"/>
              <property name="Title" type="s" access="read"/>
              <property name="Status" type="s" access="read"/>
              <property name="WindowId" type="i" access="read"/>
              <property name="IconName" type="s" access="read"/>
              <property name="IconPixmap" type="a(iiay)" access="read"/>
              <property name="OverlayIconName" type="s" access="read"/>
              <property name="AttentionIconName" type="s" access="read"/>
              <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
              <property name="ItemIsMenu" type="b" access="read"/>
              <property name="Menu" type="o" access="read"/>
              <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
              <signal name="NewTitle"/>
              <signal name="NewIcon"/>
              <signal name="NewToolTip"/>
              <signal name="NewStatus"><arg name="status" type="s"/></signal>
            </interface>
            """);

        private static readonly string[] s_properties =
            ["Category", "Id", "Title", "Status", "WindowId", "IconName", "IconPixmap", "OverlayIconName", "AttentionIconName", "ToolTip", "ItemIsMenu", "Menu"];

        public string Path => ItemPath;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([s_introspection], ReadOnlySpan<string>.Empty);
                return ValueTask.CompletedTask;
            }

            var request = context.Request;
            switch (request.InterfaceAsString, request.MemberAsString)
            {
                case (ItemInterface, "Activate") or (ItemInterface, "SecondaryActivate"):
                    Reply(context, null, null);
                    owner.Activated?.Invoke(owner, EventArgs.Empty);
                    break;
                case (ItemInterface, "ContextMenu") or (ItemInterface, "Scroll"):
                    // Menü zeigt das Panel selbst über dbusmenu
                    Reply(context, null, null);
                    break;
                case (PropertiesInterface, "Get"):
                {
                    var reader = request.GetBodyReader();
                    var iface = reader.ReadString();
                    var name = reader.ReadString();
                    if (iface != ItemInterface || !s_properties.Contains(name))
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                        break;
                    }

                    Reply(context, "v", (ref MessageWriter w) => WriteProperty(ref w, name));
                    break;
                }

                case (PropertiesInterface, "GetAll"):
                    Reply(context, "a{sv}", (ref MessageWriter w) =>
                    {
                        var dict = w.WriteDictionaryStart();
                        foreach (var name in s_properties)
                        {
                            w.WriteDictionaryEntryStart();
                            w.WriteString(name);
                            WriteProperty(ref w, name);
                        }

                        w.WriteDictionaryEnd(dict);
                    });
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>Wert als Variante (Signatur + Wert).</summary>
        private void WriteProperty(ref MessageWriter w, string name)
        {
            switch (name)
            {
                case "Category":
                    w.WriteVariantString("Communications");
                    break;
                case "Id":
                    w.WriteVariantString(AppIdentity.AppId);
                    break;
                case "Title":
                    w.WriteVariantString(owner._title);
                    break;
                case "Status":
                    w.WriteVariantString("Active");
                    break;
                case "WindowId":
                    w.WriteVariantInt32(0);
                    break;
                case "IconName":
                    // Symbol aus dem Icon-Theme (Flatpak/Paket installieren es unter der App-ID); sonst IconPixmap
                    w.WriteVariantString(AppIdentity.AppId);
                    break;
                case "IconPixmap":
                    w.WriteSignature("a(iiay)");
                    WritePixmaps(ref w, owner._pixmaps);
                    break;
                case "OverlayIconName" or "AttentionIconName":
                    w.WriteVariantString(string.Empty);
                    break;
                case "ToolTip":
                {
                    string title, body;
                    lock (owner._lock)
                    {
                        title = owner._toolTipTitle;
                        body = owner._toolTipBody;
                    }

                    w.WriteSignature("(sa(iiay)ss)");
                    w.WriteStructureStart();
                    w.WriteString(string.Empty);
                    WritePixmaps(ref w, []);
                    w.WriteString(title);
                    w.WriteString(body);
                    break;
                }

                case "ItemIsMenu":
                    w.WriteVariantBool(false);
                    break;
                case "Menu":
                    w.WriteVariantObjectPath(MenuPath);
                    break;
            }
        }

        private static void WritePixmaps(ref MessageWriter w, IReadOnlyList<TrayIconPixmap> pixmaps)
        {
            var array = w.WriteArrayStart(DBusType.Struct);
            foreach (var p in pixmaps)
            {
                w.WriteStructureStart();
                w.WriteInt32(p.Width);
                w.WriteInt32(p.Height);
                w.WriteArray(p.Argb);
            }

            w.WriteArrayEnd(array);
        }
    }

    // --- dbusmenu ------------------------------------------------------------------------------------------------------

    private sealed class MenuHandler(StatusNotifierTray owner) : IPathMethodHandler
    {
        private static readonly ReadOnlyMemory<byte> s_introspection = Encoding.UTF8.GetBytes(
            """
            <interface name="com.canonical.dbusmenu">
              <property name="Version" type="u" access="read"/>
              <property name="TextDirection" type="s" access="read"/>
              <property name="Status" type="s" access="read"/>
              <property name="IconThemePath" type="as" access="read"/>
              <method name="GetLayout"><arg type="i" name="parentId" direction="in"/><arg type="i" name="recursionDepth" direction="in"/><arg type="as" name="propertyNames" direction="in"/><arg type="u" name="revision" direction="out"/><arg type="(ia{sv}av)" name="layout" direction="out"/></method>
              <method name="GetGroupProperties"><arg type="ai" name="ids" direction="in"/><arg type="as" name="propertyNames" direction="in"/><arg type="a(ia{sv})" name="properties" direction="out"/></method>
              <method name="GetProperty"><arg type="i" name="id" direction="in"/><arg type="s" name="name" direction="in"/><arg type="v" name="value" direction="out"/></method>
              <method name="Event"><arg type="i" name="id" direction="in"/><arg type="s" name="eventId" direction="in"/><arg type="v" name="data" direction="in"/><arg type="u" name="timestamp" direction="in"/></method>
              <method name="EventGroup"><arg type="a(isvu)" name="events" direction="in"/><arg type="ai" name="idErrors" direction="out"/></method>
              <method name="AboutToShow"><arg type="i" name="id" direction="in"/><arg type="b" name="needUpdate" direction="out"/></method>
              <method name="AboutToShowGroup"><arg type="ai" name="ids" direction="in"/><arg type="ai" name="updatesNeeded" direction="out"/><arg type="ai" name="idErrors" direction="out"/></method>
              <signal name="ItemsPropertiesUpdated"><arg type="a(ia{sv})" name="updatedProps"/><arg type="a(ias)" name="removedProps"/></signal>
              <signal name="LayoutUpdated"><arg type="u" name="revision"/><arg type="i" name="parent"/></signal>
              <signal name="ItemActivationRequested"><arg type="i" name="id"/><arg type="u" name="timestamp"/></signal>
            </interface>
            """);

        private static readonly string[] s_menuProperties = ["Version", "TextDirection", "Status", "IconThemePath"];

        public string Path => MenuPath;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([s_introspection], ReadOnlySpan<string>.Empty);
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<TrayMenuEntry> menu;
            uint revision;
            lock (owner._lock)
            {
                menu = owner._menu;
                revision = owner._revision;
            }

            var request = context.Request;
            var reader = request.GetBodyReader();
            switch (request.InterfaceAsString, request.MemberAsString)
            {
                case (MenuInterface, "GetLayout"):
                {
                    var parent = reader.ReadInt32();
                    Reply(context, "u(ia{sv}av)", (ref MessageWriter w) =>
                    {
                        w.WriteUInt32(revision);
                        if (parent == 0)
                        {
                            WriteRoot(ref w, menu);
                        }
                        else
                        {
                            WriteItem(ref w, menu.FirstOrDefault(e => e.Id == parent) ?? new TrayMenuEntry(parent, null), inVariant: false);
                        }
                    });
                    break;
                }

                case (MenuInterface, "GetGroupProperties"):
                {
                    var ids = reader.ReadArrayOfInt32();
                    Reply(context, "a(ia{sv})", (ref MessageWriter w) =>
                    {
                        var array = w.WriteArrayStart(DBusType.Struct);
                        foreach (var entry in menu.Where(e => ids.Length == 0 || ids.Contains(e.Id)))
                        {
                            w.WriteStructureStart();
                            w.WriteInt32(entry.Id);
                            WriteProperties(ref w, entry);
                        }

                        w.WriteArrayEnd(array);
                    });
                    break;
                }

                case (MenuInterface, "GetProperty"):
                {
                    var id = reader.ReadInt32();
                    var name = reader.ReadString();
                    var entry = menu.FirstOrDefault(e => e.Id == id);
                    Reply(context, "v", (ref MessageWriter w) =>
                    {
                        switch (name)
                        {
                            case "label":
                                w.WriteVariantString(entry?.Label ?? string.Empty);
                                break;
                            case "type":
                                w.WriteVariantString(entry?.IsSeparator == true ? "separator" : "standard");
                                break;
                            default:
                                w.WriteVariantBool(true);
                                break;
                        }
                    });
                    break;
                }

                case (MenuInterface, "Event"):
                {
                    var id = reader.ReadInt32();
                    var eventId = reader.ReadString();
                    Reply(context, null, null);
                    if (eventId == "clicked" && menu.Any(e => e.Id == id && !e.IsSeparator))
                    {
                        owner.MenuItemInvoked?.Invoke(owner, id);
                    }

                    break;
                }

                case (MenuInterface, "EventGroup"):
                {
                    var clicked = new List<int>();
                    var array = reader.ReadArrayStart(DBusType.Struct);
                    while (reader.HasNext(array))
                    {
                        reader.AlignStruct();
                        var id = reader.ReadInt32();
                        var eventId = reader.ReadString();
                        reader.ReadVariantValue();
                        reader.ReadUInt32();
                        if (eventId == "clicked" && menu.Any(e => e.Id == id && !e.IsSeparator))
                        {
                            clicked.Add(id);
                        }
                    }

                    Reply(context, "ai", (ref MessageWriter w) => w.WriteArray(Array.Empty<int>()));
                    foreach (var id in clicked)
                    {
                        owner.MenuItemInvoked?.Invoke(owner, id);
                    }

                    break;
                }

                case (MenuInterface, "AboutToShow"):
                    Reply(context, "b", (ref MessageWriter w) => w.WriteBool(false));
                    break;
                case (MenuInterface, "AboutToShowGroup"):
                    Reply(context, "aiai", (ref MessageWriter w) =>
                    {
                        w.WriteArray(Array.Empty<int>());
                        w.WriteArray(Array.Empty<int>());
                    });
                    break;
                case (PropertiesInterface, "Get"):
                {
                    reader.ReadString();
                    var name = reader.ReadString();
                    Reply(context, "v", (ref MessageWriter w) => WriteMenuProperty(ref w, name));
                    break;
                }

                case (PropertiesInterface, "GetAll"):
                    Reply(context, "a{sv}", (ref MessageWriter w) =>
                    {
                        var dict = w.WriteDictionaryStart();
                        foreach (var name in s_menuProperties)
                        {
                            w.WriteDictionaryEntryStart();
                            w.WriteString(name);
                            WriteMenuProperty(ref w, name);
                        }

                        w.WriteDictionaryEnd(dict);
                    });
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }

            return ValueTask.CompletedTask;
        }

        private static void WriteMenuProperty(ref MessageWriter w, string name)
        {
            switch (name)
            {
                case "Version":
                    w.WriteVariantUInt32(3);
                    break;
                case "TextDirection":
                    w.WriteVariantString("ltr");
                    break;
                case "Status":
                    w.WriteVariantString("normal");
                    break;
                default:
                    w.WriteSignature("as");
                    w.WriteArray(Array.Empty<string>());
                    break;
            }
        }

        /// <summary>Wurzel (ID 0) mit allen Einträgen als Kinder.</summary>
        private static void WriteRoot(ref MessageWriter w, IReadOnlyList<TrayMenuEntry> menu)
        {
            w.WriteStructureStart();
            w.WriteInt32(0);
            var props = w.WriteDictionaryStart();
            w.WriteDictionaryEntryStart();
            w.WriteString("children-display");
            w.WriteVariantString("submenu");
            w.WriteDictionaryEnd(props);
            var children = w.WriteArrayStart(DBusType.Variant);
            foreach (var entry in menu)
            {
                WriteItem(ref w, entry, inVariant: true);
            }

            w.WriteArrayEnd(children);
        }

        private static void WriteItem(ref MessageWriter w, TrayMenuEntry entry, bool inVariant)
        {
            if (inVariant)
            {
                w.WriteSignature("(ia{sv}av)");
            }

            w.WriteStructureStart();
            w.WriteInt32(entry.Id);
            WriteProperties(ref w, entry);
            var none = w.WriteArrayStart(DBusType.Variant);
            w.WriteArrayEnd(none);
        }

        private static void WriteProperties(ref MessageWriter w, TrayMenuEntry entry)
        {
            var props = w.WriteDictionaryStart();
            if (entry.IsSeparator)
            {
                w.WriteDictionaryEntryStart();
                w.WriteString("type");
                w.WriteVariantString("separator");
            }
            else
            {
                w.WriteDictionaryEntryStart();
                w.WriteString("label");
                w.WriteVariantString(entry.Label!);
                w.WriteDictionaryEntryStart();
                w.WriteString("enabled");
                w.WriteVariantBool(true);
            }

            w.WriteDictionaryEnd(props);
        }
    }

    private delegate void Body(ref MessageWriter writer);

    private static void Reply(MethodContext context, string? signature, Body? body)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        var writer = context.CreateReplyWriter(signature);
        try
        {
            body?.Invoke(ref writer);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }
}
