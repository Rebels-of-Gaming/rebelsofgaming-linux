using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using RoG.Core.Content;
using RoG.Core.Features.News;
using RoG.Linux.App.Services;
using RoG.Linux.App.ViewModels;
using RoG.Linux.App.Views;
using RoG.Linux.Tests.Support;

namespace RoG.Linux.Tests.App;

/// <summary>Stufe 9 „N – Inhalte“: News-Redaktion, Match-Editor, Reiter Galerie/Uploads, Social-Media-Karte.</summary>
public class ContentAdminTests
{
    internal static async Task<TestServices> SignedInAsync(string me = FakeContentAdminRoutes.MeEditor)
    {
        var t = await TestServices.CreateAsync(TestTokens.Wahke);
        t.Api.Overrides["/v1/me"] = me;
        t.Services.CurrentUser = await t.Services.Account.GetMeAsync();
        return t;
    }

    private static string Text(IEnumerable<ContentInline> inlines) => string.Concat(inlines.Select(i => i switch
    {
        TextInline text => text.Text,
        LinkInline link => Text(link.Children),
        _ => string.Empty,
    }));

    private static JsonNode LastBody(TestServices t, string method, string path) =>
        JsonNode.Parse(t.Api.Writes.Last(w => w.Method == method && w.Path == path).Body)!;

    // ---- Sichtbarkeit (Rechte + Scopes, nie Rollennamen) ----

    [AvaloniaFact]
    public async Task Entry_points_follow_permissions_and_scopes()
    {
        using (var t = await SignedInAsync())
        {
            var news = new NewsViewModel(t.Services);
            Assert.True(news.CanWrite);
            Assert.True(news.CanEditAll);
            var matches = new MatchesViewModel(t.Services, () => { });
            Assert.True(matches.CanManage);
            var media = new MediaAdminViewModel(t.Services);
            Assert.True(media.CanGallery);
            Assert.True(media.CanUploads);
            Assert.True(new SocialStatusViewModel(t.Services).CanWrite);
        }

        using (var t = await SignedInAsync(FakeContentAdminRoutes.MeAuthor))
        {
            var news = new NewsViewModel(t.Services);
            Assert.True(news.CanWrite);
            Assert.False(news.CanEditAll); // veröffentlichte News nur mit news.publish
            Assert.False(new MatchesViewModel(t.Services, () => { }).CanManage);
            var media = new MediaAdminViewModel(t.Services);
            Assert.False(media.CanGallery);
            Assert.False(media.CanUploads);
            Assert.False(new SocialStatusViewModel(t.Services).CanRead);
        }

        // Rechte da, aber der Token hat die Scopes nicht → nichts
        using (var t = await SignedInAsync(FakeContentAdminRoutes.MeNoScopes))
        {
            Assert.False(new NewsViewModel(t.Services).CanWrite);
            Assert.False(new MatchesViewModel(t.Services, () => { }).CanManage);
            Assert.False(new MediaAdminViewModel(t.Services).CanGallery);
            Assert.False(new MediaAdminViewModel(t.Services).CanUploads);
            var social = new SocialStatusViewModel(t.Services);
            await social.ShowAsync("news", FakeContentAdminRoutes.DraftId);
            Assert.False(social.IsShown);
        }

        using (var guest = await TestServices.CreateAsync(guest: true))
        {
            Assert.False(new NewsViewModel(guest.Services).CanWrite);
            Assert.False(new MatchesViewModel(guest.Services, () => { }).CanManage);
        }
    }

