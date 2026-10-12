using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

public sealed class IndexModel(KkDbContext db, TimeProvider clock) : PageModelBase
{
    public sealed class Row
    {
        public required Project Project { get; init; }

        public int TasksDone { get; init; }

        public int TasksTotal { get; init; }

        public int Seconds { get; init; }
    }

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Projekte", "Alle Manuskripte, die gerade bearbeitet werden oder wurden.", ("Projekte", null));
        var today = Fmt.Today(clock);

        var rows = db.Project.AsNoTracking().Select(p => new Row
        {
            Project = p,
            TasksDone = p.Tasks.Count(t => t.State == TaskState.Done),
            TasksTotal = p.Tasks.Count(),
            Seconds = db.TimeEntry.Where(t => t.ProjectId == p.Id).Sum(t => (int?)t.Seconds) ?? 0,
        });
        var customers = await db.Customer.AsNoTracking().Include(c => c.Projects).Where(c => c.Projects.Any()).ToListAsync();

        var spec = new ListSpec<Row>
        {
            Id = "projects",
            Action = "/Projects",
            Label = "Projekte",
            Noun = "Projekte",
            SearchPlaceholder = "Titel, Kunde, Nummer, Genre ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(r => EF.Functions.ILike(r.Project.Title, like) || EF.Functions.ILike(r.Project.Number, like) || EF.Functions.ILike(r.Project.Genre ?? "", like)
                    || EF.Functions.ILike(r.Project.Customer!.Company ?? "", like) || EF.Functions.ILike(r.Project.Customer.LastName ?? "", like)
                    || EF.Functions.ILike(r.Project.Customer.FirstName ?? "", like) || EF.Functions.ILike(r.Project.Customer.PenName ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Row>("status", "Status", ListBuilder.EnumOptions<ProjectStatus>(Labels.Of), (q, v) => q.Where(r => r.Project.Status == Enum.Parse<ProjectStatus>(v))),
                new FilterSpec<Row>("aktiv", "Bearbeitung", [new("laufend", "Laufende Projekte"), new("beendet", "Abgeschlossen oder archiviert")],
                    (q, v) => v == "laufend"
                        ? q.Where(r => r.Project.Status != ProjectStatus.Done && r.Project.Status != ProjectStatus.Archived && r.Project.Status != ProjectStatus.Cancelled)
                        : q.Where(r => r.Project.Status == ProjectStatus.Done || r.Project.Status == ProjectStatus.Archived || r.Project.Status == ProjectStatus.Cancelled)),
                new FilterSpec<Row>("art", "Leistung", ListBuilder.EnumOptions<ServiceType>(Labels.Of), (q, v) => q.Where(r => r.Project.ServiceType == Enum.Parse<ServiceType>(v))),
                new FilterSpec<Row>("kunde", "Kunde", [.. customers.OrderBy(c => c.DisplayName).Select(c => new FilterOption(c.Id.ToString(), c.DisplayName))],
                    (q, v) => q.Where(r => r.Project.CustomerId == long.Parse(v))),
                new FilterSpec<Row>("frist", "Frist", [new("ueberfaellig", "Überfällig"), new("woche", "In 7 Tagen")],
                    (q, v) => v == "ueberfaellig"
                        ? q.Where(r => r.Project.DueDate < today && r.Project.Status != ProjectStatus.Done && r.Project.Status != ProjectStatus.Archived && r.Project.Status != ProjectStatus.Cancelled)
                        : q.Where(r => r.Project.DueDate >= today && r.Project.DueDate <= today.AddDays(7))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>)>
            {
                ["updated"] = ("Zuletzt geändert", (q, d) => d ? q.OrderByDescending(r => r.Project.UpdateDate) : q.OrderBy(r => r.Project.UpdateDate)),
                ["due"] = ("Frist", (q, d) => d ? q.OrderByDescending(r => r.Project.DueDate) : q.OrderBy(r => r.Project.DueDate == null).ThenBy(r => r.Project.DueDate)),
                ["title"] = ("Titel", (q, d) => d ? q.OrderByDescending(r => r.Project.Title) : q.OrderBy(r => r.Project.Title)),
                ["number"] = ("Nummer", (q, d) => d ? q.OrderByDescending(r => r.Project.Number) : q.OrderBy(r => r.Project.Number)),
                ["status"] = ("Status", (q, d) => d ? q.OrderByDescending(r => r.Project.Status) : q.OrderBy(r => r.Project.Status)),
            },
            DefaultSort = "updated",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(rows, Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("project-table", "Projekte", result.Items,
        [
            (new TableColumn("Nummer", Mono: true, HideOnPhone: true), r => Ui.Text(r.Project.Number)),
            (new TableColumn("Projekt"), r => Ui.NameWithSub(r.Project.Title, $"/Projects/Details?id={r.Project.Id}", r.Project.Customer?.DisplayName ?? "")),
            (new TableColumn("Status"), r => Labels.Pill(r.Project.Status)),
            (new TableColumn("Leistung", HideOnPhone: true), r => Ui.Text(Labels.Of(r.Project.ServiceType))),
            (new TableColumn("Frist", HideOnPhone: true), r => DueCell(r.Project, today)),
            (new TableColumn("Aufgaben", Numeric: true, HideOnPhone: true), r => Ui.Text(r.TasksTotal == 0 ? "" : $"{r.TasksDone}/{r.TasksTotal}")),
            (new TableColumn("Zeit", Numeric: true, HideOnPhone: true), r => Ui.Text(r.Seconds == 0 ? "" : Fmt.Duration(r.Seconds))),
        ],
        r => [new RowAction("eye", "Öffnen", $"/Projects/Details?id={r.Project.Id}"), new RowAction("edit", "Bearbeiten", $"/Projects/Edit?id={r.Project.Id}")])
        with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Projekte" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Lege dein erstes Projekt an." : "Ändere die Suche oder den Filter.", EmptyIcon = "folder" };
    }

    private static Microsoft.AspNetCore.Html.IHtmlContent DueCell(Project p, DateOnly today)
    {
        if (p.DueDate is null)
        {
            return Ui.Text("");
        }

        var open = p.Status is not (ProjectStatus.Done or ProjectStatus.Archived or ProjectStatus.Cancelled);
        return open && p.DueDate < today ? Ui.Pill(Fmt.Date(p.DueDate), "error") : Ui.Text(Fmt.Date(p.DueDate));
    }
}
