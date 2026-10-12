using System.Text;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Time;

/// <summary>The working time: a stop watch, the entries (made by the editor, by the stop watch or by hand) with their totals, and what was done per day.</summary>
public sealed class IndexModel(KkDbContext db, ICurrentUser current, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed class Row
    {
        public required TimeEntry Entry { get; init; }

        public required string ProjectTitle { get; init; }

        public required string CustomerName { get; init; }

        public required string UserName { get; init; }

        public string? DocumentTitle { get; init; }
    }

    public sealed record DayRow(DateOnly Day, int Seconds, int Words);

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public TimeEntry? Running { get; private set; }

    public string? RunningProject { get; private set; }

    public List<SelectListItem> ProjectOptions { get; private set; } = [];

    public string CsvUrl => Toolbar.UrlWithout(string.Empty) + (Toolbar.UrlWithout(string.Empty).Contains('?') ? "&" : "?") + "handler=Csv";

    public int TotalSeconds { get; private set; }

    public int OpenSeconds { get; private set; }

    public int Words { get; private set; }

    public int Days { get; private set; }

    public List<DayRow> PerDay { get; private set; } = [];

    public Microsoft.AspNetCore.Html.IHtmlContent HoursChart { get; private set; } = Microsoft.AspNetCore.Html.HtmlString.Empty;

    public Microsoft.AspNetCore.Html.IHtmlContent WordsChart { get; private set; } = Microsoft.AspNetCore.Html.HtmlString.Empty;

    [BindProperty]
    public long ProjectId { get; set; }

    [BindProperty]
    public string? Note { get; set; }

    public async Task OnGetAsync() => await BuildAsync();

    public async Task<IActionResult> OnPostStartAsync()
    {
        var userId = current.UserId!.Value;
        if (await db.TimeEntry.AnyAsync(t => t.UserId == userId && t.IsRunning))
        {
            FlashError("Es läuft schon eine Stoppuhr. Beende sie zuerst.");
            return RedirectToPage();
        }

        if (!await db.Project.AnyAsync(p => p.Id == ProjectId))
        {
            FlashError("Bitte wähle ein Projekt.");
            return RedirectToPage();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        db.TimeEntry.Add(new TimeEntry
        {
            ProjectId = ProjectId, UserId = userId, StartDate = now, EndDate = now, Kind = TimeEntryKind.Timer, IsRunning = true,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        });
        await db.SaveChangesAsync();
        Flash("Die Stoppuhr läuft.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStopAsync(long id)
    {
        var entry = await db.TimeEntry.FirstOrDefaultAsync(t => t.Id == id && t.UserId == current.UserId && t.IsRunning);
        if (entry is not null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            entry.IsRunning = false;
            entry.EndDate = now;
            entry.Seconds = (int)Math.Max(0, (now - entry.StartDate).TotalSeconds);
            await db.SaveChangesAsync();
            await log.LogAsync("Stoppuhr beendet", "TimeEntry", entry.Id, Fmt.Duration(entry.Seconds));
            Flash($"Die Stoppuhr ist beendet: {Fmt.Duration(entry.Seconds)}.");
        }

        return RedirectToPage();
    }

    /// <summary>The entries of the list as a CSV file (with the filters of the page).</summary>
    public async Task<IActionResult> OnGetCsvAsync()
    {
        var (_, filtered) = await BuildAsync();
        var rows = await filtered.OrderByDescending(r => r.Entry.StartDate).ToListAsync();
        var sb = new StringBuilder("Datum;Beginn;Ende;Dauer (h);Projekt;Kunde;Dokument;Notiz;Art;Wer;Abgerechnet\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(';', Csv(Fmt.Date(r.Entry.StartDate)), Csv(Fmt.Time(r.Entry.StartDate)), Csv(Fmt.Time(r.Entry.EndDate)), Csv(Fmt.Hours(r.Entry.Seconds)),
                Csv(r.ProjectTitle), Csv(r.CustomerName), Csv(r.DocumentTitle), Csv(r.Entry.Note), Csv(Labels.Of(r.Entry.Kind)), Csv(r.UserName), Csv(r.Entry.InvoiceLineId is null ? "nein" : "ja"))).Append('\n');
        }

        // Excel opens a CSV with a byte order mark as UTF-8.
        return File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv; charset=utf-8", $"Zeiten {Fmt.Today(clock):yyyy-MM-dd}.csv");
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        // A value that starts like a formula is made harmless: a spreadsheet would run it.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private async Task<(ListResult<Row> Result, IQueryable<Row> Filtered)> BuildAsync()
    {
        Head("Zeiterfassung", "Wann wurde woran gearbeitet, und wie viele Wörter sind dabei korrigiert worden?", ("Projekte", "/Projects"), ("Zeiterfassung", null));
        ViewData["ActiveHref"] = "/Time";
        var userId = current.UserId!.Value;
        var today = Fmt.Today(clock);

        Running = await db.TimeEntry.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.IsRunning);
        if (Running is not null)
        {
            RunningProject = await db.Project.AsNoTracking().Where(p => p.Id == Running.ProjectId).Select(p => p.Title).FirstOrDefaultAsync();
        }

        var open = await db.Project.AsNoTracking().Where(p => p.Status != ProjectStatus.Archived && p.Status != ProjectStatus.Cancelled && p.Status != ProjectStatus.Done).OrderBy(p => p.Title).ToListAsync();
        ProjectOptions = [.. open.Select(p => new SelectListItem(p.Title, p.Id.ToString()))];
        var allProjects = await db.Project.AsNoTracking().Where(p => db.TimeEntry.Any(t => t.ProjectId == p.Id)).OrderBy(p => p.Title).ToListAsync();
        var users = await db.User.AsNoTracking().Where(u => u.Role != UserRole.Client).OrderBy(u => u.DisplayName).ToListAsync();

        static DateTime Utc(DateOnly d) => Fmt.UtcFromLocal(d.ToDateTime(TimeOnly.MinValue));
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var todayUtc = Utc(today);
        var weekStartUtc = Utc(weekStart);
        var monthStartUtc = Utc(monthStart);
        var lastMonthStartUtc = Utc(lastMonthStart);
        var thirtyDaysUtc = Utc(today.AddDays(-30));
        var fromUtc = Utc(today.AddDays(-13));

        var rows = db.TimeEntry.AsNoTracking().Where(t => !t.IsRunning).Select(t => new Row
        {
            Entry = t,
            ProjectTitle = t.Project!.Title,
            CustomerName = t.Project.Customer!.Company ?? t.Project.Customer.LastName ?? "",
            UserName = t.User!.DisplayName,
            DocumentTitle = db.Document.Where(d => d.Id == t.DocumentId).Select(d => d.Title).FirstOrDefault(),
        });

        var spec = new ListSpec<Row>
        {
            Id = "times",
            Action = "/Time",
            Label = "Zeiten",
            Noun = "Einträge",
            SearchPlaceholder = "Projekt, Kunde, Notiz ...",
            Search = (q, text) =>
            {
                var like = Like.Contains(text);
                return q.Where(r => EF.Functions.ILike(r.ProjectTitle, like) || EF.Functions.ILike(r.CustomerName, like) || EF.Functions.ILike(r.Entry.Note ?? "", like) || EF.Functions.ILike(r.DocumentTitle ?? "", like));
            },
            Filters =
            [
                new FilterSpec<Row>("zeitraum", "Zeitraum", [new("heute", "Heute"), new("woche", "Diese Woche"), new("monat", "Dieser Monat"), new("letzter", "Letzter Monat"), new("30", "Letzte 30 Tage")],
                    (q, v) => v switch
                    {
                        "heute" => q.Where(r => r.Entry.StartDate >= todayUtc),
                        "woche" => q.Where(r => r.Entry.StartDate >= weekStartUtc),
                        "monat" => q.Where(r => r.Entry.StartDate >= monthStartUtc),
                        "letzter" => q.Where(r => r.Entry.StartDate >= lastMonthStartUtc && r.Entry.StartDate < monthStartUtc),
                        _ => q.Where(r => r.Entry.StartDate >= thirtyDaysUtc),
                    }),
                new FilterSpec<Row>("projekt", "Projekt", [.. allProjects.Select(p => new FilterOption(p.Id.ToString(), p.Title))], (q, v) => q.Where(r => r.Entry.ProjectId == long.Parse(v))),
                new FilterSpec<Row>("wer", "Wer", [.. users.Select(u => new FilterOption(u.Id.ToString(), u.DisplayName))], (q, v) => q.Where(r => r.Entry.UserId == long.Parse(v))),
                new FilterSpec<Row>("art", "Art", ListBuilder.EnumOptions<TimeEntryKind>(Labels.Of), (q, v) => q.Where(r => r.Entry.Kind == Enum.Parse<TimeEntryKind>(v))),
                new FilterSpec<Row>("abrechnung", "Abrechnung", [new("offen", "Noch nicht abgerechnet"), new("abgerechnet", "Abgerechnet")],
                    (q, v) => v == "offen" ? q.Where(r => r.Entry.InvoiceLineId == null && r.Entry.IsBillable) : q.Where(r => r.Entry.InvoiceLineId != null)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>)>
            {
                ["date"] = ("Datum", (q, d) => d ? q.OrderByDescending(r => r.Entry.StartDate) : q.OrderBy(r => r.Entry.StartDate)),
                ["duration"] = ("Dauer", (q, d) => d ? q.OrderByDescending(r => r.Entry.Seconds) : q.OrderBy(r => r.Entry.Seconds)),
                ["project"] = ("Projekt", (q, d) => d ? q.OrderByDescending(r => r.ProjectTitle) : q.OrderBy(r => r.ProjectTitle)),
            },
            DefaultSort = "date",
            DefaultDescending = true,
        };

        var result = await ListBuilder.RunAsync(rows, Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("time-table", "Arbeitszeiten", result.Items,
        [
            (new TableColumn("Datum"), r => Ui.NameWithSub(Fmt.Date(r.Entry.StartDate), $"/Time/Edit?id={r.Entry.Id}", $"{Fmt.Time(r.Entry.StartDate)} – {Fmt.Time(r.Entry.EndDate)}")),
            (new TableColumn("Dauer", Numeric: true), r => Ui.Text(Fmt.Duration(r.Entry.Seconds))),
            (new TableColumn("Projekt"), r => Ui.NameWithSub(r.ProjectTitle, $"/Projects/Details?id={r.Entry.ProjectId}&tab=time", r.CustomerName)),
            (new TableColumn("Dokument / Notiz", HideOnPhone: true), r => Ui.Text(r.DocumentTitle ?? r.Entry.Note)),
            (new TableColumn("Art", HideOnPhone: true), r => Ui.Text(Labels.Of(r.Entry.Kind))),
            (new TableColumn("Wer", HideOnPhone: true), r => Ui.Text(r.UserName)),
            (new TableColumn("Status"), r => r.Entry.InvoiceLineId is not null ? Ui.Pill("abgerechnet", "ok") : r.Entry.IsBillable ? Ui.Text("") : Ui.Pill("nicht abrechenbar", "plain")),
        ],
        r => [new RowAction("edit", "Bearbeiten", $"/Time/Edit?id={r.Entry.Id}")])
        with { EmptyTitle = Toolbar.Total == 0 ? "Noch keine Zeiten" : "Nichts gefunden", EmptyText = Toolbar.Total == 0 ? "Die Zeit läuft im Editor automatisch mit. Du kannst auch die Stoppuhr starten oder eine Zeit eintragen." : "Ändere die Suche oder den Filter.", EmptyIcon = "clock" };

        var filtered = result.Filtered;
        TotalSeconds = await filtered.SumAsync(r => (int?)r.Entry.Seconds) ?? 0;
        OpenSeconds = await filtered.Where(r => r.Entry.InvoiceLineId == null && r.Entry.IsBillable).SumAsync(r => (int?)r.Entry.Seconds) ?? 0;

        // The days: the last two weeks of what is listed.
        var from = today.AddDays(-13);
        var recent = await filtered.Where(r => r.Entry.StartDate >= fromUtc).Select(r => new { r.Entry.StartDate, r.Entry.Seconds, r.Entry.ProjectId, r.Entry.UserId }).ToListAsync();
        var projectIds = recent.Select(r => r.ProjectId).Distinct().ToList();
        var activity = await db.WorkActivity.AsNoTracking().Where(a => a.At >= fromUtc && projectIds.Contains(a.ProjectId))
            .Select(a => new { a.At, Edited = a.WordsAdded + a.WordsChanged }).ToListAsync();
        Words = activity.Sum(a => a.Edited);
        Days = recent.Select(r => DateOnly.FromDateTime(Fmt.Local(r.StartDate))).Distinct().Count();
        PerDay = [.. Enumerable.Range(0, 14).Select(i => from.AddDays(i)).Select(day => new DayRow(day,
            recent.Where(r => DateOnly.FromDateTime(Fmt.Local(r.StartDate)) == day).Sum(r => r.Seconds),
            activity.Where(a => DateOnly.FromDateTime(Fmt.Local(a.At)) == day).Sum(a => a.Edited)))];
        HoursChart = BarChart.Render("chart-hours", "Arbeitszeit pro Tag", [.. PerDay.Select(d => new Bar(d.Day.ToString("dd.MM.", Fmt.Culture), d.Seconds / 3600.0, $"{Fmt.Date(d.Day)}: {Fmt.Duration(d.Seconds)}"))], "Stunden");
        WordsChart = BarChart.Render("chart-words", "Bearbeitete Wörter pro Tag", [.. PerDay.Select(d => new Bar(d.Day.ToString("dd.MM.", Fmt.Culture), d.Words, $"{Fmt.Date(d.Day)}: {Fmt.Integer(d.Words)} Wörter"))], "Wörter");
        return (result, filtered);
    }
}
