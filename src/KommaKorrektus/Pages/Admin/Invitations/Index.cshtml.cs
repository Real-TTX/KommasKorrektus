using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Admin.Invitations;

/// <summary>All invitations: who was invited, as what, and whether the link was used, has run out or was taken back.</summary>
public sealed class IndexModel(KkDbContext db, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head("Einladungen", "Links, mit denen sich neue Benutzer und Kunden registrieren.", ("Verwaltung", null), ("Einladungen", null));
        var now = clock.GetUtcNow().UtcDateTime;

        var spec = new ListSpec<Invitation>
        {
            Id = "invitations",
            Action = "/Admin/Invitations",
            Label = "Einladungen",
            Noun = "Einladungen",
            SearchPlaceholder = "E-Mail-Adresse oder Name ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(i => EF.Functions.ILike(i.Email, like) || EF.Functions.ILike(i.DisplayName ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Invitation>("status", "Status", [new("offen", "Offen"), new("angenommen", "Angenommen"), new("abgelaufen", "Abgelaufen"), new("zurueckgezogen", "Zurückgezogen")],
                    (q, v) => v switch
                    {
                        "offen" => q.Where(i => i.AcceptedDate == null && i.RevokedDate == null && i.ExpiresDate > now),
                        "angenommen" => q.Where(i => i.AcceptedDate != null),
                        "abgelaufen" => q.Where(i => i.AcceptedDate == null && i.RevokedDate == null && i.ExpiresDate <= now),
                        _ => q.Where(i => i.RevokedDate != null && i.AcceptedDate == null),
                    }),
                new FilterSpec<Invitation>("rolle", "Rolle", ListBuilder.EnumOptions<UserRole>(Labels.Of), (q, v) => q.Where(i => i.Role == Enum.Parse<UserRole>(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Invitation>, bool, IOrderedQueryable<Invitation>>)>
            {
                ["created"] = ("Angelegt", (q, d) => d ? q.OrderByDescending(i => i.CreateDate) : q.OrderBy(i => i.CreateDate)),
                ["email"] = ("E-Mail", (q, d) => d ? q.OrderByDescending(i => i.Email) : q.OrderBy(i => i.Email)),
                ["expires"] = ("Gültig bis", (q, d) => d ? q.OrderByDescending(i => i.ExpiresDate) : q.OrderBy(i => i.ExpiresDate)),
            },
            DefaultSort = "created",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(db.Invitation.AsNoTracking().Include(i => i.Customer), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("invitation-table", "Einladungen", result.Items,
        [
            (new TableColumn("E-Mail"), i => Ui.NameWithSub(i.Email, $"/Admin/Invitations/Edit?id={i.Id}", i.DisplayName)),
            (new TableColumn("Rolle", HideOnPhone: true), i => Ui.Text(Labels.Of(i.Role) + (i.Customer is null ? "" : ": " + i.Customer.DisplayName))),
            (new TableColumn("Gültig bis", HideOnPhone: true), i => Ui.Text(Fmt.Date(i.ExpiresDate))),
            (new TableColumn("Status"), i => StatusPill(i, now)),
        ],
        i => [new RowAction("eye", "Öffnen", $"/Admin/Invitations/Edit?id={i.Id}")]);
        Table = Table with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Einladungen" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Lade jemanden ein, dann erscheint die Einladung hier." : "Ändere die Suche oder den Filter.", EmptyIcon = "mail" };
    }

    public static Microsoft.AspNetCore.Html.IHtmlContent StatusPill(Invitation i, DateTime now)
        => i.AcceptedDate is not null ? Ui.Pill("angenommen", "ok")
            : i.RevokedDate is not null ? Ui.Pill("zurückgezogen", "plain")
            : i.ExpiresDate <= now ? Ui.Pill("abgelaufen", "warn")
            : Ui.Pill("offen", "info");
}
