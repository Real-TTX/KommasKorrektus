using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>A file the customer sent back: it becomes the next version of the document (a new round of the ping pong), so that it can be compared with every other one.</summary>
[RequestSizeLimit(DocumentService.MaxUploadBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentService.MaxUploadBytes + 1024 * 1024)]
public sealed class NewVersionModel(KkDbContext db, DocumentService documents, ProjectService projects, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    [Display(Name = "Name der Fassung")]
    public string? Label { get; set; }

    [BindProperty]
    [Display(Name = "Anmerkung")]
    public string? Comment { get; set; }

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    public Document Document { get; private set; } = null!;

    /// <summary>The number the new version will have.</summary>
    public int NextNo => Document.CurrentVersionNo + 1;

    public async Task<IActionResult> OnGetAsync() => await PrepareAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (UploadedFile is null || UploadedFile.Length == 0)
        {
            ModelState.AddModelError(nameof(UploadedFile), "Bitte wähle eine Datei aus.");
            return Page();
        }

        if (UploadedFile.Length > DocumentService.MaxUploadBytes)
        {
            ModelState.AddModelError(nameof(UploadedFile), $"Die Datei ist größer als {DocumentService.MaxUploadBytes / 1024 / 1024} MB.");
            return Page();
        }

        byte[] bytes;
        await using (var stream = UploadedFile.OpenReadStream())
        using (var buffer = new MemoryStream((int)UploadedFile.Length))
        {
            await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            bytes = buffer.ToArray();
        }

        try
        {
            var document = await db.Document.Include(d => d.Project).FirstAsync(d => d.Id == Id);
            var previous = document.CurrentVersionNo;
            var result = await documents.AddFileVersionAsync(document, UploadedFile.FileName, bytes, Label, Comment, HttpContext.RequestAborted);
            projects.AddHistory(document.Project!.CustomerId, $"Datei vom Kunden zu „{document.Title}“", $"Fassung {result.Version.VersionNo}: +{result.Delta.WordsAdded} −{result.Delta.WordsRemoved} Wörter", document.ProjectId, "Document", document.Id);
            await db.SaveChangesAsync();
            await log.LogAsync("Datei vom Kunden als Fassung", "Document", document.Id, $"Fassung {result.Version.VersionNo}");
            Flash($"Die Datei ist Fassung {result.Version.VersionNo}: {(result.Delta.HasChanges ? $"+{result.Delta.WordsAdded} Wörter, −{result.Delta.WordsRemoved} entfernt, {result.Delta.WordsChanged} ersetzt gegenüber Fassung {previous}." : $"der Text ist derselbe wie in Fassung {previous}.")}");
            return RedirectToPage("/Documents/Compare", new { id = Id, from = previous, to = result.Version.VersionNo });
        }
        catch (ImportException ex)
        {
            ModelState.AddModelError(nameof(UploadedFile), ex.Message);
            return Page();
        }
        catch (StorageException ex)
        {
            ModelState.AddModelError(nameof(UploadedFile), "Die Datei konnte nicht abgelegt werden: " + ex.Message);
            return Page();
        }
    }

    private async Task<bool> PrepareAsync()
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id);
        if (document?.Project is null)
        {
            return false;
        }

        Document = document;
        Head("Datei vom Kunden", null, ("Projekte", "/Projects"), (document.Project.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={document.Id}"), ("Datei vom Kunden", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        return true;
    }
}
