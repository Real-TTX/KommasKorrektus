using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Portal;

/// <summary>The invoices and offers of the client's customers (issued ones; drafts are not shown): status, amount, and the PDF to download.</summary>
public sealed class InvoicesModel(KkDbContext db, ICurrentUser current) : PortalModelBase(db, current)
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public decimal Open { get; private set; }

    public async Task OnGetAsync()
    {
        PortalHead("Rechnungen & Angebote", "Alles, was dir dein Lektorat geschickt hat.", ("Meine Projekte", "/Portal"), ("Rechnungen & Angebote", null));
        ViewData["ActiveHref"] = "/Portal/Invoices";

        Open = await MyInvoices.Where(i => i.Type == DocType.Invoice && (i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid)).SumAsync(i => (decimal?)(i.GrossTotal - i.PaidTotal)) ?? 0;

        var spec = new ListSpec<Invoice>
        {
            Id = "portal-invoices",
            Action = "/Portal/Invoices",
            Label = "Rechnungen",
            Noun = "Belege",
            SearchPlaceholder = "Nummer oder Betreff ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(i => EF.Functions.ILike(i.Number ?? "", like) || EF.Functions.ILike(i.Subject ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Invoice>("art", "Art", ListBuilder.EnumOptions<DocType>(Labels.Of), (q, v) => q.Where(i => i.Type == Enum.Parse<DocType>(v))),
                new FilterSpec<Invoice>("status", "Status", [new("offen", "Offen"), new("bezahlt", "Bezahlt")],
                    (q, v) => v == "bezahlt" ? q.Where(i => i.Status == DocStatus.Paid) : q.Where(i => i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Invoice>, bool, IOrderedQueryable<Invoice>>)>
            {
                ["date"] = ("Datum", (q, d) => d ? q.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id) : q.OrderBy(i => i.IssueDate).ThenBy(i => i.Id)),
                ["amount"] = ("Betrag", (q, d) => d ? q.OrderByDescending(i => i.GrossTotal) : q.OrderBy(i => i.GrossTotal)),
            },
            DefaultSort = "date",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(MyInvoices.AsNoTracking().Include(i => i.Project), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("portal-invoice-table", "Rechnungen und Angebote", result.Items,
        [
            (new TableColumn("Nummer", Mono: true), i => Ui.NameWithSub(i.Number ?? "", $"/Portal/InvoiceFile?id={i.Id}&inline=true", Labels.Of(i.Type))),
            (new TableColumn("Projekt", HideOnPhone: true), i => Ui.Text(i.Project?.Title)),
            (new TableColumn("Datum", HideOnPhone: true), i => Ui.Text(Fmt.Date(i.IssueDate))),
            (new TableColumn("Fällig", HideOnPhone: true), i => Ui.Text(i.Type == DocType.Offer ? (i.ValidUntil is null ? "" : "gültig bis " + Fmt.Date(i.ValidUntil)) : Fmt.Date(i.DueDate))),
            (new TableColumn("Betrag", Numeric: true), i => Ui.Text(Fmt.Money(i.GrossTotal, i.Currency))),
            (new TableColumn("Status"), i => Labels.Pill(i.Status, i.Type)),
        ],
        i => [new RowAction("download", "PDF herunterladen", $"/Portal/InvoiceFile?id={i.Id}")]);
        Table = Table with { EmptyTitle = Toolbar.Total == 0 ? "Noch nichts da" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Rechnungen und Angebote erscheinen hier, sobald dein Lektorat sie schickt." : "Ändere die Suche oder den Filter.", EmptyIcon = "dollar" };
    }
}
