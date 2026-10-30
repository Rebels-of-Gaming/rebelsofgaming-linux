using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Platform;
using RoG.Linux.Tests.Screenshots;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 10 – Barrierefreiheit als Test: (1) alle AXAML-Dateien statisch (Symbol-Knöpfe, Listen, Eingabefelder haben
/// einen Namen für Screenreader; keine fest verdrahteten Farben), (2) der Visual Tree aller Seiten zur Laufzeit (fängt
/// auch Knöpfe aus Code), (3) Tastatur: Tab erreicht Seitenleiste und Seite mit sichtbarem Fokusrahmen, Escape überall gleich.
/// </summary>
public partial class AccessibilityTests
{
    private static readonly string[] s_buttons = ["Button", "ToggleButton", "RepeatButton", "SplitButton", "DropDownButton", "ToggleSplitButton", "HyperlinkButton"];
    private static readonly string[] s_lists = ["ListBox", "TreeView", "DataGrid", "TabControl", "SelectingItemsControl"];
    private static readonly string[] s_inputs = ["TextBox", "AutoCompleteBox", "NumericUpDown", "ComboBox", "DatePicker", "TimePicker", "CalendarDatePicker", "Slider", "MaskedTextBox"];
    private static readonly string[] s_checks = ["CheckBox", "RadioButton", "ToggleSwitch"];

    /// <summary>
    /// Begründete Ausnahmen für feste Farben (Datei → Grund). App.axaml: Fluent-Palette = Theme-Definition (wie RoGTheme).
    /// </summary>
    private static readonly Dictionary<string, string> s_colorExceptions = new(StringComparer.Ordinal)
    {
        ["App.axaml"] = "Fluent-Palette je Design (Theme-Definition, keine Seite)",
        ["MatchesViewModel.cs"] = "Medaillenfarben Silber/Bronze – feste Metallfarben wie in der Windows-App, Gold aus dem Theme",
        ["AdminFieldViewModel.cs"] = "Startwert eines Farbfelds im Formular (Daten, keine Oberflächenfarbe)",
    };

    private static string AppSource => Path.Combine(LocalizationTests.RepoRoot(), "src", "RoG.Linux.App");

    private static IEnumerable<(string File, XElement Element)> AxamlElements() =>
        Directory.EnumerateFiles(AppSource, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f, LoadOptions.SetLineInfo).Descendants().Select(e => (Path.GetRelativePath(AppSource, f), e)));

    private static bool HasName(XElement e) =>
        e.Attributes().Any(a => a.Name.LocalName is "AutomationProperties.Name" or "AutomationProperties.LabeledBy" && !string.IsNullOrWhiteSpace(a.Value));

    /// <summary>Sichtbarer Text im Knopf: Content-Attribut, Textknoten oder ein TextBlock/Run mit Text.</summary>
    private static bool HasText(XElement e) =>
        !string.IsNullOrWhiteSpace((string?)e.Attribute("Content"))
        || e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
        || e.Descendants().Any(d => d.Name.LocalName is "TextBlock" or "SelectableTextBlock" or "AccessText" or "Run" or "ContentPresenter"
            && (d.Name.LocalName == "ContentPresenter" || !string.IsNullOrWhiteSpace((string?)d.Attribute("Text")) || !string.IsNullOrWhiteSpace(d.Value)));

    private static string Where((string File, XElement Element) x) =>
        $"{x.File}:{((System.Xml.IXmlLineInfo)x.Element).LineNumber} <{x.Element.Name.LocalName}>";

    [Fact]
    public void Axaml_icon_buttons_lists_and_inputs_have_accessible_names()
    {
        var missing = new List<string>();
        foreach (var x in AxamlElements())
        {
            var name = x.Element.Name.LocalName;
            var problem = name switch
            {
                _ when s_buttons.Contains(name) && !HasName(x.Element) && !HasText(x.Element) => "Symbol-Knopf ohne AutomationProperties.Name",
                _ when s_lists.Contains(name) && !HasName(x.Element) => "Liste ohne AutomationProperties.Name",
                _ when s_inputs.Contains(name) && !HasName(x.Element) => "Eingabefeld ohne Name/Beschriftung",
                _ when s_checks.Contains(name) && !HasName(x.Element) && !HasText(x.Element) && x.Element.Attribute("OnContent") is null => "Schalter ohne Text/Name",
                _ => null,
            };
            if (problem is not null)
            {
                missing.Add($"{Where(x)}: {problem}");
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [GeneratedRegex(@"^\s*#[0-9A-Fa-f]{3,8}\s*$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"""#[0-9A-Fa-f]{3,8}""|Color\.(Parse|FromRgb|FromArgb|FromUInt32)\(|\bBrushes\.|\bColors\.")]
    private static partial Regex CodeColor();

    private static readonly string[] s_namedColors = ["Red", "White", "Black", "Gray", "Grey", "Green", "Blue", "Yellow", "Orange", "Silver", "DarkGray", "LightGray"];

    [Fact]
    public void Views_use_theme_colors_only()
    {
        // Farben nur per DynamicResource/Theme-Klassen – Themes/*.axaml definieren sie, alles andere bindet sie
        var hits = new List<string>();
        foreach (var x in AxamlElements().Where(x => !x.File.StartsWith("Themes", StringComparison.Ordinal) && !s_colorExceptions.ContainsKey(Path.GetFileName(x.File))))
        {
            if (x.Element.Name.LocalName is "Color" or "SolidColorBrush" or "LinearGradientBrush" or "RadialGradientBrush")
            {
                hits.Add($"{Where(x)}: Pinsel in einer Ansicht");
            }

            hits.AddRange(x.Element.Attributes()
                .Where(a => HexColor().IsMatch(a.Value) || s_namedColors.Contains(a.Value.Trim()))
                .Select(a => $"{Where(x)}: {a.Name.LocalName}=\"{a.Value}\""));
        }

        foreach (var folder in new[] { "Views", "Controls", "ViewModels" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppSource, folder), "*.cs"))
            {
                if (s_colorExceptions.ContainsKey(Path.GetFileName(file)))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                hits.AddRange(lines.Select((l, i) => (l, i)).Where(t => CodeColor().IsMatch(t.l) && !t.l.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .Select(t => $"{Path.GetRelativePath(AppSource, file)}:{t.i + 1}: {t.l.Trim()}"));
            }
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    /// <summary>Begründungen in der Ausnahmeliste dürfen nicht veralten: jede Datei gibt es noch.</summary>
    [Fact]
    public void Color_exceptions_point_to_existing_files()
    {
        var files = Directory.EnumerateFiles(AppSource, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.All(s_colorExceptions.Keys, f => Assert.Contains(f, files));
    }

    // ---- Laufzeit: Visual Tree aller Seiten ----

    /// <summary>Lücken im sichtbaren Baum: Knöpfe ohne Text/Namen, Listen und Eingabefelder ohne Namen.</summary>
    internal static List<string> Audit(Visual root)
    {
        var problems = new List<string>();
        foreach (var c in root.GetVisualDescendants().OfType<Control>())
        {
            // Teile von Vorlagen (Bildlaufleisten, Pfeile im Zahlenfeld …) benennt das Steuerelement selbst
            if (c.TemplatedParent is not null || !c.IsEffectivelyVisible)
            {
                continue;
            }

            var name = AutomationProperties.GetName(c);
            var labeled = AutomationProperties.GetLabeledBy(c) is not null;
            var named = !string.IsNullOrWhiteSpace(name) || labeled;
            var problem = c switch
            {
                ComboBox when !named => "Auswahlfeld ohne Name",
                SelectingItemsControl when !named => "Liste ohne Name",
                TextBox or NumericUpDown or AutoCompleteBox or DatePicker or TimePicker or Slider when !named => "Eingabefeld ohne Name",
                Button or ToggleButton when !named && !HasVisibleText(c) => "Knopf ohne Text/Name",
                _ => null,
            };
            if (problem is not null)
            {
                problems.Add($"{problem}: {Describe(c)}");
            }
        }

        return problems;
    }

    private static bool HasVisibleText(Control button) =>
        button is ContentControl { Content: string s } && !string.IsNullOrWhiteSpace(s)
        || button.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text));

    private static string Describe(Control c)
    {
        var path = c.GetLogicalAncestors().OfType<UserControl>().FirstOrDefault()?.GetType().Name ?? TopLevel.GetTopLevel(c)?.GetType().Name;
        return $"{path} › {c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? string.Empty : "#" + c.Name)} [{string.Join(' ', c.Classes)}]";
    }

    private static IEnumerable<NavItem> Pages(ShellViewModel shell) =>
        shell.MainItems.Concat(shell.FooterItems).Append(shell.SearchItem).Concat(shell.HiddenItems).Where(i => i.Implemented && i.IsVisible);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_page_names_its_buttons_lists_and_inputs(bool guest)
    {
        using var t = guest ? await TestServices.CreateAsync(guest: true) : await TestServices.CreateAsync(TestTokens.Wahke);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        await ScreenshotTests.Settle(200);

        var problems = new SortedSet<string>(StringComparer.Ordinal);
        var pages = Pages(shell).ToList();
        Assert.True(pages.Count >= 18, "zu wenige Seiten gefunden");
        foreach (var page in pages)
        {
            shell.Navigate(page);
            await ScreenshotTests.Settle(250);
            foreach (var p in Audit(window))
            {
                problems.Add($"{page.Key}: {p}");
            }
        }

        window.Close();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Verwaltung mit drei Rechte-Profilen (Moderation/System, Minecraft, Inhalte) – zusammen alle Reiter.</summary>
    [AvaloniaTheory]
    [InlineData("admin")]
    [InlineData("minecraft")]
    [InlineData("content")]
    public async Task Every_admin_tab_names_its_buttons_lists_and_inputs(string profile)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = profile switch
        {
            "admin" => FakeAdminRoutes.MeAdmin,
            "minecraft" => FakeMinecraftAdminRoutes.MeAdmin,
            _ => FakeContentAdminRoutes.MeEditor,
        };
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        Assert.True(shell.NavigateTo("Nav_Admin"));
        var admin = Assert.IsType<AdminViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => admin.HasAccess);

        var problems = new SortedSet<string>(StringComparer.Ordinal);
        Assert.NotEmpty(admin.Tabs);
        foreach (var tab in admin.Tabs.Select(x => x.Tab).ToList())
        {
            admin.SelectedTab = tab;
            if (admin.SelectedContent is LoadableViewModel loadable)
            {
                await ViewModelTests.WaitUntil(() => loadable.ShowContent || loadable.HasError);
            }

            await ScreenshotTests.Settle(250);
            foreach (var p in Audit(window))
            {
                problems.Add($"{tab}: {p}");
            }
        }

        window.Close();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Der Prüfer selbst: findet einen Symbol-Knopf, eine Liste und ein Eingabefeld ohne Namen.</summary>
    [AvaloniaFact]
    public void Audit_finds_unnamed_icon_buttons_lists_and_inputs()
    {
        var panel = new StackPanel();
        panel.Children.Add(new Button { Content = new PathIcon() });
        panel.Children.Add(new Button { Content = "Speichern" });
        panel.Children.Add(new ListBox { ItemsSource = new[] { "ET", "CS2" } });
        panel.Children.Add(new TextBox());
        var named = new TextBox();
        AutomationProperties.SetName(named, "Suche");
        panel.Children.Add(named);
        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();

        var problems = Audit(window);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("Knopf", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("Liste", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("Eingabefeld", StringComparison.Ordinal));
        window.Close();
    }

    // ---- Tastatur ----

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    [AvaloniaTheory]
    [InlineData("Nav_Home")]
    [InlineData("Nav_News")]
    [InlineData("Nav_Members")]
    [InlineData("Nav_Calendar")]
    [InlineData("Nav_Servers")]
    [InlineData("Nav_Settings")]
    public async Task Tab_reaches_sidebar_and_page_with_a_visible_focus_frame(string key)
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        shell.NavigateTo(key);
        await ScreenshotTests.Settle(400);

        var page = window.GetVisualDescendants().OfType<ContentControl>().First(c => c.Content == shell.CurrentPage && c.Content is not null);
        var nav = window.GetVisualDescendants().OfType<RoG.Linux.App.Views.NavPane>().First();
        var seen = new List<IInputElement>();
        for (var i = 0; i < 60; i++)
        {
            Press(window, Key.Tab);
            if (window.FocusManager?.GetFocusedElement() is { } focused)
            {
                seen.Add(focused);
            }
        }

        var controls = seen.OfType<Control>().Distinct().ToList();
        Assert.True(controls.Count >= 5, $"{key}: Tab erreicht nur {controls.Count} Elemente");
        Assert.Contains(controls, c => c.GetVisualAncestors().Contains(nav));
        Assert.Contains(controls, c => c.GetVisualAncestors().Contains(page));

        // Sichtbarer Fokus: das zuletzt fokussierte Element zeigt :focus-visible mit dem Off-White-Rahmen des Themes
        var last = Assert.IsAssignableFrom<Control>(seen[^1]);
        Assert.True(((IPseudoClasses)last.Classes).Contains(":focus-visible"), $"{key}: kein :focus-visible an {Describe(last)}");
        Assert.NotNull(last.FocusAdorner);

        // … und er ist wirklich zu sehen: am linken Rand des Elements (1 px innen) liegt die Rahmenfarbe
        await ScreenshotTests.Settle(50);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        var edge = last.TranslatePoint(new Point(1, last.Bounds.Height / 2), window)!.Value;
        var pixel = PixelAt(frame, (int)edge.X, (int)edge.Y);
        Assert.True(pixel.R > 200 && pixel.G > 200 && pixel.B > 200, $"{key}: Fokusrahmen an {Describe(last)} nicht sichtbar ({pixel})");
        window.Close();
    }

    private static Avalonia.Media.Color PixelAt(Avalonia.Media.Imaging.Bitmap frame, int x, int y)
    {
        var buffer = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }

        return Avalonia.Media.Color.FromRgb(buffer[2], buffer[1], buffer[0]); // BGRA
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_menu_then_the_detail()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = 480;
        window.Height = 800;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();

        // schmal: Menü als Überlagerung – Escape schließt es
        shell.TogglePaneCommand.Execute(null);
        Assert.True(shell.IsOverlayOpen);
        Press(window, Key.Escape);
        Assert.False(shell.IsOverlayOpen);

        // Liste-Detail: Escape schließt das Detail und bleibt auf der Seite
        shell.NavigateTo("Nav_News");
        var news = Assert.IsType<NewsViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => news.ShowContent);
        news.Selected = news.Items[0];
        await ViewModelTests.WaitUntil(() => news.IsDetailOpen);
        Press(window, Key.Escape);
        Assert.False(news.IsDetailOpen);
        Assert.Equal("Nav_News", shell.CurrentItem?.Key);
        window.Close();
    }

    /// <summary>Jedes eigene Fenster schließt mit Escape (nach seinen Überlagerungen) – gleich wie die Editor-Fenster.</summary>
    [Fact]
    public void Every_secondary_window_handles_escape()
    {
        var windows = Directory.EnumerateFiles(Path.Combine(AppSource, "Views"), "*Window.axaml.cs").ToList();
        Assert.True(windows.Count >= 8);
        Assert.All(windows, f => Assert.Contains("Key.Escape", File.ReadAllText(f), StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_chat_window_but_keeps_a_draft()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var chat = new ChatViewModel(t.Services, "01CONV1", "Nyx");
        var window = RoG.Linux.App.Views.ChatWindow.Open(chat);
        await ScreenshotTests.Settle(100);

        var closed = false;
        window.Closed += (_, _) => closed = true;
        chat.Draft = "Bin gleich da";
        Press(window, Key.Escape);
        Assert.False(closed);

        chat.Draft = string.Empty;
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); // schließt schon beim Drücken
        Assert.True(closed);
    }
}
