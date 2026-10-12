using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.TextBlocks;

public sealed class IndexModel(KkDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Textbausteine", "Fertige Texte für den Anfang und das Ende von Angeboten und Rechnungen und für E-Mails.", ("Rechnungen", "/Invoices"), ("Textbausteine", null));
        ViewData["ActiveHref"] = "/TextBlocks";

        var spec = new ListSpec<TextBlock>
        {
            Id = "textblocks",
            Action = "/TextBlocks",
            Label = "Textbausteine",
            Noun = "Textbausteine",
            Search = (q, text) => q.Where(t => EF.Functions.ILike(t.Name, Like.Contains(text)) || EF.Functions.ILike(t.Text, Like.Contains(text))),
            Filters =
            [
                new FilterSpec<TextBlock>("art", "Art", ListBuilder.EnumOptions<TextBlockKind>(Labels.Of), (q, v) => q.Where(t => t.Kind == Enum.Parse<TextBlockKind>(v))),
                new FilterSpec<TextBlock>("dokument", "Dokument", ListBuilder.EnumOptions<DocType>(Labels.Of), (q, v) => q.Where(t => t.DocType == Enum.Parse<DocType>(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<TextBlock>, bool, IOrderedQueryable<TextBlock>>)>
            {
                ["name"] = ("Name", (q, d) => d ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name)),
                ["kind"] = ("Art", (q, d) => d ? q.OrderByDescending(t => t.Kind) : q.OrderBy(t => t.Kind)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(db.TextBlock.AsNoTracking(), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("textblock-table", "Textbausteine", result.Items,
        [
            (new TableColumn("Name"), t => Ui.NameWithSub(t.Name, $"/TextBlocks/Edit?id={t.Id}", t.Text.Length > 90 ? t.Text[..90].Replace('\n', ' ') + " ..." : t.Text.Replace('\n', ' '))),
            (new TableColumn("Art"), t => Ui.Text(Labels.Of(t.Kind))),
            (new TableColumn("Für", HideOnPhone: true), t => Ui.Text(t.DocType is { } type ? Labels.Of(type) : "alle")),
            (new TableColumn("Vorgabe", HideOnPhone: true), t => t.IsDefault ? Ui.Pill("Standard", "accent") : Ui.Text("")),
        ],
        t => [new RowAction("edit", "Bearbeiten", $"/TextBlocks/Edit?id={t.Id}")])
        with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Textbausteine" : "Nichts gefunden", EmptyIcon = "type" };
    }
}
