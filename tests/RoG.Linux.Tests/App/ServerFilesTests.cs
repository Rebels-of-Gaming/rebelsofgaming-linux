using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Tests.Support;
using static RoG.Linux.Tests.App.ViewModelTests;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 7 (Teil F): Reiter Configs (Diff, Speichern mit Rückfrage, Versionen, Formular, Laden/Herunterladen), Dateien, Backups.</summary>
public class ServerFilesTests
{
    private const string Slug = FakeServerFilesRoutes.Slug;

    internal static readonly ServerAbilities Full = new(Power: true, Console: true, ConsoleCommand: true, CanCommand: true,
        FilesRead: true, FilesWrite: true, FilesApprove: true, Backups: true);

    internal static readonly ServerAbilities ReadOnly = new(Power: false, Console: true, ConsoleCommand: false, CanCommand: false, FilesRead: true);

    internal static async Task<ServerConfigsViewModel> LoadedConfigsAsync(TestServices t, ServerAbilities? abilities = null)
    {
        var configs = new ServerConfigsViewModel(t.Services, Slug, abilities ?? Full);
        await configs.EnsureLoadedAsync();
        await WaitUntil(() => configs.Versions.Count == 3 && !configs.IsFileLoading);
        Assert.Equal("etmain/banners.cfg", configs.SelectedFile?.Path); // erste Datei wie Windows
        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/server.cfg");
        await WaitUntil(() => configs.Content == FakeServerFilesRoutes.ServerCfg && configs.Versions.Count == 3 && !configs.IsFileLoading);
        return configs;
    }

    private static string Put(TestServices t, string suffix) =>
        t.Api.Writes.Single(w => w.Method == "PUT" && w.Path == $"/v1/servers/{Slug}/configs/{suffix}").Body;

