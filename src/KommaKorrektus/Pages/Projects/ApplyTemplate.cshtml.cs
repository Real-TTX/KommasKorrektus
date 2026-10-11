using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Projects;

/// <summary>Adds the tasks of a template to a project that exists already.</summary>
public sealed class ApplyTemplateModel(KkDbContext db, ProjectService projects) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long ProjectId { get; set; }

    [BindProperty]
    public long TemplateId { get; set; }

    public Project Project { get; private set; } = null!;

    public List<SelectListItem> Templates { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await PrepareAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        var project = await db.Project.FirstAsync(p => p.Id == ProjectId);
        var count = await projects.ApplyTemplateAsync(project, TemplateId);
        await db.SaveChangesAsync();
        Flash(count == 0 ? "Die Vorlage hat keine Aufgaben." : $"{count} Aufgaben sind angelegt.");
        return RedirectToPage("/Projects/Details", new { id = ProjectId, tab = "tasks" });
    }

    private async Task<bool> PrepareAsync()
    {
        var project = await db.Project.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ProjectId);
        if (project is null)
        {
            return false;
        }

        Project = project;
        Head("Aufgaben aus Vorlage", null, ("Projekte", "/Projects"), (project.Title, $"/Projects/Details?id={project.Id}&tab=tasks"), ("Aufgaben aus Vorlage", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        Templates = (await db.TaskTemplate.AsNoTracking().Include(t => t.Items).OrderBy(t => t.Name).ToListAsync())
            .Select(t => new SelectListItem($"{t.Name} ({t.Items.Count} Aufgaben)", t.Id.ToString())).ToList();
        return true;
    }
}
