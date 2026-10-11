using KommaKorrektus.Core.Diff;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Documents;

/// <summary>Old and new in one view: next to each other, or as one document with the changes in the text.</summary>
public sealed class CompareModel(KkDbContext db, DocumentService documents) : PageModelBase
{
    /// <summary>A line of the view: a paragraph, or a run of unchanged paragraphs folded away.</summary>
    public sealed record Item(ParagraphDiff? Paragraph, IReadOnlyList<ParagraphDiff>? Folded);

    private const int PageSize = 80;

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int From { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "side";

    /// <summary>Show only what changed (with a paragraph of context) and fold the rest away.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Changes { get; set; } = true;

    public Document Document { get; private set; } = null!;

    public List<Item> Items { get; private set; } = [];

    public DiffStats Stats { get; private set; } = DiffStats.None;

    public PagerModel Pager { get; private set; } = null!;

    public List<SelectListItem> VersionOptions { get; private set; } = [];

    public bool Inline => Mode == "inline";

    public async Task<IActionResult> OnGetAsync()
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        Document = document;
        if (To <= 0 || To > document.CurrentVersionNo)
        {
            To = document.CurrentVersionNo;
        }

        if (From <= 0 || From > document.CurrentVersionNo)
        {
            From = Math.Max(1, To - 1);
        }

        Mode = Mode == "inline" ? "inline" : "side";
        var title = $"Vergleich: {document.Title}";
        Head(title, $"Fassung {From} → Fassung {To}", ("Projekte", "/Projects"), (document.Project!.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={document.Id}"), ("Vergleich", null));
        ViewData["ActiveHref"] = "/Projects";
        ViewData["Width"] = "wide";

        VersionOptions = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == Id).OrderBy(v => v.VersionNo)
            .Select(v => new SelectListItem("Fassung " + v.VersionNo + (v.Label != null && v.Label != "" ? " – " + v.Label : ""), v.VersionNo.ToString())).ToListAsync();

        var (diff, stats) = await documents.CompareAsync(document, From, To, HttpContext.RequestAborted);
        Stats = stats;
        var all = Build(diff);

        var page = Math.Max(1, ListQuery.Page(Request));
        var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        page = Math.Min(page, pages);
        Items = all.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = all.Count, Url = p => Url.Page("/Documents/Compare", new { id = Id, from = From, to = To, mode = Mode, changes = Changes, page = p })! };
        return Page();
    }

    private List<Item> Build(IReadOnlyList<ParagraphDiff> diff)
    {
        var items = new List<Item>();
        if (!Changes)
        {
            items.AddRange(diff.Select(p => new Item(p, null)));
            return items;
        }

        var show = new bool[diff.Count];
        for (var i = 0; i < diff.Count; i++)
        {
            if (diff[i].Change != ParagraphChange.Unchanged)
            {
                for (var j = Math.Max(0, i - 1); j <= Math.Min(diff.Count - 1, i + 1); j++)
                {
                    show[j] = true;
                }
            }
        }

        var run = new List<ParagraphDiff>();
        for (var i = 0; i < diff.Count; i++)
        {
            if (show[i])
            {
                if (run.Count > 0)
                {
                    items.Add(new Item(null, [.. run]));
                    run.Clear();
                }

                items.Add(new Item(diff[i], null));
            }
            else
            {
                run.Add(diff[i]);
            }
        }

        if (run.Count > 0)
        {
            items.Add(new Item(null, [.. run]));
        }

        return items;
    }
}
