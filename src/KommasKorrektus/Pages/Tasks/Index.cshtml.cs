using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Tasks;

/// <summary>The tasks of all projects in one list: what is due, what is overdue, who does what.</summary>
public sealed class IndexModel(KkDbContext db, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Aufgaben", "Alles, was in deinen Projekten zu tun ist.", ("Projekte", "/Projects"), ("Aufgaben", null));
        ViewData["ActiveHref"] = "/Tasks";
        var today = Fmt.Today(clock);
        var users = await db.User.AsNoTracking().Where(u => u.Role != UserRole.Client).OrderBy(u => u.DisplayName).ToListAsync();

        var spec = new ListSpec<ProjectTask>
        {
            Id = "tasks",
            Action = "/Tasks",
            Label = "Aufgaben",
            Noun = "Aufgaben",
            SearchPlaceholder = "Aufgabe oder Projekt ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(t => EF.Functions.ILike(t.Title, like) || EF.Functions.ILike(t.Project!.Title, like));
            },
            Filters =
            [
                new FilterSpec<ProjectTask>("status", "Status", [new("offen", "Offen oder in Arbeit"), .. ListBuilder.EnumOptions<TaskState>(Labels.Of)],
                    (q, v) => v == "offen" ? q.Where(t => t.State != TaskState.Done) : q.Where(t => t.State == Enum.Parse<TaskState>(v))),
                new FilterSpec<ProjectTask>("faellig", "Fällig", [new("ueberfaellig", "Überfällig"), new("heute", "Heute"), new("woche", "In 7 Tagen")],
                    (q, v) => v switch
                    {
                        "ueberfaellig" => q.Where(t => t.State != TaskState.Done && t.DueDate < today),
                        "heute" => q.Where(t => t.State != TaskState.Done && t.DueDate == today),
                        _ => q.Where(t => t.State != TaskState.Done && t.DueDate >= today && t.DueDate <= today.AddDays(7)),
                    }),
                new FilterSpec<ProjectTask>("wer", "Zuständig", [.. users.Select(u => new FilterOption(u.Id.ToString(), u.DisplayName))], (q, v) => q.Where(t => t.AssignedUserId == long.Parse(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<ProjectTask>, bool, IOrderedQueryable<ProjectTask>>)>
            {
                ["due"] = ("Fälligkeit", (q, d) => d ? q.OrderByDescending(t => t.DueDate) : q.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate)),
                ["title"] = ("Aufgabe", (q, d) => d ? q.OrderByDescending(t => t.Title) : q.OrderBy(t => t.Title)),
                ["project"] = ("Projekt", (q, d) => d ? q.OrderByDescending(t => t.Project!.Title) : q.OrderBy(t => t.Project!.Title)),
                ["state"] = ("Status", (q, d) => d ? q.OrderByDescending(t => t.State) : q.OrderBy(t => t.State)),
            },
            DefaultSort = "due",
        };

        var all = db.ProjectTask.AsNoTracking().Include(t => t.Project).Include(t => t.AssignedUser).Where(t => t.Project!.Status != ProjectStatus.Archived);
        var result = await ListBuilder.RunAsync(all, Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("task-table", "Aufgaben", result.Items,
        [
            (new TableColumn("Aufgabe"), t => Ui.NameWithSub(t.Title, $"/Projects/Task?projectId={t.ProjectId}&id={t.Id}", t.Project?.Title ?? "")),
            (new TableColumn("Fällig"), t => t.DueDate is not null && t.State != TaskState.Done && t.DueDate < today ? Ui.Pill(Fmt.Date(t.DueDate), "error") : Ui.Text(Fmt.Date(t.DueDate))),
            (new TableColumn("Status"), t => Labels.Pill(t.State)),
            (new TableColumn("Zuständig", HideOnPhone: true), t => Ui.Text(t.AssignedUser?.DisplayName)),
            (new TableColumn("Geplant", Numeric: true, HideOnPhone: true), t => Ui.Text(t.EstimatedMinutes is { } m ? Fmt.Duration(m * 60) : "")),
        ],
        t => [new RowAction("folder", "Zum Projekt", $"/Projects/Details?id={t.ProjectId}&tab=tasks"), new RowAction("edit", "Bearbeiten", $"/Projects/Task?projectId={t.ProjectId}&id={t.Id}")])
        with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Aufgaben" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Aufgaben legst du in einem Projekt an, am schnellsten aus einer Vorlage." : "Ändere die Suche oder den Filter.", EmptyIcon = "check-square" };
    }
}
