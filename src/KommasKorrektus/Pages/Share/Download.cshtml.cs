using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Share;

/// <summary>A file of a document behind a share link. There is no page: the address is the download. Only what the link allows is made.</summary>
public sealed class DownloadModel(ShareService shares, ICurrentUser current, KkDbContext db, DocumentService documents) : ShareModelBase(shares, current)
{
    private readonly ShareService _shares = shares;

    public async Task<IActionResult> OnGetAsync(long id, ExportFormat format)
    {
        if (!await InitAsync())
        {
            return NotFound();
        }

        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.ProjectId == Link.ProjectId, HttpContext.RequestAborted);
        if (document is null || (Link.DocumentId is { } only && only != document.Id) || !Link.AllowDownload)
        {
            return NotFound();
        }

        var to = ShareService.VersionFor(Link, document);
        var from = IsChanges(format) ? 1 : to;
        if (IsChanges(format) && (!Link.ShowChanges || to == from))
        {
            // There are no changes to show (not allowed, or the text is still the original): the clean file is what the visitor gets.
            format = format == ExportFormat.DocxTracked ? ExportFormat.DocxClean : ExportFormat.PdfClean;
        }

        if (format is not (ExportFormat.DocxTracked or ExportFormat.DocxClean or ExportFormat.PdfChanges or ExportFormat.PdfClean))
        {
            return NotFound();
        }

        try
        {
            var author = string.IsNullOrWhiteSpace(CompanyName) ? "Lektorat" : CompanyName;
            var result = await documents.ExportAsync(document, from, to, format, author, HttpContext.RequestAborted);
            if (!IsStaffPreview)
            {
                await _shares.LogAsync(Link, ShareAction.Download, HttpContext, $"{document.Title}: {format}", HttpContext.RequestAborted);
            }

            return File(result.Bytes, result.ContentType, result.FileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private static bool IsChanges(ExportFormat format) => format is ExportFormat.DocxTracked or ExportFormat.PdfChanges;
}
