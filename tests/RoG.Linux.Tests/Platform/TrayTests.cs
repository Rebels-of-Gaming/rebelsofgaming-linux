using RoG.Linux.Platform;
using RoG.Linux.Platform.Desktop;
using RoG.Linux.Tests.Support;
using Tmds.DBus.Protocol;

namespace RoG.Linux.Tests.Platform;

/// <summary>Tray über StatusNotifierItem + dbusmenu gegen einen privaten Bus – so, wie ein Panel das Symbol abfragt.</summary>
[Trait("Category", "DBus")]
public class TrayTests
{
    private static readonly TrayIconPixmap s_icon = new(2, 2, [255, 224, 50, 74, 255, 224, 50, 74, 255, 224, 50, 74, 255, 224, 50, 74]);

    [Fact]
    public async Task Tray_registers_shows_properties_menu_and_reports_clicks()
    {
        await using var bus = await PrivateBus.StartAsync();
        var watcher = new FakeStatusNotifierWatcher();
        await bus.ServeAsync("org.kde.StatusNotifierWatcher", watcher);
        await using var tray = new StatusNotifierTray("Rebels of Gaming", [s_icon], bus.Address);
        tray.SetMenu([new(1, "Öffnen"), new(2, "Nachrichten"), new(3, null), new(9, "Beenden")]);
        tray.SetToolTip("Rebels of Gaming", "3 ungelesen");

        Assert.True(await tray.StartAsync());
        var service = Assert.Single(watcher.Registered);

        // Panel-Seite: Eigenschaften lesen
        using var panel = new DBusConnection(bus.Address);
        await panel.ConnectAsync();
        var props = await CallAsync(panel, service, "/StatusNotifierItem", "org.freedesktop.DBus.Properties", "GetAll", "s",
            (ref MessageWriter w) => w.WriteString("org.kde.StatusNotifierItem"),
            (m, _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue());
        Assert.Equal(AppIdentity.AppId, props["Id"].GetString());
        Assert.Equal("Rebels of Gaming", props["Title"].GetString());
        Assert.Equal("Active", props["Status"].GetString());
        Assert.Equal("/MenuBar", props["Menu"].GetObjectPathAsString());
        Assert.False(props["ItemIsMenu"].GetBool());
        Assert.Equal(1, props["IconPixmap"].Count);
        Assert.Equal("3 ungelesen", props["ToolTip"].GetItem(3).GetString());

        // Menü
        var labels = await CallAsync(panel, service, "/MenuBar", "com.canonical.dbusmenu", "GetLayout", "iias",
            (ref MessageWriter w) =>
            {
                w.WriteInt32(0);
                w.WriteInt32(-1);
                w.WriteArray(Array.Empty<string>());
            },
            (m, _) =>
            {
                var r = m.GetBodyReader();
                r.ReadUInt32();
                r.AlignStruct();
                Assert.Equal(0, r.ReadInt32());
                Assert.Equal("submenu", r.ReadDictionaryOfStringToVariantValue()["children-display"].GetString());
                return r.ReadArrayOfVariantValue()
                    .Select(child =>
                    {
                        var props = child.GetItem(1);
                        var values = Enumerable.Range(0, props.Count).Select(props.GetDictionaryEntry).Where(e => e.Key.GetString() is "label" or "type").ToDictionary(e => e.Key.GetString(), e => e.Value.GetString());
                        return values.TryGetValue("label", out var label) ? label : values["type"];
                    })
                    .ToList();
            });
        Assert.Equal(["Öffnen", "Nachrichten", "separator", "Beenden"], labels);

        var invoked = new TaskCompletionSource<int>();
        tray.MenuItemInvoked += (_, id) => invoked.TrySetResult(id);
        await CallAsync(panel, service, "/MenuBar", "com.canonical.dbusmenu", "Event", "isvu",
            (ref MessageWriter w) =>
            {
                w.WriteInt32(9);
                w.WriteString("clicked");
                w.WriteVariantInt32(0);
                w.WriteUInt32(0);
            },
            (_, _) => true);
        Assert.Equal(9, await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var activated = new TaskCompletionSource();
        tray.Activated += (_, _) => activated.TrySetResult();
        await CallAsync(panel, service, "/StatusNotifierItem", "org.kde.StatusNotifierItem", "Activate", "ii",
            (ref MessageWriter w) =>
            {
                w.WriteInt32(10);
                w.WriteInt32(10);
            },
            (_, _) => true);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Without_watcher_or_panel_the_tray_is_not_visible()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using (var none = new StatusNotifierTray("Rebels of Gaming", [s_icon], bus.Address))
        {
            Assert.False(await none.StartAsync());
        }

        var watcher = new FakeStatusNotifierWatcher { HostRegistered = false };
        await bus.ServeAsync("org.kde.StatusNotifierWatcher", watcher);
        await using var tray = new StatusNotifierTray("Rebels of Gaming", [s_icon], bus.Address);
        Assert.False(await tray.StartAsync());
        Assert.Single(watcher.Registered);
    }

    [Fact]
    public async Task Tray_registers_again_when_the_panel_starts_later()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using var tray = new StatusNotifierTray("Rebels of Gaming", [s_icon], bus.Address);
        Assert.False(await tray.StartAsync());

        var changed = new TaskCompletionSource();
        tray.VisibilityChanged += (_, _) => changed.TrySetResult();
        var watcher = new FakeStatusNotifierWatcher();
        await bus.ServeAsync("org.kde.StatusNotifierWatcher", watcher);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(tray.IsVisible);
        Assert.Single(watcher.Registered);
    }

    private delegate void Args(ref MessageWriter writer);

    private static async Task<T> CallAsync<T>(DBusConnection connection, string destination, string path, string iface, string member, string? signature, Args? args, MessageValueReader<T> reader)
    {
        var writer = connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(destination, path, iface, member, signature, MessageFlags.None);
            args?.Invoke(ref writer);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        return await connection.CallMethodAsync(message, reader, null);
    }
}
