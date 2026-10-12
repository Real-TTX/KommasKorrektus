using System.Text.Json;
using System.Text.RegularExpressions;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
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
}
