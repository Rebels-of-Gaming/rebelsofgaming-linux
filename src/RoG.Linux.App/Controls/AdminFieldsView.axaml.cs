using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Controls;

/// <summary>Formularfelder (<see cref="ViewModels.AdminFieldViewModel"/>) untereinander – wie AdminFieldsView der Windows-App.</summary>
public sealed partial class AdminFieldsView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> FieldsProperty = AvaloniaProperty.Register<AdminFieldsView, IEnumerable?>(nameof(Fields));

    public AdminFieldsView()
    {
        InitializeComponent();
    }

    public IEnumerable? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }
}
