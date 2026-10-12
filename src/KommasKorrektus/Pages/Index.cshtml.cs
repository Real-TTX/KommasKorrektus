using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages;

public sealed class IndexModel(KkDbContext db, ConfigStore config, ICurrentUser current, TimeProvider clock) : PageModelBase
{
    public sealed record Step(string Title, string Text, string Href, bool Done);

    public List<Step> Steps { get; private set; } = [];

    public int ActiveProjects { get; private set; }

    public int OpenTasks { get; private set; }

    public int OverdueTasks { get; private set; }

    public decimal OpenAmount { get; private set; }

    public int OverdueInvoices { get; private set; }

    public int WeekSeconds { get; private set; }

    public int WeekWords { get; private set; }

    public List<Project> DueProjects { get; private set; } = [];

    public List<ProjectTask> DueTasks { get; private set; } = [];

    public List<Invoice> OpenInvoices { get; private set; } = [];

    public DateOnly Today { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (current.Role == UserRole.Client)
        {
            return RedirectToPage("/Portal/Index");
        }

        Head("Start", "Was heute ansteht.", ("Start", null));
        Today = Fmt.Today(clock);
        var weekStart = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        var weekStartUtc = Fmt.UtcFromLocal(weekStart.ToDateTime(TimeOnly.MinValue));
        var inAWeek = Today.AddDays(7);

        ActiveProjects = await db.Project.CountAsync(p => p.Status == ProjectStatus.InProgress || p.Status == ProjectStatus.Commissioned || p.Status == ProjectStatus.Review);
        OpenTasks = await db.ProjectTask.CountAsync(t => t.State != TaskState.Done && t.Project!.Status != ProjectStatus.Archived);
        OverdueTasks = await db.ProjectTask.CountAsync(t => t.State != TaskState.Done && t.DueDate < Today);

        var open = db.Invoice.Where(i => i.Type == DocType.Invoice && (i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid));
        OpenAmount = await open.SumAsync(i => (decimal?)(i.GrossTotal - i.PaidTotal)) ?? 0;
        OverdueInvoices = await open.CountAsync(i => i.DueDate < Today);
        OpenInvoices = await open.Include(i => i.Customer).OrderBy(i => i.DueDate).Take(5).ToListAsync();

        WeekSeconds = await db.TimeEntry.Where(t => t.UserId == current.UserId && t.StartDate >= weekStartUtc).SumAsync(t => (int?)t.Seconds) ?? 0;
        WeekWords = await db.WorkActivity.Where(a => a.UserId == current.UserId && a.At >= weekStartUtc).SumAsync(a => (int?)(a.WordsAdded + a.WordsChanged)) ?? 0;

        DueProjects = await db.Project.Include(p => p.Customer)
            .Where(p => p.DueDate != null && p.DueDate <= inAWeek && p.Status != ProjectStatus.Done && p.Status != ProjectStatus.Archived && p.Status != ProjectStatus.Cancelled)
            .OrderBy(p => p.DueDate).Take(6).ToListAsync();
        DueTasks = await db.ProjectTask.Include(t => t.Project)
            .Where(t => t.State != TaskState.Done && t.DueDate != null && t.DueDate <= inAWeek)
            .OrderBy(t => t.DueDate).Take(6).ToListAsync();

        if (current.IsAdmin)
        {
            var c = config.Current;
            Steps =
            [
                new("Firmendaten eintragen", "Name, Anschrift, Steuernummer und Bankverbindung stehen auf jeder Rechnung.", "/Admin/Company", !string.IsNullOrWhiteSpace(c.Company.Name) && !string.IsNullOrWhiteSpace(c.Company.Iban)),
                new("E-Mail einrichten", "Für Einladungen, Anmeldecodes und den Versand von Rechnungen.", "/Admin/Mail", c.Mail.Enabled && !string.IsNullOrWhiteSpace(c.Mail.Host)),
                new("Speicher festlegen", "Dokumente liegen im Docker-Volume oder auf einem NAS.", "/Admin/Storage", c.Storage.Mode == StorageMode.Smb && !string.IsNullOrWhiteSpace(c.Storage.Host)),
                new("Backup planen", "Datenbank und Dokumente regelmäßig sichern.", "/Admin/Backup/Plans", await db.BackupPlan.AnyAsync()),
                new("Ersten Kunden anlegen", "Dann kann das erste Projekt starten.", "/Customers/Edit", await db.Customer.AnyAsync()),
            ];
        }

        return Page();
    }
}
