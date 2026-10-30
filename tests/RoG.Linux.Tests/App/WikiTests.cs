using Avalonia.Headless.XUnit;
using RoG.Core.Content;
using RoG.Core.Navigation;
using RoG.Core.Security;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.Platform.Secrets;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 8: Wiki – Baum, Seite, Links, Suche, Kategorien, Bearbeiten mit Konflikt, Versionen, neue Seite, offline.</summary>
public class WikiTests
{
    [AvaloniaFact]
    public async Task Tree_page_toc_watch_and_rights()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);

        Assert.Equal(["Clan", "Technik", "Intern · " + t.Services.T("Wiki_Internal")], wiki.Namespaces.Select(n => n.Label));
        Assert.Equal("clan", wiki.SelectedNamespace?.Value);
        Assert.Equal(["Clan-Regeln", "Verhalten im Voice", "Neu im Clan"], wiki.Items.Select(i => i.Title));
        Assert.Equal([0, 1, 0], wiki.Items.Select(i => i.Depth));
        Assert.True(wiki.CanEdit);
        Assert.True(wiki.CanCreate);
        Assert.True(wiki.CanApprove);
        Assert.False(wiki.IsGuest);
        Assert.False(wiki.IsDetailOpen);

        wiki.Selected = wiki.Items[0];
        await ViewModelTests.WaitUntil(() => wiki.ShowPage);
        Assert.True(wiki.IsDetailOpen);
        Assert.Equal("Clan-Regeln", wiki.Title);
        Assert.Equal("CLAN", wiki.NamespaceName);
        Assert.Contains("Regeln", wiki.PageMeta, StringComparison.Ordinal);
        Assert.Equal(["Allgemein", "Voice", "Server", "Strafen"], wiki.Toc.Select(e => e.Text));
        Assert.True(wiki.HasToc);
        Assert.Contains(wiki.Document!.Blocks, b => b is TableBlock);
        Assert.False(wiki.IsOfflineCopy);
        Assert.False(wiki.IsSavedOffline); // ohne Schlüsselbund keine Offline-Kopie
        Assert.True(wiki.ShowOfflineUnavailable);

        string? heading = null;
        wiki.HeadingRequested += (_, h) => heading = h;
        wiki.GoToHeadingCommand.Execute(wiki.Toc[2]);
        Assert.Equal("Server", heading);

        Assert.Equal(t.Services.T("Wiki_Watch").ToUpper(t.Services.Culture), wiki.WatchLabel);
        await wiki.ToggleWatchCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Wiki_Unwatch").ToUpper(t.Services.Culture), wiki.WatchLabel);
        Assert.Equal(t.Services.T("Wiki_Watching"), wiki.Feedback.Message);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/wiki/pages/" + FakeWikiRoutes.Rules + "/watch");

        await wiki.OpenInBrowserCommand.ExecuteAsync(null);
        Assert.Equal(FakeWikiRoutes.Wiki + "/clan/regeln", t.Browser.Opened[^1].ToString());

        // Bereich wechseln → Baum des Bereichs
        wiki.SelectedNamespace = wiki.Namespaces[1];
        await ViewModelTests.WaitUntil(() => wiki.Items.Any(i => i.Title == "ET-Server einrichten"));
        Assert.Equal([0, 1, 1], wiki.Items.Select(i => i.Depth));

        // Escape schließt die Seite
        ((IDetailPage)wiki).CloseDetail();
        Assert.False(wiki.IsDetailOpen);
        Assert.True(wiki.ShowSelectHint);
    }

    [AvaloniaFact]
    public async Task Wiki_links_deep_links_and_search_open_the_page_in_the_app()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = FakeWikiRoutes.MeWithWikiRights;
        var shell = new ShellViewModel(t.Services);
        await shell.InitializeAsync();

        // rog://wiki/bereich/seite
        Assert.True(shell.OpenDeepLink(new Uri("rog://wiki/clan/regeln")));
        var wiki = Assert.IsType<WikiViewModel>(shell.CurrentPage);
        await ViewModelTests.WaitUntil(() => wiki.Title == "Clan-Regeln" && wiki.ShowPage);
        await ViewModelTests.WaitUntil(() => wiki.Selected?.Id == FakeWikiRoutes.Rules);

        // Link im Seiteninhalt (RichContentView → OpenLinkAsync) auf eine Seite in einem anderen Bereich
        var link = FindLink(wiki.Document!, "et-server");
        Assert.Equal(FakeWikiRoutes.Wiki + "/technik/server/et-server", link.ToString());
        await t.Services.OpenLinkAsync(link);
        await ViewModelTests.WaitUntil(() => wiki.Title == "ET-Server einrichten" && wiki.ShowPage);
        Assert.Same(wiki, shell.CurrentPage);
        Assert.Equal("technik", wiki.SelectedNamespace?.Value);
        await ViewModelTests.WaitUntil(() => wiki.Selected?.Id == FakeWikiRoutes.EtServer);
        Assert.Equal(["Ports", "Mods"], wiki.Toc.Select(e => e.Text));

        // Treffer der globalen Suche bzw. Benachrichtigung (Website-Adresse des Wikis)
        Assert.NotNull(t.Services.RouteFor(new Uri(FakeWikiRoutes.Wiki + "/clan/neu-im-clan")));
        await t.Services.OpenLinkAsync(new Uri(FakeWikiRoutes.Wiki + "/clan/neu-im-clan"));
        await ViewModelTests.WaitUntil(() => wiki.Title == "Neu im Clan" && wiki.ShowPage);
        Assert.Equal("clan", wiki.SelectedNamespace?.Value);

        // nur ein Bereich → Bereich wählen; Dateien gibt es nur auf der Website
        Assert.True(shell.OpenDeepLink(new Uri("rog://wiki/technik")));
        await ViewModelTests.WaitUntil(() => wiki.SelectedNamespace?.Value == "technik" && !wiki.HasPage);
        Assert.True(shell.OpenDeepLink(new Uri("rog://wiki/file/supply-karte.png")));
        await ViewModelTests.WaitUntil(() => t.Browser.Opened.Count > 0);
        Assert.Equal(FakeWikiRoutes.Wiki + "/file/supply-karte.png", t.Browser.Opened[^1].ToString());

        // unbekannte Seite → Fehlerzustand rechts statt Absturz
        Assert.True(shell.OpenDeepLink(new Uri("rog://wiki/clan/gibt-es-nicht")));
        await ViewModelTests.WaitUntil(() => wiki.PageError is not null);
        Assert.True(wiki.ShowPageState);
        Assert.False(wiki.ShowPage);
    }

    [AvaloniaFact]
    public async Task Search_and_categories()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);

        wiki.SearchText = "ET";
        Assert.False(wiki.CanSaveOffline);
        await wiki.SearchCommand.ExecuteAsync(null);
        Assert.Equal(["ET-Server einrichten", "Clan-Regeln"], wiki.Items.Select(i => i.Title));
        Assert.Equal("Technik · UDP 27960 für den ET-Server freigeben", wiki.Items[0].Subtitle);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.Query.Contains("q=ET", StringComparison.Ordinal));

        await wiki.ClearSearchCommand.ExecuteAsync(null);
        Assert.Equal(3, wiki.Items.Count);

        wiki.ShowCategories = true;
        await ViewModelTests.WaitUntil(() => wiki.Items.Count == 3 && wiki.Items.All(i => i.IsCategory));
        Assert.Equal("Server", wiki.Items[2].Title);
        Assert.Equal(t.Services.Format("Linux_WikiCategoryCount", 2), wiki.Items[2].Subtitle);

        wiki.Selected = wiki.Items[2];
        await ViewModelTests.WaitUntil(() => wiki.HasCategory && wiki.Items.Count == 2 && !wiki.Items[0].IsCategory);
        Assert.Equal("Server", wiki.CategoryTitle);
        Assert.Null(wiki.Selected);
        Assert.Equal("Technik", wiki.Items[0].Subtitle);

        wiki.Selected = wiki.Items[1];
        await ViewModelTests.WaitUntil(() => wiki.Title == "CS2-Server");

        await wiki.LeaveCategoryCommand.ExecuteAsync(null);
        Assert.False(wiki.HasCategory);
        Assert.All(wiki.Items, i => Assert.True(i.IsCategory));
    }

    [AvaloniaFact]
    public async Task Edit_with_preview_conflict_take_theirs_and_save()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);
        await OpenRulesAsync(wiki);

        await wiki.EditCommand.ExecuteAsync(null);
        Assert.True(wiki.IsEditing);
        Assert.False(wiki.ShowReader);
        Assert.Equal(FakeWikiRoutes.CurrentContent, wiki.EditContent);

        await wiki.PreviewCommand.ExecuteAsync(null);
        Assert.True(wiki.HasPreview);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/wiki/render" && w.Body.Contains("\"page_id\":\"" + FakeWikiRoutes.Rules + "\"", StringComparison.Ordinal));

        // Jemand anderes hat inzwischen gespeichert → Konflikt mit Unterschied (aktuelle → eigene Fassung)
        wiki.EditContent = FakeWikiRoutes.CurrentContent + "\nKein Spam im Chat.";
        wiki.EditSummary = FakeWikiRoutes.ConflictSummary;
        await wiki.SaveCommand.ExecuteAsync(null);
        Assert.True(wiki.IsEditing);
        Assert.True(wiki.HasConflict);
        Assert.Equal(t.Services.Format("Wiki_Conflict", "Kaito"), wiki.ConflictMessage);
        Assert.True(wiki.HasDiff);
        Assert.Contains(wiki.DiffLines, l => l.IsAdded && l.Text == "Kein Spam im Chat.");
        Assert.Contains(wiki.DiffLines, l => l.IsRemoved && l.Text == "Respekt und Fairplay gegenüber allen – auch im Chat.");
        Assert.Equal("- Respekt und Fairplay gegenüber allen – auch im Chat.", wiki.DiffLines.First(l => l.IsRemoved).Display);
        Assert.Contains("\"base_revision_id\":\"" + FakeWikiRoutes.CurrentRevision + "\"", t.Api.Writes[^1].Body, StringComparison.Ordinal);

        // Deren Fassung übernehmen → Editor enthält die aktuelle Fassung, Speichern geht auf deren Version
        wiki.TakeTheirsCommand.Execute(null);
        Assert.False(wiki.HasConflict);
        Assert.False(wiki.HasDiff);
        Assert.Equal(FakeWikiRoutes.ConflictContent, wiki.EditContent);
        await wiki.SaveCommand.ExecuteAsync(null);
        Assert.False(wiki.IsEditing);
        Assert.Contains("\"base_revision_id\":\"" + FakeWikiRoutes.ConflictRevision + "\"", t.Api.Writes.Last(w => w.Method == "PUT").Body, StringComparison.Ordinal);
        await ViewModelTests.WaitUntil(() => wiki.ShowPage);
        Assert.Equal(t.Services.T("Wiki_Saved"), wiki.Feedback.Message);

        // Abbrechen bzw. Escape beendet den Editor, ohne die Seite zu schließen
        await wiki.EditCommand.ExecuteAsync(null);
        ((IDetailPage)wiki).CloseDetail();
        Assert.False(wiki.IsEditing);
        Assert.True(wiki.HasPage);
    }

    [AvaloniaFact]
    public async Task Image_upload_registers_the_file_and_embeds_it()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);
        await OpenRulesAsync(wiki);
        await wiki.EditCommand.ExecuteAsync(null);

        await wiki.InsertImageAsync(new MemoryStream(new byte[10]), "notizen.txt", 10);
        Assert.Equal(t.Services.T("Wiki_ImageOnly"), wiki.Feedback.Message);

        using var image = new MemoryStream(FakeImages.Png(64, 64, 64));
        await wiki.InsertImageAsync(image, "Supply Karte.png", image.Length);
        Assert.EndsWith("[[Datei:Supply Karte.png|mini]]\n", wiki.EditContent, StringComparison.Ordinal);
        Assert.Equal(t.Services.T("Wiki_ImageInserted"), wiki.Feedback.Message);
        var register = Assert.Single(t.Api.Writes, w => w.Path == "/v1/wiki/files");
        Assert.Contains("\"namespace\":\"clan\"", register.Body, StringComparison.Ordinal);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/uploads/presign" && w.Body.Contains("\"visibility\":\"public\"", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Revisions_compare_restore_approve_and_reject_with_confirmation()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);
        await OpenRulesAsync(wiki);

        wiki.IsRevisionsExpanded = true;
        await ViewModelTests.WaitUntil(() => wiki.Revisions.Count == 3);
        var pending = wiki.Revisions[0];
        Assert.True(pending.CanDecide);
        Assert.False(pending.CanRestore);
        Assert.Equal(t.Services.T("Linux_WikiStatusPending"), pending.Status);
        Assert.Contains("Kaito", pending.Text, StringComparison.Ordinal);
        Assert.True(wiki.Revisions[2].CanRestore);
        Assert.False(wiki.Revisions[2].HasStatus);

        // Vergleich der ersten Fassung mit der aktuellen
        await wiki.Revisions[2].CompareCommand.ExecuteAsync(null);
        Assert.True(wiki.HasDiff);
        Assert.Contains(wiki.DiffLines, l => l.IsRemoved && l.Text == "Respekt gegenüber allen.");
        Assert.Contains(wiki.DiffLines, l => l.IsAdded && l.Text == "Push-to-Talk ist Pflicht.");
        Assert.DoesNotContain(wiki.DiffLines, l => l.Text == "== Voice ==");
        wiki.CloseDiffCommand.Execute(null);
        Assert.False(wiki.HasDiff);

        // Wiederherstellen erst nach Rückfrage; Abbrechen schickt nichts
        var restore = wiki.Revisions[2].RestoreCommand.ExecuteAsync(null);
        Assert.True(wiki.Confirm.IsOpen);
        Assert.Equal(t.Services.T("Config_RollbackTitle"), wiki.Confirm.Title);
        wiki.Confirm.CancelCommand.Execute(null);
        await restore;
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/restore", StringComparison.Ordinal));

        restore = wiki.Revisions[2].RestoreCommand.ExecuteAsync(null);
        wiki.Confirm.AcceptCommand.Execute(null);
        await restore;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/wiki/revisions/" + FakeWikiRoutes.FirstRevision + "/restore");
        Assert.Equal(t.Services.T("Wiki_Restored"), wiki.Feedback.Message);

        // Vorschlag freigeben (Recht wiki.approve)
        wiki.IsRevisionsExpanded = true;
        await ViewModelTests.WaitUntil(() => wiki.Revisions.Count == 3);
        var approve = wiki.Revisions[0].ApproveCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Approval_ApproveTitle"), wiki.Confirm.Title);
        wiki.Confirm.AcceptCommand.Execute(null);
        await approve;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/wiki/revisions/" + FakeWikiRoutes.PendingRevision + "/approve");
        Assert.Equal(t.Services.T("Wiki_Approved"), wiki.Feedback.Message);

        var reject = wiki.Revisions[0].RejectCommand.ExecuteAsync(null);
        Assert.Equal(t.Services.T("Approval_RejectTitle"), wiki.Confirm.Title);
        ((IDetailPage)wiki).CloseDetail(); // Escape beantwortet die Rückfrage mit Nein
        await reject;
        Assert.False(wiki.Confirm.IsOpen);
        Assert.True(wiki.HasPage);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path.EndsWith("/reject", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task New_page_needs_a_title_and_opens_after_creating()
    {
        using var t = await TestServices.CreateAsync(TestTokens.Wahke);
        var wiki = await CreateAsync(t);

        wiki.StartCreateCommand.Execute(null);
        Assert.True(wiki.IsCreating);
        Assert.True(wiki.IsDetailOpen);
        Assert.False(wiki.ShowPage);
        Assert.Equal(4, wiki.ParentOptions.Count); // (oberste Ebene) + Baum
        Assert.Equal(string.Empty, wiki.NewParent?.Value);
        Assert.Equal("wikitext", wiki.NewFormat?.Value);
        Assert.Equal(t.Services.Format("Linux_WikiNewPageHint", "Clan"), wiki.NewPageHint);

        Assert.False(await wiki.CreateAsync());
        Assert.Equal(t.Services.T("Wiki_NewTitleMissing"), wiki.Feedback.Message);

        wiki.NewTitle = " Clan-Abend ";
        wiki.NewContent = "Jeden Freitag um 20 Uhr.";
        wiki.NewParent = wiki.ParentOptions[1];
        Assert.True(await wiki.CreateAsync());
        Assert.False(wiki.IsCreating);
        var create = Assert.Single(t.Api.Writes, w => w.Path == "/v1/wiki/namespaces/clan/pages");
        Assert.Contains("\"title\":\"Clan-Abend\"", create.Body, StringComparison.Ordinal);
        Assert.Contains("\"parent_id\":\"" + FakeWikiRoutes.Rules + "\"", create.Body, StringComparison.Ordinal);
        Assert.Equal("Clan-Abend", wiki.Title);
        Assert.Equal(t.Services.T("Wiki_Created"), wiki.Feedback.Message);

        // Escape bricht das Formular ab
        wiki.StartCreateCommand.Execute(null);
        ((IDetailPage)wiki).CloseDetail();
        Assert.False(wiki.IsCreating);
        Assert.True(wiki.ShowPage);
    }

    [AvaloniaFact]
    public async Task Guests_read_public_sections_only_and_see_a_sign_in_hint()
    {
        using var t = await TestServices.CreateAsync(guest: true);
        var signIn = 0;
        var wiki = new WikiViewModel(t.Services, () => signIn++);
        await wiki.LoadAsync();

        Assert.True(wiki.IsGuest);
        Assert.Equal(["Clan"], wiki.Namespaces.Select(n => n.Label));
        Assert.False(wiki.CanEdit);
        Assert.False(wiki.CanCreate);
        Assert.False(wiki.CanWatch);
        Assert.False(wiki.ShowOfflineUnavailable); // Gäste: kein Hinweis auf den Schlüsselbund

        wiki.Selected = wiki.Items[0];
        await ViewModelTests.WaitUntil(() => wiki.ShowPage);
        await wiki.EditCommand.ExecuteAsync(null);
        Assert.False(wiki.IsEditing);

        wiki.SignInCommand.Execute(null);
        Assert.Equal(1, signIn);
    }

    [AvaloniaFact]
    public async Task Offline_copies_are_encrypted_shown_without_network_and_removed_on_sign_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "rog-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var api = new FakeApi();
            api.Overrides["/v1/me"] = FakeWikiRoutes.MeWithWikiRights;
            var folder = Path.Combine(root, "data", "wiki-production");

            // Alte Kopie (über 30 Tage nicht aktualisiert) fällt beim Start weg
            Directory.CreateDirectory(folder);
            var stale = Path.Combine(folder, "01JALT.de.dat");
            await File.WriteAllTextAsync(stale, "x");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - AppServices.CacheRetention - TimeSpan.FromDays(1));

            using var services = await CreateWithProtectorAsync(root, api);
            Assert.False(File.Exists(stale));
            Assert.NotNull(services.WikiOffline);
            services.CurrentUser = await services.Account.GetMeAsync();

            var wiki = new WikiViewModel(services, () => { });
            await wiki.LoadAsync();
            Assert.True(wiki.IsOfflineAvailable);
            Assert.False(wiki.ShowOfflineUnavailable);
            Assert.True(wiki.CanSaveOffline);

            // Gelesene Seite → verschlüsselte Kopie
            wiki.Selected = wiki.Items[0];
            await ViewModelTests.WaitUntil(() => wiki.IsSavedOffline);
            var file = Assert.Single(Directory.GetFiles(folder));
            Assert.EndsWith(FakeWikiRoutes.Rules + ".de.dat", file, StringComparison.Ordinal);
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.False(bytes.AsSpan().IndexOf("Clan-Regeln"u8) >= 0);
            Assert.False(bytes.AsSpan().IndexOf("Fairplay"u8) >= 0);

            // Bereich offline speichern: alle Seiten des Baums
            wiki.SelectedNamespace = wiki.Namespaces[1];
            await ViewModelTests.WaitUntil(() => wiki.Items.Any(i => i.Title == "CS2-Server"));
            await wiki.SaveNamespaceOfflineCommand.ExecuteAsync(null);
            Assert.Equal(services.Format("Linux_WikiSavedOffline", 3), wiki.Feedback.Message);
            Assert.Equal(4, Directory.GetFiles(folder).Length);

            // Seite als Offline-Kopie
            // Ohne Netz und ohne HTTP-Cache (sonst antwortet der Kern mit dem letzten Stand): Liste aus den Kopien
            services.Api.Handler.ClearCache();
            api.Offline = true;
            var offline = new WikiViewModel(services, () => { });
            await offline.LoadAsync();
            Assert.True(offline.IsOfflineList);
            Assert.False(offline.CanSaveOffline);
            Assert.False(offline.CanCreate);
            Assert.Equal(4, offline.Items.Count);
            Assert.Equal(services.T("Wiki_OfflineList"), offline.Feedback.Message);
            offline.Selected = offline.Items.First(i => i.Title == "Clan-Regeln");
            await ViewModelTests.WaitUntil(() => offline.ShowPage);
            Assert.True(offline.IsOfflineCopy);
            Assert.Equal(4, offline.Toc.Count);

            // Deep Link ohne Netz: über Bereich und Adresse aus den Kopien
            await offline.OpenPathAsync("technik/server/et-server");
            await ViewModelTests.WaitUntil(() => offline.Title == "ET-Server einrichten");
            Assert.True(offline.IsOfflineCopy);

            // Abmelden löscht den Ordner
            services.ClearCaches();
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<WikiViewModel> CreateAsync(TestServices t)
    {
        t.Api.Overrides["/v1/me"] = FakeWikiRoutes.MeWithWikiRights;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        var wiki = new WikiViewModel(t.Services, () => { });
        await wiki.LoadAsync();
        return wiki;
    }

    private static async Task OpenRulesAsync(WikiViewModel wiki)
    {
        wiki.Selected = wiki.Items.First(i => i.Id == FakeWikiRoutes.Rules);
        await ViewModelTests.WaitUntil(() => wiki.ShowPage && wiki.Title == "Clan-Regeln");
    }

    private static Uri FindLink(RichDocument document, string contains)
    {
        IEnumerable<ContentInline> Inlines(ContentBlock block) => block switch
        {
            ParagraphBlock p => p.Inlines,
            ListBlock l => l.Items.SelectMany(i => i).SelectMany(Inlines),
            _ => [],
        };
        return document.Blocks.SelectMany(Inlines).OfType<LinkInline>().First(l => l.Target.ToString().Contains(contains, StringComparison.Ordinal)).Target;
    }

    private static async Task<AppServices> CreateWithProtectorAsync(string root, FakeApi api)
    {
        var tokens = new FallbackTokenStore(new SecretServiceTokenStore(new FakeSecretBackend(), "production"));
        await tokens.SetAsync(TestTokens.Wahke);
        return await AppServices.CreateAsync(new AppServicesOptions
        {
            SettingsDirectory = Path.Combine(root, "config"),
            CacheDirectory = Path.Combine(root, "cache"),
            DataDirectory = Path.Combine(root, "data"),
            Tokens = tokens,
            Protector = new XorProtector(),
            ApiHandler = api,
            ImageHandler = new FakeImages(),
            StorageHandler = new FakeStorage(),
            Browser = new RecordingBrowser(),
            Notifier = new SilentNotifier(),
            Autostart = new FakeAutostart(),
            Clipboard = new FakeClipboard(),
            Launcher = new FakeLauncher(),
            Environment = "production",
            Language = "de",
        });
    }

    /// <summary>Fake-Schutz für Tests: verändert jedes Byte (Klartext steht nie in der Datei), ohne Schlüsselbund.</summary>
    private sealed class XorProtector : IPayloadProtector
    {
        public Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(data.Select(b => (byte)(b ^ 0x5A)).ToArray());

        public Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default) => ProtectAsync(data, cancellationToken);
    }
}
