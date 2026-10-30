using Avalonia;
using Avalonia.Controls;

namespace RoG.Linux.App.Views;

/// <summary>
/// Reiter „Uploads“ der Verwaltung – DataContext ist ein <see cref="ViewModels.UploadReportsAdminViewModel"/>. Breit:
/// Meldungen links, Speicher rechts; schmal untereinander in voller Breite.
/// </summary>
public sealed partial class UploadsAdminView : UserControl
{
    public UploadsAdminView()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var width = Math.Max(0, e.NewSize.Width - 32); // Rand links/rechts
        var wide = width >= 420 + 24 + 300;
        ReportsColumn.Width = wide ? Math.Min(720, width - 24 - 300) : width;
        StorageColumn.Width = wide ? 300 : width;
    }
}