    [AvaloniaFact]
    public async Task News_and_matches_pages_request_the_editor()
    {
        using var t = await SignedInAsync();
        var news = new NewsViewModel(t.Services);
        await news.LoadAsync();
        NewsEditorRequest? request = null;
        news.EditorRequested += (_, r) => request = r;
        news.WriteCommand.Execute(null);
        Assert.NotNull(request);
        Assert.Null(request!.Slug);
        Assert.True(request.Rights.Publish);

        news.Selected = news.Items[0];
        await ViewModelTests.WaitUntil(() => news.Detail.ShowContent);
        news.EditCommand.Execute(null);
        Assert.Equal(news.Items[0].Slug, request.Slug);

        var matches = new MatchesViewModel(t.Services, () => { });
        await matches.LoadAsync();
        MatchEditorRequest? matchRequest = null;
        matches.EditorRequested += (_, r) => matchRequest = r;
        matches.AddMatchCommand.Execute(null);
        Assert.Null(matchRequest!.MatchId);
        matches.Selected = matches.Items[0];
        matches.EditMatchCommand.Execute(null);
        Assert.Equal(matches.Items[0].Id, matchRequest.MatchId);
    }

    // ---- News-Redaktion ----

    [AvaloniaFact]
    public async Task News_editor_lists_drafts_switches_languages_and_validates()
    {
        using var t = await SignedInAsync();
        var editor = new NewsEditorViewModel(t.Services, NewsRights.For(t.Services.CurrentUser));
        Assert.Equal(3, editor.StatusOptions.Count); // news.publish: Entwurf, veröffentlicht, geplant
        await editor.StartAsync(null);
        Assert.Equal(4, editor.CategoryOptions.Count); // „keine“ + drei
        Assert.Equal(3, editor.Drafts.Count);
        Assert.True(editor.Drafts[0].Scheduled); // geplante zuerst
        Assert.Contains(t.Services.T("NewsEdit_Scheduled"), editor.Drafts[0].Meta, StringComparison.Ordinal);
        Assert.False(editor.IsSaved);
        Assert.Equal(t.Services.T("NewsEdit_New"), editor.Heading);

        // Titel (DE) fehlt → nichts gesendet
        var writes = t.Api.Writes.Count;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(writes, t.Api.Writes.Count);
        Assert.True(editor.Feedback.IsError);
        Assert.Contains(t.Services.T("NewsEdit_Error_title"), editor.Feedback.Message, StringComparison.Ordinal);

        // Texte je Sprache
        editor.Title = "Clanabend im November";
        editor.Body = "Wir treffen uns **Freitag**.";
        editor.SelectedLanguage = editor.LanguageOptions[1];
        Assert.Equal(string.Empty, editor.Title);
        editor.Title = "Clan night in November";
        editor.SelectedLanguage = editor.LanguageOptions[0];
        Assert.Equal("Clanabend im November", editor.Title);

        // Geplant ohne Zeitpunkt → Prüfung
        editor.SelectedStatus = editor.StatusOptions[2];
        Assert.True(editor.IsScheduled);
        editor.PublishDate = null;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Contains(t.Services.T("NewsEdit_Error_published_at"), editor.Feedback.Message, StringComparison.Ordinal);

        editor.PublishDate = DateTime.Today.AddDays(3);
        editor.PublishTime = new TimeSpan(18, 30, 0);
        editor.SelectedCategory = editor.CategoryOptions[1];
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.Feedback.IsSuccess);
        Assert.Equal(t.Services.T("NewsEdit_ScheduledDone"), editor.Feedback.Message);
        Assert.Equal(FakeContentAdminRoutes.NewNewsId, editor.Id);
        Assert.True(editor.Changed);
        var body = LastBody(t, "POST", "/v1/news");
        Assert.Equal("Clanabend im November", body["title"]!["de"]!.GetValue<string>());
        Assert.Equal("Clan night in November", body["title"]!["en"]!.GetValue<string>());
        Assert.Null(body["title"]!["fr"]); // leere Übersetzung entfällt
        Assert.Equal("scheduled", body["status"]!.GetValue<string>());
        Assert.Equal("01NC1", body["category_id"]!.GetValue<string>());
        Assert.EndsWith("Z", body["published_at"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task News_editor_opens_translations_saves_with_warning_and_deletes_after_confirm()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/news/" + FakeContentAdminRoutes.DraftSlug] = FakeContentAdminRoutes.DraftDetail;
        var editor = new NewsEditorViewModel(t.Services, NewsRights.For(t.Services.CurrentUser));
        await editor.StartAsync(null);
        editor.SelectedDraft = editor.Drafts.First(d => d.Slug == FakeContentAdminRoutes.DraftSlug);
        await ViewModelTests.WaitUntil(() => editor.Id == FakeContentAdminRoutes.DraftId);
        Assert.Equal("Turnier am Samstag", editor.Title);
        Assert.Equal("01NC3", editor.SelectedCategory!.Value); // Kategorie per Slug
        Assert.Equal("draft", editor.SelectedStatus!.Value);
        Assert.True(editor.CanDelete);
        Assert.Equal(t.Services.T("NewsEdit_Edit"), editor.Heading);
        editor.SelectedLanguage = editor.LanguageOptions[1];
        Assert.Equal("Tournament on Saturday", editor.Title);
        editor.SelectedLanguage = editor.LanguageOptions[0];

        // Social-Karte für die gespeicherte News
        await ViewModelTests.WaitUntil(() => editor.Social.Targets.Count == 2);
        Assert.True(editor.Social.IsShown);

        // Speichern → PATCH mit Hinweis der Website
        editor.Teaser = "Unser ET-Squad spielt den Herbst-Cup.";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.Feedback.IsInfo);
        Assert.Contains("nur für Mitglieder", editor.Feedback.Message, StringComparison.Ordinal);
        var patch = LastBody(t, "PATCH", "/v1/news/" + FakeContentAdminRoutes.DraftId);
        Assert.Equal("Unser ET-Squad spielt den Herbst-Cup.", patch["teaser"]!["de"]!.GetValue<string>());
        Assert.Equal("Our ET squad enters the autumn cup.", patch["teaser"]!["en"]!.GetValue<string>());

        // Löschen: erst nach Bestätigung
        var delete = editor.DeleteCommand.ExecuteAsync(null);
        Assert.True(editor.Confirmation.IsOpen);
        Assert.True(editor.CloseOverlay()); // Escape = Nein
        await delete;
        Assert.DoesNotContain(t.Api.Writes, w => w.Method == "DELETE");

        delete = editor.DeleteCommand.ExecuteAsync(null);
        editor.Confirmation.AcceptCommand.Execute(null);
        await delete;
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/news/" + FakeContentAdminRoutes.DraftId);
        Assert.Null(editor.Id);
        Assert.Equal(t.Services.T("NewsEdit_Deleted"), editor.Feedback.Message);
    }

