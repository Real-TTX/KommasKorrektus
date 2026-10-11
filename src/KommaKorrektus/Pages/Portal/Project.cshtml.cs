using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Portal;

/// <summary>One project of a client: where it stands, its documents with the changes made, and its invoices.</summary>
public sealed class ProjectModel(KkDbContext db, ICurrentUser current) : PortalModelBase(db, current)
{
    public sealed record DocumentRow(Document Document, int Words, DateTime Changed);

    public Project Project { get; private set; } = null!;

    public List<DocumentRow> Documents { get; private set; } = [];

    public List<Invoice> Invoices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var project = await MyProjects.AsNoTracking().Include(p => p.Customer).FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
        {
            return NotFound();
        }

        Project = project;
        PortalHead(project.Title, project.Customer?.DisplayName, ("Meine Projekte", "/Portal"), (project.Title, null));

        var documents = await Db.Document.AsNoTracking().Where(d => d.ProjectId == id).OrderBy(d => d.Title).ToListAsync();
        foreach (var d in documents)
        {
            var version = await Db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == d.Id && v.VersionNo == d.CurrentVersionNo).Select(v => new { v.WordCount, v.CreateDate }).FirstOrDefaultAsync();
            Documents.Add(new DocumentRow(d, version?.WordCount ?? 0, version?.CreateDate ?? d.UpdateDate));
        }

        Invoices = await MyInvoices.AsNoTracking().Where(i => i.ProjectId == id).OrderByDescending(i => i.IssueDate).ToListAsync();
        return Page();
    }
}
