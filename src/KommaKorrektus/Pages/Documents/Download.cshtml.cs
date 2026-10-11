using KommaKorrektus.Data;
using KommaKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Documents;

/// <summary>The file as it was uploaded (the original of version 1).</summary>
public sealed class DownloadModel(KkDbContext db, FileStoreService files) : PageModelBase
{
    public async Task<IActionResult> OnGetAsync(long id)
    {
        var document = await db.Document.AsNoTracking().Include(d => d.OriginalFile).FirstOrDefaultAsync(d => d.Id == id);
        if (document?.OriginalFile is null)
        {
            return NotFound();
        }

        try
        {
            return File(await files.OpenAsync(document.OriginalFile, HttpContext.RequestAborted), document.OriginalFile.ContentType, document.OriginalFile.FileName);
        }
        catch (StorageException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage("/Documents/Details", new { id });
        }
    }
}
