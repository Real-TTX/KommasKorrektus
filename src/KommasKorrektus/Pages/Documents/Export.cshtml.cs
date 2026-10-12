using KommasKorrektus.Data;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>Makes a file of two versions (Word with tracked changes, PDF, text) and sends it. There is no page: the address is the download.</summary>
public sealed class ExportModel(KkDbContext db, DocumentService documents, ICurrentUser current, ActivityLogger log) : PageModelBase
{
    public async Task<IActionResult> OnGetAsync(long id, int from, int to, ExportFormat format)
    {
        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (document is null)
        {
            return NotFound();
        }

        if (to <= 0 || to > document.CurrentVersionNo) { to = document.CurrentVersionNo; }
        // Without a starting version: what the editor changed in this round, that is everything since the file of the customer (or since the original).
        if (from <= 0 || from > document.CurrentVersionNo) { from = await documents.RoundStartAsync(document, to, HttpContext.RequestAborted); }

        try
        {
            var result = await documents.ExportAsync(document, from, to, format, current.Name ?? "KommasKorrektus", HttpContext.RequestAborted);
            await log.LogAsync("Dokument exportiert", "Document", document.Id, $"{format}, Fassung {from} → {to}");
            return File(result.Bytes, result.ContentType, result.FileName);
        }
        catch (InvalidOperationException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage("/Documents/Details", new { id });
        }
    }
}
