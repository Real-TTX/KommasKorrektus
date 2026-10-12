using KommasKorrektus.Core.Billing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Billing;

/// <summary>The file of an issued document: the PDF as it was issued (kept, never redrawn), the XML of the e-invoice, or the XRechnung. There is no page, the address is the download.</summary>
public abstract class DocDownloadModelBase(KkDbContext db, BillingService billing, FileStoreService files, TimeProvider clock) : PageModelBase
{
    protected abstract string Base { get; }

    public async Task<IActionResult> OnGetAsync(long id, string? kind, bool inline = false)
    {
        var invoice = await db.Invoice.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null)
        {
            return NotFound();
        }

        try
        {
            if (invoice.Status == DocStatus.Draft)
            {
                var draft = await billing.BuildDraftAsync(invoice, HttpContext.RequestAborted);
                return Send(InvoicePdf.Render(draft), "application/pdf", $"Entwurf {Labels(invoice.Type)}.pdf", inline);
            }

            if (kind is "xml" or "xrechnung")
            {
                var document = await billing.BuildIssuedAsync(invoice, HttpContext.RequestAborted);
                var profile = kind == "xrechnung" ? EInvoiceProfile.XRechnung : EInvoiceProfile.Zugferd;
                if (invoice.Type == DocType.Offer)
                {
                    FlashError("Für ein Angebot gibt es keine E-Rechnung.");
                    return RedirectToPage($"{Base}/Details", new { id });
                }

                if (EInvoice.Problems(document, profile) is { Count: > 0 } problems)
                {
                    FlashError("Die E-Rechnung ist nicht möglich: " + string.Join(" ", problems));
                    return RedirectToPage($"{Base}/Details", new { id });
                }

                var xml = System.Text.Encoding.UTF8.GetBytes(EInvoice.Create(document, profile));
                return Send(xml, "application/xml", $"{invoice.Number}{(profile == EInvoiceProfile.XRechnung ? "-xrechnung" : "")}.xml", false);
            }

            var stored = invoice.PdfFileId is { } fileId ? await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId) : null;
            if (stored is not null)
            {
                return Send(await files.ReadAllAsync(stored, HttpContext.RequestAborted), "application/pdf", $"{Labels(invoice.Type)} {invoice.Number}.pdf", inline);
            }

            // Should not happen (issuing keeps the PDF); if the file is gone it is drawn again from the snapshot.
            return Send(InvoicePdf.Render(await billing.BuildIssuedAsync(invoice, HttpContext.RequestAborted)), "application/pdf", $"{Labels(invoice.Type)} {invoice.Number}.pdf", inline);
        }
        catch (StorageException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage($"{Base}/Details", new { id });
        }
    }

    private static string Labels(DocType type) => UiKit.Labels.Of(type);

    private IActionResult Send(byte[] bytes, string contentType, string fileName, bool inline)
    {
        _ = clock;
        var safe = FileStoreService.SafeFileName(fileName);
        Response.Headers.ContentDisposition = $"{(inline ? "inline" : "attachment")}; filename=\"{safe}\"; filename*=UTF-8''{Uri.EscapeDataString(safe)}";
        return new FileContentResult(bytes, contentType);
    }
}
