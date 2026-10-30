using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltungs-Reiter „Mod-Log“: lädt beim ersten Anzeigen, Escape schließt offene Bereiche/Rückfragen.</summary>
public sealed partial class ModLogView : UserControl
{
    public ModLogView()
    {
        InitializeComponent();
        ModerationTabHost.Attach(this);
    }
}
