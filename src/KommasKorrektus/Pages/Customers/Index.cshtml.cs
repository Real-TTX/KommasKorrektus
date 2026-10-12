using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Customers;

public sealed class IndexModel(KkDbContext db) : PageModelBase
{
    public sealed class Row
    {
        public required Customer Customer { get; init; }

        public int Projects { get; init; }

        public decimal Open { get; init; }
    }

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Kunden", "Autorinnen, Autoren und Verlage mit ihrer Geschichte.", ("Kunden", null));

        var rows = db.Customer.AsNoTracking().Select(c => new Row
        {
            Customer = c,
            Projects = c.Projects.Count(),
            Open = db.Invoice.Where(i => i.CustomerId == c.Id && i.Type == DocType.Invoice && (i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid))
                .Sum(i => (decimal?)(i.GrossTotal - i.PaidTotal)) ?? 0,
        });

        var spec = new ListSpec<Row>
        {
            Id = "customers",
            Action = "/Customers",
            Label = "Kunden",
            Noun = "Kunden",
            SearchPlaceholder = "Name, Pseudonym, E-Mail, Ort, Nummer ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(r => EF.Functions.ILike(r.Customer.Number, like) || EF.Functions.ILike(r.Customer.Company ?? "", like)
                    || EF.Functions.ILike(r.Customer.FirstName ?? "", like) || EF.Functions.ILike(r.Customer.LastName ?? "", like)
                    || EF.Functions.ILike(r.Customer.PenName ?? "", like) || EF.Functions.ILike(r.Customer.Email ?? "", like)
                    || EF.Functions.ILike(r.Customer.City ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Row>("art", "Art", ListBuilder.EnumOptions<CustomerKind>(Labels.Of), (q, v) => q.Where(r => r.Customer.Kind == Enum.Parse<CustomerKind>(v))),
                new FilterSpec<Row>("status", "Status", [new("aktiv", "Aktiv"), new("archiviert", "Archiviert")],
                    (q, v) => v == "archiviert" ? q.Where(r => r.Customer.IsArchived) : q.Where(r => !r.Customer.IsArchived)),
                new FilterSpec<Row>("offen", "Offene Rechnungen", [new("ja", "Mit offenem Betrag")], (q, _) => q.Where(r => r.Open > 0)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>)>
            {
                ["name"] = ("Name", (q, d) => d
                    ? q.OrderByDescending(r => r.Customer.Kind == CustomerKind.Company ? r.Customer.Company : r.Customer.LastName).ThenByDescending(r => r.Customer.FirstName)
                    : q.OrderBy(r => r.Customer.Kind == CustomerKind.Company ? r.Customer.Company : r.Customer.LastName).ThenBy(r => r.Customer.FirstName)),
                ["number"] = ("Nummer", (q, d) => d ? q.OrderByDescending(r => r.Customer.Number) : q.OrderBy(r => r.Customer.Number)),
                ["city"] = ("Ort", (q, d) => d ? q.OrderByDescending(r => r.Customer.City) : q.OrderBy(r => r.Customer.City)),
                ["projects"] = ("Projekte", (q, d) => d ? q.OrderByDescending(r => r.Projects) : q.OrderBy(r => r.Projects)),
                ["open"] = ("Offener Betrag", (q, d) => d ? q.OrderByDescending(r => r.Open) : q.OrderBy(r => r.Open)),
                ["created"] = ("Angelegt", (q, d) => d ? q.OrderByDescending(r => r.Customer.CreateDate) : q.OrderBy(r => r.Customer.CreateDate)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(rows, Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("customer-table", "Kunden", result.Items,
        [
            (new TableColumn("Nummer", Mono: true, HideOnPhone: true), r => Ui.Text(r.Customer.Number)),
            (new TableColumn("Name"), r => Ui.NameWithSub(r.Customer.DisplayName, $"/Customers/Details?id={r.Customer.Id}",
                string.Join(" · ", new[] { r.Customer.PenName is { Length: > 0 } pen ? $"Pseudonym {pen}" : null, r.Customer.Kind == CustomerKind.Company && r.Customer.PersonName.Length > 0 ? r.Customer.PersonName : null, r.Customer.IsArchived ? "archiviert" : null }.Where(s => s is not null)))),
            (new TableColumn("E-Mail", HideOnPhone: true), r => Ui.Text(r.Customer.Email)),
            (new TableColumn("Ort", HideOnPhone: true), r => Ui.Text(r.Customer.City)),
            (new TableColumn("Projekte", Numeric: true), r => Ui.Text(r.Projects.ToString())),
            (new TableColumn("Offen", Numeric: true, HideOnPhone: true), r => Ui.Text(r.Open > 0 ? Fmt.Money(r.Open) : "")),
        ],
        r => [new RowAction("eye", "Öffnen", $"/Customers/Details?id={r.Customer.Id}"), new RowAction("edit", "Bearbeiten", $"/Customers/Edit?id={r.Customer.Id}")]);
        Table = Table with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Kunden" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Lege deinen ersten Kunden an." : "Ändere die Suche oder den Filter.", EmptyIcon = "users" };
    }
}
