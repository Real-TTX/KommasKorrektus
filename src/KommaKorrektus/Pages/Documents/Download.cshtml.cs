using KommaKorrektus.Data;
using KommaKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Documents;

/// <summary>The file as it was uploaded (the original of version 1).</summary>
public sealed class DownloadModel(KkDbContext db, FileStoreService files) : PageModelBase
{
    /// <summary>
    /// <paramref name="inline"/> shows a PDF in the browser instead of saving it (the editor shows the original next to the text). Nothing else is ever shown
    /// from this address: a file that is not a PDF is always a download.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(long id, bool inline = false)
    {
        var document = await db.Document.AsNoTracking().Include(d => d.OriginalFile).FirstOrDefaultAsync(d => d.Id == id);
        if (document?.OriginalFile is null)
        {
            return NotFound();
        }

        try
        {
            var stream = await files.OpenAsync(document.OriginalFile, HttpContext.RequestAborted);
            if (inline && document.OriginalFile.ContentType == "application/pdf")
            {
                return File(stream, "application/pdf");
            }

            return File(stream, document.OriginalFile.ContentType, document.OriginalFile.FileName);
        }
        catch (StorageException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage("/Documents/Details", new { id });
        }
    }
}
