using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Products;

public sealed class IndexModel(KkDbContext db, Configuration.ConfigStore config) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Produkte", "Leistungen und Artikel für Angebote und Rechnungen.", ("Rechnungen", "/Invoices"), ("Produkte", null));
        ViewData["ActiveHref"] = "/Products";
        var small = config.Current.Company.SmallBusiness;
        var categories = await db.Product.AsNoTracking().Where(p => p.Category != null).Select(p => p.Category!).Distinct().OrderBy(c => c).ToListAsync();

        var spec = new ListSpec<Product>
        {
            Id = "products",
            Action = "/Products",
            Label = "Produkte",
            Noun = "Produkte",
            SearchPlaceholder = "Name, Nummer, Beschreibung ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(p => EF.Functions.ILike(p.Name, like) || EF.Functions.ILike(p.Number, like) || EF.Functions.ILike(p.Description ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Product>("kategorie", "Kategorie", [.. categories.Select(c => new FilterOption(c, c))], (q, v) => q.Where(p => p.Category == v)),
                new FilterSpec<Product>("status", "Status", [new("aktiv", "Aktiv"), new("inaktiv", "Nicht mehr angeboten")], (q, v) => v == "aktiv" ? q.Where(p => p.IsActive) : q.Where(p => !p.IsActive)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Product>, bool, IOrderedQueryable<Product>>)>
            {
                ["name"] = ("Name", (q, d) => d ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name)),
                ["number"] = ("Nummer", (q, d) => d ? q.OrderByDescending(p => p.Number) : q.OrderBy(p => p.Number)),
                ["price"] = ("Preis", (q, d) => d ? q.OrderByDescending(p => p.NetPrice) : q.OrderBy(p => p.NetPrice)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(db.Product.AsNoTracking(), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("product-table", "Produkte", result.Items,
        [
            (new TableColumn("Nummer", Mono: true, HideOnPhone: true), p => Ui.Text(p.Number)),
            (new TableColumn("Produkt"), p => Ui.NameWithSub(p.Name, $"/Products/Edit?id={p.Id}", p.Description)),
            (new TableColumn("Einheit", HideOnPhone: true), p => Ui.Text(p.Unit)),
            (new TableColumn("Preis (netto)", Numeric: true), p => Ui.Text(Fmt.Money(p.NetPrice))),
            (new TableColumn("USt.", Numeric: true, HideOnPhone: true), p => Ui.Text(small ? "" : Fmt.Number(p.VatRate, 0) + " %")),
            (new TableColumn("Kategorie", HideOnPhone: true), p => Ui.Text(p.Category)),
            (new TableColumn("Status"), p => p.IsActive ? Ui.Text("") : Ui.Pill("inaktiv", "plain")),
        ],
        p => [new RowAction("edit", "Bearbeiten", $"/Products/Edit?id={p.Id}")])
        with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Produkte" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Lege deine Leistungen an: Sie sind dann in Angeboten und Rechnungen mit einem Klick eingefügt." : "Ändere die Suche oder den Filter.", EmptyIcon = "tag" };
    }
}
