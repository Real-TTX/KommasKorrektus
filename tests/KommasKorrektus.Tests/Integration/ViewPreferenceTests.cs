using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>The way a person looks at a text (text or page, and the mode of a comparison) is kept with the account, not with the browser.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ViewPreferenceTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public ViewPreferenceTests(AppFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = _app.NewClient();
        await AppFixture.LoginAsync(client, email, password);
        return client;
    }

    private static async Task<HttpStatusCode> PostViewAsync(HttpClient client, long documentId, string? view)
    {
        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={documentId}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={documentId}&handler=View") { Content = JsonContent.Create(new { view }) };
        request.Headers.Add("RequestVerificationToken", token);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static string ViewOfEditor(string html) => Regex.Match(html, "\"view\":\"(\\w+)\"").Groups[1].Value;

    [DatabaseFact]
    public async Task The_page_is_the_standard_and_the_view_of_the_editor_follows_the_account_to_the_next_browser()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Ansicht");
        await _app.CreateUserAsync("ansicht-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        await _app.CreateUserAsync("ansicht-zwei@example.test", "Zwei-Passwort-2026", UserRole.User);

        using var first = await SignedInAsync("ansicht-eins@example.test", "Eins-Passwort-2026");
        Assert.Equal("page", ViewOfEditor(await first.GetStringAsync($"/Documents/Editor?id={document.Id}")));
        Assert.Equal(HttpStatusCode.OK, await PostViewAsync(first, document.Id, "text"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostViewAsync(first, document.Id, "nonsense"));

        // another browser of the same account, and another account
        using var elsewhere = await SignedInAsync("ansicht-eins@example.test", "Eins-Passwort-2026");
        Assert.Equal("text", ViewOfEditor(await elsewhere.GetStringAsync($"/Documents/Editor?id={document.Id}")));
        using var second = await SignedInAsync("ansicht-zwei@example.test", "Zwei-Passwort-2026");
        Assert.Equal("page", ViewOfEditor(await second.GetStringAsync($"/Documents/Editor?id={document.Id}")));

        // the page of a version opens the way the person left it (the text here), and an explicit choice is remembered
        var version = $"/Documents/Version?id={document.Id}&no=1";
        Assert.DoesNotContain("kk-sheet", await elsewhere.GetStringAsync(version));
        Assert.Contains("kk-sheet", await elsewhere.GetStringAsync(version + "&view=page"));
        Assert.Equal("page", ViewOfEditor(await elsewhere.GetStringAsync($"/Documents/Editor?id={document.Id}")));
        // somebody who did not choose gets the page
        Assert.Contains("kk-sheet", await second.GetStringAsync(version));
    }

    [DatabaseFact]
    public async Task What_the_editor_has_open_is_kept_with_the_account_too()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Offen");
        await _app.CreateUserAsync("offen-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("offen-eins@example.test", "Eins-Passwort-2026");

        string Options(string html) => Regex.Match(html, "\"options\":\\{([^}]*)\\}").Groups[1].Value;
        Assert.Contains("\"reference\":false", Options(await client.GetStringAsync($"/Documents/Editor?id={document.Id}")));

        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={document.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={document.Id}&handler=View") { Content = JsonContent.Create(new { reference = true, side = (bool?)null, view = (string?)null }) };
        request.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var options = Options(await client.GetStringAsync($"/Documents/Editor?id={document.Id}"));
        Assert.Contains("\"reference\":true", options);
        Assert.Contains("\"side\":false", options);
        Assert.Contains("\"showResolved\":false", options);

        // whether the comments that are ticked off are shown is kept too, and the rest stays as it was
        using var shown = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={document.Id}&handler=View") { Content = JsonContent.Create(new { showResolved = true }) };
        shown.Headers.Add("RequestVerificationToken", await AppFixture.TokenAsync(client, $"/Documents/Editor?id={document.Id}"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(shown)).StatusCode);
        options = Options(await client.GetStringAsync($"/Documents/Editor?id={document.Id}"));
        Assert.Contains("\"showResolved\":true", options);
        Assert.Contains("\"reference\":true", options);
    }

    [DatabaseFact]
    public async Task The_size_of_the_writing_is_kept_with_the_account_and_the_comparison_uses_it()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Groesse");
        await _app.CreateUserAsync("groesse-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("groesse-eins@example.test", "Eins-Passwort-2026");
        var editor = $"/Documents/Editor?id={document.Id}";
        var compare = $"/Documents/Compare?id={document.Id}&from=1&to=2&mode=pages";

        string ZoomOf(string html) => Regex.Match(html, "\"zoom\":(\\d+)").Groups[1].Value;
        async Task<HttpStatusCode> PostAsync(string page, string handler, object body)
        {
            var token = await AppFixture.TokenAsync(client, page);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{page}{(page.Contains('?') ? "&" : "?")}handler={handler}") { Content = JsonContent.Create(body) };
            request.Headers.Add("RequestVerificationToken", token);
            return (await client.SendAsync(request)).StatusCode;
        }

        // the size the page and the text were made for, until somebody chooses another
        Assert.Equal("100", ZoomOf(await client.GetStringAsync(editor)));
        Assert.Contains("data-scale=\"1\"", await client.GetStringAsync(compare));

        Assert.Equal(HttpStatusCode.OK, await PostAsync(editor, "View", new { zoom = 125 }));
        Assert.Equal("125", ZoomOf(await client.GetStringAsync(editor)));
        var pages = await client.GetStringAsync(compare);
        Assert.Contains("data-scale=\"1.25\"", pages);
        Assert.Contains("data-zoom=\"125\"", pages);

        // the comparison can change it too, and it is the same choice; what is not one of the sizes is refused
        Assert.Equal(HttpStatusCode.OK, await PostAsync(compare, "Zoom", new { zoom = 90 }));
        Assert.Equal("90", ZoomOf(await client.GetStringAsync(editor)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(compare, "Zoom", new { zoom = 130 }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(editor, "View", new { zoom = 5000 }));
        Assert.Equal("90", ZoomOf(await client.GetStringAsync(editor)));

        // the other things the editor keeps stay as they were
        Assert.Equal(HttpStatusCode.OK, await PostAsync(editor, "View", new { reference = true }));
        Assert.Equal("90", ZoomOf(await client.GetStringAsync(editor)));
    }

    [DatabaseFact]
    public async Task A_comparison_opens_the_way_the_account_looked_at_one_last()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Vergleich");
        await _app.CreateUserAsync("vergleich-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("vergleich-eins@example.test", "Eins-Passwort-2026");
        var url = $"/Documents/Compare?id={document.Id}&from=1&to=2";

        // two pages next to each other are the standard (a text without a Word file still has a page)
        var pages = await client.GetStringAsync(url);
        Assert.Contains("kk-pages", pages);
        Assert.Contains("kk-desk--old", pages);
        Assert.Contains("kk-desk--new", pages);

        // the pages are the pages as they were (nothing marked, nothing folded away): what differs is found in the browser, the same way as in the editor
        Assert.Contains("data-n=\"1\"", pages);
        Assert.DoesNotContain("<ins", pages);
        Assert.DoesNotContain("<del", pages);
        Assert.DoesNotContain("kk-diff__gap", pages);
        Assert.Contains("compare-pages.js", pages);

        // a choice sticks without being named again
        Assert.Contains("kk-diff--side", await client.GetStringAsync(url + "&mode=side"));
        Assert.Contains("kk-diff--side", await client.GetStringAsync(url));
        Assert.Contains("kk-diff--inline", await client.GetStringAsync(url + "&mode=inline"));
        Assert.Contains("kk-diff--inline", await client.GetStringAsync(url));

        var stored = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().User.AsNoTracking().Where(u => u.Email == "vergleich-eins@example.test").Select(u => u.CompareView).SingleAsync());
        Assert.Equal("inline", stored);
    }

    [DatabaseFact]
    public async Task The_earlier_version_beside_the_text_is_chosen_by_a_rule_that_is_kept()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Regel");
        await _app.CreateUserAsync("regel-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("regel-eins@example.test", "Eins-Passwort-2026");

        async Task<JsonElement> ReferenceAsync(string? from, bool withPage = true)
            => (await client.GetFromJsonAsync<JsonElement>($"/Documents/Editor?id={document.Id}&handler=Reference" + (from is null ? "" : "&from=" + from) + (withPage ? "" : "&withPage=false"))).Clone();

        // without a choice: the document has two versions and the second is closed, so the next change begins a new pass, which is changed against this version
        var previous = await ReferenceAsync(null);
        Assert.Equal(2, previous.GetProperty("from").GetInt32());
        Assert.Equal(2, previous.GetProperty("to").GetInt32());
        Assert.Equal("previous", previous.GetProperty("selected").GetString());
        Assert.Contains("kk-lp", previous.GetProperty("html").GetString());
        // the page of that version as it was, nothing marked: what differs from the text is found in the browser while it is written
        Assert.Contains("data-n=\"1\"", previous.GetProperty("html").GetString());
        Assert.DoesNotContain("<ins", previous.GetProperty("html").GetString());
        Assert.DoesNotContain("<del", previous.GetProperty("html").GetString());
        Assert.True(previous.GetProperty("texts").GetArrayLength() > 0);
        Assert.True(previous.GetProperty("self").GetBoolean());
        Assert.Equal("previous", previous.GetProperty("rules")[0].GetProperty("value").GetString());
        Assert.StartsWith("Der gespeicherte Text (Fassung 2", previous.GetProperty("rules")[0].GetProperty("text").GetString());
        // the versions to choose: the closed current one (its saved text) and the ones before it
        Assert.Equal(["2", "1"], previous.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("value").GetString()!).ToArray());

        // the text of that version is enough to mark the changes in the text of the editor: without the page there is no markup, and the same text
        var textOnly = await ReferenceAsync(null, withPage: false);
        Assert.False(textOnly.TryGetProperty("html", out _));
        Assert.Equal(previous.GetProperty("texts").GetRawText(), textOnly.GetProperty("texts").GetRawText());
        Assert.Equal(2, textOnly.GetProperty("from").GetInt32());

        // the rules, a number, and nonsense (which is the version before)
        Assert.Equal("original", (await ReferenceAsync("original")).GetProperty("selected").GetString());
        Assert.Equal(1, (await ReferenceAsync("1")).GetProperty("from").GetInt32());
        // the number of the version that was "the version before" stays that version when the work goes on (the editor asks for it by number)
        Assert.Equal(2, (await ReferenceAsync("2")).GetProperty("from").GetInt32());
        // with one round only, its beginning is the original
        Assert.Equal("original", (await ReferenceAsync("round")).GetProperty("selected").GetString());
        Assert.Equal(2, (await ReferenceAsync("9999")).GetProperty("from").GetInt32());
        Assert.Equal("previous", (await ReferenceAsync("'; drop table")).GetProperty("selected").GetString());

        // the choice is kept with the account
        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={document.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={document.Id}&handler=View") { Content = JsonContent.Create(new { referenceFrom = "round", view = (string?)null, reference = (bool?)null, side = (bool?)null }) };
        request.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        Assert.Contains("\"referenceFrom\":\"round\"", await client.GetStringAsync($"/Documents/Editor?id={document.Id}"));
    }

    [DatabaseFact]
    public async Task The_version_before_is_the_one_below_the_pass_that_is_made_and_does_not_move_when_the_pass_is_saved_or_closed()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Durchgang");
        await _app.CreateUserAsync("durchgang-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("durchgang-eins@example.test", "Eins-Passwort-2026");

        async Task<JsonElement> ReferenceAsync(string? from = null)
            => (await client.GetFromJsonAsync<JsonElement>($"/Documents/Editor?id={document.Id}&handler=Reference&withPage=false" + (from is null ? "" : "&from=" + from))).Clone();

        // version 2 is closed: a pass that begins now is changed against it
        var closed = await ReferenceAsync();
        Assert.Equal(2, closed.GetProperty("from").GetInt32());
        Assert.True(closed.GetProperty("self").GetBoolean());

        // the first save of the new pass makes version 3 (in work); "the version before" is version 2 still: nothing moves under the hands of the person who works
        await _app.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<DocumentService>();
            var db = sp.GetRequiredService<KkDbContext>();
            var tracked = await db.Document.FirstAsync(d => d.Id == document.Id);
            await service.SaveAsync(tracked, "Es war einmal eine Hexe.\nSie hatte einen Besen und flog los.\nDer Rabe folgte ihr, laut krächzend.", VersionOrigin.Working, null, null);
        });
        var working = await ReferenceAsync();
        Assert.Equal(2, working.GetProperty("from").GetInt32());
        Assert.Equal(3, working.GetProperty("to").GetInt32());
        Assert.False(working.GetProperty("self").GetBoolean());
        Assert.StartsWith("Die Fassung davor (Fassung 2", working.GetProperty("rules")[0].GetProperty("text").GetString());

        // a customer's file is looked through against the version before it: what did the customer change
        await _app.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<DocumentService>();
            var db = sp.GetRequiredService<KkDbContext>();
            var tracked = await db.Document.FirstAsync(d => d.Id == document.Id);
            await service.AddFileVersionAsync(tracked, "vom-kunden.txt", System.Text.Encoding.UTF8.GetBytes("Es war einmal eine Hexe.\n\nSie hatte einen Besen.\n\nDer Rabe folgte ihr."), null, null);
        });
        var customer = await ReferenceAsync();
        Assert.Equal(4, customer.GetProperty("to").GetInt32());
        Assert.Equal(3, customer.GetProperty("from").GetInt32());
        Assert.StartsWith("Was der Kunde geändert hat", customer.GetProperty("rules")[0].GetProperty("text").GetString());
    }

    [DatabaseFact]
    public async Task A_document_that_has_only_its_original_is_compared_with_that_text_so_that_what_is_typed_before_the_first_save_is_marked()
    {
        await _app.EnsureAdminAsync();
        var documentId = await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var customer = new Customer { Number = "K-" + Guid.NewGuid().ToString("N")[..6], Kind = CustomerKind.Person, FirstName = "Test", LastName = "Original", Email = "original@example.test" };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
            var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..6], CustomerId = customer.Id, Title = "Projekt Original" };
            db.Project.Add(project);
            await db.SaveChangesAsync();
            var import = await sp.GetRequiredService<DocumentService>().ImportAsync(project, "Nur das Original", "text.txt", System.Text.Encoding.UTF8.GetBytes("Erster Absatz.\nZweiter Absatz."));
            await db.SaveChangesAsync();
            return import.Document.Id;
        });
        await _app.CreateUserAsync("original-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("original-eins@example.test", "Eins-Passwort-2026");

        var reference = (await client.GetFromJsonAsync<JsonElement>($"/Documents/Editor?id={documentId}&handler=Reference&withPage=false")).Clone();
        Assert.True(reference.GetProperty("self").GetBoolean());
        Assert.Equal(1, reference.GetProperty("from").GetInt32());
        Assert.Equal(1, reference.GetProperty("to").GetInt32());
        Assert.Equal("previous", reference.GetProperty("selected").GetString());
        Assert.Equal(new[] { "Erster Absatz.", "Zweiter Absatz." }, reference.GetProperty("texts").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Contains("gespeicherte", reference.GetProperty("rules")[0].GetProperty("text").GetString());
    }

    [DatabaseFact]
    public async Task How_the_changes_are_marked_in_the_text_is_kept_with_the_account_and_nothing_else_is_taken()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Markup");
        await _app.CreateUserAsync("markup-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("markup-eins@example.test", "Eins-Passwort-2026");
        var editor = $"/Documents/Editor?id={document.Id}";

        async Task<HttpStatusCode> PostAsync(object body)
        {
            var token = await AppFixture.TokenAsync(client, editor);
            using var request = new HttpRequestMessage(HttpMethod.Post, editor + "&handler=View") { Content = JsonContent.Create(body) };
            request.Headers.Add("RequestVerificationToken", token);
            return (await client.SendAsync(request)).StatusCode;
        }

        // all of it is the standard; the editor also says how long a standard page is (it shows the pages as it counts) and which kind each entry of the style sheet is (for the check)
        var opened = await client.GetStringAsync(editor);
        Assert.Contains("\"markup\":\"all\"", opened);
        Assert.Contains("\"normChars\":1800", opened);
        Assert.Contains("editor-norm", opened);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(new { markup = "simple" }));
        Assert.Contains("\"markup\":\"simple\"", await client.GetStringAsync(editor));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(new { markup = "off" }));
        Assert.Contains("\"markup\":\"off\"", await client.GetStringAsync(editor));

        // what is not one of the ways is no change, and the other things the editor keeps stay as they were
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(new { markup = "everything" }));
        Assert.Contains("\"markup\":\"off\"", await client.GetStringAsync(editor));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(new { zoom = 110 }));
        var page = await client.GetStringAsync(editor);
        Assert.Contains("\"markup\":\"off\"", page);
        Assert.Contains("\"zoom\":110", page);
    }
}
