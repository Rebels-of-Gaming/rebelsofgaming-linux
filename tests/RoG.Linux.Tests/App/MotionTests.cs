using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Screenshots;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>
/// Stufe 10: „Bewegung reduzieren“ nimmt alle Übergänge aus dem Baum (Knopf-Skalierung, Ein-/Ausblenden) und ersetzt die
/// laufende „unbestimmte“ Fortschrittsanzeige durch eine ruhende Leiste; ausgeschaltet ist alles wieder wie vorher –
/// auch in Fenstern, die erst danach aufgehen.
/// </summary>
public class MotionTests
{
    /// <summary>
    /// Elemente mit Übergängen. Ausnahme: der Schalterknopf (ToggleSwitch) – Avalonia setzt dessen Gleit-Übergang im Code
    /// als lokalen Wert, den kein Stil überschreibt (0,1 s, nur nach eigenem Klick; Annahme im Bericht).
    /// </summary>
    private static List<string> Moving(Window window) =>
        window.GetVisualDescendants().OfType<Animatable>()
            .Where(a => a.Transitions is { Count: > 0 } && (a as Control)?.TemplatedParent is not ToggleSwitch)
            .Select(a => a.GetType().Name + "#" + (a as Control)?.Name)
            .ToList();

    [AvaloniaFact]
    public async Task Reduce_motion_removes_transitions_and_stops_the_busy_bar()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var window = RoG.Linux.App.App.CreateMainWindow(t.Services);
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;
        await shell.InitializeAsync();
        shell.NavigateTo("Nav_Settings");
        var busy = new ProgressBar { IsIndeterminate = true, Width = 200 };
        window.GetVisualDescendants().OfType<StackPanel>().First(p => p.IsEffectivelyVisible).Children.Add(busy);
        await ScreenshotTests.Settle(300);

        Assert.NotEmpty(Moving(window)); // Fluent bringt Übergänge mit (Knöpfe, Bildlaufleisten)
        var indicator = busy.GetVisualDescendants().OfType<Border>().First(b => b.Name == "IndeterminateProgressBarIndicator");
        Assert.True(indicator.IsVisible);

        try
        {
            MotionService.Apply(true);
            await ScreenshotTests.Settle(100);
            Assert.Contains(MotionService.ReduceClass, window.Classes);
            Assert.Empty(Moving(window));
            Assert.False(indicator.IsVisible);

            // neues Fenster übernimmt den Zustand
            var second = new Window { Width = 300, Height = 200, Content = new Button { Content = "OK" } };
            second.Show();
            await ScreenshotTests.Settle(50);
            Assert.Contains(MotionService.ReduceClass, second.Classes);
            Assert.Empty(Moving(second));
            second.Close();

            MotionService.Apply(false);
            await ScreenshotTests.Settle(100);
            Assert.NotEmpty(Moving(window));
            Assert.True(indicator.IsVisible);
        }
        finally
        {
            MotionService.Apply(false);
            window.Close();
        }
    }
}