    [AvaloniaFact]
    public async Task News_editor_preview_and_cover()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/news/" + FakeContentAdminRoutes.DraftSlug] = FakeContentAdminRoutes.DraftDetail;
        var editor = new NewsEditorViewModel(t.Services, NewsRights.For(t.Services.CurrentUser));
        await editor.StartAsync(FakeContentAdminRoutes.DraftSlug);
        Assert.Equal(FakeContentAdminRoutes.DraftId, editor.Id);

        // Vorschau: Markdown nativ, leere Sprache fällt auf Deutsch zurück
        editor.TogglePreviewCommand.Execute(null);
        Assert.True(editor.IsPreviewOpen);
        Assert.Equal("Turnier am Samstag", editor.PreviewTitle);
        var blocks = editor.PreviewBody!.Blocks;
        Assert.Contains(blocks, b => b is HeadingBlock h && Text(h.Inlines) == "Ablauf");
        Assert.Contains(blocks, b => b is ListBlock);
        Assert.Contains(blocks, b => b is QuoteBlock);
        editor.SelectedLanguage = editor.LanguageOptions[2]; // FR leer
        Assert.Equal("Turnier am Samstag", editor.PreviewTitle);
        editor.Body = "Inscription jusqu'à *vendredi*.";
        Assert.Contains("vendredi", Text(Assert.IsType<ParagraphBlock>(editor.PreviewBody!.Blocks[0]).Inlines), StringComparison.Ordinal);

