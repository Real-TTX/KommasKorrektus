using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>
/// The ping pong between editor and customer: a file the customer sends back is the next version of the document (a new round), so that it can be compared with
/// every other version, and the formatting of the page and of the Word export follows the file of the round.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PingPongTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PingPongTests(AppFixture app) => _app = app;

    /// <summary>A Word document (version 1), our pass over it (version 2), and the file the customer sent back: our change accepted, one more change of their own.</summary>
    private async Task<(Document Document, byte[] CustomerFile, string[] Second, string[] Customer)> RoundTripAsync(string name)
    {
        await _app.EnsureAdminAsync();
        return await _app.WithScopeAsync(async sp =>
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

            var customerText = second.ToArray();
            customerText[2] += " Der Kunde ergänzt einen Satz.";
            var customerFile = DocxWriter.Write(docx, first, customerText, RevisionMode.Accepted, "Kunde", DateTime.UtcNow);
            return (await db.Document.AsNoTracking().FirstAsync(d => d.Id == import.Document.Id), customerFile, second, customerText);
        });
    }

    private static string DocumentXml(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    [DatabaseFact]
    public async Task A_file_of_the_customer_becomes_the_next_version_and_starts_a_new_round()
    {
        var (document, customerFile, second, customer) = await RoundTripAsync("Runde");

        var added = await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var tracked = await db.Document.FirstAsync(d => d.Id == document.Id);
            var result = await sp.GetRequiredService<DocumentService>().AddFileVersionAsync(tracked, "kunde-v2.docx", customerFile, "Datei vom Kunden v2", "Mit Anmerkungen am Rand.");
            return result;
        });

        Assert.True(added.Created);
        Assert.Equal(3, added.Version.VersionNo);
        Assert.Equal(VersionOrigin.Upload, added.Version.Origin);
        Assert.Equal("Datei vom Kunden v2", added.Version.Label);
        Assert.Equal("Mit Anmerkungen am Rand.", added.Version.Comment);
        Assert.NotNull(added.Version.SourceFileId);
        // what the customer changed is the difference to the version before
        Assert.True(added.Delta.HasChanges);
        Assert.True(added.Delta.WordsAdded >= 5);
        Assert.Equal(string.Join('\n', customer), added.Version.Text);

        await _app.WithScopeAsync(async sp =>
        {
            var documents = sp.GetRequiredService<DocumentService>();
            var db = sp.GetRequiredService<KkDbContext>();
            var current = await db.Document.AsNoTracking().FirstAsync(d => d.Id == document.Id);
            Assert.Equal(3, current.CurrentVersionNo);

            // rounds: version 1 and 2 belong to the first, 3 begins the second
            Assert.Equal(1, (await documents.RoundOfAsync(current, 2))!.StartNo);
            var round = (await documents.RoundOfAsync(current, 3))!;
            Assert.Equal(3, round.StartNo);
            Assert.NotNull(round.Docx);
            Assert.Equal(customer, round.Paragraphs);

            // our next pass goes on from the customer's file: a new working version, compared with theirs
            var saved = await documents.SaveAsync(current, string.Join('\n', customer).Replace("Besen", "Reisigbesen"), VersionOrigin.Working, null, null);
            await db.SaveChangesAsync();
            Assert.Equal(4, saved.Version.VersionNo);
            Assert.Equal(3, await documents.RoundStartAsync(current, 4));
            var (_, stats) = await documents.CompareAsync(current, 3, 4);
            Assert.True(stats.HasChanges);
        });
    }

    [DatabaseFact]
    public async Task The_word_export_and_the_page_follow_the_file_of_the_round()
    {
        var (document, customerFile, _, customer) = await RoundTripAsync("Export");
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var tracked = await db.Document.FirstAsync(d => d.Id == document.Id);
            var documents = sp.GetRequiredService<DocumentService>();
            await documents.AddFileVersionAsync(tracked, "kunde-v2.docx", customerFile, null, null);
            var own = string.Join('\n', customer).Replace("dicker", "dickste");
            await documents.SaveAsync(tracked, own, VersionOrigin.Manual, "Zweiter Durchgang", null);
            await db.SaveChangesAsync();
        });

        // The tracked changes of this round are what the editor did since the file of the customer, not everything since the original.
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");
        var export = await client.GetAsync($"/Documents/Export?id={document.Id}&format={nameof(ExportFormat.DocxTracked)}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Contains("<w:ins ", DocumentXml(bytes));
        // (the change is marked letter by letter, dick[er][ste]: accepting it gives our text, rejecting it the customer's, which is where this round began)
        Assert.Contains("dickste", string.Join('\n', Core.RevisionView.Accepted(bytes)));
        Assert.Contains("dicker", string.Join('\n', Core.RevisionView.Rejected(bytes)));

        // and from the original on, both rounds are in it
        var all = await client.GetAsync($"/Documents/Export?id={document.Id}&from=1&format={nameof(ExportFormat.DocxTracked)}");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var allBytes = await all.Content.ReadAsByteArrayAsync();
        Assert.Contains("fetter", string.Join('\n', Core.RevisionView.Rejected(allBytes)));
        Assert.Contains("dickste", string.Join('\n', Core.RevisionView.Accepted(allBytes)));
        Assert.Contains("Der Kunde ergänzt einen Satz.", DocumentXml(allBytes));

        // the page of the newest version is drawn in the formatting of the customer's file (it matched), and the comparison with the version before shows the change
        var page = await _app.WithScopeAsync(async sp => await sp.GetRequiredService<LayoutService>().ForChangesAsync(
            await sp.GetRequiredService<KkDbContext>().Document.AsNoTracking().FirstAsync(d => d.Id == document.Id), 3, 4));
        Assert.True(page.Formatted);
        Assert.Contains("dickerste", System.Text.RegularExpressions.Regex.Replace(page.Html, "<[^>]+>", string.Empty));
        var compare = await AppFixture.TextAsync(client, $"/Documents/Compare?id={document.Id}&from=2&to=3&mode=page&changes=false");
        Assert.Contains("kk-sheet", compare);
        Assert.Contains("Der Kunde ergänzt einen Satz.", compare);
    }

    [DatabaseFact]
    public async Task The_page_takes_the_file_from_the_customer_and_the_list_of_versions_offers_it_again()
    {
        var (document, customerFile, _, _) = await RoundTripAsync("Seite");
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");

        var token = await AppFixture.TokenAsync(client, $"/Documents/NewVersion?id={document.Id}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(document.Id.ToString()), "Id" },
            { new StringContent("Datei vom Kunden v2"), "Label" },
            { new StringContent(string.Empty), "Comment" },
            { new StringContent(token), "__RequestVerificationToken" },
        };
        var file = new ByteArrayContent(customerFile);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(file, "UploadedFile", "kunde-v2.docx");
        var response = await client.PostAsync($"/Documents/NewVersion?id={document.Id}", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Documents/Compare", AppFixture.LocationPath(response));
        Assert.Contains("from=2", response.Headers.Location!.ToString());
        Assert.Contains("to=3", response.Headers.Location!.ToString());

        var details = await AppFixture.TextAsync(client, $"/Documents/Details?id={document.Id}");
        Assert.Contains("Datei vom Kunden v2", details);
        Assert.Contains($"/Documents/Download?id={document.Id}&version=3", details);

        var download = await client.GetAsync($"/Documents/Download?id={document.Id}&version=3");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(customerFile, await download.Content.ReadAsByteArrayAsync());
        // version 1 has the original, not the file of the customer
        var original = await client.GetAsync($"/Documents/Download?id={document.Id}");
        Assert.NotEqual(customerFile, await original.Content.ReadAsByteArrayAsync());

        // a file that is not a document is refused in words
        var bad = new MultipartFormDataContent
        {
            { new StringContent(document.Id.ToString()), "Id" },
            { new StringContent(await AppFixture.TokenAsync(client, $"/Documents/NewVersion?id={document.Id}")), "__RequestVerificationToken" },
        };
        var junk = new ByteArrayContent([1, 2, 3, 4, 5]);
        junk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        bad.Add(junk, "UploadedFile", "kunde.bin");
        var refused = await client.PostAsync($"/Documents/NewVersion?id={document.Id}", bad);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Dieses Dateiformat kann ich nicht lesen", await AppFixture.TextAsync(refused));
    }
}
