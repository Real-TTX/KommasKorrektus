using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Releases;

/// <summary>"Mit mir geteilt": the projects and documents that were released to this account (staff and clients alike), each opening the page of its link.</summary>
[Authorize(Policy = "SignedIn")]
public sealed class IndexModel(KkDbContext db, ICurrentUser current, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Mit mir geteilt", "Projekte und Dokumente, die dir freigegeben wurden.", ("Mit mir geteilt", null));
        var me = current.UserId;
        var now = clock.GetUtcNow().UtcDateTime;

        var spec = new ListSpec<ShareLink>
        {
            Id = "releases",
            Action = "/Releases",
            Label = "Freigaben",
            Noun = "Freigaben",
            SearchPlaceholder = "Titel oder Projekt ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(l => EF.Functions.ILike(l.Title, like) || EF.Functions.ILike(l.Project!.Title, like));
            },
            Filters =
            [
                new FilterSpec<ShareLink>("status", "Status", [new("gueltig", "Gültig"), new("beendet", "Abgelaufen oder zurückgezogen")],
                    (q, v) => v == "gueltig" ? q.Where(l => l.RevokedDate == null && l.ExpiresDate > now) : q.Where(l => l.RevokedDate != null || l.ExpiresDate <= now)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<ShareLink>, bool, IOrderedQueryable<ShareLink>>)>
            {
                ["created"] = ("Freigegeben am", (q, d) => d ? q.OrderByDescending(l => l.CreateDate) : q.OrderBy(l => l.CreateDate)),
                ["title"] = ("Titel", (q, d) => d ? q.OrderByDescending(l => l.Title) : q.OrderBy(l => l.Title)),
                ["expires"] = ("Gültig bis", (q, d) => d ? q.OrderByDescending(l => l.ExpiresDate) : q.OrderBy(l => l.ExpiresDate)),
            },
            DefaultSort = "created",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(db.ShareLink.AsNoTracking().Include(l => l.Project).Where(l => l.UserId == me), Request, spec, HttpContext.RequestAborted);
        var senderIds = result.Items.Where(l => l.CreateUserId is not null).Select(l => l.CreateUserId!.Value).Distinct().ToList();
        var senders = await db.User.AsNoTracking().Where(u => senderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("release-table", "Freigaben", result.Items,
        [
            (new TableColumn("Titel"), l => Ui.NameWithSub(l.Title, $"/s/{l.Token}", l.DocumentId is null ? "ganzes Projekt" : "ein Dokument")),
            (new TableColumn("Projekt", HideOnPhone: true), l => Ui.Text(l.Project!.Title)),
            (new TableColumn("Von", HideOnPhone: true), l => Ui.Text(l.CreateUserId is { } id && senders.TryGetValue(id, out var name) ? name : "")),
            (new TableColumn("Gültig bis", HideOnPhone: true), l => Ui.Text(Fmt.Date(l.ExpiresDate))),
            (new TableColumn("Status"), l => l.RevokedDate is not null ? Ui.Pill("zurückgezogen", "plain") : l.ExpiresDate <= now ? Ui.Pill("abgelaufen", "warn") : Ui.Pill("gültig", "ok")),
        ],
        l => l.IsActive(now) ? [new RowAction("eye", "Öffnen", $"/s/{l.Token}")] : []);
        Table = Table with
        {
            EmptyTitle = Toolbar.Total == 0 ? "Dir wurde noch nichts freigegeben" : "Nichts gefunden",
            EmptyText = Toolbar.Total == 0 ? "Wenn dir jemand ein Projekt oder ein Dokument freigibt, erscheint es hier." : "Ändere die Suche oder den Filter.",
            EmptyIcon = "share",
        };
    }
}
