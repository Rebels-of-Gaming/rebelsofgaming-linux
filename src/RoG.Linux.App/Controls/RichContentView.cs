using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using RoG.Core.Content;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Zeichnet ein <see cref="RichDocument"/> (HTML der API, im Kern geparst) mit nativen Avalonia-Steuerelementen – keine
/// WebView, kein Skript: Absätze als markierbarer Text, Überschriften in Barlow Condensed, Zitate mit roter Linie, Code
/// in Monospace, Spoiler als Expander, Tabellen als Raster, Bilder nur von der Plattform. Links öffnen den Standardbrowser
/// (über <see cref="LinkOpener"/>, nur http/https/mailto).
/// </summary>
public sealed class RichContentView : UserControl
{
    public static readonly StyledProperty<RichDocument?> DocumentProperty = AvaloniaProperty.Register<RichContentView, RichDocument?>(nameof(Document));

    private readonly StackPanel _root = new() { Spacing = 14 };

    public RichContentView()
    {
        Content = _root;
        Focusable = false;
        // Farben kommen beim Aufbau aus dem aktiven Design → bei Designwechsel neu aufbauen
        ActualThemeVariantChanged += (_, _) => Render();
    }

    /// <summary>Öffnet Links (vom Fenster gesetzt: xdg-open); ohne Wert sind Links nur Text.</summary>
    public static Func<Uri, Task>? LinkOpener { get; set; }

    /// <summary>Lädt Bilder (vom Fenster gesetzt: ImageLoader der App).</summary>
    public static Func<Uri, int, Task<Avalonia.Media.Imaging.Bitmap?>>? ImageSource { get; set; }

