using KommasKorrektus.Data;
using KommasKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>The file as it was uploaded: the original of version 1, or with <c>version</c> the file of the customer a later version came from.</summary>
public sealed class DownloadModel(KkDbContext db, FileStoreService files) : PageModelBase
{
    /// <summary>
    /// <paramref name="inline"/> shows a PDF in the browser instead of saving it (the editor shows the original next to the text). Nothing else is ever shown
    /// from this address: a file that is not a PDF is always a download.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(long id, bool inline = false, int version = 0)
    {
        var document = await db.Document.AsNoTracking().Include(d => d.OriginalFile).FirstOrDefaultAsync(d => d.Id == id);
        var file = version > 1
            ? (await db.DocumentVersion.AsNoTracking().Include(v => v.SourceFile).FirstOrDefaultAsync(v => v.DocumentId == id && v.VersionNo == version))?.SourceFile
            : document?.OriginalFile;
        if (document is null || file is null)
        {
            return NotFound();
        }

        try
        {
            var stream = await files.OpenAsync(file, HttpContext.RequestAborted);
            if (inline && file.ContentType == "application/pdf")
            {
                return File(stream, "application/pdf");
            }

            return File(stream, file.ContentType, file.FileName);
        }
        catch (StorageException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage("/Documents/Details", new { id });
        }
    }
}
