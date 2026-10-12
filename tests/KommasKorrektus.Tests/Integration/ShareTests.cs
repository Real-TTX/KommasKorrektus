using System.Net;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public sealed class ShareTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public ShareTests(AppFixture app) => _app = app;

    private async Task<(ShareLink Link, Document Document)> NewLinkAsync(string name, string? password = "Rabenwald1", int days = 7, bool allowDownload = true, bool showChanges = true)
    {
        await _app.EnsureAdminAsync();
        var (_, project, document) = await _app.CreateCustomerWithDocumentAsync(name);
        var link = await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Gegenlesen " + name, "Bitte prüfen.", days, password, allowDownload, showChanges, allowComments: true, allowApproval: true, "Annika", "annika@example.test"));
        return (link, document);
    }

    [DatabaseFact]
    public async Task A_link_with_a_password_shows_nothing_until_the_password_is_given()
    {
        var (link, document) = await NewLinkAsync("Passwort");
        using var visitor = _app.NewClient();

        var gate = await visitor.GetAsync($"/s/{link.Token}");
        var html = await AppFixture.TextAsync(gate);
        Assert.Equal(HttpStatusCode.OK, gate.StatusCode);
        Assert.Contains("mit einem Passwort geschützt", html);
        Assert.DoesNotContain("Text Passwort", html);

        // not even the document page or a download is reachable around the gate
        Assert.DoesNotContain("<ins>", await AppFixture.TextAsync(visitor, $"/s/{link.Token}/document/{document.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxClean")).StatusCode);

        var wrong = await AppFixture.PostFormAsync(visitor, $"/s/{link.Token}", new Dictionary<string, string> { ["Password"] = "falsch" }, $"/s/{link.Token}?handler=Password");
        Assert.Contains("Das Passwort stimmt nicht.", await AppFixture.TextAsync(wrong));

        var right = await AppFixture.PostFormAsync(visitor, $"/s/{link.Token}", new Dictionary<string, string> { ["Password"] = "Rabenwald1" }, $"/s/{link.Token}?handler=Password");
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);

        var content = await AppFixture.TextAsync(visitor, $"/s/{link.Token}");
        Assert.Contains("Text Passwort", content);
        Assert.Contains("Bitte prüfen.", content);
    }

    [DatabaseFact]
    public async Task The_changes_are_shown_and_the_files_can_be_downloaded_when_the_link_allows_it()
    {
        var (link, document) = await NewLinkAsync("Änderungen", password: null);
        using var visitor = _app.NewClient();

        var page = await AppFixture.TextAsync(visitor, $"/s/{link.Token}/document/{document.Id}?mode=changes");
        Assert.Contains("<del>hate</del>", page);
        Assert.Contains("<ins>hatte</ins>", page);

        var docx = await visitor.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxTracked");
        Assert.Equal(HttpStatusCode.OK, docx.StatusCode);
        Assert.Equal((byte)'P', (await docx.Content.ReadAsByteArrayAsync())[0]);

        var pdf = await visitor.GetAsync($"/s/{link.Token}/download/{document.Id}?format=PdfChanges");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        // every visit and download is counted for the editor
        var counted = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().ShareAccess.AsNoTracking().Where(a => a.ShareLinkId == link.Id).Select(a => a.Action).ToListAsync());
        Assert.Contains(ShareAction.View, counted);
        Assert.Equal(2, counted.Count(a => a == ShareAction.Download));
    }

    [DatabaseFact]
    public async Task A_link_that_does_not_allow_downloads_or_changes_keeps_both_back()
    {
        var (link, document) = await NewLinkAsync("Streng", password: null, allowDownload: false, showChanges: false);
        using var visitor = _app.NewClient();

        var page = await AppFixture.TextAsync(visitor, $"/s/{link.Token}/document/{document.Id}");
        Assert.DoesNotContain("<ins>", page);
        Assert.DoesNotContain("Herunterladen", page);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxClean")).StatusCode);
    }

    [DatabaseFact]
    public async Task Remarks_and_the_approval_of_a_visitor_reach_the_editor()
    {
        var (link, document) = await NewLinkAsync("Rückmeldung", password: null);
        using var visitor = _app.NewClient();
        var url = $"/s/{link.Token}/document/{document.Id}";

        var comment = await AppFixture.PostFormAsync(visitor, url, new Dictionary<string, string> { ["Input.Name"] = "Annika", ["Input.Paragraph"] = "2", ["Input.Text"] = "Das Komma bitte lassen." }, url + "?handler=Comment");
        Assert.Equal(HttpStatusCode.Redirect, comment.StatusCode);
        var approval = await AppFixture.PostFormAsync(visitor, url, new Dictionary<string, string> { ["Approval.Name"] = "Annika Federmann" }, url + "?handler=Approve");
        Assert.Equal(HttpStatusCode.Redirect, approval.StatusCode);

        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var saved = await db.DocumentComment.AsNoTracking().SingleAsync(c => c.ShareLinkId == link.Id);
            Assert.Equal("Das Komma bitte lassen.", saved.Text);
            Assert.Equal("Annika", saved.AuthorName);
            Assert.Equal(1, saved.ParagraphIndex);
            Assert.Null(saved.AuthorUserId);

            var approved = await db.ShareLink.AsNoTracking().SingleAsync(l => l.Id == link.Id);
            Assert.Equal("Annika Federmann", approved.ApprovedByName);
            Assert.NotNull(approved.ApprovedDate);
        });

        // a remark without text is refused
        var empty = await AppFixture.PostFormAsync(visitor, url, new Dictionary<string, string> { ["Input.Name"] = "Annika", ["Input.Text"] = " " }, url + "?handler=Comment");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Bitte schreibe eine Anmerkung.", await AppFixture.TextAsync(empty));
    }

    [DatabaseFact]
    public async Task A_link_that_ran_out_was_taken_back_or_never_existed_shows_nothing()
    {
        var (expired, _) = await NewLinkAsync("Abgelaufen", password: null);
        var (revoked, _) = await NewLinkAsync("Zurückgezogen", password: null);
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            (await db.ShareLink.FirstAsync(l => l.Id == expired.Id)).ExpiresDate = DateTime.UtcNow.AddMinutes(-1);
            (await db.ShareLink.FirstAsync(l => l.Id == revoked.Id)).RevokedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
        using var visitor = _app.NewClient();

        var gone = await visitor.GetAsync($"/s/{expired.Token}");
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        var html = await AppFixture.TextAsync(gone);
        Assert.Contains("abgelaufen", html);
        Assert.DoesNotContain("Gegenlesen", html);

        var taken = await visitor.GetAsync($"/s/{revoked.Token}");
        Assert.Equal(HttpStatusCode.Gone, taken.StatusCode);
        Assert.Contains("zurückgezogen", await AppFixture.TextAsync(taken));

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/s/{Guid.NewGuid()}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Ten_wrong_passwords_lock_the_link_even_for_the_right_one()
    {
        var (link, _) = await NewLinkAsync("Gesperrt");
        using var visitor = _app.NewClient();
        var post = $"/s/{link.Token}?handler=Password";

        for (var i = 0; i < ShareService.MaxWrongPasswords; i++)
        {
            await AppFixture.PostFormAsync(visitor, $"/s/{link.Token}", new Dictionary<string, string> { ["Password"] = "falsch" + i }, post);
        }

        var right = await AppFixture.PostFormAsync(visitor, $"/s/{link.Token}", new Dictionary<string, string> { ["Password"] = "Rabenwald1" }, post);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Contains("Zu viele falsche Versuche", await AppFixture.TextAsync(right));
    }

    [DatabaseFact]
    public async Task The_visit_of_a_signed_in_editor_is_not_counted_as_the_visit_of_the_customer()
    {
        var (link, _) = await NewLinkAsync("Vorschau", password: null);
        using var editor = _app.NewClient();
        await AppFixture.LoginAsync(editor, "admin@example.test", "Admin-Passwort-2026");
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync($"/s/{link.Token}")).StatusCode);

        var views = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().ShareLink.AsNoTracking().Where(l => l.Id == link.Id).Select(l => l.ViewCount).SingleAsync());
        Assert.Equal(0, views);
    }
}
