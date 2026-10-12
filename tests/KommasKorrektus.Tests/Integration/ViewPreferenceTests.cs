using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
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

        async Task<JsonElement> ReferenceAsync(string? from)
            => (await client.GetFromJsonAsync<JsonElement>($"/Documents/Editor?id={document.Id}&handler=Reference" + (from is null ? "" : "&from=" + from))).Clone();

        // without a choice: the version before (the document has two versions)
        var previous = await ReferenceAsync(null);
        Assert.Equal(1, previous.GetProperty("from").GetInt32());
        Assert.Equal(2, previous.GetProperty("to").GetInt32());
        Assert.Equal("previous", previous.GetProperty("selected").GetString());
        Assert.Contains("kk-lp", previous.GetProperty("html").GetString());
        // the page of that version as it was, nothing marked: what differs from the text is found in the browser while it is written
        Assert.Contains("data-n=\"1\"", previous.GetProperty("html").GetString());
        Assert.DoesNotContain("<ins", previous.GetProperty("html").GetString());
        Assert.DoesNotContain("<del", previous.GetProperty("html").GetString());
        Assert.True(previous.GetProperty("paragraphs").GetInt32() > 0);
        Assert.Equal("previous", previous.GetProperty("rules")[0].GetProperty("value").GetString());
        Assert.Equal("1", previous.GetProperty("versions")[0].GetProperty("value").GetString());

        // the rules, a number, and nonsense (which is the version before)
        Assert.Equal("original", (await ReferenceAsync("original")).GetProperty("selected").GetString());
        Assert.Equal(1, (await ReferenceAsync("1")).GetProperty("from").GetInt32());
        // with one round only, its beginning is the original
        Assert.Equal("original", (await ReferenceAsync("round")).GetProperty("selected").GetString());
        Assert.Equal(1, (await ReferenceAsync("9999")).GetProperty("from").GetInt32());
        Assert.Equal("previous", (await ReferenceAsync("'; drop table")).GetProperty("selected").GetString());

        // the choice is kept with the account
        var token = await AppFixture.TokenAsync(client, $"/Documents/Editor?id={document.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Documents/Editor?id={document.Id}&handler=View") { Content = JsonContent.Create(new { referenceFrom = "round", view = (string?)null, reference = (bool?)null, side = (bool?)null }) };
        request.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        Assert.Contains("\"referenceFrom\":\"round\"", await client.GetStringAsync($"/Documents/Editor?id={document.Id}"));
    }
}
