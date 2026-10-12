using KommasKorrektus.Core.Billing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Templates;

public sealed class IndexModel(KkDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Vorlagen", "Das Aussehen von Angeboten und Rechnungen: Logo, Farbe, Schrift, Spalten und Fußzeile.", ("Rechnungen", "/Invoices"), ("Vorlagen", null));
        ViewData["ActiveHref"] = "/Templates";

        var spec = new ListSpec<InvoiceTemplate>
        {
            Id = "templates",
            Action = "/Templates",
            Label = "Vorlagen",
            Noun = "Vorlagen",
            Search = (q, text) => q.Where(t => EF.Functions.ILike(t.Name, Like.Contains(text))),
            Sorts = new Dictionary<string, (string, Func<IQueryable<InvoiceTemplate>, bool, IOrderedQueryable<InvoiceTemplate>>)>
            {
                ["name"] = ("Name", (q, d) => d ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(db.InvoiceTemplate.AsNoTracking(), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("template-table", "Vorlagen", result.Items,
        [
            (new TableColumn("Name"), t => Ui.NameWithSub(t.Name, $"/Templates/Edit?id={t.Id}", InvoiceTemplateSettings.Parse(t.SettingsJson) is var s ? $"{(s.FontFamily == "Serif" ? "Serifenschrift" : "Serifenlose Schrift")} · Logo {(s.LogoPosition == "Left" ? "links" : "rechts")}" : null)),
            (new TableColumn("Farbe", HideOnPhone: true), t => Ui.Text(InvoiceTemplateSettings.SafeColor(InvoiceTemplateSettings.Parse(t.SettingsJson).AccentColor))),
            (new TableColumn("Logo", HideOnPhone: true), t => Ui.Text(t.LogoFileId is null ? "keins" : "ja")),
            (new TableColumn("Vorgabe"), t => t.IsDefault ? Ui.Pill("Standard", "accent") : Ui.Text("")),
        ],
        t => [new RowAction("edit", "Bearbeiten", $"/Templates/Edit?id={t.Id}")])
        with { EmptyTitle = "Noch keine Vorlagen", EmptyIcon = "template" };
    }
}
