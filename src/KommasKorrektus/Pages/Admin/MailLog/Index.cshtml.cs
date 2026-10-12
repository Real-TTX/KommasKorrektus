using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Admin.MailLog;

/// <summary>Every mail the application tried to send. Without a mail server the text is kept here, so an invitation or a code can still be handed on.</summary>
public sealed class IndexModel(KkDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public static string Of(MailKind k) => k switch
    {
        MailKind.LoginCode => "Anmeldecode",
        MailKind.Invitation => "Einladung",
        MailKind.ShareLink => "Freigabe-Link",
        MailKind.Document => "Rechnung / Angebot",
        MailKind.Test => "Test",
        _ => "Sonstiges",
    };

    public static Microsoft.AspNetCore.Html.IHtmlContent Pill(MailStatus s) => s switch
    {
        MailStatus.Sent => Ui.Pill("gesendet", "ok"),
        MailStatus.Failed => Ui.Pill("fehlgeschlagen", "error"),
        _ => Ui.Pill("nicht gesendet", "warn"),
    };

    public async Task OnGetAsync()
    {
        Head("E-Mail-Protokoll", "Gesendete und nicht gesendete Nachrichten.", ("Verwaltung", null), ("E-Mail-Protokoll", null));

        var spec = new ListSpec<Data.Entities.MailLog>
        {
            Id = "maillog",
            Action = "/Admin/MailLog",
            Label = "E-Mails",
            Noun = "Nachrichten",
            SearchPlaceholder = "Empfänger oder Betreff ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(m => EF.Functions.ILike(m.ToAddress, like) || EF.Functions.ILike(m.Subject, like));
            },
            Filters =
            [
                new FilterSpec<Data.Entities.MailLog>("status", "Status", ListBuilder.EnumOptions<MailStatus>(s => s switch { MailStatus.Sent => "Gesendet", MailStatus.Failed => "Fehlgeschlagen", _ => "Nicht gesendet" }), (q, v) => q.Where(m => m.Status == Enum.Parse<MailStatus>(v))),
                new FilterSpec<Data.Entities.MailLog>("art", "Art", ListBuilder.EnumOptions<MailKind>(Of), (q, v) => q.Where(m => m.Kind == Enum.Parse<MailKind>(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Data.Entities.MailLog>, bool, IOrderedQueryable<Data.Entities.MailLog>>)>
            {
                ["date"] = ("Zeit", (q, d) => d ? q.OrderByDescending(m => m.SentDate).ThenByDescending(m => m.Id) : q.OrderBy(m => m.SentDate).ThenBy(m => m.Id)),
                ["to"] = ("Empfänger", (q, d) => d ? q.OrderByDescending(m => m.ToAddress) : q.OrderBy(m => m.ToAddress)),
            },
            DefaultSort = "date",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(db.MailLog.AsNoTracking(), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("maillog-table", "E-Mail-Protokoll", result.Items,
        [
            (new TableColumn("Wann"), m => Ui.Text(Fmt.Moment(m.SentDate))),
            (new TableColumn("An", HideOnPhone: true), m => Ui.Text(m.ToAddress)),
            (new TableColumn("Betreff"), m => Ui.NameWithSub(m.Subject, $"/Admin/MailLog/Details?id={m.Id}", Of(m.Kind) + (m.Status == MailStatus.Failed && m.Error is not null ? " · " + m.Error : ""))),
            (new TableColumn("Status"), m => Pill(m.Status)),
        ],
        m => [new RowAction("eye", "Öffnen", $"/Admin/MailLog/Details?id={m.Id}")]);
        Table = Table with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine E-Mails" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Einladungen, Codes und Links erscheinen hier." : "Ändere die Suche oder den Filter.", EmptyIcon = "mail" };
    }
}
