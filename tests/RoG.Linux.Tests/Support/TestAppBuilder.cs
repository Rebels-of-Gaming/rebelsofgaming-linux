using Avalonia;
using Avalonia.Headless;
using RoG.Linux.Tests.Support;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RoG.Linux.Tests.Support;

/// <summary>Headless-Avalonia mit echtem Skia-Rendering (für Screenshots), ohne Fenster-System.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RoG.Linux.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