    public RichDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        _root.Children.Clear();
        foreach (var block in Document?.Blocks ?? [])
        {
            if (Build(block) is { } control)
            {
                _root.Children.Add(control);
            }
        }
    }

    private static Control? Build(ContentBlock block) => block switch
    {
        ParagraphBlock p => Paragraph(p.Inlines, "body", p.Centered),
        HeadingBlock h => Heading(h),
        ListBlock l => List(l),
        QuoteBlock q => Quote(q),
        CodeBlock c => Code(c),
        ImageBlock i => Picture(i),
        MediaLinkBlock m => MediaLink(m),
        RuleBlock => new Border { Height = 1, Background = Res<IBrush>("RoGLineBrush"), Margin = new Thickness(0, 6) },
        SpoilerBlock s => Spoiler(s),
        TableBlock t => Table(t),
        _ => null,
    };

    private static SelectableTextBlock Paragraph(IReadOnlyList<ContentInline> inlines, string style, bool centered = false)
    {
        var text = new SelectableTextBlock
        {
            Classes = { style },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
            SelectionBrush = Res<IBrush>("RoGActionRedBrush"),
            Inlines = [],
        };
        foreach (var inline in inlines)
        {
            Append(text.Inlines!, inline, TextStyle.None);
        }

        return text;
    }

    private static void Append(InlineCollection target, ContentInline inline, TextStyle outer)
    {
        switch (inline)
        {
            case TextInline t:
                target.Add(Run(t.Text, outer | t.Style));
                break;
            case LineBreakInline:
                target.Add(new LineBreak());
                break;
            case LinkInline l:
                var label = string.Concat(l.Children.Select(Plain)).Trim();
                // Link als eigenes, fokussierbares Element im Textfluss (Tab erreicht ihn, Enter öffnet)
                var link = new Button
                {
                    Classes = { "link" },
                    Padding = default,
                    Margin = default,
                    MinHeight = 0,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Content = new TextBlock { Text = label.Length > 0 ? label : l.Target.ToString(), FontSize = 15, LineHeight = double.NaN },
                };
                ToolTip.SetTip(link, l.Target.ToString());
                AutomationProperties.SetName(link, label.Length > 0 ? label : l.Target.ToString());
                link.Click += async (_, _) =>
                {
                    if (LinkOpener is { } open)
                    {
                        await open(l.Target);
                    }
                };
                target.Add(new InlineUIContainer { Child = link, BaselineAlignment = BaselineAlignment.TextBottom });
                break;
        }
    }

    private static string Plain(ContentInline inline) => inline switch
    {
        TextInline t => t.Text,
        LinkInline l => string.Concat(l.Children.Select(Plain)),
        LineBreakInline => " ",
        _ => string.Empty,
    };

    private static Run Run(string text, TextStyle style)
    {
        var run = new Run(text);
        if (style.HasFlag(TextStyle.Bold))
        {
            run.FontWeight = FontWeight.Bold;
        }

        if (style.HasFlag(TextStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (style.HasFlag(TextStyle.Underline))
        {
            run.TextDecorations = TextDecorations.Underline;
        }
        else if (style.HasFlag(TextStyle.Strikethrough))
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        if (style.HasFlag(TextStyle.Code))
        {
            run.FontFamily = Res<FontFamily>("RoGMonoFont");
            run.Background = Res<IBrush>("RoGBg3Brush");
        }

        if (style.HasFlag(TextStyle.Small))
        {
            run.FontSize = 13;
        }

        return run;
    }

    private static Control Heading(HeadingBlock h)
    {
        var text = string.Concat(h.Inlines.Select(Plain)).Trim();
        var block = new SelectableTextBlock
        {
            Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontFamily = Res<FontFamily>("RoGDisplayFont"),
            FontWeight = h.Level <= 2 ? FontWeight.Bold : FontWeight.SemiBold,
            FontSize = h.Level switch { 1 => 30, 2 => 26, 3 => 21, _ => 18 },
            LetterSpacing = 0.6,
            Foreground = Res<IBrush>("RoGInkBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        AutomationProperties.SetHeadingLevel(block, h.Level);
        return block;
    }

    private static Control List(ListBlock l)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(4, 0, 0, 0) };
        var number = 1;
        foreach (var item in l.Items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
            row.Children.Add(new TextBlock
            {
                Text = l.Ordered ? $"{number++}." : "•",
                Classes = { "body" },
                Foreground = Res<IBrush>("RoGRedBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            var content = new StackPanel { Spacing = 6 };
            foreach (var child in item)
            {
                if (Build(child) is { } c)
                {
                    content.Children.Add(c);
                }
            }

            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Quote(QuoteBlock q)
    {
        var inner = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(q.Cite))
        {
            inner.Children.Add(new TextBlock { Text = q.Cite, Classes = { "muted", "strong" } });
        }

        foreach (var child in q.Blocks)
        {
            if (Build(child) is { } c)
            {
                inner.Children.Add(c);
            }
        }

        return new Border
        {
            BorderBrush = Res<IBrush>("RoGRedBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = Res<IBrush>("RoGBg2Brush"),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(16, 10),
            Child = inner,
        };
    }

    private static Control Code(CodeBlock c) => new Border
    {
        Background = Res<IBrush>("RoGBg3Brush"),
        BorderBrush = Res<IBrush>("RoGLineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = Res<CornerRadius>("RoGControlRadius"),
        Padding = new Thickness(14, 10),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock { Text = c.Code, FontFamily = Res<FontFamily>("RoGMonoFont"), FontSize = 13, Foreground = Res<IBrush>("RoGInkBrush") },
        },
    };

    private static Control Picture(ImageBlock i)
    {
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 480, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(image, i.Alt ?? string.Empty);
        if (ImageSource is { } load)
        {
            _ = SetAsync(image, load(i.Source, 1280));
        }

        Control result = new Border { CornerRadius = Res<CornerRadius>("RoGControlRadius"), ClipToBounds = true, Child = image, HorizontalAlignment = HorizontalAlignment.Left };
        if (i.Link is { } target)
        {
            var button = new Button { Classes = { "link" }, Content = result };
            AutomationProperties.SetName(button, i.Alt ?? target.ToString());
            button.Click += async (_, _) =>
            {
                if (LinkOpener is { } open)
                {
                    await open(target);
                }
            };
            result = button;
        }

        return result;

        static async Task SetAsync(Image image, Task<Avalonia.Media.Imaging.Bitmap?> bitmap) => image.Source = await bitmap;
    }

    private static Control MediaLink(MediaLinkBlock m)
    {
        var label = string.IsNullOrWhiteSpace(m.Label) ? m.Target.Host : m.Label;
        var button = new Button
        {
            Classes = { "secondary" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new PathIcon { Data = Res<Geometry>("IconOpenExternal"), Width = 16, Height = 16 },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        ToolTip.SetTip(button, m.Target.ToString());
        button.Click += async (_, _) =>
        {
            if (LinkOpener is { } open)
            {
                await open(m.Target);
            }
        };
        return button;
    }

    private static Control Spoiler(SpoilerBlock s)
    {
        var inner = new StackPanel { Spacing = 10 };
        foreach (var child in s.Blocks)
        {
            if (Build(child) is { } c)
            {
                inner.Children.Add(c);
            }
        }

        return new Expander { Header = s.Title, Content = inner, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private static Control Table(TableBlock t)
    {
        var columns = t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Cells.Count);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("Auto", Math.Max(1, columns)))) };
        for (var r = 0; r < t.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = t.Rows[r];
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = Paragraph(row.Cells[c], "body");
                if (row.IsHeader)
                {
                    cell.FontWeight = FontWeight.SemiBold;
                }

                var border = new Border
                {
                    Child = cell,
                    Padding = new Thickness(10, 6),
                    BorderBrush = Res<IBrush>("RoGLineBrush"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = row.IsHeader ? Res<IBrush>("RoGBg3Brush") : null,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Child = grid, BorderBrush = Res<IBrush>("RoGLineBrush"), BorderThickness = new Thickness(1), CornerRadius = Res<CornerRadius>("RoGControlRadius"), ClipToBounds = true },
        };
    }

    private static T Res<T>(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is T typed ? typed : default!;
}
