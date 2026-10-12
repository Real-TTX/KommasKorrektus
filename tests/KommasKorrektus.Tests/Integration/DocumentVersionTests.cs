using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Writing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>A version is a pass over the text, not a click: saving goes into the version being worked on until the editor closes the pass.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class DocumentVersionTests : IClassFixture<AppFixture>
{
    private const string Original = "Es war einmal eine Hexe.\nSie hate einen Besen.";
    private const string Pass1 = "Es war einmal eine Hexe.\nSie hatte einen Besen.";
    private const string Pass1More = "Es war einmal eine Hexe.\nSie hatte einen alten Besen.";
    private const string Pass1Final = "Es war einmal eine Hexe.\nSie hatte einen alten Besen und flog los.";
    private const string Pass2 = "Es war einmal eine junge Hexe.\nSie hatte einen alten Besen und flog los.";

    private readonly AppFixture _app;

    public DocumentVersionTests(AppFixture app) => _app = app;

    private Task<long> ImportAsync(string name)
        => _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var customer = new Customer { Number = "K-" + Guid.NewGuid().ToString("N")[..6], Kind = CustomerKind.Person, FirstName = "Test", LastName = name, Email = name.ToLowerInvariant() + "@example.test" };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
            var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..6], CustomerId = customer.Id, Title = "Projekt " + name };
            db.Project.Add(project);
            await db.SaveChangesAsync();
            var import = await sp.GetRequiredService<DocumentService>().ImportAsync(project, "Text " + name, "text.txt", System.Text.Encoding.UTF8.GetBytes(Original));
            await db.SaveChangesAsync();
            return import.Document.Id;
        });

    private sealed record Saved(DocumentService.SaveResult Result, List<DocumentVersion> Versions, int Current);

    private Task<Saved> SaveAsync(long documentId, string text, VersionOrigin origin, string? label = null)
        => _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var document = await db.Document.FirstAsync(d => d.Id == documentId);
            var result = await sp.GetRequiredService<DocumentService>().SaveAsync(document, text, origin, label, null);
            var versions = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == documentId).OrderBy(v => v.VersionNo).ToListAsync();
            return new Saved(result, versions, document.CurrentVersionNo);
        });

    private static string[] Paragraphs(DocumentVersion version) => TextMetrics.Paragraphs(version.Text);

    [DatabaseFact]
    public async Task Saving_again_and_again_stays_in_one_version()
    {
        var id = await ImportAsync("Mehrfach");

        var first = await SaveAsync(id, Pass1, VersionOrigin.Working);
        Assert.True(first.Result.Created);
        Assert.Equal(2, first.Result.Version.VersionNo);
        Assert.Equal(VersionOrigin.Working, first.Result.Version.Origin);

        await SaveAsync(id, Pass1More, VersionOrigin.Working);
        var third = await SaveAsync(id, Pass1Final, VersionOrigin.Working);

        Assert.False(third.Result.Created);
        Assert.Equal(2, third.Versions.Count);
        Assert.Equal(2, third.Current);
        Assert.Equal(TextMetrics.Paragraphs(Pass1Final), Paragraphs(third.Versions[1]));

        // What the version says it changed is measured against the version before it (the original), not against the save before.
        var against = DocumentDiff.Stats(DocumentDiff.Compare(Paragraphs(third.Versions[0]), Paragraphs(third.Versions[1])));
        Assert.Equal((against.WordsAdded, against.WordsRemoved, against.WordsChanged), (third.Versions[1].WordsAdded, third.Versions[1].WordsRemoved, third.Versions[1].WordsChanged));
        Assert.True(third.Versions[1].WordsAdded > third.Result.Delta.WordsAdded);
    }

    [DatabaseFact]
    public async Task Closing_the_pass_makes_the_next_change_begin_a_new_version()
    {
        var id = await ImportAsync("Abschluss");
        await SaveAsync(id, Pass1, VersionOrigin.Working);

        // Nothing changed since the last save: closing only closes (and names) the version, it makes no new one.
        var closed = await SaveAsync(id, Pass1, VersionOrigin.Manual, "Erster Durchgang");
        Assert.False(closed.Result.Created);
        Assert.Equal(VersionOrigin.Manual, closed.Result.Version.Origin);
        Assert.Equal("Erster Durchgang", closed.Result.Version.Label);
        Assert.Equal(2, closed.Versions.Count);

        var next = await SaveAsync(id, Pass2, VersionOrigin.Working);
        Assert.True(next.Result.Created);
        Assert.Equal(3, next.Result.Version.VersionNo);
        Assert.Equal(3, next.Current);
        Assert.Equal(VersionOrigin.Working, next.Versions[2].Origin);
        Assert.Equal(TextMetrics.Paragraphs(Pass1), Paragraphs(next.Versions[1]));

        // ... and it goes on in the new one.
        var again = await SaveAsync(id, Pass2 + " Der Rabe folgte ihr.", VersionOrigin.Working);
        Assert.False(again.Result.Created);
        Assert.Equal(3, again.Versions.Count);
        Assert.Equal(TextMetrics.Paragraphs(Pass1), Paragraphs(again.Versions[1]));
    }

    [DatabaseFact]
    public async Task Closing_with_a_change_saves_and_closes_in_one_go()
    {
        var id = await ImportAsync("Zugleich");
        await SaveAsync(id, Pass1, VersionOrigin.Working);

        var closed = await SaveAsync(id, Pass1More, VersionOrigin.Manual);
        Assert.False(closed.Result.Created);
        Assert.Equal(2, closed.Versions.Count);
        Assert.Equal(VersionOrigin.Manual, closed.Versions[1].Origin);
        Assert.Equal(TextMetrics.Paragraphs(Pass1More), Paragraphs(closed.Versions[1]));

        // From the original straight to a closed version.
        var other = await ImportAsync("Gleich");
        var direct = await SaveAsync(other, Pass1, VersionOrigin.Manual, "Fertig");
        Assert.True(direct.Result.Created);
        Assert.Equal(VersionOrigin.Manual, direct.Result.Version.Origin);
    }

    [DatabaseFact]
    public async Task The_original_is_never_closed_or_renamed()
    {
        var id = await ImportAsync("Original");

        var saved = await SaveAsync(id, Original, VersionOrigin.Manual, "Mein Name");

        Assert.False(saved.Result.Created);
        Assert.Single(saved.Versions);
        Assert.Equal(VersionOrigin.Import, saved.Versions[0].Origin);
        Assert.Equal("Original", saved.Versions[0].Label);
    }

    [DatabaseFact]
    public async Task A_closed_version_can_be_named_afterwards_without_a_new_one()
    {
        var id = await ImportAsync("Umbenennen");
        await SaveAsync(id, Pass1, VersionOrigin.Manual);

        var named = await SaveAsync(id, Pass1, VersionOrigin.Manual, "Nach dem Korrektorat");

        Assert.False(named.Result.Created);
        Assert.Equal(2, named.Versions.Count);
        Assert.Equal("Nach dem Korrektorat", named.Versions[1].Label);
    }

    private async Task<JsonElement> PostSaveAsync(HttpClient client, long documentId, string text, string kind, int baseVersion, string? label = null)
    {
        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={documentId}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={documentId}&handler=Save")
        {
            Content = JsonContent.Create(new { text, kind, label, comment = (string?)null, activeSeconds = 0, keystrokes = 0, baseVersion, force = false }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    [DatabaseFact]
    public async Task The_editor_saves_by_hand_and_automatically_into_one_version_and_closes_it_on_request()
    {
        var id = await ImportAsync("Editor");
        await _app.EnsureAdminAsync();
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.test", "Admin-Passwort-2026");

        var first = await PostSaveAsync(client, id, Pass1, "manual", 1);
        Assert.True(first.GetProperty("created").GetBoolean());
        Assert.Equal(2, first.GetProperty("versionNo").GetInt32());
        Assert.Equal("working", first.GetProperty("state").GetString());

        var second = await PostSaveAsync(client, id, Pass1More, "manual", 2);
        var third = await PostSaveAsync(client, id, Pass1Final, "auto", 2);
        Assert.False(second.GetProperty("created").GetBoolean());
        Assert.False(third.GetProperty("created").GetBoolean());
        Assert.Equal(2, third.GetProperty("versionNo").GetInt32());

        var closed = await PostSaveAsync(client, id, Pass1Final, "close", 2, "Erster Durchgang");
        Assert.False(closed.GetProperty("created").GetBoolean());
        Assert.Equal("closed", closed.GetProperty("state").GetString());

        var next = await PostSaveAsync(client, id, Pass2, "manual", 2);
        Assert.True(next.GetProperty("created").GetBoolean());
        Assert.Equal(3, next.GetProperty("versionNo").GetInt32());
        Assert.Equal("working", next.GetProperty("state").GetString());

        var page = await AppFixture.TextAsync(client, $"/Documents/Editor?id={id}");
        Assert.Contains("Fassung 3 · in Arbeit", page);
        Assert.Contains("Durchgang abschließen", page);
    }
}