    [AvaloniaFact]
    public async Task Configs_show_the_whitelist_and_save_only_after_diff_and_confirmation()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t);
        Assert.Equal(3, configs.Files.Count);
        Assert.Equal("etmain/banners.cfg", configs.Files[0].Path); // sortiert
        Assert.Equal(FakeServerFilesRoutes.ServerCfg, configs.Content);
        Assert.EndsWith("\n16", configs.LineNumbers, StringComparison.Ordinal);
        Assert.False(configs.IsDirty);
        Assert.False(configs.SaveCommand.CanExecute(null));
        Assert.Equal(["Nur speichern", "Speichern + neu laden", "Speichern + Neustart"], configs.AfterOptions.Select(o => o.Label));
        Assert.StartsWith("28.10.2026", configs.Versions[0].Text, StringComparison.Ordinal);
        Assert.Contains("Slots für Clanwar erhöht", configs.Versions[0].Text, StringComparison.Ordinal);

        configs.Content = configs.Content.Replace("sv_maxclients 24", "sv_maxclients 32", StringComparison.Ordinal);
        configs.Note = "Clanwar-Abend";
        configs.SelectedAfter = configs.AfterOptions[1];
        Assert.True(configs.IsDirty);
        Assert.True(configs.SaveCommand.CanExecute(null));

        var save = configs.SaveCommand.ExecuteAsync(null);
        await WaitUntil(() => configs.Confirm.IsOpen);
        Assert.Equal(t.Services.T("Linux_ConfigSaveTitle"), configs.Confirm.Title);
        Assert.Contains("etmain/server.cfg", configs.Confirm.Text, StringComparison.Ordinal);
        Assert.Contains("Speichern + neu laden", configs.Confirm.Text, StringComparison.Ordinal);
        Assert.True(configs.HasDiff);
        Assert.Contains(configs.DiffLines, l => l.IsRemoved && l.Text == "set sv_maxclients 24" && l.OldNumber == "4");
        Assert.Contains(configs.DiffLines, l => l.IsAdded && l.Text == "set sv_maxclients 32" && l.NewNumber == "4");
        Assert.Contains(configs.DiffLines, l => !l.IsAdded && !l.IsRemoved); // Kontext
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PUT");

        configs.Confirm.AcceptCommand.Execute(null);
        await save;
        var body = Put(t, FakeServerFilesRoutes.ServerCfgId);
        Assert.Contains("sv_maxclients 32", body, StringComparison.Ordinal);
        Assert.Contains("\"after\":\"exec\"", body, StringComparison.Ordinal);
        Assert.Contains("\"note\":\"Clanwar-Abend\"", body, StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Config_Saved"), configs.Feedback.Message);
        Assert.True(configs.Feedback.IsSuccess);
        Assert.Equal(string.Empty, configs.Note);
        Assert.False(configs.HasDiff);
    }

    [AvaloniaFact]
    public async Task Configs_with_validation_errors_or_a_cancelled_question_are_not_saved()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t);

        configs.Content += "\nset sv_cheats 1";
        await configs.SaveCommand.ExecuteAsync(null);
        Assert.False(configs.Confirm.IsOpen);
        Assert.True(configs.HasErrors);
        Assert.Contains("Zeile 17", Assert.Single(configs.Errors), StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Linux_ConfigHasErrors"), configs.Feedback.Message);

        configs.Content = configs.Content.Replace("\nset sv_cheats 1", "\nset g_warmup 10", StringComparison.Ordinal);
        Assert.False(configs.HasDiff); // Text geändert → alte Prüfung weg
        var save = configs.SaveCommand.ExecuteAsync(null);
        await WaitUntil(() => configs.Confirm.IsOpen);
        Assert.True(configs.CloseOverlay()); // Escape
        await save;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PUT");
        Assert.True(configs.IsDirty);

        // Prüfen ohne Änderung gegenüber dem Server
        configs.Content = FakeServerFilesRoutes.ServerCfg;
        await configs.PreviewCommand.ExecuteAsync(null);
        Assert.True(configs.IsUnchanged);
        Assert.Empty(configs.DiffLines);
    }

    [AvaloniaFact]
    public async Task Configs_roll_back_a_version_and_ask_before_dropping_edits()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t);

        var rollback = configs.Versions[1].RollbackCommand.ExecuteAsync(null);
        await WaitUntil(() => configs.Confirm.IsOpen);
        Assert.Equal(t.Services.T("Config_RollbackTitle"), configs.Confirm.Title);
        Assert.Contains("Kaito", configs.Confirm.Text, StringComparison.Ordinal);
        configs.Confirm.AcceptCommand.Execute(null);
        await rollback;
        var write = Assert.Single(t.Api.Writes, w => w.Path.EndsWith("/rollback", StringComparison.Ordinal));
        Assert.Equal($"/v1/servers/{Slug}/configs/versions/01JVER0000000000000000002/rollback", write.Path);
        Assert.Contains("\"after\":\"save\"", write.Body, StringComparison.Ordinal);

        // Ungespeicherte Änderung: Wechsel fragt nach, Abbrechen bleibt bei der Datei
        configs.Content += "\n// Test";
        var serverCfg = configs.SelectedFile;
        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/mapcycle.cfg");
        await WaitUntil(() => configs.Confirm.IsOpen);
        Assert.Equal(t.Services.T("Linux_ConfigDiscardTitle"), configs.Confirm.Title);
        configs.Confirm.CancelCommand.Execute(null);
        await WaitUntil(() => configs.SelectedFile == serverCfg);
        Assert.EndsWith("// Test", configs.Content, StringComparison.Ordinal);

        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/mapcycle.cfg");
        await WaitUntil(() => configs.Confirm.IsOpen);
        configs.Confirm.AcceptCommand.Execute(null);
        await WaitUntil(() => configs.Content == FakeServerFilesRoutes.Mapcycle && !configs.IsFileLoading);
        Assert.True(configs.NeedsApproval is false); // mit Freigaberecht kein Antrag
        Assert.True(configs.HasTemplate);
    }

    [AvaloniaFact]
    public async Task Configs_template_form_validates_like_the_website_and_keeps_passwords()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t);
        Assert.True(configs.HasTemplate);
        await configs.OpenTemplateCommand.ExecuteAsync(null);
        var form = Assert.IsType<ConfigTemplateFormViewModel>(configs.Template);
        Assert.Equal("Server-Einstellungen (ET/RtCW)", form.Title);
        Assert.Equal(5, form.Fields.Count);
        var slots = form.Fields.Single(f => f.Name == "sv_maxclients");
        Assert.Equal("24", slots.Text);
        var password = form.Fields.Single(f => f.Name == "g_password");
        Assert.True(password.IsPassword);
        Assert.True(password.CanClearPassword);
        Assert.Equal(t.Services.T("Template_PasswordKeep"), password.Placeholder);
        Assert.Equal(string.Empty, password.Text);

        slots.Text = "99";
        await form.ApplyCommand.ExecuteAsync(null);
        Assert.True(slots.HasError);
        Assert.Contains("Slots", form.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PUT");

        slots.Text = "20";
        form.Note = "Clanwar";
        await form.ApplyCommand.ExecuteAsync(null);
        var body = Put(t, FakeServerFilesRoutes.ServerCfgId + "/template");
        Assert.Contains("\"sv_maxclients\":20", body, StringComparison.Ordinal);
        Assert.DoesNotContain("g_password", body, StringComparison.Ordinal); // leer = unverändert
        Assert.Contains("\"note\":\"Clanwar\"", body, StringComparison.Ordinal);
        Assert.Null(configs.Template);
        Assert.Equal(t.Services.T("Config_Saved"), configs.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task Configs_load_a_local_file_into_the_editor_and_download_the_server_state()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t);
        var local = "set sv_hostname \"RoG Scrim\"\r\nset sv_maxclients 12\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(local)).ToArray();
        await configs.ImportAsync("scrim.cfg", bytes.Length, () => Task.FromResult<Stream>(new MemoryStream(bytes)));
        Assert.Equal("set sv_hostname \"RoG Scrim\"\nset sv_maxclients 12\n", configs.Content);
        Assert.True(configs.IsDirty);
        Assert.True(configs.Feedback.IsInfo);
        Assert.Contains("scrim.cfg", configs.Feedback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "PUT"); // erst nach Prüfung + Bestätigung

        await configs.ImportAsync("riesig.cfg", ServerConfigsViewModel.MaxBytes + 1, () => throw new InvalidOperationException("nicht öffnen"));
        Assert.Equal(t.Services.T("Linux_ConfigImportTooLarge"), configs.Feedback.Message);
        await configs.ImportAsync("bild.png", 4, () => Task.FromResult<Stream>(new MemoryStream([0x89, 0x50, 0x00, 0xFF])));
        Assert.Equal(t.Services.T("Linux_ConfigImportNotText"), configs.Feedback.Message);

        await configs.DownloadCommand.ExecuteAsync(null);
        var saved = Path.Combine(t.Services.DownloadDirectory, "server.cfg");
        Assert.Equal(saved, configs.SavedPath);
        Assert.Equal(FakeServerFilesRoutes.ServerCfg, await File.ReadAllTextAsync(saved)); // Stand des Servers, nicht der Editor
        await configs.DownloadCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(t.Services.DownloadDirectory, "server (2).cfg"), configs.SavedPath);
        await configs.ShowInFolderCommand.ExecuteAsync(null);
        Assert.Equal(configs.SavedPath, Assert.Single(t.Files.Shown));
    }

    [AvaloniaFact]
    public async Task Configs_are_read_only_without_write_permission()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t, ReadOnly);
        Assert.True(configs.IsReadOnly);
        Assert.False(configs.HasTemplate);
        Assert.False(configs.Versions[0].CanWrite);
        Assert.Single(configs.AfterOptions);
        configs.Content += "\nx";
        Assert.False(configs.SaveCommand.CanExecute(null));
        Assert.False(configs.PreviewCommand.CanExecute(null));
        await configs.Versions[0].RollbackCommand.ExecuteAsync(null);
        Assert.False(configs.Confirm.IsOpen);
        Assert.Empty(t.Api.Writes);
    }

    [AvaloniaFact]
    public async Task Configs_without_approval_right_on_a_four_eyes_file_hint_at_the_request()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var configs = await LoadedConfigsAsync(t, Full with { FilesApprove = false });
        Assert.False(configs.NeedsApproval); // server.cfg ohne Pflicht
        configs.SelectedFile = configs.Files.Single(f => f.Path == "etmain/mapcycle.cfg");
        await WaitUntil(() => configs.Content == FakeServerFilesRoutes.Mapcycle && !configs.IsFileLoading);
        Assert.True(configs.NeedsApproval);
        configs.Content = configs.Content.Replace("map radar", "map fueldump", StringComparison.Ordinal);
        var save = configs.SaveCommand.ExecuteAsync(null);
        await WaitUntil(() => configs.Confirm.IsOpen);
        Assert.Contains(t.Services.T("Linux_ConfigApprovalHint"), configs.Confirm.Text, StringComparison.Ordinal);
        configs.Confirm.CancelCommand.Execute(null);
        await save;
    }

    [AvaloniaFact]
    public async Task Configs_errors_are_translated()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Failures[$"/v1/servers/{Slug}/configs"] = (HttpStatusCode.Forbidden, FakeAccountRoutes.Problem(403, "insufficient_scope"));
        var configs = new ServerConfigsViewModel(t.Services, Slug, Full);
        await configs.EnsureLoadedAsync();
        Assert.True(configs.HasError);
        Assert.Equal(t.Services.Localizer.GetError("insufficient_scope"), configs.ErrorMessage);
        t.Api.Failures.Clear();
        await configs.RefreshCommand.ExecuteAsync(null);
        Assert.True(configs.ShowContent);
    }

    [AvaloniaFact]
    public async Task Files_browse_folders_preview_and_manage_backups()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var files = new ServerFilesViewModel(t.Services, Slug, Full);
        await files.EnsureLoadedAsync();
        Assert.Equal(["etmain", "legacy", "etlded.x86_64"], files.Items.Select(i => i.Name));
        Assert.False(files.CanGoUp);
        Assert.EndsWith(" MB", files.Items[2].Meta.Split(" · ")[0], StringComparison.Ordinal);

        await files.Items[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal("etmain", files.CurrentDirectory);
        Assert.Equal(["/", "etmain"], files.Crumbs.Select(c => c.Name));
        Assert.True(files.Crumbs[1].IsCurrent);
        Assert.Equal("mapscripts", files.Items[0].Name); // Ordner zuerst
        var cfg = files.Items.Single(i => i.Name == "server.cfg");
        Assert.True(cfg.CanCopy);
        Assert.False(cfg.CanChange);
        var bak = files.Items.Single(i => i.Name == "server.cfg.bak");
        Assert.True(bak.CanChange);
        Assert.True(bak.IsBackup);
        Assert.False(files.Items.Single(i => i.Name == "pak0.pk3").CanCopy);

        await cfg.OpenCommand.ExecuteAsync(null);
        Assert.Equal("etmain/server.cfg", files.PreviewTitle);
        Assert.Equal(FakeServerFilesRoutes.ServerCfg, files.Preview);
        await files.Items.Single(i => i.Name == "etconsole.log").OpenCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Files_TooLarge"), files.Preview);
        Assert.True(files.CloseOverlay()); // Escape schließt die Vorschau
        Assert.False(files.HasPreview);

        // Sicherungskopie: Vorschlag trifft keinen vorhandenen Namen
        var copy = cfg.CopyCommand.ExecuteAsync(null);
        await WaitUntil(() => files.Prompt.IsOpen);
        Assert.Equal("etmain/server.cfg.2.bak", files.Prompt.Text);
        files.Prompt.AcceptCommand.Execute(null);
        await copy;
        var write = Assert.Single(t.Api.Writes, w => w.Path.EndsWith("/files/copy", StringComparison.Ordinal));
        Assert.Contains("\"to\":\"etmain/server.cfg.2.bak\"", write.Body, StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Files_Copied"), files.Feedback.Message);

        // Umbenennen nur auf .bak/.old
        var rename = bak.RenameCommand.ExecuteAsync(null);
        await WaitUntil(() => files.Prompt.IsOpen);
        files.Prompt.Text = "etmain/server.cfg";
        files.Prompt.AcceptCommand.Execute(null);
        await rename;
        Assert.Equal(t.Services.T("Files_BackupNameHint"), files.Feedback.Message);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/files/rename", StringComparison.Ordinal));
        rename = bak.RenameCommand.ExecuteAsync(null);
        await WaitUntil(() => files.Prompt.IsOpen);
        files.Prompt.Text = "etmain/server.cfg.vor-clanwar.old";
        files.Prompt.AcceptCommand.Execute(null);
        await rename;
        Assert.Contains("\"to\":\"etmain/server.cfg.vor-clanwar.old\"", t.Api.Writes.Single(w => w.Path.EndsWith("/files/rename", StringComparison.Ordinal)).Body, StringComparison.Ordinal);

        bak = files.Items.Single(i => i.Name == "server.cfg.bak");
        var delete = bak.DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => files.Confirm.IsOpen);
        Assert.Equal(t.Services.T("Files_DeleteTitle"), files.Confirm.Title);
        files.Confirm.AcceptCommand.Execute(null);
        await delete;
        Assert.Contains("\"path\":\"etmain/server.cfg.bak\"", t.Api.Writes.Single(w => w.Path.EndsWith("/files/delete", StringComparison.Ordinal)).Body, StringComparison.Ordinal);
        Assert.DoesNotContain(files.Items, i => i.Name == "server.cfg.bak");
        Assert.Equal(t.Services.T("Files_Deleted"), files.Feedback.Message);

        // Herunterladen in den Downloads-Ordner (nur Text bis 512 KB)
        await files.Items.Single(i => i.Name == "server.cfg").DownloadCommand.ExecuteAsync(null);
        Assert.Equal(FakeServerFilesRoutes.ServerCfg, await File.ReadAllTextAsync(Path.Combine(t.Services.DownloadDirectory, "server.cfg")));
        Assert.True(files.HasSavedPath);
        await files.Items.Single(i => i.Name == "pak0.pk3").DownloadCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Linux_FilesDownloadTooLarge"), files.Feedback.Message);

        await files.Items.Single(i => i.Name == "mapscripts").OpenCommand.ExecuteAsync(null);
        Assert.True(files.IsEmpty);
        await files.Crumbs[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, files.CurrentDirectory);
        Assert.Equal(3, files.Items.Count);
    }

    [AvaloniaFact]
    public async Task Files_without_write_permission_only_read_and_cancelled_prompts_do_nothing()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var files = new ServerFilesViewModel(t.Services, Slug, ReadOnly);
        await files.OpenAsync("etmain");
        Assert.All(files.Items, i => Assert.False(i.CanCopy || i.CanChange));
        Assert.True(files.Items.Single(i => i.Name == "server.cfg").CanDownload);

        var writable = new ServerFilesViewModel(t.Services, Slug, Full);
        await writable.OpenAsync("etmain");
        var copy = writable.Items.Single(i => i.Name == "server.cfg").CopyCommand.ExecuteAsync(null);
        await WaitUntil(() => writable.Prompt.IsOpen);
        Assert.True(writable.CloseOverlay()); // Escape
        await copy;
        Assert.Empty(t.Api.Writes);
        await writable.UpCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, writable.CurrentDirectory);
    }

    [AvaloniaFact]
    public async Task Backups_list_create_download_lock_restore_and_delete()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var backups = new ServerBackupsViewModel(t.Services, Slug, Full);
        await backups.EnsureLoadedAsync();
        Assert.Equal(4, backups.Items.Count);
        var running = backups.Items[0];
        Assert.True(running.IsRunning);
        Assert.False(running.CanDownload);
        Assert.Contains(t.Services.T("Backup_Running"), running.Meta, StringComparison.Ordinal);
        var ready = backups.Items[1];
        Assert.Equal("Vor Map-Update", ready.Name);
        Assert.Contains("700 MB", ready.Meta, StringComparison.Ordinal);
        Assert.True(ready.CanRestore);
        Assert.True(backups.Items[2].IsFailed);
        Assert.Contains(t.Services.T("Backup_Failed"), backups.Items[2].Meta, StringComparison.Ordinal);
        var locked = backups.Items[3];
        Assert.False(locked.CanDelete);
        Assert.Equal(t.Services.T("Backup_Unlock"), locked.LockLabel);

        backups.NewName = "Vor Clanwar";
        await backups.CreateCommand.ExecuteAsync(null);
        Assert.Contains("\"name\":\"Vor Clanwar\"", t.Api.Writes.Single(w => w.Method == "POST" && w.Path == $"/v1/servers/{Slug}/backups").Body, StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Backup_Created"), backups.Feedback.Message);
        Assert.Equal(string.Empty, backups.NewName);

        ready = backups.Items[1];
        await ready.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("https://node1.example.test/download/backup?token=kurzlebig", Assert.Single(t.Browser.Opened).ToString());

        var restore = ready.RestoreCommand.ExecuteAsync(null);
        await WaitUntil(() => backups.Confirm.IsOpen);
        Assert.Contains("„Vor Map-Update“", backups.Confirm.Text, StringComparison.Ordinal);
        backups.Confirm.AcceptCommand.Execute(null);
        await restore;
        Assert.Contains("\"truncate\":false", t.Api.Writes.Single(w => w.Path.EndsWith("/restore", StringComparison.Ordinal)).Body, StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Backup_RestoreStarted"), backups.Feedback.Message);

        await ready.ToggleLockCommand.ExecuteAsync(null);
        Assert.Contains("\"locked\":true", t.Api.Writes.Single(w => w.Path.EndsWith("/lock", StringComparison.Ordinal)).Body, StringComparison.Ordinal);

        ready = backups.Items[1];
        var delete = ready.DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => backups.Confirm.IsOpen);
        backups.Confirm.AcceptCommand.Execute(null);
        await delete;
        Assert.Single(t.Api.Writes, w => w.Method == "DELETE" && w.Path == $"/v1/servers/{Slug}/backups/{ready.Uuid}");
        Assert.DoesNotContain(ready, backups.Items);
    }

    [AvaloniaFact]
    public async Task Backups_restore_needs_power_and_a_shared_feedback_is_used()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var shared = new FeedbackViewModel();
        var backups = new ServerBackupsViewModel(t.Services, Slug, Full with { Power = false }, shared);
        Assert.False(backups.ShowOwnFeedback);
        await backups.EnsureLoadedAsync();
        Assert.False(backups.Items[1].CanRestore);
        Assert.True(backups.Items[1].CanDownload);

        t.Api.Failures[$"/v1/servers/{Slug}/backups/{backups.Items[1].Uuid}/lock"] = (HttpStatusCode.Forbidden, FakeAccountRoutes.Problem(403, "approval_required"));
        await backups.Items[1].ToggleLockCommand.ExecuteAsync(null);
        Assert.Same(shared, backups.Feedback);
        Assert.True(shared.IsError);
        Assert.Equal(t.Services.Localizer.GetError("approval_required"), shared.Message);
    }
}
