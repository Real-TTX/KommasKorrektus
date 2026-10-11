using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Portal;

/// <summary>
/// Every page of the portal answers one question first: does this client have access to this customer? A client sees the projects, documents and invoices of the
/// customers it was given, nothing else; a request for anything else is "not found", so that the existence of other records is not revealed.
/// </summary>
public abstract class PortalModelBase(KkDbContext db, ICurrentUser current) : PageModelBase
{
    protected KkDbContext Db => db;

    protected ICurrentUser Current => current;

    /// <summary>The customers this account may see, as a query that is part of the SQL of the page (not a list in memory).</summary>
    protected IQueryable<long> MyCustomerIds
    {
        get
        {
            var userId = current.UserId;
            return db.UserCustomer.Where(uc => uc.UserId == userId).Select(uc => uc.CustomerId);
        }
    }

    protected IQueryable<Project> MyProjects => db.Project.Where(p => MyCustomerIds.Contains(p.CustomerId));

    protected IQueryable<Invoice> MyInvoices => db.Invoice.Where(i => MyCustomerIds.Contains(i.CustomerId) && i.Status != DocStatus.Draft);

    protected void PortalHead(string title, string? sub, params (string Text, string? Url)[] crumbs)
    {
        Head(title, sub, crumbs);
        ViewData["ActiveHref"] = "/Portal";
    }
}
