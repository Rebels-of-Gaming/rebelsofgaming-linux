using Avalonia.Controls;
using Avalonia.Threading;
using RoG.Linux.App.ViewModels;

namespace RoG.Linux.App.Views;

/// <summary>Formular sichtbar → Fokus aufs Geburtsdatum (erste Eingabe); Escape schließt über die Squad-Seite.</summary>
public sealed partial class ApplicationFormView : UserControl
{
    public ApplicationFormView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ApplicationFormViewModel form)
            {
                form.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ApplicationFormViewModel.ShowContent) && form.ShowContent)
                    {
                        Dispatcher.UIThread.Post(() => BirthdatePicker.Focus(), DispatcherPriority.Background);
                    }
                };
            }
        };
    }
}
