using Avalonia.Markup.Xaml;
using RoG.Core.Localization;

namespace RoG.Linux.App.Localization;

/// <summary>Aktive Übersetzungen – vor dem ersten View gesetzt (App-Start, Tests).</summary>
public static class Loc
{
    public static ILocalizer? Current { get; set; }

    public static string Get(string key) => Current?.GetString(key) ?? key;
}

/// <summary>
/// Text aus den Ressourcen in AXAML: <c>Text="{l:T NewsPage_Title.Text}"</c>. Die Schlüssel sind dieselben wie in der
/// Windows-App (<c>x:Uid</c> + Eigenschaft bzw. <see cref="StringKeys"/>). Sprachwechsel wirkt nach Neustart (wie Windows).
/// </summary>
public sealed class T : MarkupExtension
{
    public T()
    {
    }

    public T(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
