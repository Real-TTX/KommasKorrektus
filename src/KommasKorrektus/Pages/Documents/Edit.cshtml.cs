using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>The properties of a document (title, notes) and its deletion. The text is edited in the editor.</summary>
public sealed class EditModel(KkDbContext db, ProjectService projects, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib dem Dokument einen Titel.")]
        [Display(Name = "Titel")]
        public string Title { get; set; } = "";

        [Display(Name = "Notizen")]
        public string? Notes { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Document Document { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        Input = new InputModel { Title = Document.Title, Notes = Document.Notes };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var document = await db.Document.FirstAsync(d => d.Id == Id);
        document.Title = Input.Title.Trim();
        document.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        await db.SaveChangesAsync();
        Flash("Das Dokument ist gespeichert.");
        return RedirectToPage("/Documents/Details", new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var document = await db.Document.Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        db.Document.Remove(document);
        projects.AddHistory(document.Project!.CustomerId, $"Dokument „{document.Title}“ gelöscht", null, document.ProjectId);
        await db.SaveChangesAsync();
        await log.LogAsync("Dokument gelöscht", "Document", document.Id, document.Title);
        Flash("Das Dokument ist gelöscht.");
        return RedirectToPage("/Projects/Details", new { id = document.ProjectId, tab = "documents" });
    }

    private async Task<bool> PrepareAsync()
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return false;
        }

        Document = document;
        Head("Dokument bearbeiten", null, ("Projekte", "/Projects"), (document.Project!.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={document.Id}"), ("Eigenschaften", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        return true;
    }
}
