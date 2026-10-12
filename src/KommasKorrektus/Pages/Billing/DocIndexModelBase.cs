using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Billing;

/// <summary>The list of invoices (with credit notes) or of offers: the same page twice, with different words and filters.</summary>
public abstract class DocIndexModelBase(KkDbContext db, BillingService billing, TimeProvider clock) : PageModelBase
{
    public sealed class Row
    {
        public required Invoice Invoice { get; init; }

        public required string CustomerName { get; init; }

        public string? ProjectTitle { get; init; }
    }

    protected abstract bool IsOffers { get; }

    public string Base => IsOffers ? "/Offers" : "/Invoices";

    public string NewLabel => IsOffers ? "Neues Angebot" : "Neue Rechnung";

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public decimal SumGross { get; private set; }

    public decimal SumOpen { get; private set; }

    public decimal SumOverdue { get; private set; }

    public int Overdue { get; private set; }

    public int Drafts { get; private set; }

    public async Task OnGetAsync()
    {
        var noun = IsOffers ? "Angebote" : "Rechnungen";
        Head(noun, IsOffers ? "Was du angeboten hast und was daraus geworden ist." : "Was gestellt, bezahlt und offen ist.", ("Rechnungen", IsOffers ? "/Invoices" : null), (noun, null));
        ViewData["ActiveHref"] = Base;
        var today = Fmt.Today(clock);
        var years = await db.Invoice.AsNoTracking().Select(i => i.IssueDate.Year).Distinct().OrderByDescending(y => y).ToListAsync();
        var customers = await db.Customer.AsNoTracking().Where(c => db.Invoice.Any(i => i.CustomerId == c.Id)).ToListAsync();

        var all = db.Invoice.AsNoTracking().Where(i => IsOffers ? i.Type == DocType.Offer : i.Type != DocType.Offer).Select(i => new Row
        {
            Invoice = i,
            CustomerName = i.Customer!.Company ?? ((i.Customer.FirstName ?? "") + " " + (i.Customer.LastName ?? "")),
            ProjectTitle = i.Project!.Title,
        });

        var statusOptions = IsOffers
            ? new List<FilterOption> { new("Draft", "Entwurf"), new("Issued", "Versendet"), new("Accepted", "Angenommen"), new("Declined", "Abgelehnt") }
            : [new("Draft", "Entwurf"), new("Issued", "Gestellt (offen)"), new("PartiallyPaid", "Teilweise bezahlt"), new("Paid", "Bezahlt"), new("Overdue", "Überfällig"), new("Cancelled", "Storniert")];

        var spec = new ListSpec<Row>
        {
            Id = IsOffers ? "offers" : "invoices",
            Action = Base,
            Label = noun,
            Noun = noun,
            SearchPlaceholder = "Nummer, Kunde, Projekt, Betreff ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(r => EF.Functions.ILike(r.Invoice.Number ?? "", like) || EF.Functions.ILike(r.CustomerName, like) || EF.Functions.ILike(r.ProjectTitle ?? "", like) || EF.Functions.ILike(r.Invoice.Subject ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Row>("status", "Status", statusOptions, (q, v) => v == "Overdue"
                    ? q.Where(r => (r.Invoice.Status == DocStatus.Issued || r.Invoice.Status == DocStatus.PartiallyPaid) && r.Invoice.Type == DocType.Invoice && r.Invoice.DueDate < today)
                    : q.Where(r => r.Invoice.Status == Enum.Parse<DocStatus>(v))),
                new FilterSpec<Row>("kunde", "Kunde", [.. customers.OrderBy(c => c.DisplayName).Select(c => new FilterOption(c.Id.ToString(), c.DisplayName))], (q, v) => q.Where(r => r.Invoice.CustomerId == long.Parse(v))),
                new FilterSpec<Row>("jahr", "Jahr", [.. years.Select(y => new FilterOption(y.ToString(), y.ToString()))], (q, v) => q.Where(r => r.Invoice.IssueDate.Year == int.Parse(v))),
                .. IsOffers ? Array.Empty<FilterSpec<Row>>() : [new FilterSpec<Row>("art", "Art", [new("Invoice", "Rechnung"), new("CreditNote", "Gutschrift")], (q, v) => q.Where(r => r.Invoice.Type == Enum.Parse<DocType>(v)))],
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>)>
            {
                ["date"] = ("Datum", (q, d) => d ? q.OrderByDescending(r => r.Invoice.IssueDate).ThenByDescending(r => r.Invoice.Id) : q.OrderBy(r => r.Invoice.IssueDate).ThenBy(r => r.Invoice.Id)),
                ["number"] = ("Nummer", (q, d) => d ? q.OrderByDescending(r => r.Invoice.Number) : q.OrderBy(r => r.Invoice.Number)),
                ["customer"] = ("Kunde", (q, d) => d ? q.OrderByDescending(r => r.CustomerName) : q.OrderBy(r => r.CustomerName)),
                ["amount"] = ("Betrag", (q, d) => d ? q.OrderByDescending(r => r.Invoice.GrossTotal) : q.OrderBy(r => r.Invoice.GrossTotal)),
                ["due"] = (IsOffers ? "Gültig bis" : "Fällig am", (q, d) => IsOffers
                    ? (d ? q.OrderByDescending(r => r.Invoice.ValidUntil) : q.OrderBy(r => r.Invoice.ValidUntil))
                    : (d ? q.OrderByDescending(r => r.Invoice.DueDate) : q.OrderBy(r => r.Invoice.DueDate))),
            },
            DefaultSort = "date",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(all, Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;

        var filtered = result.Filtered.Select(r => r.Invoice);
        var billed = filtered.Where(i => i.Status != DocStatus.Draft && i.Status != DocStatus.Cancelled && i.Type != DocType.CreditNote);
        SumGross = await billed.SumAsync(i => (decimal?)i.GrossTotal) ?? 0;
        if (!IsOffers)
        {
            var open = filtered.Where(i => i.Type == DocType.Invoice && (i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid));
            SumOpen = await open.SumAsync(i => (decimal?)(i.GrossTotal - i.PaidTotal)) ?? 0;
            var late = open.Where(i => i.DueDate < today);
            SumOverdue = await late.SumAsync(i => (decimal?)(i.GrossTotal - i.PaidTotal)) ?? 0;
            Overdue = await late.CountAsync();
        }

        Drafts = await filtered.CountAsync(i => i.Status == DocStatus.Draft);

        Table = TableModel.Of(IsOffers ? "offer-table" : "invoice-table", noun, result.Items,
        [
            (new TableColumn("Nummer", Mono: true), r => Ui.NameWithSub(r.Invoice.Number ?? "Entwurf", $"{Base}/Details?id={r.Invoice.Id}", r.Invoice.Type == DocType.CreditNote ? "Gutschrift" : r.Invoice.Subject)),
            (new TableColumn("Kunde"), r => Ui.NameWithSub(r.CustomerName.Trim(), $"/Customers/Details?id={r.Invoice.CustomerId}", r.ProjectTitle)),
            (new TableColumn("Datum", HideOnPhone: true), r => Ui.Text(Fmt.Date(r.Invoice.IssueDate))),
            (new TableColumn(IsOffers ? "Gültig bis" : "Fällig", HideOnPhone: true), r => Ui.Text(Fmt.Date(IsOffers ? r.Invoice.ValidUntil : r.Invoice.DueDate))),
            (new TableColumn("Betrag", Numeric: true), r => Ui.Text(Fmt.Money(r.Invoice.GrossTotal, r.Invoice.Currency))),
            .. IsOffers ? Array.Empty<(TableColumn, Func<Row, Microsoft.AspNetCore.Html.IHtmlContent>)>() : [(new TableColumn("Offen", Numeric: true, HideOnPhone: true), r => Ui.Text(r.Invoice.Type == DocType.Invoice && r.Invoice.Status is DocStatus.Issued or DocStatus.PartiallyPaid ? Fmt.Money(r.Invoice.GrossTotal - r.Invoice.PaidTotal, r.Invoice.Currency) : ""))],
            (new TableColumn("Status"), r => Ui.Pill(billing.StatusText(r.Invoice), billing.StatusTone(r.Invoice))),
        ],
        r => [new RowAction("eye", "Öffnen", $"{Base}/Details?id={r.Invoice.Id}"), .. r.Invoice.Status == DocStatus.Draft ? [new RowAction("edit", "Bearbeiten", $"{Base}/Edit?id={r.Invoice.Id}")] : Array.Empty<RowAction>()])
        with
        {
            EmptyTitle = Toolbar.Total == 0 ? (IsOffers ? "Noch keine Angebote" : "Noch keine Rechnungen") : "Nichts gefunden",
            EmptyText = Toolbar.Total == 0 ? "Lege das erste Dokument an: Positionen kommen aus deinen Produkten und aus der Zeiterfassung." : "Ändere die Suche oder den Filter.",
            EmptyIcon = IsOffers ? "file-text" : "dollar",
        };
    }
}
