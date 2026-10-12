using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>The page view: a document drawn in the layout of the Word file it came from, in the editor, the comparison and the pages of authors and clients.</summary>
[Collection(IntegrationCollection.Name)]
public sealed partial class PageViewTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PageViewTests(AppFixture app) => _app = app;

    [GeneratedRegex(@"/layout/[0-9a-f]{32}\.css")]
    private static partial Regex StylesheetAddress();

    /// <summary>A customer with a project and a Word document (the sample with headings, a list, a table ...) that has a second version with a changed word.</summary>
    private Task<(Customer Customer, Project Project, Document Document, string[] First, string[] Second)> WordDocumentAsync(string name)
        => _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var customer = new Customer { Number = "K-" + Guid.NewGuid().ToString("N")[..6], Kind = CustomerKind.Person, FirstName = "Test", LastName = name, Email = name.ToLowerInvariant() + "@example.test" };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
            var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..6], CustomerId = customer.Id, Title = "Projekt " + name };
            db.Project.Add(project);
            await db.SaveChangesAsync();

            var docx = Core.SampleDocx.Build();
            var documents = sp.GetRequiredService<DocumentService>();
            var import = await documents.ImportAsync(project, "Word " + name, "sample.docx", docx);
            await db.SaveChangesAsync();
            var first = DocxReader.ReadParagraphs(docx).ToArray();
            var second = first.ToArray();
            second[1] = second[1].Replace("fetter", "dicker");
            await documents.SaveAsync(import.Document, string.Join('\n', second), VersionOrigin.Manual, null, null);
            await db.SaveChangesAsync();
            return (customer, project, await db.Document.AsNoTracking().FirstAsync(d => d.Id == import.Document.Id), first, second);
        });

    private async Task<HttpClient> AdminAsync()
    {
        await _app.EnsureAdminAsync();
        var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");
        return client;
    }

    private static async Task<JsonElement> LayoutAsync(HttpClient client, long documentId, IEnumerable<string> paragraphs)
    {
        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={documentId}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={documentId}&handler=Layout") { Content = JsonContent.Create(new { paragraphs }) };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    [DatabaseFact]
    public async Task The_editor_draws_its_text_as_a_page_and_the_stylesheet_is_open_to_everybody()
    {
        var (_, _, document, _, second) = await WordDocumentAsync("Seite");
        using var editor = await AdminAsync();

        var page = await LayoutAsync(editor, document.Id, second);
        Assert.True(page.GetProperty("ok").GetBoolean());
        Assert.True(page.GetProperty("formatted").GetBoolean());
        var html = page.GetProperty("html").GetString()!;
        Assert.Contains("Kapitel 1", html);
        Assert.Contains("dicker", html);
        Assert.DoesNotContain("fetter", html);
        // The page of the unsaved text carries the formatting of the file: the editor puts the numbers of the list in, and the page says what a new paragraph looks like.
        Assert.Contains("data-m=\"1.\"", html);
        Assert.Contains("data-nx=", html);
        Assert.Equal(second.Length, Regex.Matches(html, @"<p class=""kk-lp [^""]*"" id=""p\d+"" data-n=""\d+""").Count);

        // The stylesheet is named by a hash and open to anybody who has the page (a reader behind a share link has no account).
        var address = page.GetProperty("css").GetString()!;
        Assert.Matches(StylesheetAddress(), address);
        using var anonymous = _app.NewClient();
        var css = await anonymous.GetAsync(address);
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", css.Headers.CacheControl!.ToString());
        Assert.Contains("font-weight:bold", await css.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/layout/" + new string('0', 32) + ".css")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/layout/not-a-key.css")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_text_without_a_place_for_every_paragraph_is_refused_so_that_the_editor_stays_in_the_text_view()
    {
        var (_, _, document, first, _) = await WordDocumentAsync("Zelle");
        using var editor = await AdminAsync();

        // The only paragraph of a table cell is deleted: the export leaves an empty one in the cell, so the page would have one paragraph too many.
        var page = await LayoutAsync(editor, document.Id, first.Where(p => p != "Angaben"));
        Assert.False(page.GetProperty("ok").GetBoolean());
        Assert.Contains("Tabellenzelle", page.GetProperty("reason").GetString());
    }

    [DatabaseFact]
    public async Task A_text_document_has_a_plain_page_and_says_so()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Plain");
        using var editor = await AdminAsync();
        var page = await LayoutAsync(editor, document.Id, ["Es war einmal eine Hexe.", "Der Rabe folgte ihr."]);
        Assert.True(page.GetProperty("ok").GetBoolean());
        Assert.False(page.GetProperty("formatted").GetBoolean());
        Assert.Contains("Es war einmal eine Hexe.", page.GetProperty("html").GetString());
    }

    [DatabaseFact]
    public async Task The_comparison_and_a_version_can_be_read_as_a_page_with_the_changes_in_the_formatted_text()
    {
        var (_, _, document, _, _) = await WordDocumentAsync("Vergleich");
        using var editor = await AdminAsync();

        var compare = await AppFixture.TextAsync(editor, $"/Documents/Compare?id={document.Id}&from=1&to=2&mode=page&changes=false");
        Assert.Contains("kk-sheet", compare);
        Assert.Matches(StylesheetAddress(), compare);
        Assert.Matches("<del class=\"kk-del\">(<span[^>]*>)?fetter(</span>)?</del>", compare);
        Assert.Matches("<ins class=\"kk-ins\">(<span[^>]*>)?dicker(</span>)?</ins>", compare);
        // The pages of the file's own revision marks (by another author) are no changes of the editor.
        Assert.DoesNotContain("gestrichen", compare);
        Assert.Contains("data-diff", compare);

        // Only what changed, with the paragraphs around it; the rest is folded away.
        var folded = await AppFixture.TextAsync(editor, $"/Documents/Compare?id={document.Id}&from=1&to=2&mode=page&changes=true");
        Assert.Contains("unveränderte Absätze", folded);

        var version = await AppFixture.TextAsync(editor, $"/Documents/Version?id={document.Id}&no=2&view=page");
        Assert.Contains("kk-sheet", version);
        Assert.Contains("dicker", version);
        Assert.Contains("Fußnoten", version);

        // The view is remembered with the account: the page that was asked for just now is what a version opens as, until text is asked for.
        Assert.Contains("kk-sheet", await AppFixture.TextAsync(editor, $"/Documents/Version?id={document.Id}&no=2"));
        var text = await AppFixture.TextAsync(editor, $"/Documents/Version?id={document.Id}&no=2&view=text");
        Assert.DoesNotContain("kk-sheet", text);
        Assert.DoesNotContain("kk-sheet", await AppFixture.TextAsync(editor, $"/Documents/Version?id={document.Id}&no=2"));
    }

    [DatabaseFact]
    public async Task A_share_link_shows_the_page_with_the_changes_only_when_it_may()
    {
        var (_, project, document, _, _) = await WordDocumentAsync("Link");
        var open = await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Offen", null, 7, null, true, showChanges: true, allowComments: true, allowApproval: false, "Annika", null));
        var closed = await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Zu", null, 7, null, true, showChanges: false, allowComments: true, allowApproval: false, "Annika", null));
        using var visitor = _app.NewClient();

        var withChanges = await AppFixture.TextAsync(visitor, $"/s/{open.Token}/document/{document.Id}?mode=page");
        Assert.Contains("kk-sheet", withChanges);
        Assert.Matches("<ins class=\"kk-ins\">(<span[^>]*>)?dicker(</span>)?</ins>", withChanges);

        // A link that keeps the changes back draws the clean page: nothing of the old words, not even in the markup.
        var clean = await AppFixture.TextAsync(visitor, $"/s/{closed.Token}/document/{document.Id}?mode=page");
        Assert.Contains("kk-sheet", clean);
        Assert.Contains("dicker", clean);
        Assert.DoesNotContain("fetter", clean);
        Assert.DoesNotContain("kk-ins", clean);
        Assert.DoesNotContain("kk-del", clean);
        Assert.DoesNotContain("data-diff", clean);
    }

    [DatabaseFact]
    public async Task A_client_reads_the_page_of_its_own_documents_and_never_the_page_of_another_clients()
    {
        await _app.EnsureAdminAsync();
        var (customerA, _, documentA, _, _) = await WordDocumentAsync("Anna");
        var (_, _, documentB, _, _) = await WordDocumentAsync("Berta");
        await _app.CreateUserAsync("seite-anna@example.test", "Anna-Passwort-2026", UserRole.Client, customerA.Id);
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "seite-anna@example.test", "Anna-Passwort-2026");

        var own = await AppFixture.TextAsync(client, $"/Portal/Document?id={documentA.Id}&mode=page");
        Assert.Contains("kk-sheet", own);
        Assert.Matches("<ins class=\"kk-ins\">(<span[^>]*>)?dicker(</span>)?</ins>", own);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Document?id={documentB.Id}&mode=page")).StatusCode);
    }
}
