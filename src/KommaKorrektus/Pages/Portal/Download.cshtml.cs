using KommaKorrektus.Data;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Portal;

/// <summary>A file of a document of a client (Word or PDF, with or without the changes). There is no page: the address is the download.</summary>
public sealed class DownloadModel(KkDbContext db, ICurrentUser current, DocumentService documents, Configuration.ConfigStore config) : PortalModelBase(db, current)
{
    public async Task<IActionResult> OnGetAsync(long id, ExportFormat format)
    {
        var document = await Db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && MyCustomerIds.Contains(d.Project!.CustomerId));
        if (document is null || format is not (ExportFormat.DocxTracked or ExportFormat.DocxClean or ExportFormat.PdfChanges or ExportFormat.PdfClean))
        {
            return NotFound();
        }

        var to = document.CurrentVersionNo;
        var changes = format is ExportFormat.DocxTracked or ExportFormat.PdfChanges;
        if (changes && to == 1)
        {
            format = format == ExportFormat.DocxTracked ? ExportFormat.DocxClean : ExportFormat.PdfClean;
            changes = false;
        }

        try
        {
            var author = string.IsNullOrWhiteSpace(config.Current.Company.Name) ? "Lektorat" : config.Current.Company.Name;
            var result = await documents.ExportAsync(document, changes ? 1 : to, to, format, author, HttpContext.RequestAborted);
            return File(result.Bytes, result.ContentType, result.FileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
