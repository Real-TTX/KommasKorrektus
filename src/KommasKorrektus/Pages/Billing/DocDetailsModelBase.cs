using KommasKorrektus.Core.Billing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Billing;

/// <summary>An offer, an invoice or a credit note, as it is: the numbers, the positions, the payments, what can be done with it next.</summary>
public abstract class DocDetailsModelBase(KkDbContext db, BillingService billing, ActivityLogger log) : PageModelBase
{
    protected abstract bool IsOfferPage { get; }

    public string Base => IsOfferPage ? "/Offers" : "/Invoices";

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public Invoice Invoice { get; private set; } = null!;

    public Customer Customer { get; private set; } = null!;

    public string? ProjectTitle { get; private set; }

    public Invoice? Source { get; private set; }

    public List<Invoice> Derived { get; private set; } = [];

    public List<Payment> Payments { get; private set; } = [];

    public IReadOnlyList<string> EInvoiceProblems { get; private set; } = [];

    public bool HasEInvoice { get; private set; }

    public bool Overdue => billing.IsOverdue(Invoice);

    public decimal Open => Invoice.Type == DocType.Invoice ? Invoice.GrossTotal - Invoice.PaidTotal : 0;

    public string Title => Labels.Of(Invoice.Type);

    public string StatusText => billing.StatusText(Invoice);

    public string StatusTone => billing.StatusTone(Invoice);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        // A draft belongs on the edit page: the details of something that has no number yet are not much.
        return Invoice.Status == DocStatus.Draft ? RedirectToPage($"{Base}/Edit", new { id = Id }) : Page();
    }

    public async Task<IActionResult> OnPostMarkSentAsync()
    {
        if (!await LoadAsync()) { return NotFound(); }
        await billing.MarkSentAsync(Invoice);
        Flash($"{Title} ist als versendet vermerkt.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostAcceptAsync()
    {
        if (!await LoadAsync()) { return NotFound(); }
        var invoice = await db.Invoice.FirstAsync(i => i.Id == Id);
        invoice.Status = DocStatus.Accepted;
        billing.AddHistoryNote(invoice, $"Angebot {invoice.Number} angenommen");
        await db.SaveChangesAsync();
        Flash("Das Angebot ist als angenommen vermerkt.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeclineAsync()
    {
        if (!await LoadAsync()) { return NotFound(); }
        var invoice = await db.Invoice.FirstAsync(i => i.Id == Id);
        invoice.Status = DocStatus.Declined;
        billing.AddHistoryNote(invoice, $"Angebot {invoice.Number} abgelehnt");
        await db.SaveChangesAsync();
        Flash("Das Angebot ist als abgelehnt vermerkt.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostCreateInvoiceAsync()
    {
        var offer = await db.Invoice.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == Id);
        if (offer is null) { return NotFound(); }
        try
        {
            var invoice = await billing.InvoiceFromOfferAsync(offer);
            Flash("Die Rechnung ist als Entwurf angelegt.");
            return RedirectToPage("/Invoices/Edit", new { id = invoice.Id });
        }
        catch (BillingException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage(new { id = Id });
        }
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        var invoice = await db.Invoice.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == Id);
        if (invoice is null) { return NotFound(); }
        try
        {
            var credit = await billing.CancelWithCreditNoteAsync(invoice);
            await log.LogAsync("Rechnung storniert", "Invoice", invoice.Id, $"Gutschrift {credit.Number}");
            Flash($"Die Rechnung ist storniert. Gutschrift {credit.Number} ist erstellt.");
            return RedirectToPage("/Invoices/Details", new { id = credit.Id });
        }
        catch (BillingException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage(new { id = Id });
        }
    }

    public async Task<IActionResult> OnPostDuplicateAsync()
    {
        var source = await db.Invoice.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == Id);
        if (source is null) { return NotFound(); }
        var copy = await billing.DuplicateAsync(source);
        Flash("Die Kopie ist als Entwurf angelegt.");
        return RedirectToPage($"{(copy.Type == DocType.Offer ? "/Offers" : "/Invoices")}/Edit", new { id = copy.Id });
    }

    public async Task<IActionResult> OnPostRemovePaymentAsync(long paymentId)
    {
        var invoice = await db.Invoice.FirstOrDefaultAsync(i => i.Id == Id);
        var payment = await db.Payment.FirstOrDefaultAsync(p => p.Id == paymentId && p.InvoiceId == Id);
        if (invoice is null || payment is null) { return NotFound(); }
        await billing.RemovePaymentAsync(invoice, payment);
        Flash("Die Zahlung ist entfernt.");
        return RedirectToPage(new { id = Id, tab = "payments" });
    }

    private async Task<bool> LoadAsync()
    {
        var invoice = await db.Invoice.AsNoTracking().Include(i => i.Lines).Include(i => i.Payments).Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == Id);
        if (invoice?.Customer is null || (invoice.Type == DocType.Offer) != IsOfferPage)
        {
            return false;
        }

        Invoice = invoice;
        Customer = invoice.Customer;
        ProjectTitle = invoice.ProjectId is { } pid ? await db.Project.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Title).FirstOrDefaultAsync() : null;
        Source = invoice.SourceDocumentId is { } sid ? await db.Invoice.AsNoTracking().FirstOrDefaultAsync(i => i.Id == sid) : null;
        Derived = await db.Invoice.AsNoTracking().Where(i => i.SourceDocumentId == Id).OrderBy(i => i.IssueDate).ToListAsync();
        Payments = [.. invoice.Payments.OrderByDescending(p => p.PaidDate)];
        var heading = $"{Title} {invoice.Number ?? "(Entwurf)"}";
        Head(heading, $"{Customer.DisplayName}{(ProjectTitle is null ? "" : " · " + ProjectTitle)}", ("Rechnungen", "/Invoices"), (IsOfferPage ? "Angebote" : "Rechnungen", Base), (invoice.Number ?? "Entwurf", null));
        ViewData["ActiveHref"] = Base;

        if (invoice.Status != DocStatus.Draft && invoice.Type != DocType.Offer)
        {
            var document = await billing.BuildIssuedAsync(invoice);
            EInvoiceProblems = EInvoice.Problems(document);
            HasEInvoice = EInvoiceProblems.Count == 0;
        }

        return true;
    }
}
