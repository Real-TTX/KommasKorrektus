using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Auth;
using KommaKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Customers;

public sealed class DetailsModel(KkDbContext db, InvitationService invitations, SessionCache cache, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed record PortalUser(long UserId, string Name, string Email, DateTime? LastLogin, bool IsActive);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public Customer Customer { get; private set; } = null!;

    public TableModel Projects { get; private set; } = null!;

    public TableModel Invoices { get; private set; } = null!;

    public List<HistoryEntry> History { get; private set; } = [];

    public List<CustomerContact> Contacts { get; private set; } = [];

    public List<PortalUser> PortalUsers { get; private set; } = [];

    public List<Invitation> OpenInvitations { get; private set; } = [];

    public int ProjectCount { get; private set; }

    public decimal Revenue { get; private set; }

    public decimal Open { get; private set; }

    public int Seconds { get; private set; }

    public int Words { get; private set; }

    public string Address => string.Join(", ", new[] { Customer.Street, string.Join(' ', new[] { Customer.PostalCode, Customer.City }.Where(s => !string.IsNullOrWhiteSpace(s))) }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAccessAsync(long userId)
    {
        var link = await db.UserCustomer.FirstOrDefaultAsync(c => c.CustomerId == Id && c.UserId == userId);
        if (link is not null)
        {
            db.UserCustomer.Remove(link);
            await db.SaveChangesAsync();
            cache.InvalidateUser(userId);
            await log.LogAsync("Portal-Zugang entzogen", "Customer", Id, $"Benutzer {userId}");
            Flash("Der Zugang ist entzogen.");
        }

        return RedirectToPage(new { id = Id, tab = "portal" });
    }

    public async Task<IActionResult> OnPostRevokeInvitationAsync(long invitationId)
    {
        var invitation = await db.Invitation.FirstOrDefaultAsync(i => i.Id == invitationId && i.CustomerId == Id);
        if (invitation is not null)
        {
            await invitations.RevokeAsync(invitation);
            Flash("Die Einladung ist zurückgezogen.");
        }

        return RedirectToPage(new { id = Id, tab = "portal" });
    }

    private async Task<bool> LoadAsync()
    {
        var customer = await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == Id);
        if (customer is null)
        {
            return false;
        }

        Customer = customer;
        Head(customer.DisplayName, string.IsNullOrWhiteSpace(customer.PenName) ? customer.Number : $"{customer.Number} · Pseudonym {customer.PenName}", ("Kunden", "/Customers"), (customer.DisplayName, null));
        ViewData["ActiveHref"] = "/Customers";

        var projects = await db.Project.AsNoTracking().Where(p => p.CustomerId == Id).OrderByDescending(p => p.UpdateDate).ToListAsync();
        ProjectCount = projects.Count;
        Projects = TableModel.Of("customer-projects", "Projekte des Kunden", projects,
        [
            (new TableColumn("Nummer", Mono: true, HideOnPhone: true), p => Ui.Text(p.Number)),
            (new TableColumn("Projekt"), p => Ui.NameWithSub(p.Title, $"/Projects/Details?id={p.Id}", Labels.Of(p.ServiceType))),
            (new TableColumn("Status"), p => Labels.Pill(p.Status)),
            (new TableColumn("Frist", HideOnPhone: true), p => Ui.Text(Fmt.Date(p.DueDate))),
        ],
        p => [new RowAction("eye", "Öffnen", $"/Projects/Details?id={p.Id}")]) with { EmptyTitle = "Noch keine Projekte", EmptyText = "Lege das erste Projekt für diesen Kunden an.", EmptyIcon = "folder" };

        var invoices = await db.Invoice.AsNoTracking().Where(i => i.CustomerId == Id).OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).ToListAsync();
        Invoices = TableModel.Of("customer-invoices", "Rechnungen und Angebote des Kunden", invoices,
        [
            (new TableColumn("Nummer", Mono: true), i => Ui.Link(i.Number ?? "Entwurf", $"/Invoices/Details?id={i.Id}")),
            (new TableColumn("Art", HideOnPhone: true), i => Ui.Text(Labels.Of(i.Type))),
            (new TableColumn("Datum", HideOnPhone: true), i => Ui.Text(Fmt.Date(i.IssueDate))),
            (new TableColumn("Betrag", Numeric: true), i => Ui.Text(Fmt.Money(i.GrossTotal, i.Currency))),
            (new TableColumn("Status"), i => Labels.Pill(i.Status, i.Type)),
        ],
        i => [new RowAction("eye", "Öffnen", $"/Invoices/Details?id={i.Id}")]) with { EmptyTitle = "Noch keine Rechnungen", EmptyText = "Angebote und Rechnungen dieses Kunden erscheinen hier.", EmptyIcon = "dollar" };

        var billed = invoices.Where(i => i.Type == DocType.Invoice && i.Status is DocStatus.Issued or DocStatus.PartiallyPaid or DocStatus.Paid).ToList();
        Revenue = billed.Sum(i => i.NetTotal);
        Open = billed.Where(i => i.Status is DocStatus.Issued or DocStatus.PartiallyPaid).Sum(i => i.GrossTotal - i.PaidTotal);

        var projectIds = projects.Select(p => p.Id).ToList();
        Seconds = await db.TimeEntry.Where(t => projectIds.Contains(t.ProjectId)).SumAsync(t => (int?)t.Seconds) ?? 0;
        Words = await db.WorkActivity.Where(a => projectIds.Contains(a.ProjectId)).SumAsync(a => (int?)(a.WordsAdded + a.WordsChanged)) ?? 0;

        History = await db.HistoryEntry.AsNoTracking().Where(h => h.CustomerId == Id).OrderByDescending(h => h.OccurredDate).Take(100).ToListAsync();
        Contacts = await db.CustomerContact.AsNoTracking().Where(c => c.CustomerId == Id).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name).ToListAsync();
        PortalUsers = await db.UserCustomer.AsNoTracking().Where(c => c.CustomerId == Id)
            .Select(c => new PortalUser(c.UserId, c.User!.DisplayName, c.User.Email, c.User.LastLoginDate, c.User.IsActive)).ToListAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        OpenInvitations = await db.Invitation.AsNoTracking()
            .Where(i => i.CustomerId == Id && i.AcceptedDate == null && i.RevokedDate == null && i.ExpiresDate > now).OrderByDescending(i => i.CreateDate).ToListAsync();
        return true;
    }
}
