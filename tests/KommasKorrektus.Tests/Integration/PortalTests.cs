using System.Net;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommasKorrektus.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public sealed class PortalTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PortalTests(AppFixture app) => _app = app;

    private async Task<Invoice> IssuedInvoiceAsync(long customerId, string number)
        => await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var invoice = new Invoice { Type = DocType.Invoice, Number = number, CustomerId = customerId, Status = DocStatus.Issued, IssueDate = DateOnly.FromDateTime(DateTime.Today), DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)), GrossTotal = 119m, NetTotal = 100m, VatTotal = 19m };
            db.Invoice.Add(invoice);
            await db.SaveChangesAsync();
            return invoice;
        });

    [DatabaseFact]
    public async Task A_client_sees_the_projects_documents_and_invoices_of_its_customer_and_nothing_else()
    {
        await _app.EnsureAdminAsync();
        var (customerA, projectA, documentA) = await _app.CreateCustomerWithDocumentAsync("Anna");
        var (customerB, projectB, documentB) = await _app.CreateCustomerWithDocumentAsync("Berta");
        var invoiceA = await IssuedInvoiceAsync(customerA.Id, "RE-A-1");
        var invoiceB = await IssuedInvoiceAsync(customerB.Id, "RE-B-1");
        await _app.CreateUserAsync("anna@example.test", "Anna-Passwort-2026", UserRole.Client, customerA.Id);

        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "anna@example.test", "Anna-Passwort-2026");

        var home = await AppFixture.TextAsync(client, "/Portal");
        Assert.Contains(projectA.Title, home);
        Assert.DoesNotContain(projectB.Title, home);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Portal/Project?id={projectA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Project?id={projectB.Id}")).StatusCode);

        var documentPage = await AppFixture.TextAsync(client, $"/Portal/Document?id={documentA.Id}");
        Assert.Contains("<ins>hatte</ins>", documentPage);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Document?id={documentB.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Portal/Download?id={documentA.Id}&format=DocxTracked")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Download?id={documentB.Id}&format=DocxTracked")).StatusCode);

        var invoices = await AppFixture.TextAsync(client, "/Portal/Invoices");
        Assert.Contains("RE-A-1", invoices);
        Assert.DoesNotContain("RE-B-1", invoices);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/InvoiceFile?id={invoiceB.Id}")).StatusCode);
        _ = invoiceA;
    }

    [DatabaseFact]
    public async Task A_client_cannot_reach_the_pages_of_the_editor_or_the_administration()
    {
        await _app.EnsureAdminAsync();
        var (customer, _, document) = await _app.CreateCustomerWithDocumentAsync("Carla");
        await _app.CreateUserAsync("carla@example.test", "Carla-Passwort-2026", UserRole.Client, customer.Id);
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "carla@example.test", "Carla-Passwort-2026");

        foreach (var path in new[] { "/Customers", "/Projects", "/Admin/Users", "/Admin/Backup", "/Invoices", $"/Documents/Details?id={document.Id}", $"/Documents/Editor?id={document.Id}", $"/Documents/Export?id={document.Id}&format=DocxClean" })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{path} gave {(int)response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.Redirect)
            {
                Assert.StartsWith("/Portal", AppFixture.LocationPath(response));
            }
        }
    }

    [DatabaseFact]
    public async Task A_remark_of_a_client_is_kept_with_its_author_and_seen_only_by_that_client()
    {
        await _app.EnsureAdminAsync();
        var (customer, _, document) = await _app.CreateCustomerWithDocumentAsync("Dora");
        var user = await _app.CreateUserAsync("dora@example.test", "Dora-Passwort-2026", UserRole.Client, customer.Id);
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "dora@example.test", "Dora-Passwort-2026");

        var url = $"/Portal/Document?id={document.Id}";
        var response = await AppFixture.PostFormAsync(client, url, new Dictionary<string, string> { ["Input.Paragraph"] = "1", ["Input.Text"] = "Gefällt mir." }, url + "&handler=Comment");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Gefällt mir.", await AppFixture.TextAsync(client, url));

        var saved = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().DocumentComment.AsNoTracking().SingleAsync(c => c.DocumentId == document.Id));
        Assert.Equal(user.Id, saved.AuthorUserId);
        Assert.Null(saved.ShareLinkId);
    }

    [DatabaseFact]
    public async Task An_invitation_can_be_accepted_once_and_gives_access_to_the_customer()
    {
        await _app.EnsureAdminAsync();
        var (customer, _, _) = await _app.CreateCustomerWithDocumentAsync("Emma");
        var invitation = await _app.WithScopeAsync(sp => sp.GetRequiredService<Services.Auth.InvitationService>().CreateAsync("emma@example.test", "Emma", UserRole.Client, customer.Id, null));

        using var client = _app.NewClient();
        var page = await AppFixture.TextAsync(client, $"/Account/Register?token={invitation.Token}");
        Assert.Contains("emma@example.test", page);

        var register = await AppFixture.PostFormAsync(client, $"/Account/Register?token={invitation.Token}", new Dictionary<string, string>
        {
            ["Token"] = invitation.Token.ToString(), ["Input.DisplayName"] = "Emma Test", ["Input.Method"] = "password", ["Input.Password"] = "Emma-Passwort-2026", ["Input.PasswordRepeat"] = "Emma-Passwort-2026",
        });
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Portal")).StatusCode);

        // the link works once
        using var second = _app.NewClient();
        Assert.Contains("schon benutzt", await AppFixture.TextAsync(second, $"/Account/Register?token={invitation.Token}"));
    }
}
