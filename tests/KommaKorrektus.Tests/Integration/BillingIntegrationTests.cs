using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KommaKorrektus.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public sealed class BillingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public BillingIntegrationTests(AppFixture app) => _app = app;

    private async Task PrepareCompanyAsync()
    {
        await _app.EnsureAdminAsync();
        await _app.WithScopeAsync(sp =>
        {
            sp.GetRequiredService<ConfigStore>().Update(c =>
            {
                c.Company.Name = "Federkiel Lektorat";
                c.Company.Street = "Schreibweg 12";
                c.Company.PostalCode = "20095";
                c.Company.City = "Hamburg";
                c.Company.TaxNumber = "27/123/45678";
                c.Company.Iban = "DE89370400440532013000";
                c.Company.Email = "post@federkiel.example.com";
            });
            return Task.CompletedTask;
        });
    }

    private async Task<Customer> CustomerAsync(string name)
        => await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var customer = new Customer { Number = "K-" + Guid.NewGuid().ToString("N")[..6], Kind = CustomerKind.Person, FirstName = "Test", LastName = name, Street = "Rabenweg 3", PostalCode = "20095", City = "Hamburg", Email = "kunde@example.test" };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
            return customer;
        });

    private Task<Invoice> IssuedAsync(Customer customer, DocType type = DocType.Invoice, decimal quantity = 10, decimal unitPrice = 55)
        => _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var billing = sp.GetRequiredService<BillingService>();
            var invoice = await billing.NewDraftAsync(type, await db.Customer.FirstAsync(c => c.Id == customer.Id), null);
            invoice.Subject = "Lektorat";
            invoice.Lines.Add(new InvoiceLine { Position = 1, Title = "Lektorat", Quantity = quantity, Unit = "Stunde", UnitPrice = unitPrice, VatRate = 19 });
            BillingService.Recalculate(invoice);
            db.Invoice.Add(invoice);
            await db.SaveChangesAsync();
            await billing.IssueAsync(invoice);
            return invoice;
        });

    [DatabaseFact]
    public async Task Documents_are_issued_with_numbers_without_gaps_and_the_pdf_is_kept()
    {
        await PrepareCompanyAsync();
        var customer = await CustomerAsync("Rechnung");

        var first = await IssuedAsync(customer);
        var second = await IssuedAsync(customer);

        Assert.Equal(DocStatus.Issued, first.Status);
        Assert.Matches(@"^RE-\d{4}-\d{4}$", first.Number!);
        var n1 = int.Parse(first.Number![^4..]);
        var n2 = int.Parse(second.Number![^4..]);
        Assert.Equal(n1 + 1, n2);
        Assert.Equal(550m, first.NetTotal);
        Assert.Equal(104.5m, first.VatTotal);
        Assert.Equal(654.5m, first.GrossTotal);

        // the PDF that was issued is kept (and is the one with the e-invoice data inside)
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var stored = await db.StoredFile.AsNoTracking().FirstAsync(f => f.Id == first.PdfFileId);
            var bytes = await sp.GetRequiredService<FileStoreService>().ReadAllAsync(stored);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
            Assert.Contains("factur-x.xml", System.Text.Encoding.Latin1.GetString(bytes));
            Assert.NotNull((await db.Invoice.AsNoTracking().FirstAsync(i => i.Id == first.Id)).SnapshotJson);
        });
    }

    [DatabaseFact]
    public async Task A_document_that_is_missing_what_the_law_requires_is_not_issued_and_uses_no_number()
    {
        await PrepareCompanyAsync();
        var customer = await CustomerAsync("Unvollständig");
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            (await db.Customer.FirstAsync(c => c.Id == customer.Id)).City = null;
            await db.SaveChangesAsync();
        });

        var before = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().NumberSequence.AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<BillingException>(() => IssuedAsync(customer));
        var after = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().NumberSequence.AsNoTracking().ToListAsync());
        Assert.Equal(before.Sum(s => s.NextValue), after.Sum(s => s.NextValue));
    }

    [DatabaseFact]
    public async Task Payments_settle_an_invoice_and_cancelling_makes_a_credit_note()
    {
        await PrepareCompanyAsync();
        var customer = await CustomerAsync("Zahlung");
        var invoice = await IssuedAsync(customer, quantity: 2, unitPrice: 100);

        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var billing = sp.GetRequiredService<BillingService>();
            var tracked = await db.Invoice.Include(i => i.Lines).FirstAsync(i => i.Id == invoice.Id);

            await billing.AddPaymentAsync(tracked, 100m, new DateOnly(2026, 10, 9), PaymentMethod.Transfer, "Teil");
            Assert.Equal(DocStatus.PartiallyPaid, tracked.Status);
            await billing.AddPaymentAsync(tracked, 138m, new DateOnly(2026, 10, 10), PaymentMethod.Transfer, "Rest");
            Assert.Equal(DocStatus.Paid, tracked.Status);

            var credit = await billing.CancelWithCreditNoteAsync(tracked);
            Assert.Equal(DocType.CreditNote, credit.Type);
            Assert.Matches(@"^GS-\d{4}-\d{4}$", credit.Number!);
            Assert.Equal(DocStatus.Cancelled, tracked.Status);
            Assert.Equal(invoice.Id, credit.SourceDocumentId);
        });
    }

    [DatabaseFact]
    public async Task An_issued_offer_becomes_an_invoice_draft_with_the_same_positions()
    {
        await PrepareCompanyAsync();
        var customer = await CustomerAsync("Angebot");
        var offer = await IssuedAsync(customer, DocType.Offer, quantity: 3, unitPrice: 80);
        Assert.Matches(@"^AN-\d{4}-\d{4}$", offer.Number!);

        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var draft = await sp.GetRequiredService<BillingService>().InvoiceFromOfferAsync(await db.Invoice.Include(i => i.Lines).FirstAsync(i => i.Id == offer.Id));
            Assert.Equal(DocStatus.Draft, draft.Status);
            Assert.Equal(DocType.Invoice, draft.Type);
            Assert.Equal(3m, draft.Lines.Single().Quantity);
            Assert.Equal(80m, draft.Lines.Single().UnitPrice);
        });
    }
}
