using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.TaskTemplates;

public sealed class IndexModel(KkDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Aufgabenvorlagen", "Listen mit den üblichen Schritten, die ein neues Projekt bekommt.", ("Projekte", "/Projects"), ("Aufgabenvorlagen", null));
        ViewData["ActiveHref"] = "/TaskTemplates";

        var spec = new ListSpec<TaskTemplate>
        {
            Id = "templates",
            Action = "/TaskTemplates",
            Label = "Aufgabenvorlagen",
            Noun = "Vorlagen",
            Search = (q, text) => q.Where(t => EF.Functions.ILike(t.Name, Like.Contains(text)) || EF.Functions.ILike(t.Description ?? "", Like.Contains(text))),
            Filters = [new FilterSpec<TaskTemplate>("art", "Leistung", ListBuilder.EnumOptions<ServiceType>(Labels.Of), (q, v) => q.Where(t => t.ServiceType == Enum.Parse<ServiceType>(v)))],
            Sorts = new Dictionary<string, (string, Func<IQueryable<TaskTemplate>, bool, IOrderedQueryable<TaskTemplate>>)>
            {
                ["name"] = ("Name", (q, d) => d ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(db.TaskTemplate.AsNoTracking().Include(t => t.Items), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("template-table", "Aufgabenvorlagen", result.Items,
        [
            (new TableColumn("Name"), t => Ui.NameWithSub(t.Name, $"/TaskTemplates/Edit?id={t.Id}", t.Description)),
            (new TableColumn("Leistung", HideOnPhone: true), t => Ui.Text(t.ServiceType is { } s ? Labels.Of(s) : "")),
            (new TableColumn("Aufgaben", Numeric: true), t => Ui.Text(t.Items.Count.ToString())),
            (new TableColumn("Geplant", Numeric: true, HideOnPhone: true), t => Ui.Text(Fmt.Duration(t.Items.Sum(i => i.EstimatedMinutes ?? 0) * 60))),
        ],
        t => [new RowAction("edit", "Bearbeiten", $"/TaskTemplates/Edit?id={t.Id}")])
        with { EmptyTitle = "Noch keine Vorlagen", EmptyText = "Eine Vorlage spart dir bei jedem Projekt das Anlegen der Aufgaben.", EmptyIcon = "template" };
    }
}
