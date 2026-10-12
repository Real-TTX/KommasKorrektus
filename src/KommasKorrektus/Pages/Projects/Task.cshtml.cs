using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

public sealed class TaskModel(KkDbContext db, TimeProvider clock) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib der Aufgabe einen Titel.")]
        [Display(Name = "Aufgabe")]
        public string Title { get; set; } = "";

        [Display(Name = "Beschreibung")]
        public string? Description { get; set; }

        [Display(Name = "Status")]
        public TaskState State { get; set; } = TaskState.Open;

        [Display(Name = "Fällig am")]
        public DateOnly? DueDate { get; set; }

        [Display(Name = "Zuständig")]
        public long? AssignedUserId { get; set; }

        [Display(Name = "Geplante Minuten")]
        [Range(0, 100000, ErrorMessage = "Bitte gib Minuten zwischen 0 und 100000 ein.")]
        public int? EstimatedMinutes { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public List<SelectListItem> Users { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Id is { } id)
        {
            var t = await db.ProjectTask.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == ProjectId);
            if (t is null)
            {
                return NotFound();
            }

            Input = new InputModel { Title = t.Title, Description = t.Description, State = t.State, DueDate = t.DueDate, AssignedUserId = t.AssignedUserId, EstimatedMinutes = t.EstimatedMinutes };
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

        ProjectTask task;
        if (Id is { } id)
        {
            task = await db.ProjectTask.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == ProjectId) ?? throw new InvalidOperationException("Die Aufgabe existiert nicht mehr.");
        }
        else
        {
            task = new ProjectTask { ProjectId = ProjectId, SortOrder = (await db.ProjectTask.Where(x => x.ProjectId == ProjectId).MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1 };
            db.ProjectTask.Add(task);
        }

        task.Title = Input.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        if (task.State != Input.State)
        {
            task.CompletedDate = Input.State == TaskState.Done ? clock.GetUtcNow().UtcDateTime : null;
        }

        task.State = Input.State;
        task.DueDate = Input.DueDate;
        task.AssignedUserId = Input.AssignedUserId;
        task.EstimatedMinutes = Input.EstimatedMinutes;
        await db.SaveChangesAsync();
        Flash("Die Aufgabe ist gespeichert.");
        return RedirectToPage("/Projects/Details", new { id = ProjectId, tab = "tasks" });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var task = await db.ProjectTask.FirstOrDefaultAsync(x => x.Id == Id && x.ProjectId == ProjectId);
        if (task is not null)
        {
            db.ProjectTask.Remove(task);
            await db.SaveChangesAsync();
            Flash("Die Aufgabe ist gelöscht.");
        }

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
        var title = Id is null ? "Neue Aufgabe" : "Aufgabe bearbeiten";
        Head(title, null, ("Projekte", "/Projects"), (project.Title, $"/Projects/Details?id={project.Id}&tab=tasks"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        Users = await db.User.AsNoTracking().Where(u => u.IsActive && u.Role != UserRole.Client).OrderBy(u => u.DisplayName).Select(u => new SelectListItem(u.DisplayName, u.Id.ToString())).ToListAsync();
        return true;
    }
}
