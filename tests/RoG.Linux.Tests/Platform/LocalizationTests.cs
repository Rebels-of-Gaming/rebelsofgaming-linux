using RoG.Core.Localization;
using RoG.Linux.Platform.Localization;

namespace RoG.Linux.Tests.Platform;

public class LocalizationTests
{
    public static TheoryData<string> Languages => new() { "de", "en", "fr" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Windows_texts_are_embedded_for_every_language(string language)
    {
        var texts = ReswLocalizer.Load(language, "Resources");
        Assert.True(texts.Count > 1000, $"{language}: nur {texts.Count} Texte");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Linux_texts_have_the_same_keys_in_every_language(string language)
    {
        var german = ReswLocalizer.Load("de", "Linux");
        var other = ReswLocalizer.Load(language, "Linux");
        Assert.NotEmpty(german);
        Assert.Equal(german.Keys.Order(StringComparer.Ordinal), other.Keys.Order(StringComparer.Ordinal));
        Assert.All(other, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{language}: {kv.Key} leer"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Linux_texts_do_not_mention_windows(string language)
    {
        var l = new ReswLocalizer(language);
        Assert.DoesNotContain("Windows", l.GetString("LoginPage_Privacy.Text"), StringComparison.Ordinal);
        Assert.Contains("Linux", l.GetString("Settings_AppVersion"), StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_text_overrides_windows_text()
    {
        var l = new ReswLocalizer("de");
        Assert.Contains("Schlüsselbund", l.GetString("LoginPage_Privacy.Text"), StringComparison.Ordinal);
        Assert.Equal("IM BROWSER ANMELDEN", l.GetString("LoginPage_SignIn.Content"));
    }

    [Fact]
    public void Unknown_key_is_returned_visibly_and_errors_fall_back()
    {
        var l = new ReswLocalizer("en");
        Assert.Equal("Does_Not_Exist", l.GetString("Does_Not_Exist"));
        Assert.Equal(l.GetString("Error_unknown_error"), l.GetError("some_new_code"));
        Assert.Equal("Sign-in failed", l.GetString("LoginPage_Error.Title"));
    }

    [Fact]
    public void Unknown_language_falls_back_to_german() => Assert.Equal("de", new ReswLocalizer("xx").Language);

    [Fact]
    public void Attached_property_keys_have_short_alias()
    {
        Assert.Equal("A.AutomationProperties.Name", ReswLocalizer.ShortKey("A.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"));
        Assert.Null(ReswLocalizer.ShortKey("A.Text"));
        var l = new ReswLocalizer("de");
        Assert.Equal("Unterhaltungen suchen", l.GetString("MessagesPage_Search.AutomationProperties.Name"));
    }

    [Fact]
    public void Keys_used_by_the_linux_views_exist()
    {
        // Jeder Schlüssel aus den AXAML-Dateien muss in den Ressourcen stehen (sonst stünde der Schlüssel in der Oberfläche)
        var root = RepoRoot();
        var keys = Directory.EnumerateFiles(Path.Combine(root, "src", "RoG.Linux.App"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\{l:T ([^,}]+)[^}]*\}").Select(m => m.Groups[1].Value.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(keys);
        foreach (var language in new[] { "de", "en", "fr" })
        {
            var l = new ReswLocalizer(language);
            Assert.All(keys, k => Assert.NotEqual(k, l.GetString(k)));
        }
    }

    [Fact]
    public void Linux_keys_used_in_code_exist()
    {
        // Linux-Texte, die ViewModels per T(…)/Format(…) holen („Linux_…“ als Zeichenkette im Code)
        var root = RepoRoot();
        var keys = Directory.EnumerateFiles(Path.Combine(root, "src", "RoG.Linux.App"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(Linux_[A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(keys);
        foreach (var language in new[] { "de", "en", "fr" })
        {
            var l = new ReswLocalizer(language);
            Assert.All(keys, k => Assert.NotEqual(k, l.GetString(k)));
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoG.Linux.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("RoG.Linux.sln nicht gefunden");
    }
}
