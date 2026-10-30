using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>Verwaltungs-Reiter „Meldungen“: lädt beim ersten Anzeigen, Escape schließt offene Bereiche/Rückfragen.</summary>
public sealed partial class ReportsAdminView : UserControl
{
    public ReportsAdminView()
    {
        InitializeComponent();
        ModerationTabHost.Attach(this);
    }
}
