using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

public sealed class DetailsModel(KkDbContext db, ProjectService projects, ConfigStore config, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed record DocumentRow(Document Document, int Words, int Chars, DateTime Changed, int Versions);

    public sealed record TimeRow(TimeEntry Entry, string UserName, string? DocumentTitle);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public ProjectStatus NewStatus { get; set; }

    public Project Project { get; private set; } = null!;

    public Customer Customer { get; private set; } = null!;

    public List<DocumentRow> Documents { get; private set; } = [];

    public List<ProjectTask> Tasks { get; private set; } = [];

    public List<TimeRow> Times { get; private set; } = [];

    public List<StyleEntry> Styles { get; private set; } = [];

    public List<ShareLink> Shares { get; private set; } = [];

    public List<Invoice> Invoices { get; private set; } = [];

    public int TotalSeconds { get; private set; }

    public int BilledSeconds { get; private set; }

    public int TotalWords { get; private set; }

    public int TotalChars { get; private set; }

    public int WordsDone { get; private set; }

    public DateOnly Today { get; private set; }

    public decimal NormPages => config.Current.Company.NormPageCharacters <= 0 ? 0 : (decimal)TotalChars / config.Current.Company.NormPageCharacters;

    /// <summary>What the project would cost at its price and the size of the documents (netto), or null when there is nothing to calculate.</summary>
    public decimal? Estimate
    {
        get
        {
            var p = Project;
            return p.PricingModel switch
            {
                PricingModel.Flat => p.FlatPrice,
                PricingModel.PerNormPage when p.Rate is { } rate && TotalChars > 0 => Math.Round(NormPages * rate, 2),
                PricingModel.PerWord when p.Rate is { } rate && TotalWords > 0 => Math.Round(TotalWords * rate, 2),
                PricingModel.Hourly when p.Rate is { } rate && p.EstimatedHours is { } hours => Math.Round(hours * rate, 2),
                _ => null,
            };
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostStatusAsync()
    {
        var project = await db.Project.FirstOrDefaultAsync(p => p.Id == Id);
        if (project is null)
        {
            return NotFound();
        }

        projects.SetStatus(project, NewStatus);
        await db.SaveChangesAsync();
        await log.LogAsync("Projektstatus geändert", "Project", project.Id, Labels.Of(NewStatus));
        Flash($"Der Status ist jetzt „{Labels.Of(NewStatus)}“.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostTaskAsync(long taskId, TaskState state)
    {
        var task = await db.ProjectTask.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == Id);
        if (task is not null)
        {
            task.State = state;
            task.CompletedDate = state == TaskState.Done ? clock.GetUtcNow().UtcDateTime : null;
            await db.SaveChangesAsync();
        }

        return RedirectToPage(new { id = Id, tab = "tasks" });
    }

    private async Task<bool> LoadAsync()
    {
        var project = await db.Project.AsNoTracking().Include(p => p.Customer).FirstOrDefaultAsync(p => p.Id == Id);
        if (project?.Customer is null)
        {
            return false;
        }

        Project = project;
        Customer = project.Customer;
        NewStatus = project.Status;
        Today = Fmt.Today(clock);
        Head(project.Title, $"{project.Number} · {Customer.DisplayName}", ("Projekte", "/Projects"), (project.Title, null));
        ViewData["ActiveHref"] = "/Projects";

        Documents = (await db.Document.AsNoTracking().Where(d => d.ProjectId == Id).OrderBy(d => d.Title)
            .Select(d => new
            {
                Document = d,
                Version = d.Versions.Where(v => v.VersionNo == d.CurrentVersionNo).Select(v => new { v.WordCount, v.CharCount, v.CreateDate }).FirstOrDefault(),
                Versions = d.Versions.Count(),
            }).ToListAsync())
            .Select(x => new DocumentRow(x.Document, x.Version?.WordCount ?? 0, x.Version?.CharCount ?? 0, x.Version?.CreateDate ?? x.Document.UpdateDate, x.Versions)).ToList();
        TotalWords = Documents.Sum(d => d.Words);
        TotalChars = Documents.Sum(d => d.Chars);

        Tasks = await db.ProjectTask.AsNoTracking().Include(t => t.AssignedUser).Where(t => t.ProjectId == Id).OrderBy(t => t.State == TaskState.Done).ThenBy(t => t.SortOrder).ToListAsync();
        Times = (await db.TimeEntry.AsNoTracking().Where(t => t.ProjectId == Id).OrderByDescending(t => t.StartDate).Take(30)
            .Select(t => new { Entry = t, UserName = t.User!.DisplayName, DocumentTitle = db.Document.Where(d => d.Id == t.DocumentId).Select(d => d.Title).FirstOrDefault() }).ToListAsync())
            .Select(x => new TimeRow(x.Entry, x.UserName, x.DocumentTitle)).ToList();
        TotalSeconds = await db.TimeEntry.Where(t => t.ProjectId == Id).SumAsync(t => (int?)t.Seconds) ?? 0;
        BilledSeconds = await db.TimeEntry.Where(t => t.ProjectId == Id && t.InvoiceLineId != null).SumAsync(t => (int?)t.Seconds) ?? 0;
        WordsDone = await db.WorkActivity.Where(a => a.ProjectId == Id).SumAsync(a => (int?)(a.WordsAdded + a.WordsChanged)) ?? 0;
        Styles = await db.StyleEntry.AsNoTracking().Where(s => s.ProjectId == Id).OrderBy(s => s.Category).ThenBy(s => s.Term).ToListAsync();
        Shares = await db.ShareLink.AsNoTracking().Where(s => s.ProjectId == Id).OrderByDescending(s => s.CreateDate).ToListAsync();
        Invoices = await db.Invoice.AsNoTracking().Where(i => i.ProjectId == Id).OrderByDescending(i => i.IssueDate).ToListAsync();
        return true;
    }
}
