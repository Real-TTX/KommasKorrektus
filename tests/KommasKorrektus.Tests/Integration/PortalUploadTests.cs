using System.Net;
using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Tests.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>A client may send a file back through the portal when the administrator allows it: it becomes the next version, with the comments that are in it.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PortalUploadTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PortalUploadTests(AppFixture app) => _app = app;

    private Task SetUploadsAsync(bool allowed) => _app.WithScopeAsync(sp =>
    {
        sp.GetRequiredService<ConfigStore>().Update(c => c.Security.AllowPortalUploads = allowed);
        return Task.CompletedTask;
    });

    /// <param name="formOf">The document whose page the form (and its token) comes from: the client's own, also when the file is sent to another.</param>
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, long documentId, byte[] file, string fileName, string note, long? formOf = null)
    {
        var token = await AppFixture.TokenAsync(client, $"/Portal/Document?id={formOf ?? documentId}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(note), "UploadNote" },
            { new ByteArrayContent(file), "UploadedFile", fileName },
        };
        return await client.PostAsync($"/Portal/Document?id={documentId}&handler=Upload", form);
    }

    [DatabaseFact]
    public async Task Without_the_permission_a_client_cannot_send_a_file_back_and_does_not_see_the_form()
    {
        await _app.EnsureAdminAsync();
        var (customer, _, document) = await _app.CreateCustomerWithDocumentAsync("Sperre");
        await _app.CreateUserAsync("sperre@example.test", "Sperre-Passwort-2026", UserRole.Client, customer.Id);
        await SetUploadsAsync(false);

        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "sperre@example.test", "Sperre-Passwort-2026");
        Assert.DoesNotContain("Eine Datei zurückschicken", await AppFixture.TextAsync(client, $"/Portal/Document?id={document.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(client, document.Id, ImportedCommentTests.WordWithComments(), "zurueck.docx", "")).StatusCode);

        var versions = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().DocumentVersion.AsNoTracking().CountAsync(v => v.DocumentId == document.Id));
        Assert.Equal(2, versions);
    }

    [DatabaseFact]
    public async Task A_client_sends_a_file_back_and_it_is_the_next_version_of_its_own_document_only()
    {
        await _app.EnsureAdminAsync();
        var (customerA, _, documentA) = await _app.CreateCustomerWithDocumentAsync("Hochlader");
        var (_, _, documentB) = await _app.CreateCustomerWithDocumentAsync("Fremd");
        var anna = await _app.CreateUserAsync("hochlader@example.test", "Hochlader-Passwort-2026", UserRole.Client, customerA.Id);
        await SetUploadsAsync(true);
        try
        {
            using var client = _app.NewClient();
            await AppFixture.LoginAsync(client, "hochlader@example.test", "Hochlader-Passwort-2026");
            Assert.Contains("Eine Datei zurückschicken", await AppFixture.TextAsync(client, $"/Portal/Document?id={documentA.Id}"));

            var response = await UploadAsync(client, documentA.Id, ImportedCommentTests.WordWithComments(), "zurueck.docx", "Bitte die Kommentare lesen.");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            var (versions, comments, history) = await _app.WithScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<KkDbContext>();
                var v = await db.DocumentVersion.AsNoTracking().Where(x => x.DocumentId == documentA.Id).OrderBy(x => x.VersionNo).ToListAsync();
                var c = await db.DocumentComment.AsNoTracking().Where(x => x.DocumentId == documentA.Id).ToListAsync();
                var h = await db.HistoryEntry.AsNoTracking().Where(x => x.CustomerId == customerA.Id && x.RefId == documentA.Id).ToListAsync();
                return (v, c, h);
            });
            var added = versions[^1];
            Assert.Equal(3, versions.Count);
            Assert.Equal(VersionOrigin.Upload, added.Origin);
            Assert.Equal(anna.Id, added.CreateUserId);
            Assert.Contains("Bitte die Kommentare lesen.", added.Comment);
            Assert.Equal(4, comments.Count);
            Assert.All(comments, c => Assert.Equal(added.VersionNo, c.VersionNo));
            Assert.Contains(history, h => h.Title.Contains("Datei von"));

            // a document of somebody else is "not found", and nothing is added to it
            Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(client, documentB.Id, ImportedCommentTests.WordWithComments(), "fremd.docx", "", formOf: documentA.Id)).StatusCode);
            var others = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().DocumentVersion.AsNoTracking().CountAsync(v => v.DocumentId == documentB.Id));
            Assert.Equal(2, others);

            // something that is no document is refused with a word, not an error page
            var refused = await UploadAsync(client, documentA.Id, [1, 2, 3, 4], "kaputt.bin", "");
            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        }
        finally
        {
            await SetUploadsAsync(false);
        }
    }
}
