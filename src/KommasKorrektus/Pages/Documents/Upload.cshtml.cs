using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

[RequestSizeLimit(DocumentService.MaxUploadBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentService.MaxUploadBytes + 1024 * 1024)]
public sealed class UploadModel(KkDbContext db, DocumentService documents, ProjectService projects, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long ProjectId { get; set; }

    [BindProperty]
    [Display(Name = "Titel")]
    public string? Title { get; set; }

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    public Project Project { get; private set; } = null!;

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
            var project = await db.Project.FirstAsync(p => p.Id == ProjectId);
            var result = await documents.ImportAsync(project, Title ?? "", UploadedFile.FileName, bytes, HttpContext.RequestAborted);
            await db.SaveChangesAsync();
            projects.AddHistory(project.CustomerId, $"Dokument „{result.Document.Title}“ hochgeladen", result.Document.FileName, project.Id, "Document", result.Document.Id);
            await db.SaveChangesAsync();
            await log.LogAsync("Dokument hochgeladen", "Document", result.Document.Id, result.Document.Title);
            Flash($"Das Dokument ist importiert (Fassung 1). {(result.Notes.Count > 0 ? "Bitte lies den Hinweis." : "")}".Trim());
            return RedirectToPage("/Documents/Details", new { id = result.Document.Id });
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
        var project = await db.Project.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ProjectId);
        if (project is null)
        {
            return false;
        }

        Project = project;
        Head("Dokument hochladen", null, ("Projekte", "/Projects"), (project.Title, $"/Projects/Details?id={project.Id}&tab=documents"), ("Dokument hochladen", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        return true;
    }
}
