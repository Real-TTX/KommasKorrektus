using KommasKorrektus.Data;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Portal;

/// <summary>The portal of a client: the projects of the customers the account was given access to. Read-only: documents, changes and invoices.</summary>
public sealed class IndexModel(KkDbContext db, ICurrentUser current) : PortalModelBase(db, current)
{
    public List<Data.Entities.Project> Projects { get; private set; } = [];

    public int OpenInvoices { get; private set; }

    public async Task OnGetAsync()
    {
        Head("Meine Projekte", "Alles zu deinen Projekten an einem Ort.", ("Meine Projekte", null));
        Projects = await MyProjects.AsNoTracking().Include(p => p.Customer).OrderByDescending(p => p.UpdateDate).ToListAsync();
        OpenInvoices = await MyInvoices.CountAsync(i => i.Type == Data.Entities.DocType.Invoice && (i.Status == Data.Entities.DocStatus.Issued || i.Status == Data.Entities.DocStatus.PartiallyPaid));
    }
}
