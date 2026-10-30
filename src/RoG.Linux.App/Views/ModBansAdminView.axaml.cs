using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltungs-Reiter „Sperren“: lädt beim ersten Anzeigen, Escape schließt offene Bereiche/Rückfragen.</summary>
public sealed partial class ModBansAdminView : UserControl
{
    public ModBansAdminView()
    {
        InitializeComponent();
        ModerationTabHost.Attach(this);
    }
}
