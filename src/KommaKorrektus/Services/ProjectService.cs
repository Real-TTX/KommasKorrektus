using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services;

/// <summary>What happens to a project over its life: tasks from a template, status changes, and the entries in the history of the customer that go with them.</summary>
public sealed class ProjectService(KkDbContext db, TimeProvider clock)
{
    /// <summary>Writes a line in the history of the customer (an entry the program makes, not a note of the editor). It is saved with the next SaveChanges of the caller.</summary>
    public void AddHistory(long customerId, string title, string? text = null, long? projectId = null, string? refType = null, long? refId = null)
        => db.HistoryEntry.Add(new HistoryEntry
        {
            CustomerId = customerId,
            ProjectId = projectId,
            Kind = HistoryKind.System,
            Title = title,
            Text = text,
            OccurredDate = clock.GetUtcNow().UtcDateTime,
            RefType = refType,
            RefId = refId,
        });

    /// <summary>Puts the tasks of a template on a project. The due dates count from the start of the project, or from today when it has none. Returns how many were made.</summary>
    public async Task<int> ApplyTemplateAsync(Project project, long templateId, CancellationToken ct = default)
    {
        var template = await db.TaskTemplate.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
        {
            return 0;
        }

        var start = project.StartDate ?? Fmt.Today(clock);
        var order = project.Id == 0 ? 0 : await db.ProjectTask.Where(t => t.ProjectId == project.Id).MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;
        foreach (var item in template.Items.OrderBy(i => i.SortOrder))
        {
            project.Tasks.Add(new ProjectTask
            {
                Title = item.Title,
                Description = item.Description,
                EstimatedMinutes = item.EstimatedMinutes,
                DueDate = item.DueOffsetDays is { } days ? start.AddDays(days) : null,
                SortOrder = ++order,
            });
        }

        return template.Items.Count;
    }

    /// <summary>Changes the status; a finished project gets its end date, a reopened one loses it. The customer's history records it.</summary>
    public void SetStatus(Project project, ProjectStatus status)
    {
        if (project.Status == status)
        {
            return;
        }

        var old = project.Status;
        project.Status = status;
        project.DoneDate = status == ProjectStatus.Done ? Fmt.Today(clock) : status == ProjectStatus.Archived ? project.DoneDate : null;
        AddHistory(project.CustomerId, $"Projekt „{project.Title}“: {UiKit.Labels.Of(status)}", $"Vorher: {UiKit.Labels.Of(old)}", project.Id == 0 ? null : project.Id, "Project", project.Id == 0 ? null : project.Id);
    }

    /// <summary>
    /// Deletes a project and what hangs on it (tasks, documents, style sheet, time). Refused when invoices or offers refer to it:
    /// they have to stay readable, so such a project is archived.
    /// </summary>
    public async Task<string?> DeleteAsync(Project project, CancellationToken ct = default)
    {
        if (await db.Invoice.AnyAsync(i => i.ProjectId == project.Id, ct))
        {
            return "Zu diesem Projekt gibt es Rechnungen oder Angebote. Archiviere das Projekt, statt es zu löschen.";
        }

        foreach (var task in await db.ProjectTask.Where(t => t.ProjectId == project.Id).ToListAsync(ct)) { db.ProjectTask.Remove(task); }
        foreach (var style in await db.StyleEntry.Where(s => s.ProjectId == project.Id).ToListAsync(ct)) { db.StyleEntry.Remove(style); }
        foreach (var time in await db.TimeEntry.Where(t => t.ProjectId == project.Id).ToListAsync(ct)) { db.TimeEntry.Remove(time); }
        foreach (var doc in await db.Document.Where(d => d.ProjectId == project.Id).ToListAsync(ct)) { db.Document.Remove(doc); }
        db.Project.Remove(project);
        AddHistory(project.CustomerId, $"Projekt „{project.Title}“ gelöscht");
        return null;
    }
}
