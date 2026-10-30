using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>
/// Verwaltungs-Reiter „Einsprüche“: lädt beim ersten Anzeigen, holt die Entscheidung ins Bild (Fokus in die
/// Begründung), Escape schließt sie.
/// </summary>
public sealed partial class BanAppealsView : UserControl
{
    private AppealDecisionViewModel? _decision;

    public BanAppealsView()
    {
        InitializeComponent();
        ModerationTabHost.Attach(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_decision is not null)
        {
            _decision.PropertyChanged -= OnDecisionChanged;
        }

        _decision = (DataContext as BanAppealsViewModel)?.Decision;
        if (_decision is not null)
        {
            _decision.PropertyChanged += OnDecisionChanged;
        }
    }

    private void OnDecisionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppealDecisionViewModel.IsOpen) && _decision?.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DecisionPanel.BringIntoView();
                NoteBox.Focus();
            });
        }
    }
}
