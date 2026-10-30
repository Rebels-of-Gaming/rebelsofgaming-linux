using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using RoG.Core.Content;
using RoG.Linux.App.Localization;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Forum: BBCode-Leiste (Tag um die Markierung des Antwort- bzw. Themenfelds), Strg+Enter sendet die Antwort, ein
/// Zitat setzt die Eingabemarke ans Ende der Antwort.
/// </summary>
public sealed partial class ForumView : UserControl
{
    /// <summary>Tag, Beschriftung, Schlüssel für Tooltip/Screenreader (Texte der Windows-App).</summary>
    private static readonly (string Tag, string Label, string Key, FontWeight Weight, FontStyle Style)[] s_formats =
    [
        ("b", "B", "ForumPage_Bold", FontWeight.Bold, FontStyle.Normal),
        ("i", "I", "ForumPage_Italic", FontWeight.Normal, FontStyle.Italic),
        ("u", "U", "ForumPage_Underline", FontWeight.Normal, FontStyle.Normal),
        ("quote", "❝", "ForumPage_QuoteTag", FontWeight.Normal, FontStyle.Normal),
        ("code", "</>", "ForumPage_Code", FontWeight.Normal, FontStyle.Normal),
        ("spoiler", "▒", "ForumPage_Spoiler", FontWeight.Normal, FontStyle.Normal),
        ("url", string.Empty, "ForumPage_Link", FontWeight.Normal, FontStyle.Normal),
    ];

    private ForumViewModel? _forum;

    public ForumView()
    {
        InitializeComponent();
        Fill(ReplyToolbar, ReplyBox);
        Fill(NewToolbar, NewBody);
    }

    private static void Fill(WrapPanel toolbar, TextBox target)
    {
        foreach (var (tag, label, key, weight, style) in s_formats)
        {
            var name = Loc.Get(key + ".AutomationProperties.Name");
            var button = new Button
            {
                Classes = { "format" },
                Content = label.Length == 0
                    ? new PathIcon { Data = (Geometry)Avalonia.Application.Current!.FindResource("IconLink")!, Width = 16, Height = 16 }
                    : new TextBlock { Text = label, FontWeight = weight, FontStyle = style, TextDecorations = tag == "u" ? TextDecorations.Underline : null },
                Tag = tag,
            };
            ToolTip.SetTip(button, Loc.Get(key + ".ToolTipService.ToolTip"));
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) => Wrap(target, tag);
            toolbar.Children.Add(button);
        }
    }

    /// <summary>BBCode um die Markierung setzen (ohne Markierung: zwischen die Tags springen).</summary>
    private static void Wrap(TextBox box, string tag)
    {
        var text = box.Text ?? string.Empty;
        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = Math.Abs(box.SelectionEnd - box.SelectionStart);
        var edit = BbCode.Wrap(text, start, length, tag);
        box.Text = edit.Text; // Bindung schreibt in den Entwurf
        box.Focus();
        box.SelectionStart = edit.SelectionStart;
        box.SelectionEnd = edit.SelectionStart + edit.SelectionLength;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_forum is not null)
        {
            _forum.Thread.DraftReplaced -= OnDraftReplaced;
        }

        _forum = DataContext as ForumViewModel;
        if (_forum is not null)
        {
            _forum.Thread.DraftReplaced += OnDraftReplaced;
        }
    }

    private void OnDraftReplaced(object? sender, EventArgs e)
    {
        ReplyBox.Focus();
        ReplyBox.CaretIndex = ReplyBox.Text?.Length ?? 0;
    }

    private void OnReplyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _forum?.Thread.SendCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            _forum.Thread.SendCommand.Execute(null);
        }
    }
}
