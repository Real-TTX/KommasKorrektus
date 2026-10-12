using System.Net;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

/// <summary>A release to one account instead of a public link: the sign-in is the gate, everybody else is told "not found".</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ShareToAccountTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public ShareToAccountTests(AppFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = _app.NewClient();
        await AppFixture.LoginAsync(client, email, password);
        return client;
    }

    [DatabaseFact]
    public async Task A_release_to_an_account_asks_for_the_sign_in_and_is_closed_to_everybody_else()
    {
        await _app.EnsureAdminAsync();
        var (customer, project, document) = await _app.CreateCustomerWithDocumentAsync("Konto");
        var anna = await _app.CreateUserAsync("anna-konto@example.test", "Anna-Passwort-2026", UserRole.Client, customer.Id);
        await _app.CreateUserAsync("berta-konto@example.test", "Berta-Passwort-2026", UserRole.Client);
        var link = await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Für Anna", "Hallo Anna.", 7, "wird-ignoriert", true, true, true, false, null, null, anna.Id));
        Assert.Null(link.PasswordHash);

        // nobody signed in: the request to sign in, and nothing about the project, not even around the page
        using var anonymous = _app.NewClient();
        var gate = await anonymous.GetAsync($"/s/{link.Token}");
        var gateText = await AppFixture.TextAsync(gate);
        Assert.Equal(HttpStatusCode.OK, gate.StatusCode);
        Assert.Contains("für ein bestimmtes Konto", gateText);
        Assert.Contains($"/Account/Login?returnUrl=/s/{link.Token}", gateText.Replace("%2F", "/"));
        Assert.DoesNotContain("Für Anna", gateText);
        Assert.DoesNotContain("Text Konto", await AppFixture.TextAsync(anonymous, $"/s/{link.Token}/document/{document.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxClean")).StatusCode);

        // another account: "not found", as for a link that does not exist
        using var berta = await SignedInAsync("berta-konto@example.test", "Berta-Passwort-2026");
        var closed = await berta.GetAsync($"/s/{link.Token}");
        Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
        Assert.Contains("gibt es nicht", await AppFixture.TextAsync(closed));
        Assert.Equal(HttpStatusCode.NotFound, (await berta.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxClean")).StatusCode);

        // the account itself: the content, and the visit is written down with the account
        using var annaClient = await SignedInAsync("anna-konto@example.test", "Anna-Passwort-2026");
        var page = await AppFixture.TextAsync(annaClient, $"/s/{link.Token}/document/{document.Id}?mode=changes");
        Assert.Contains("<ins>hatte</ins>", page);
        Assert.Equal(HttpStatusCode.OK, (await annaClient.GetAsync($"/s/{link.Token}/download/{document.Id}?format=DocxClean")).StatusCode);
        var visits = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().ShareAccess.AsNoTracking().Where(a => a.ShareLinkId == link.Id).ToListAsync());
        Assert.Contains(visits, a => a.Action == ShareAction.View && a.UserId == anna.Id);
        Assert.Contains(visits, a => a.Action == ShareAction.Download && a.UserId == anna.Id);

        // the people of the house may look, and it is not counted as the visit of the account
        using var admin = await SignedInAsync("admin@example.test", "Admin-Passwort-2026");
        Assert.Contains("<ins>hatte</ins>", await AppFixture.TextAsync(admin, $"/s/{link.Token}/document/{document.Id}?mode=changes"));
        var after = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().ShareAccess.AsNoTracking().CountAsync(a => a.ShareLinkId == link.Id));
        Assert.Equal(visits.Count, after);
    }

    [DatabaseFact]
    public async Task Mit_mir_geteilt_lists_only_what_was_released_to_the_account()
    {
        await _app.EnsureAdminAsync();
        var (customer, project, _) = await _app.CreateCustomerWithDocumentAsync("Liste");
        var anna = await _app.CreateUserAsync("anna-liste@example.test", "Anna-Passwort-2026", UserRole.Client, customer.Id);
        await _app.CreateUserAsync("berta-liste@example.test", "Berta-Passwort-2026", UserRole.Client);
        await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Freigabe Liste Anna", null, 7, null, true, true, false, false, null, null, anna.Id));
        await _app.WithScopeAsync(sp => sp.GetRequiredService<ShareService>().CreateAsync(project, null, null, "Öffentlicher Link Liste", null, 7, null, true, true, false, false, null, null));

        using var annaClient = await SignedInAsync("anna-liste@example.test", "Anna-Passwort-2026");
        var annaList = await AppFixture.TextAsync(annaClient, "/Releases");
        Assert.Contains("Freigabe Liste Anna", annaList);
        Assert.DoesNotContain("Öffentlicher Link Liste", annaList);
        Assert.Contains("Mit mir geteilt", annaList);

        using var berta = await SignedInAsync("berta-liste@example.test", "Berta-Passwort-2026");
        Assert.DoesNotContain("Freigabe Liste Anna", await AppFixture.TextAsync(berta, "/Releases"));

        // a page that is not for a signed-in person at all goes to the sign-in
        using var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Releases")).StatusCode);
    }

    [DatabaseFact]
    public async Task The_editor_makes_a_release_for_an_account_of_the_customer_and_for_nobody_else()
    {
        await _app.EnsureAdminAsync();
        var (customer, project, _) = await _app.CreateCustomerWithDocumentAsync("Formular");
        var anna = await _app.CreateUserAsync("anna-form@example.test", "Anna-Passwort-2026", UserRole.Client, customer.Id);
        var berta = await _app.CreateUserAsync("berta-form@example.test", "Berta-Passwort-2026", UserRole.Client);

        using var admin = await SignedInAsync("admin@example.test", "Admin-Passwort-2026");
        var url = $"/Projects/Share?projectId={project.Id}";
        var page = await AppFixture.TextAsync(admin, url);
        Assert.Contains("Für wen?", page);

        Dictionary<string, string> Form(long userId) => new() { ["ProjectId"] = project.Id.ToString(), ["Input.Title"] = "Für Konto " + userId, ["Input.Days"] = "7", ["Input.UserId"] = userId.ToString(), ["Input.ShowChanges"] = "true", ["Input.AllowDownload"] = "true" };

        var refused = await AppFixture.PostFormAsync(admin, url, Form(berta.Id));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("kann keine Freigabe bekommen", await AppFixture.TextAsync(refused));

        var created = await AppFixture.PostFormAsync(admin, url, Form(anna.Id));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var link = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().ShareLink.AsNoTracking().SingleAsync(l => l.ProjectId == project.Id));
        Assert.Equal(anna.Id, link.UserId);
        Assert.Equal(anna.DisplayName, link.RecipientName);
        Assert.Equal("anna-form@example.test", link.RecipientEmail);
        Assert.Null(link.PasswordHash);
    }
}
