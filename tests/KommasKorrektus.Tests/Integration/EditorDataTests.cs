using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>What the page hands to editor.js: the script reads camelCase names, so a comment and a style entry must come that way (the serializer keeps the names of the properties).</summary>
[Collection(IntegrationCollection.Name)]
public sealed class EditorDataTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public EditorDataTests(AppFixture app) => _app = app;

    [DatabaseFact]
    public async Task The_comments_and_the_style_sheet_reach_the_editor_with_the_names_the_script_reads()
    {
        await _app.EnsureAdminAsync();
        var (_, project, document) = await _app.CreateCustomerWithDocumentAsync("Daten");
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            db.DocumentComment.Add(new DocumentComment { DocumentId = document.Id, VersionNo = 2, ParagraphIndex = 1, Quote = "hatte einen Besen", Text = "Komma?", AuthorName = "Annika" });
            db.StyleEntry.Add(new StyleEntry { ProjectId = project.Id, Category = StyleCategory.Spelling, Term = "Rabenwald", Decision = "zusammen" });
            await db.SaveChangesAsync();
        });

        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");
        var html = await client.GetStringAsync($"/Documents/Editor?id={document.Id}");
        var json = Regex.Match(html, "<script type=\"application/json\" id=\"editor-data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var data = JsonDocument.Parse(json).RootElement;

        // the autosave settings the script needs (the threshold included)
        Assert.Equal(3, data.GetProperty("autosaveSeconds").GetInt32());
        Assert.Equal(10, data.GetProperty("autosaveMinChars").GetInt32());

        var comment = data.GetProperty("comments")[0];
        Assert.True(comment.GetProperty("id").GetInt64() > 0);
        Assert.Equal(1, comment.GetProperty("paragraphIndex").GetInt32());
        Assert.Equal("hatte einen Besen", comment.GetProperty("quote").GetString());
        Assert.Equal("Komma?", comment.GetProperty("text").GetString());
        Assert.Equal("Annika", comment.GetProperty("author").GetString());
        Assert.False(comment.GetProperty("isResolved").GetBoolean());

        var style = data.GetProperty("styles")[0];
        Assert.Equal("Rabenwald", style.GetProperty("term").GetString());
        Assert.Equal("zusammen", style.GetProperty("decision").GetString());
        Assert.True(style.TryGetProperty("category", out _));
    }

    [DatabaseFact]
    public async Task A_word_can_be_put_into_the_style_sheet_while_writing()
    {
        await _app.EnsureAdminAsync();
        var (_, project, document) = await _app.CreateCustomerWithDocumentAsync("Stilblatt");
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");

        async Task<HttpResponseMessage> PostAsync(object body)
        {
            var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={document.Id}");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={document.Id}&handler=Style") { Content = JsonContent.Create(body) };
            request.Headers.Add("RequestVerificationToken", token);
            return await client.SendAsync(request);
        }

        var first = await PostAsync(new { category = "Character", term = "Rabenwald", decision = "ein Wort", note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<JsonElement>());
        Assert.True(created.GetProperty("created").GetBoolean());
        Assert.Equal("Figur", created.GetProperty("category").GetString());

        // the same term again (other case) is the same entry, changed
        var again = await PostAsync(new { category = "Spelling", term = "rabenwald", decision = "zusammen", note = (string?)null });
        Assert.False((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetBoolean());
        var rows = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().StyleEntry.AsNoTracking().Where(e => e.ProjectId == project.Id).ToListAsync());
        var entry = Assert.Single(rows);
        Assert.Equal(StyleCategory.Spelling, entry.Category);
        Assert.Equal("zusammen", entry.Decision);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(new { category = "Spelling", term = "  ", decision = (string?)null, note = (string?)null })).StatusCode);

        var html = await client.GetStringAsync($"/Documents/Editor?id={document.Id}");
        Assert.Contains("\"term\":\"Rabenwald\"", html);
    }
}
