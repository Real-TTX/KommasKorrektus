using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Mail;
using KommaKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Billing;

/// <summary>Sends an issued document by mail with the PDF attached (the e-invoice is inside the PDF). Without a mail server the page says so and offers to note it as sent.</summary>
public abstract class DocSendModelBase(KkDbContext db, BillingService billing, FileStoreService files, MailService mail, ConfigStore config, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib die E-Mail-Adresse ein.")]
        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "An")]
        public string To { get; set; } = "";

        [Required(ErrorMessage = "Bitte gib einen Betreff ein.")]
        [Display(Name = "Betreff")]
        public string Subject { get; set; } = "";

        [Required(ErrorMessage = "Bitte schreibe den Text.")]
        [Display(Name = "Nachricht")]
        public string Text { get; set; } = "";
    }

    protected abstract string Base { get; }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Reminder { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Invoice Invoice { get; private set; } = null!;

    public Customer Customer { get; private set; } = null!;

    public bool MailConfigured => mail.IsConfigured;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var title = UiKit.Labels.Of(Invoice.Type);
        var kind = Reminder ? TextBlockKind.Reminder : TextBlockKind.Mail;
        var block = await db.TextBlock.AsNoTracking().Where(t => t.Kind == kind && (t.DocType == Invoice.Type || t.DocType == null)).OrderByDescending(t => t.IsDefault).ThenByDescending(t => t.DocType == Invoice.Type).FirstOrDefaultAsync();
        var values = billing.PlaceholderValues(Invoice, Customer, Invoice.ProjectId is { } pid ? await db.Project.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Title).FirstOrDefaultAsync() : null, Invoice.Number);
        Input = new InputModel
        {
            To = Customer.Email ?? "",
            Subject = Reminder ? $"Zahlungserinnerung zu Rechnung {Invoice.Number}" : $"{title} {Invoice.Number}",
            Text = TextPlaceholders.Resolve(block?.Text ?? $"{{{{Anrede}}}}\n\nanbei erhalten Sie {title} {{{{Nummer}}}}.\n\nHerzliche Grüße", values) + "\n\n" + Signature(),
        };
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

        var stored = Invoice.PdfFileId is { } fileId ? await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId) : null;
        if (stored is null)
        {
            ModelState.AddModelError(string.Empty, "Das PDF des Dokuments fehlt.");
            return Page();
        }

        MailResult result;
        try
        {
            var pdf = await files.ReadAllAsync(stored, HttpContext.RequestAborted);
            result = await mail.SendAsync(Input.To, Input.Subject, Input.Text, MailKind.Document, [new MailAttachment($"{UiKit.Labels.Of(Invoice.Type)} {Invoice.Number}.pdf", "application/pdf", pdf)], ct: HttpContext.RequestAborted);
        }
        catch (StorageException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var tracked = await db.Invoice.FirstAsync(i => i.Id == Id);
        if (result.Delivered)
        {
            await billing.MarkSentAsync(tracked);
            await log.LogAsync("Dokument gesendet", "Invoice", tracked.Id, $"{tracked.Number} an {Input.To}");
            Flash($"Die E-Mail ist an {Input.To} unterwegs.");
            return RedirectToPage($"{Base}/Details", new { id = Id });
        }

        ModelState.AddModelError(string.Empty, result.Status == MailStatus.NotConfigured ? "Es ist noch kein E-Mail-Server eingerichtet (Verwaltung > E-Mail)." : "Die E-Mail konnte nicht gesendet werden: " + result.Error);
        return Page();
    }

    private string Signature()
    {
        var c = config.Current.Company;
        return string.Join('\n', new[] { c.Owner, c.Name, c.Phone, c.Website }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private async Task<bool> LoadAsync()
    {
        var invoice = await db.Invoice.AsNoTracking().Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == Id && i.Status != DocStatus.Draft);
        if (invoice?.Customer is null)
        {
            return false;
        }

        Invoice = invoice;
        Customer = invoice.Customer;
        var title = Reminder ? "Zahlungserinnerung senden" : "Per E-Mail senden";
        Head(title, $"{UiKit.Labels.Of(invoice.Type)} {invoice.Number} · {Customer.DisplayName}", ("Rechnungen", "/Invoices"), (invoice.Type == DocType.Offer ? "Angebote" : "Rechnungen", Base), (invoice.Number ?? "", $"{Base}/Details?id={Id}"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = Base;
        return true;
    }
}
