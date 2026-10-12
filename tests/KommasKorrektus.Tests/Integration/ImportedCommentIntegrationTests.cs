using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Tests.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>The comments that are in a file (the author's, or the customer's that comes back) are imported with it and shown in the editor.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ImportedCommentIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public ImportedCommentIntegrationTests(AppFixture app) => _app = app;

    [DatabaseFact]
    public async Task Comments_of_a_Word_file_are_imported_with_it_and_shown_in_the_editor_and_on_the_document_page()
    {
        await _app.EnsureAdminAsync();
        var (_, project, _) = await _app.CreateCustomerWithDocumentAsync("Kommentare");
        await _app.CreateUserAsync("kommentare-eins@example.test", "Eins-Passwort-2026", UserRole.User);

        var (documentId, notes) = await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var import = await sp.GetRequiredService<DocumentService>().ImportAsync(await db.Project.FirstAsync(p => p.Id == project.Id), "Mit Kommentaren", "mit.docx", ImportedCommentTests.WordWithComments());
            await db.SaveChangesAsync();
            return (import.Document.Id, import.Notes);
        });
        Assert.Contains("4 Kommentare aus der Datei wurden übernommen.", notes);

        var rows = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().DocumentComment.AsNoTracking().Where(c => c.DocumentId == documentId).OrderBy(c => c.ParagraphIndex).ThenBy(c => c.Id).ToListAsync());
        Assert.Equal(["Hier fehlt ein Komma.", "Ist ergänzt.", "Bitte kürzen.", "Nur eine Marke."], rows.Select(c => c.Text));
        Assert.Equal([1, 1, 2, 4], rows.Select(c => c.ParagraphIndex));
        Assert.Equal([false, false, true, false], rows.Select(c => c.IsResolved));
        Assert.All(rows, c => Assert.Equal("docx", c.Source));
        Assert.All(rows, c => Assert.Equal(1, c.VersionNo));
        Assert.Equal("Maria Autorin", rows[0].AuthorName);
        Assert.Equal("einem markierten Satz", rows[0].Quote);
        Assert.Equal(new DateTime(2026, 10, 12, 14, 30, 0, DateTimeKind.Utc), rows[0].WrittenDate);
        Assert.Null(rows[0].AuthorUserId);

        // the editor and the page of the document show them, with where they came from
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "kommentare-eins@example.test", "Eins-Passwort-2026");
        var editor = await client.GetStringAsync($"/Documents/Editor?id={documentId}");
        Assert.Contains("\"source\":\"docx\"", editor);
        Assert.Contains("\"author\":\"Maria Autorin\"", editor);
        Assert.Contains("\"created\":\"12.10.2026 14:30\"", editor);
        var details = await AppFixture.TextAsync(await client.GetAsync($"/Documents/Details?id={documentId}&tab=comments"));
        Assert.Contains("Maria Autorin (Word)", details);
        Assert.Contains("Hier fehlt ein Komma.", details);
    }

    [DatabaseFact]
    public async Task Comments_in_the_file_a_customer_sends_back_belong_to_that_version()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Rueckkommentar");

        var result = await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var saved = await sp.GetRequiredService<DocumentService>().AddFileVersionAsync(await db.Document.FirstAsync(d => d.Id == document.Id), "kunde.docx", ImportedCommentTests.WordWithComments(), null, null);
            var comments = await db.DocumentComment.AsNoTracking().Where(c => c.DocumentId == document.Id).ToListAsync();
            return (saved.Version.VersionNo, saved.Version.Comment, comments);
        });

        Assert.Equal(4, result.comments.Count);
        Assert.All(result.comments, c => Assert.Equal(result.VersionNo, c.VersionNo));
        Assert.Contains("4 Kommentare aus der Datei wurden übernommen.", result.Comment);
    }

    [Fact]
    public void A_file_without_comments_brings_none()
    {
        // (the importer is the same one: the plain text of a manuscript has no remarks)
        Assert.Empty(KommasKorrektus.Core.Documents.PlainTextReader.Read("Ein Absatz.\n\nNoch einer.").Comments);
    }
}
