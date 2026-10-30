using Tmds.DBus.Protocol;

namespace RoG.Linux.Platform.Desktop;

/// <summary>
/// „Bewegung reduzieren“ des Desktops über das Settings-Portal (<c>org.freedesktop.portal.Settings</c>, auch in Flatpak
/// ohne Zusatzrecht – dasselbe Portal liefert das Farbschema). Gelesen werden, in dieser Reihenfolge:
/// <list type="number">
/// <item><c>org.freedesktop.appearance reduced-motion</c> (u: 1 = reduzieren; xdg-desktop-portal ≥ 1.20, desktopneutral)</item>
/// <item><c>org.gnome.desktop.interface enable-animations</c> (b: false = reduzieren; GNOME, auch „Animationen“ in den Einstellungen)</item>
/// <item><c>org.kde.kdeglobals.KDE AnimationDurationFactor</c> (0 = Animationen aus; KDE Plasma)</item>
/// </list>
/// Antwortet keine Quelle, gilt „nicht reduzieren“. Änderungen kommen live über <c>SettingChanged</c>.
/// </summary>
public sealed class MotionSettings(string? address = null) : IAsyncDisposable
{
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string Interface = "org.freedesktop.portal.Settings";

    internal static readonly (string Namespace, string Key)[] Sources =
    [
        ("org.freedesktop.appearance", "reduced-motion"),
        ("org.gnome.desktop.interface", "enable-animations"),
        ("org.kde.kdeglobals.KDE", "AnimationDurationFactor"),
    ];

    private readonly string? _address = address ?? DBusAddress.Session;
    private readonly Dictionary<string, bool> _known = new(StringComparer.Ordinal);
    private DBusConnection? _connection;
    private IDisposable? _watch;

    /// <summary>Zeitlimit je Aufruf – ein hängendes Portal darf den Start nicht aufhalten.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Wunsch „Bewegung reduzieren“ hat sich geändert (auf einem Hintergrund-Thread).</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Aktueller Stand: true = Animationen reduzieren.</summary>
    public bool ReduceMotion => Decide(_known);

    /// <summary>Verbinden, alle Quellen lesen und auf Änderungen hören. Ohne Bus/Portal bleibt es bei <c>false</c>.</summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_address) || _connection is not null)
        {
            return ReduceMotion;
        }

        try
        {
            var connection = new DBusConnection(_address);
            _connection = connection;
            await connection.ConnectAsync().AsTask().WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            _watch = await connection.WatchSignalAsync(
                PortalName,
                PortalPath,
                Interface,
                "SettingChanged",
                static (m, _) =>
                {
                    var r = m.GetBodyReader();
                    var ns = r.ReadString();
                    var key = r.ReadString();
                    return (ns, key, Interpret(ns, key, r.ReadVariantValue()));
                },
                (Notification<(string Ns, string Key, bool? Reduce)> n) =>
                {
                    if (n.HasValue)
                    {
                        Update(n.Value.Ns, n.Value.Key, n.Value.Reduce);
                    }
                },
                ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);

            foreach (var (ns, key) in Sources)
            {
                var value = await ReadAsync(connection, ns, key, cancellationToken).ConfigureAwait(false);
                lock (_known)
                {
                    if (value is { } v)
                    {
                        _known[ns] = v;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException or InvalidOperationException)
        {
            // kein Portal (z. B. ohne Desktop): Animationen bleiben an
        }

        return ReduceMotion;
    }

    private void Update(string ns, string key, bool? reduce)
    {
        if (!Sources.Contains((ns, key)) || reduce is null)
        {
            return;
        }

        bool before, after;
        lock (_known)
        {
            before = Decide(_known);
            _known[ns] = reduce.Value;
            after = Decide(_known);
        }

        if (before != after)
        {
            Changed?.Invoke(this, after);
        }
    }

    /// <summary>Erste Quelle in der Reihenfolge von <see cref="Sources"/>, die einen Wert hat.</summary>
    internal static bool Decide(IReadOnlyDictionary<string, bool> known)
    {
        lock (known)
        {
            foreach (var (ns, _) in Sources)
            {
                if (known.TryGetValue(ns, out var reduce))
                {
                    return reduce;
                }
            }
        }

        return false;
    }

    /// <summary>Wert einer Quelle → true = reduzieren, false = nicht, null = unbekannt/kein passender Typ.</summary>
    internal static bool? Interpret(string ns, string key, VariantValue value)
    {
        while (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue(); // „Read“ (Portal-Version 1) verpackt den Wert zweimal
        }

        return (ns, key, value.Type) switch
        {
            ("org.freedesktop.appearance", "reduced-motion", VariantValueType.UInt32) => value.GetUInt32() == 1,
            ("org.gnome.desktop.interface", "enable-animations", VariantValueType.Bool) => !value.GetBool(),
            ("org.kde.kdeglobals.KDE", "AnimationDurationFactor", VariantValueType.Double) => value.GetDouble() <= 0,
            ("org.kde.kdeglobals.KDE", "AnimationDurationFactor", VariantValueType.String) =>
                double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f <= 0 : null,
            _ => null,
        };
    }

    private async Task<bool?> ReadAsync(DBusConnection connection, string ns, string key, CancellationToken cancellationToken)
    {
        foreach (var method in new[] { "ReadOne", "Read" })
        {
            try
            {
                var writer = connection.GetMessageWriter();
                MessageBuffer call;
                try
                {
                    writer.WriteMethodCallHeader(PortalName, PortalPath, Interface, method, "ss", MessageFlags.None);
                    writer.WriteString(ns);
                    writer.WriteString(key);
                    call = writer.CreateMessage();
                }
                finally
                {
                    writer.Dispose();
                }

                return await connection.CallMethodAsync(call, (m, _) => Interpret(ns, key, m.GetBodyReader().ReadVariantValue()), (object?)null)
                    .WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (DBusErrorReplyException ex) when (ex.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod" && method == "ReadOne")
            {
                // älteres Portal ohne ReadOne → Read
            }
            catch (DBusErrorReplyException)
            {
                return null; // Schlüssel unbekannt (org.freedesktop.portal.Error.NotFound)
            }
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        _watch?.Dispose();
        _connection?.Dispose();
        _connection = null;
        return ValueTask.CompletedTask;
    }
}
