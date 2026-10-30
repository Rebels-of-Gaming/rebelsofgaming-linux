using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoG.Linux.App.ViewModels;

/// <summary>Eintrag der Seitenleiste; <see cref="Key"/> wie in der Windows-App (<c>Nav_News</c> …, für „zuletzt geöffnet“).</summary>
public sealed partial class NavItem(string key, string label, Geometry? icon, string webPath, bool implemented) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public Geometry? Icon { get; } = icon;

    /// <summary>Pfad der gleichen Seite auf der Website (für „Im Browser öffnen“, solange die App den Bereich nicht hat).</summary>
    public string WebPath { get; } = webPath;

    /// <summary>Im Spike nativ umgesetzt? Sonst Platzhalter mit Link zur Website.</summary>
    public bool Implemented { get; } = implemented;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>In der Seitenleiste sichtbar (z. B. „Verwaltung“ nur mit Verwaltungsrecht).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>Zähler (Ungelesenes), 0 = ausgeblendet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge), nameof(BadgeText))]
    public partial int Badge { get; set; }

    public bool HasBadge => Badge > 0;

    public string BadgeText => Badge > 99 ? "99+" : Badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
