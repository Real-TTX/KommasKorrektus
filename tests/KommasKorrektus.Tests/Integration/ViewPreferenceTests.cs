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
