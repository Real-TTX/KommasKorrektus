using System.Net;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>The Word file with changes that the editor exports carries the editor's open remarks as comments of Word, and nobody else's.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class CommentExportTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public CommentExportTests(AppFixture app) => _app = app;

    private static List<string> CommentTexts(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var part = doc.MainDocumentPart!.WordprocessingCommentsPart;
        return part?.Comments is null ? [] : [.. part.Comments.Elements<Comment>().Select(c => c.InnerText)];
    }

    [DatabaseFact]
    public async Task The_export_of_the_editor_has_the_open_remarks_of_the_staff_as_comments_and_the_clean_file_has_none()
    {
        await _app.EnsureAdminAsync();
        var (customer, _, document) = await _app.CreateCustomerWithDocumentAsync("Kommentarexport");
        await _app.CreateUserAsync("kunde-export@example.test", "Kunde-Passwort-2026", UserRole.Client, customer.Id);
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var admin = await db.User.FirstAsync(u => u.Role == UserRole.Admin);
            var client = await db.User.FirstAsync(u => u.Email == "kunde-export@example.test");
            DocumentComment Remark(string text, long? by) => new() { DocumentId = document.Id, VersionNo = 2, ParagraphIndex = 0, Quote = "Hexe", Text = text, AuthorUserId = by, AuthorName = by == admin.Id ? "Anna Lektorin" : "Jemand" };
            db.DocumentComment.Add(Remark("Welche Hexe ist gemeint?", admin.Id));
            db.DocumentComment.Add(Remark("Das ist erledigt.", admin.Id).With(c => c.IsResolved = true));
            db.DocumentComment.Add(Remark("Aus der Word-Datei.", admin.Id).With(c => c.Source = "docx"));
            db.DocumentComment.Add(Remark("Der Kunde schreibt dazu.", client.Id));
            db.DocumentComment.Add(Remark("Ein Besucher schreibt.", null).With(c => c.ShareLinkId = 1));
            await db.SaveChangesAsync();
        });

        using var http = _app.NewClient();
        await AppFixture.LoginAsync(http, "admin@example.test", "Admin-Passwort-2026");

        var tracked = await http.GetAsync($"/Documents/Export?id={document.Id}&format={nameof(ExportFormat.DocxTracked)}");
        Assert.Equal(HttpStatusCode.OK, tracked.StatusCode);
        var texts = CommentTexts(await tracked.Content.ReadAsByteArrayAsync());
        Assert.Single(texts);
        Assert.Contains("Welche Hexe ist gemeint?", texts[0]);

        var clean = await http.GetAsync($"/Documents/Export?id={document.Id}&format={nameof(ExportFormat.DocxClean)}");
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);
        Assert.Empty(CommentTexts(await clean.Content.ReadAsByteArrayAsync()));
    }
}

internal static class CommentTestExtensions
{
    public static T With<T>(this T item, Action<T> change)
    {
        change(item);
        return item;
    }
}
