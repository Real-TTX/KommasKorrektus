using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

public sealed class EditModel(KkDbContext db, NumberService numbers, ProjectService projects, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte wähle den Kunden.")]
        [Display(Name = "Kunde")]
        public long? CustomerId { get; set; }

        [Required(ErrorMessage = "Bitte gib einen Titel ein.")]
        [Display(Name = "Titel des Projekts")]
        public string Title { get; set; } = "";

        [Display(Name = "Leistung")]
        public ServiceType ServiceType { get; set; } = ServiceType.Copyediting;

        [Display(Name = "Status")]
        public ProjectStatus Status { get; set; } = ProjectStatus.Inquiry;

        [Display(Name = "Genre")]
        public string? Genre { get; set; }

        [Display(Name = "Beschreibung")]
        public string? Description { get; set; }

        [Display(Name = "Abrechnung")]
        public PricingModel PricingModel { get; set; } = PricingModel.PerNormPage;

        [Display(Name = "Preis")]
        [Range(0, 100000, ErrorMessage = "Bitte gib einen Preis zwischen 0 und 100000 ein.")]
        public decimal? Rate { get; set; }

        [Display(Name = "Pauschalpreis (netto)")]
        [Range(0, 10000000, ErrorMessage = "Bitte gib einen Preis zwischen 0 und 10000000 ein.")]
        public decimal? FlatPrice { get; set; }

        [Display(Name = "Geschätzter Aufwand in Stunden")]
        [Range(0, 10000, ErrorMessage = "Bitte gib Stunden zwischen 0 und 10000 ein.")]
        public decimal? EstimatedHours { get; set; }

        [Display(Name = "Beginn")]
        public DateOnly? StartDate { get; set; }

        [Display(Name = "Frist")]
        public DateOnly? DueDate { get; set; }

        [Display(Name = "Priorität")]
        public Priority Priority { get; set; } = Priority.Normal;

        [Display(Name = "Notizen")]
        public string? Notes { get; set; }

        /// <summary>When a project is made: the task list that is put on it.</summary>
        public long? TemplateId { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? CustomerId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Number { get; private set; }

    public string? DeleteBlocker { get; private set; }

    public List<SelectListItem> Customers { get; private set; } = [];

    public List<SelectListItem> Templates { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var p = await db.Project.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                CustomerId = p.CustomerId, Title = p.Title, ServiceType = p.ServiceType, Status = p.Status, Genre = p.Genre, Description = p.Description,
                PricingModel = p.PricingModel, Rate = p.Rate, FlatPrice = p.FlatPrice, EstimatedHours = p.EstimatedHours, StartDate = p.StartDate,
                DueDate = p.DueDate, Priority = p.Priority, Notes = p.Notes,
            };
            Number = p.Number;
        }
        else
        {
            Input.CustomerId = CustomerId;
            Input.StartDate = Fmt.Today(TimeProvider.System);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var i = Input;
        if (i.StartDate is { } start && i.DueDate is { } due && due < start)
        {
            ModelState.AddModelError("Input.DueDate", "Die Frist liegt vor dem Beginn.");
        }

        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return Page();
        }

        Project project;
        var isNew = Id is null;
        if (Id is { } id)
        {
            project = await db.Project.FirstOrDefaultAsync(p => p.Id == id) ?? throw new InvalidOperationException("Das Projekt existiert nicht mehr.");
        }
        else
        {
            project = new Project { Number = await numbers.NextAsync(NumberKind.Project) };
            db.Project.Add(project);
        }

        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        project.CustomerId = i.CustomerId!.Value;
        project.Title = i.Title.Trim();
        project.ServiceType = i.ServiceType;
        project.Genre = Clean(i.Genre);
        project.Description = Clean(i.Description);
        project.PricingModel = i.PricingModel;
        project.Rate = i.PricingModel == PricingModel.Flat ? null : i.Rate;
        project.FlatPrice = i.PricingModel == PricingModel.Flat ? i.FlatPrice : null;
        project.EstimatedHours = i.EstimatedHours;
        project.StartDate = i.StartDate;
        project.DueDate = i.DueDate;
        project.Priority = i.Priority;
        project.Notes = Clean(i.Notes);

        if (isNew)
        {
            project.Status = i.Status;
            if (i.TemplateId is { } templateId)
            {
                await projects.ApplyTemplateAsync(project, templateId);
            }

            await db.SaveChangesAsync();
            projects.AddHistory(project.CustomerId, $"Projekt „{project.Title}“ angelegt", project.Number, project.Id, "Project", project.Id);
        }
        else
        {
            projects.SetStatus(project, i.Status);
        }

        await db.SaveChangesAsync();
        await log.LogAsync(isNew ? "Projekt angelegt" : "Projekt geändert", "Project", project.Id, project.Title);
        Flash(isNew ? "Das Projekt ist angelegt." : "Das Projekt ist gespeichert.");
        return RedirectToPage("/Projects/Details", new { id = project.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var project = await db.Project.FirstOrDefaultAsync(p => p.Id == Id);
        if (project is null)
        {
            return NotFound();
        }

        if (await projects.DeleteAsync(project) is { } blocker)
        {
            FlashError(blocker);
            return RedirectToPage(new { id = project.Id });
        }

        await db.SaveChangesAsync();
        await log.LogAsync("Projekt gelöscht", "Project", project.Id, project.Title);
        Flash("Das Projekt ist gelöscht.");
        return RedirectToPage("/Projects/Index");
    }

    private async Task PrepareAsync()
    {
        var title = Id is null ? "Neues Projekt" : "Projekt bearbeiten";
        Head(title, null, ("Projekte", "/Projects"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";

        var current = Input.CustomerId;
        Customers = (await db.Customer.AsNoTracking().Where(c => !c.IsArchived || c.Id == current).ToListAsync())
            .OrderBy(c => c.DisplayName).Select(c => new SelectListItem($"{c.DisplayName} ({c.Number})", c.Id.ToString())).ToList();
        Templates = (await db.TaskTemplate.AsNoTracking().OrderBy(t => t.Name).ToListAsync())
            .Select(t => new SelectListItem($"{t.Name}", t.Id.ToString())).ToList();
        if (Id is { } id)
        {
            DeleteBlocker = await db.Invoice.AnyAsync(i => i.ProjectId == id)
                ? "Zu diesem Projekt gibt es Rechnungen oder Angebote. Setze es auf „Archiviert“, statt es zu löschen." : null;
        }
    }
}
