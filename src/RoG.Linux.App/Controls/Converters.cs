using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RoG.Linux.App.Controls;

/// <summary>Kleine Wertumwandler für AXAML.</summary>
public static class Converters
{
    /// <summary><c>true</c> → durchgestrichen (abgesagte Termine).</summary>
    public static FuncValueConverter<bool, TextDecorationCollection?> Strike { get; } =
        new(value => value ? TextDecorations.Strikethrough : null);
}
