using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Invoices;

/// <summary>The payment of an invoice: the money that came in, in part or in full.</summary>
public sealed class PaymentModel(KkDbContext db, BillingService billing, ActivityLogger log, TimeProvider clock) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib den Betrag ein.")]
        [Display(Name = "Betrag")]
        public decimal? Amount { get; set; }

        [Required(ErrorMessage = "Bitte gib das Datum ein.")]
        [Display(Name = "Zahlungsdatum")]
        public DateOnly? Date { get; set; }

        [Display(Name = "Art")]
        public PaymentMethod Method { get; set; } = PaymentMethod.Transfer;

        [Display(Name = "Verwendungszweck")]
        public string? Reference { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long InvoiceId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Invoice Invoice { get; private set; } = null!;

    public decimal Open => Invoice.GrossTotal - Invoice.PaidTotal;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        Input = new InputModel { Amount = Open > 0 ? Open : null, Date = Fmt.Today(clock) };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var invoice = await db.Invoice.FirstAsync(i => i.Id == InvoiceId);
        try
        {
            await billing.AddPaymentAsync(invoice, Input.Amount!.Value, Input.Date!.Value, Input.Method, Input.Reference);
        }
        catch (BillingException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        await log.LogAsync("Zahlung eingetragen", "Invoice", invoice.Id, $"{invoice.Number}: {Fmt.Money(Input.Amount.Value)}");
        Flash(invoice.Status == DocStatus.Paid ? "Die Rechnung ist bezahlt." : "Die Zahlung ist eingetragen.");
        return RedirectToPage("/Invoices/Details", new { id = invoice.Id, tab = "payments" });
    }

    private async Task<bool> LoadAsync()
    {
        var invoice = await db.Invoice.AsNoTracking().Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == InvoiceId && i.Type == DocType.Invoice && i.Status != DocStatus.Draft && i.Status != DocStatus.Cancelled);
        if (invoice is null)
        {
            return false;
        }

        Invoice = invoice;
        Head("Zahlung eintragen", $"Rechnung {invoice.Number} · {invoice.Customer?.DisplayName}", ("Rechnungen", "/Invoices"), ("Rechnungen", "/Invoices"), (invoice.Number ?? "", $"/Invoices/Details?id={invoice.Id}&tab=payments"), ("Zahlung", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Invoices";
        return true;
    }
}
