using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RoG.Linux.App.Controls;
using RoG.Linux.App.Localization;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;

namespace RoG.Linux.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var services = await AppServices.CreateAsync();
        var window = CreateMainWindow(services);
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => services.Dispose();
        window.Show();
        await ((ShellViewModel)window.DataContext!).InitializeAsync();
    }

    /// <summary>Hauptfenster mit Shell – auch von den Headless-Tests (Screenshots) benutzt.</summary>
    public static MainWindow CreateMainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Loc.Current = services.Localizer;
        RichContentView.LinkOpener = uri => services.Browser.OpenAsync(uri);
        RichContentView.ImageSource = (uri, width) => services.Images.LoadAsync(uri, width);
        return new MainWindow { DataContext = new ShellViewModel(services) };
    }
}
