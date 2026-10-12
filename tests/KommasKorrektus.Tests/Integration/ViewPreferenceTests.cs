using System.Net;
using System.Net.Http.Json;
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
    public async Task The_view_of_the_editor_follows_the_account_to_the_next_browser()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Ansicht");
        await _app.CreateUserAsync("ansicht-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        await _app.CreateUserAsync("ansicht-zwei@example.test", "Zwei-Passwort-2026", UserRole.User);

        using var first = await SignedInAsync("ansicht-eins@example.test", "Eins-Passwort-2026");
        Assert.Equal("text", ViewOfEditor(await first.GetStringAsync($"/Documents/Editor?id={document.Id}")));
        Assert.Equal(HttpStatusCode.OK, await PostViewAsync(first, document.Id, "page"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostViewAsync(first, document.Id, "nonsense"));

        // another browser of the same account, and another account
        using var elsewhere = await SignedInAsync("ansicht-eins@example.test", "Eins-Passwort-2026");
        Assert.Equal("page", ViewOfEditor(await elsewhere.GetStringAsync($"/Documents/Editor?id={document.Id}")));
        using var second = await SignedInAsync("ansicht-zwei@example.test", "Zwei-Passwort-2026");
        Assert.Equal("text", ViewOfEditor(await second.GetStringAsync($"/Documents/Editor?id={document.Id}")));

        // the page of a version opens the way the person left it, and an explicit choice is remembered
        var version = $"/Documents/Version?id={document.Id}&no=1";
        Assert.Contains("kk-sheet", await elsewhere.GetStringAsync(version));
        Assert.DoesNotContain("kk-sheet", await elsewhere.GetStringAsync(version + "&view=text"));
        Assert.Equal("text", ViewOfEditor(await elsewhere.GetStringAsync($"/Documents/Editor?id={document.Id}")));
    }

    [DatabaseFact]
    public async Task A_comparison_opens_the_way_the_account_looked_at_one_last()
    {
        await _app.EnsureAdminAsync();
        var (_, _, document) = await _app.CreateCustomerWithDocumentAsync("Vergleich");
        await _app.CreateUserAsync("vergleich-eins@example.test", "Eins-Passwort-2026", UserRole.User);
        using var client = await SignedInAsync("vergleich-eins@example.test", "Eins-Passwort-2026");
        var url = $"/Documents/Compare?id={document.Id}&from=1&to=2";

        Assert.Contains("kk-diff--side", await client.GetStringAsync(url));

        // the text of the test has no Word file behind it: "pages" still draws two pages, and the choice sticks without being named again
        var pages = await client.GetStringAsync(url + "&mode=pages");
        Assert.Contains("kk-pages", pages);
        Assert.Contains("kk-desk--old", pages);
        Assert.Contains("kk-desk--new", pages);
        Assert.Contains("kk-pages", await client.GetStringAsync(url));

        Assert.Contains("kk-diff--inline", await client.GetStringAsync(url + "&mode=inline"));
        Assert.Contains("kk-diff--inline", await client.GetStringAsync(url));

        var stored = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().User.AsNoTracking().Where(u => u.Email == "vergleich-eins@example.test").Select(u => u.CompareView).SingleAsync());
        Assert.Equal("inline", stored);
    }
}
