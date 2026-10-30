using Avalonia.Headless.XUnit;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Schließen des Fensters speichert synchron – darf nicht mit einem laufenden Speichern verklemmen.</summary>
public class SettingsConcurrencyTests
{
    [AvaloniaFact]
    public async Task Saving_now_while_a_save_from_the_ui_thread_is_running_does_not_deadlock()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        for (var i = 0; i < 20; i++)
        {
            var pending = t.Services.UpdateSettingsAsync(s => s with { LastSection = "Nav_News" });
            var linux = t.Services.UpdateLinuxSettingsAsync(s => s);
            var now = Task.Run(() => t.Services.UpdateSettingsNow(s => s with { LastSection = "Nav_Forum" }));

            // UpdateSettingsNow blockiert im Fenster den UI-Thread – hier simuliert durch Warten ohne Nachrichtenschleife
            Assert.True(now.Wait(TimeSpan.FromSeconds(10)));
            await pending;
            await linux;
        }

        Assert.Equal("Nav_Forum", t.Services.Settings.LastSection);
    }
}
