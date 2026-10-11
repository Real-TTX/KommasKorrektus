using KommaKorrektus.Data;
using KommaKorrektus.Services;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Portal;

/// <summary>The portal of a client: the projects of the customers the account was given access to. Read-only: documents, changes and invoices.</summary>
public sealed class IndexModel(KkDbContext db, ICurrentUser current) : PageModelBase
{
    public List<Data.Entities.Project> Projects { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Head("Meine Projekte", "Alles zu deinen Projekten an einem Ort.", ("Meine Projekte", null));
        var userId = current.UserId;
        Projects = await db.Project.AsNoTracking().Include(p => p.Customer)
            .Where(p => db.UserCustomer.Any(uc => uc.UserId == userId && uc.CustomerId == p.CustomerId))
            .OrderByDescending(p => p.UpdateDate).ToListAsync();
    }
}
