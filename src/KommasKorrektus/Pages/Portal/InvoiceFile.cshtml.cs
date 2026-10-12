using KommasKorrektus.Core.Billing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Portal;

/// <summary>The PDF of an issued invoice or offer, as it was issued (an invoice PDF carries the e-invoice data). Only for a client who has access to the customer.</summary>
public sealed class InvoiceFileModel(KkDbContext db, ICurrentUser current, BillingService billing, FileStoreService files) : PortalModelBase(db, current)
{
    public async Task<IActionResult> OnGetAsync(long id, bool inline = false)
    {
        var invoice = await MyInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null)
        {
            return NotFound();
        }

        byte[] bytes;
        try
        {
            var stored = invoice.PdfFileId is { } fileId ? await Db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId) : null;
            bytes = stored is not null
                ? await files.ReadAllAsync(stored, HttpContext.RequestAborted)
                : InvoicePdf.Render(await billing.BuildIssuedAsync(invoice, HttpContext.RequestAborted));
        }
        catch (StorageException)
        {
            // The kept file is not reachable (the NAS is off): the document is drawn again from what was issued.
            bytes = InvoicePdf.Render(await billing.BuildIssuedAsync(invoice, HttpContext.RequestAborted));
        }

        var name = FileStoreService.SafeFileName($"{Labels.Of(invoice.Type)} {invoice.Number}.pdf");
        Response.Headers.ContentDisposition = $"{(inline ? "inline" : "attachment")}; filename=\"{name}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        return new FileContentResult(bytes, "application/pdf");
    }
}