        // Titelbild: nur Bilder, öffentlich hochladen, dann setzen
        await editor.SetCoverAsync(new UploadSource("notes.txt", 10, () => Task.FromResult<Stream>(new MemoryStream(new byte[10]))));
        Assert.True(editor.Feedback.IsError);
        Assert.Empty(t.Storage.Puts);

        var png = FakeImages.Png(1200, 400, 30);
        await editor.SetCoverAsync(new UploadSource("titelbild.png", png.Length, () => Task.FromResult<Stream>(new MemoryStream(png))));
        Assert.True(editor.Feedback.IsSuccess);
        Assert.Single(t.Storage.Puts);
        Assert.Equal("public", LastBody(t, "POST", "/v1/uploads/presign")["visibility"]!.GetValue<string>());
        Assert.Equal("01UPNEW", LastBody(t, "POST", "/v1/news/" + FakeContentAdminRoutes.DraftId + "/cover")["upload_id"]!.GetValue<string>());

        await editor.RemoveCoverCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/news/" + FakeContentAdminRoutes.DraftId + "/cover");
    }

    [AvaloniaFact]
    public async Task News_editor_for_authors_only_drafts_and_shows_api_errors()
    {
        using var t = await SignedInAsync(FakeContentAdminRoutes.MeAuthor);
        var editor = new NewsEditorViewModel(t.Services, NewsRights.For(t.Services.CurrentUser));
        Assert.Single(editor.StatusOptions);
        await editor.StartAsync(null);
        Assert.Equal(2, editor.Drafts.Count); // keine geplanten ohne news.publish
        Assert.DoesNotContain(t.Api.Requests, r => r.RequestUri!.Query.Contains("scheduled", StringComparison.Ordinal));

        t.Api.Failures["/v1/news"] = (HttpStatusCode.UnprocessableEntity,
            """{"type":"about:blank","title":"Fehler","status":422,"code":"validation_failed","detail":"Die Adresse ist schon vergeben."}""");
        editor.Title = "Doppelt";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.Feedback.IsError);
        Assert.Equal("Die Adresse ist schon vergeben.", editor.Feedback.Message);
        Assert.False(editor.IsSaved);
    }

    [Fact]
    public void Markdown_preview_escapes_html_and_keeps_safe_links()
    {
        var html = MarkdownPreview.ToHtml("<script>x</script> [Seite](https://rebelsofgaming.com) [böse](javascript:alert(1)) `a<b`\n\n1. eins\n2. zwei\n\n---\n```\ncode <i>\n```");
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://rebelsofgaming.com/\">Seite</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.Ordinal);
        Assert.Contains("<code>a&lt;b</code>", html, StringComparison.Ordinal);
        Assert.Contains("<ol><li>eins</li><li>zwei</li></ol>", html, StringComparison.Ordinal);
        Assert.Contains("<hr>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code>code &lt;i&gt;</code></pre>", html, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkdownPreview.ToHtml("  "));
    }

    // ---- Match-Editor ----

    [AvaloniaFact]
    public async Task Match_editor_new_match_with_lineup_maps_and_links()
    {
        using var t = await SignedInAsync();
        var editor = new MatchEditorViewModel(t.Services);
        await editor.StartAsync(null);
        Assert.Equal(2, editor.SquadOptions.Count); // nur Squads mit matches.manage (ET, CS2)
        Assert.False(editor.NoSquads);
        Assert.Equal("01SQ1", editor.SelectedSquad!.Value);
        await ViewModelTests.WaitUntil(() => editor.Players.Count == 4);
        Assert.True(editor.Players[3].Substitute); // Ersatz aus dem Squad
        Assert.False(editor.IsSaved);

        // Prüfung vor dem Senden
        editor.MapsText = "supply 2:1\n" + new string('x', 61);
        editor.LinksText = "ftp://example.org/demo";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.Feedback.IsError);
        Assert.Contains(t.Services.T("MatchEdit_Error_opponent"), editor.Feedback.Message, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("MatchEdit_Error_maps"), editor.Feedback.Message, StringComparison.Ordinal);
        Assert.Contains(t.Services.T("MatchEdit_Error_links"), editor.Feedback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(t.Api.Writes, w => w.Path == "/v1/matches");

        editor.Opponent = "Iron Legion";
        editor.OpponentTag = "IL";
        editor.MapsText = "supply 2:1\nradar 2 - 0";
        editor.LinksText = "https://www.twitch.tv/videos/2281234567 Twitch-VOD";
        editor.Report = "Starker Auftritt.";
        editor.Published = true;
        editor.Players[0].Selected = true;
        editor.Players[0].Position = "Medic";
        editor.Players[3].Selected = true;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.Feedback.IsSuccess);
        Assert.Equal(FakeContentAdminRoutes.NewMatchId, editor.Id);
        Assert.True(editor.Changed);
        var body = LastBody(t, "POST", "/v1/matches");
        Assert.Equal("01SQ1", body["squad_id"]!.GetValue<string>());
        Assert.Equal("Iron Legion", body["opponent"]!.GetValue<string>());
        Assert.Equal(2, body["maps"]!.AsArray().Count);
        Assert.Equal(2, body["maps"]![1]!["score_own"]!.GetValue<int>());
        Assert.Equal("vod", body["links"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("Starker Auftritt.", body["report"]!["de"]!.GetValue<string>());
        var players = body["players"]!.AsArray();
        Assert.Equal(2, players.Count);
        Assert.Equal("Medic", players[0]!["position"]!.GetValue<string>());
        Assert.Equal("substitute", players[1]!["slot"]!.GetValue<string>());
        Assert.True(body["published"]!.GetValue<bool>());
    }

    [AvaloniaFact]
    public async Task Match_editor_edits_existing_match_attachments_and_deletes()
    {
        using var t = await SignedInAsync();
        var editor = new MatchEditorViewModel(t.Services);
        await editor.StartAsync(FakeContentAdminRoutes.MatchId);
        Assert.True(editor.IsSaved);
        Assert.Equal("Iron Legion", editor.Opponent);
        Assert.Equal("supply 2:0", editor.MapsText.Split('\n')[0]);
        Assert.Equal("radar 1:1", editor.MapsText.Split('\n')[1]);
        Assert.Contains("vod https://www.twitch.tv/videos/2281234567 Twitch-VOD", editor.LinksText, StringComparison.Ordinal);
        Assert.Equal(2, editor.Attachments.Count);
        Assert.True(editor.HasAttachments);
        await ViewModelTests.WaitUntil(() => editor.Players.Count == 4 && editor.Players[0].Selected);
        Assert.Equal("Medic", editor.Players[0].Position);
        Assert.True(editor.Players[3].Selected && editor.Players[3].Substitute);
        Assert.False(editor.Players[2].Selected);
        await ViewModelTests.WaitUntil(() => editor.Social.HasSource);

        // Anhang öffnen (kurzlebiger Link) und entfernen nach Rückfrage
        await editor.Attachments[0].OpenCommand.ExecuteAsync(null);
        Assert.Contains(t.Browser.Opened, u => u.AbsolutePath.EndsWith("01MA1.png", StringComparison.Ordinal));
        var remove = editor.Attachments[1].RemoveCommand.ExecuteAsync(null);
        editor.Confirmation.AcceptCommand.Execute(null);
        await remove;
        Assert.Single(editor.Attachments);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == $"/v1/matches/{FakeContentAdminRoutes.MatchId}/attachments/01MA2");

        // Dateien anhängen (multipart) und neu laden
        var png = FakeImages.Png(64, 64, 10);
        await editor.AttachAsync([new UploadSource("scoreboard.png", png.Length, () => Task.FromResult<Stream>(new MemoryStream(png)))]);
        Assert.Contains(t.Api.Writes, w => w.Method == "POST" && w.Path == $"/v1/matches/{FakeContentAdminRoutes.MatchId}/attachments");
        Assert.Equal(t.Services.T("MatchEdit_Attached"), editor.Feedback.Message);

        // Ändern → PATCH
        editor.Report = "Radar war knapp.";
        await editor.SaveCommand.ExecuteAsync(null);
        var patch = LastBody(t, "PATCH", "/v1/matches/" + FakeContentAdminRoutes.MatchId);
        Assert.Equal("Radar war knapp.", patch["report"]!["de"]!.GetValue<string>());
        Assert.Equal(3, patch["players"]!.AsArray().Count);

        // Löschen nach Rückfrage
        var delete = editor.DeleteCommand.ExecuteAsync(null);
        Assert.True(editor.Confirmation.IsOpen);
        editor.Confirmation.AcceptCommand.Execute(null);
        await delete;
        Assert.False(editor.IsSaved);
        Assert.Contains(t.Api.Writes, w => w.Method == "DELETE" && w.Path == "/v1/matches/" + FakeContentAdminRoutes.MatchId);
    }

    [AvaloniaFact]
    public async Task Match_editor_without_squad_rights_shows_hint()
    {
        using var t = await SignedInAsync(FakeContentAdminRoutes.MeAuthor);
        var editor = new MatchEditorViewModel(t.Services);
        await editor.StartAsync(null);
        Assert.True(editor.NoSquads);
        Assert.Null(editor.SelectedSquad);
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Contains(t.Services.T("MatchEdit_Error_squad"), editor.Feedback.Message, StringComparison.Ordinal);
    }

    // ---- Reiter Galerie und Uploads ----

    [AvaloniaFact]
    public async Task Gallery_tab_loads_lazily_approves_and_rejects_after_confirm()
    {
        using var t = await SignedInAsync();
        t.Api.Overrides["/v1/gallery/albums/clanabend-2026"] = FakeContentAdminRoutes.AlbumWithPending;
        t.Api.Overrides["/v1/gallery/albums/scrims"] = FakeContentAdminRoutes.AlbumNothingPending;
        var gallery = new GalleryAdminViewModel(t.Services);
        Assert.True(gallery.IsAvailable);
        Assert.True(gallery.HasOwnFeedback);
        Assert.False(gallery.IsLoaded);

        await ((ILazyTab)gallery).EnsureLoadedAsync();
        Assert.Equal(2, gallery.Pending.Count);
        Assert.Equal("Siegerfoto nach dem Scrim", gallery.Pending[0].Title); // älteste zuerst
        Assert.Contains("Fitti", gallery.Pending[0].Meta, StringComparison.Ordinal);
        Assert.True(gallery.Pending[1].IsVideo);
        Assert.Equal("Clanabend Oktober 2026", gallery.Pending[1].Title); // ohne Bildunterschrift: Album
        var requests = t.Api.Requests.Count;
        await ((ILazyTab)gallery).EnsureLoadedAsync(); // nur einmal
        Assert.Equal(requests, t.Api.Requests.Count);

        await gallery.Pending[0].ApproveCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/gallery/items/01GIP1/approve");
        Assert.Single(gallery.Pending);
        Assert.Equal(t.Services.T("MediaAdmin_Approved"), gallery.Feedback.Message);

        var reject = gallery.Pending[0].RejectCommand.ExecuteAsync(null);
        Assert.True(gallery.Confirmation.IsOpen);
        Assert.True(gallery.CloseOverlay());
        await reject;
        Assert.Single(gallery.Pending);
        reject = gallery.Pending[0].RejectCommand.ExecuteAsync(null);
        gallery.Confirmation.AcceptCommand.Execute(null);
        await reject;
        Assert.Contains(t.Api.Writes, w => w.Path == "/v1/gallery/items/01GIP2/reject");
        Assert.Empty(gallery.Pending);
        Assert.True(gallery.ShowEmpty);
    }

    [AvaloniaFact]
    public async Task Gallery_tab_uses_shared_feedback_and_shows_errors()
    {
        using var t = await SignedInAsync();
        var feedback = new FeedbackViewModel();
        var confirm = new InlineConfirmViewModel();
        var gallery = new GalleryAdminViewModel(t.Services, feedback, confirm);
        Assert.False(gallery.HasOwnFeedback);
        Assert.False(gallery.HasOwnConfirmation);
        Assert.Same(feedback, gallery.Feedback);
        Assert.Same(confirm, gallery.Confirmation);

        t.Api.FailWith = HttpStatusCode.InternalServerError;
        await gallery.EnsureLoadedAsync();
        Assert.NotNull(gallery.ErrorMessage);
        Assert.False(gallery.ShowEmpty);
        t.Api.FailWith = null;
        await gallery.LoadCommand.ExecuteAsync(null); // erneut versuchen
        Assert.Null(gallery.ErrorMessage);
        Assert.True(gallery.ShowEmpty); // Standard-Alben ohne can_manage
    }

    [AvaloniaFact]
    public async Task Uploads_tab_reports_storage_and_resolve()
    {
        using var t = await SignedInAsync();
        var media = new MediaAdminViewModel(t.Services);
        var uploads = media.Uploads;
        await uploads.EnsureLoadedAsync();
        Assert.Equal(2, uploads.Reports.Count);
        Assert.True(uploads.Reports[0].IsChildSafety);
        Assert.Equal("bild_2026-10-29.png", uploads.Reports[0].Title);
        Assert.False(uploads.Reports[0].HasReason);
        Assert.Contains("Nyx", uploads.Reports[0].Meta, StringComparison.Ordinal);
        Assert.True(uploads.HasStorage);
        Assert.Contains("1284", uploads.StorageText, StringComparison.Ordinal);
        Assert.Equal(3, uploads.StorageUsers.Count);
        Assert.Equal("Wahke", uploads.StorageUsers[0].Name);

        await uploads.Reports[1].OpenCommand.ExecuteAsync(null);
        Assert.Contains(t.Browser.Opened, u => u.AbsolutePath == "/8.jpg");

        await uploads.Reports[1].KeepCommand.ExecuteAsync(null);
        Assert.Equal("kept", LastBody(t, "POST", "/v1/admin/uploads/reports/01UR2/resolve")["resolution"]!.GetValue<string>());
        Assert.Single(uploads.Reports);
        Assert.Equal(t.Services.T("MediaAdmin_UploadKept"), uploads.Feedback.Message);

        var delete = uploads.Reports[0].DeleteCommand.ExecuteAsync(null);
        uploads.Confirmation.AcceptCommand.Execute(null);
        await delete;
        Assert.Equal("deleted", LastBody(t, "POST", "/v1/admin/uploads/reports/01UR1/resolve")["resolution"]!.GetValue<string>());
        Assert.True(uploads.ShowEmpty);
    }

    // ---- Social-Media-Karte ----

    [AvaloniaFact]
    public async Task Social_card_retries_and_redrafts()
    {
        using var t = await SignedInAsync();
        var social = new SocialStatusViewModel(t.Services);
        await social.ShowAsync("news", null);
        Assert.False(social.IsShown); // noch nicht gespeichert

        await social.ShowAsync("news", FakeContentAdminRoutes.DraftId);
        Assert.True(social.IsShown);
        Assert.Equal(t.Services.T("Social_State_partial"), social.StateText);
        Assert.Equal(2, social.Targets.Count);
        Assert.True(social.Targets[0].HasLink);
        Assert.False(social.Targets[0].CanRetry);
        Assert.Equal("Mastodon: HTTP 503", social.Targets[1].Error);
        Assert.True(social.Targets[1].CanRetry);
        Assert.True(social.ShowRetryFailed);
        Assert.True(social.ShowRedraft);

        await social.RetryFailedCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path == $"/v1/social/posts/{FakeContentAdminRoutes.SocialPostId}/retry-failed");
        Assert.True(social.Feedback.IsInfo); // ein Kanal nicht nutzbar
        Assert.Contains("RoG auf X", social.Feedback.Message, StringComparison.Ordinal);

        await social.Targets[1].RetryCommand.ExecuteAsync(null);
        Assert.Contains(t.Api.Writes, w => w.Path.EndsWith("/targets/01JB3D4E5F6G7H8J9K0M1N2P3S/retry", StringComparison.Ordinal));

        await social.RedraftCommand.ExecuteAsync(null); // teilweise → ohne Rückfrage
        Assert.False(social.Confirmation.IsOpen);
        Assert.Contains(t.Api.Writes, w => w.Path == $"/v1/social/sources/news/{FakeContentAdminRoutes.DraftId}/redraft" && !w.Body.Contains("repost", StringComparison.Ordinal));
        Assert.Equal(t.Services.T("Social_Redrafted"), social.Feedback.Message);

        await social.Targets[0].OpenCommand.ExecuteAsync(null);
        Assert.Contains(t.Browser.Opened, u => u.Host == "bsky.app");
    }

    [AvaloniaFact]
    public async Task Album_and_event_details_show_the_social_card()
    {
        using var t = await SignedInAsync();
        var media = new MediaViewModel(t.Services);
        await media.LoadAsync();
        media.OpenAlbumCommand.Execute(media.Albums[0]);
        await ViewModelTests.WaitUntil(() => media.Social.StateText is not null);
        Assert.True(media.Social.IsShown);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/social/sources/gallery/01GA1");
        media.BackToAlbumsCommand.Execute(null);
        Assert.False(media.Social.IsShown);

        var events = new EventsViewModel(t.Services);
        await events.LoadAsync();
        await events.Detail.ShowAsync("01EV1");
        await ViewModelTests.WaitUntil(() => events.Detail.Social.StateText is not null);
        Assert.Contains(t.Api.Requests, r => r.RequestUri!.AbsolutePath == "/v1/social/sources/event/01EV1");
    }

    // ---- Fenster ----

    [AvaloniaFact]
    public async Task Editor_windows_open_once_and_close_with_escape()
    {
        using var t = await SignedInAsync();
        var main = RoG.Linux.App.App.CreateMainWindow(t.Services);
        try
        {
            var closedWith = (bool?)null;
            var news = NewsEditorWindow.Open(t.Services, NewsRights.For(t.Services.CurrentUser), null, changed => closedWith = changed);
            Assert.Same(news, NewsEditorWindow.Open(t.Services, NewsRights.For(t.Services.CurrentUser), null));
            await ViewModelTests.WaitUntil(() => news.Editor!.Drafts.Count == 3);
            news.Close();
            Assert.Null(NewsEditorWindow.OpenWindow);
            Assert.False(closedWith);

            var first = MatchEditorWindow.Open(t.Services, FakeContentAdminRoutes.MatchId);
            Assert.Same(first, MatchEditorWindow.Open(t.Services, FakeContentAdminRoutes.MatchId));
            var fresh = MatchEditorWindow.Open(t.Services, null);
            Assert.NotSame(first, fresh);
            Assert.Equal(2, MatchEditorWindow.OpenWindows.Count);
            first.Close();
            fresh.Close();
            Assert.Empty(MatchEditorWindow.OpenWindows);
        }
        finally
        {
            main.Close();
        }
    }
}
