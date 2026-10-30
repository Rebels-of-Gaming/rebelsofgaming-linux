using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltungs-Reiter „Wortfilter“: lädt beim ersten Anzeigen, Escape schließt offene Bereiche/Rückfragen.</summary>
public sealed partial class WordFilterView : UserControl
{
    public WordFilterView()
    {
        InitializeComponent();
        ModerationTabHost.Attach(this);
    }
}
