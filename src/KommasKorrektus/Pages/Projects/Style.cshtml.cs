using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

/// <summary>An entry of the style sheet (Stilblatt): how a name, a word or a rule is handled in this manuscript.</summary>
public sealed class StyleModel(KkDbContext db) : PageModelBase
{
    public sealed class InputModel
    {
        [Display(Name = "Art")]
        public StyleCategory Category { get; set; } = StyleCategory.Spelling;

        [Required(ErrorMessage = "Bitte gib den Eintrag ein, zum Beispiel einen Namen oder ein Wort.")]
        [Display(Name = "Eintrag")]
        public string Term { get; set; } = "";

        [Display(Name = "Entscheidung")]
        public string? Decision { get; set; }

        [Display(Name = "Anmerkung")]
        public string? Note { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Id is { } id)
        {
            var s = await db.StyleEntry.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == ProjectId);
            if (s is null)
            {
                return NotFound();
            }

            Input = new InputModel { Category = s.Category, Term = s.Term, Decision = s.Decision, Note = s.Note };
        }

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

        StyleEntry entry;
        if (Id is { } id)
        {
            entry = await db.StyleEntry.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == ProjectId) ?? throw new InvalidOperationException("Der Eintrag existiert nicht mehr.");
        }
        else
        {
            entry = new StyleEntry { ProjectId = ProjectId };
            db.StyleEntry.Add(entry);
        }

        entry.Category = Input.Category;
        entry.Term = Input.Term.Trim();
        entry.Decision = string.IsNullOrWhiteSpace(Input.Decision) ? null : Input.Decision.Trim();
        entry.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        await db.SaveChangesAsync();
        Flash("Der Eintrag ist gespeichert.");
        return RedirectToPage("/Projects/Details", new { id = ProjectId, tab = "style" });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var entry = await db.StyleEntry.FirstOrDefaultAsync(x => x.Id == Id && x.ProjectId == ProjectId);
        if (entry is not null)
        {
            db.StyleEntry.Remove(entry);
            await db.SaveChangesAsync();
            Flash("Der Eintrag ist gelöscht.");
        }

        return RedirectToPage("/Projects/Details", new { id = ProjectId, tab = "style" });
    }

    private async Task<bool> PrepareAsync()
    {
        var project = await db.Project.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ProjectId);
        if (project is null)
        {
            return false;
        }

        Project = project;
        var title = Id is null ? "Neuer Eintrag im Stilblatt" : "Eintrag bearbeiten";
        Head(title, null, ("Projekte", "/Projects"), (project.Title, $"/Projects/Details?id={project.Id}&tab=style"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        return true;
    }
}
