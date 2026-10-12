using KommasKorrektus.Core.Diff;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>Old and new in one view: the texts next to each other, as one document with the changes in the text, as one page, or as two pages next to each other.</summary>
public sealed class CompareModel(KkDbContext db, DocumentService documents, LayoutService layouts, ICurrentUser current) : PageModelBase
{
    private const int PageSize = 80;

    private const int PageBlocks = 40;

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

    public List<DiffItem> Items { get; private set; } = [];

    public DiffStats Stats { get; private set; } = DiffStats.None;

    public PagerModel Pager { get; private set; } = null!;

    public List<SelectListItem> VersionOptions { get; private set; } = [];

    public bool Inline => Mode == "inline";

    /// <summary>One document in the layout of the original, with the changes in the text.</summary>
    public bool PageView => Mode == "page";

    /// <summary>The old page and the new page next to each other, each with its own changes marked.</summary>
    public bool PagesView => Mode == "pages";

    public PagePayload? Sheet { get; private set; }

    public List<LayoutItem> PageItems { get; private set; } = [];

    private List<T> Slice<T>(List<T> all, int size, out int page)
    {
        page = Math.Max(1, ListQuery.Page(Request));
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(all.Count / (double)size)));
        return all.Skip((page - 1) * size).Take(size).ToList();
    }

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

        // The way of looking is kept with the account: without a choice in the address the person gets what they chose last, and a choice is remembered.
        if (Request.Query.ContainsKey("mode"))
        {
            Mode = ViewPreferences.CompareViews.Contains(Mode) ? Mode : "side";
            await ViewPreferences.SetCompareAsync(db, current.UserId, Mode, HttpContext.RequestAborted);
        }
        else
        {
            Mode = (await ViewPreferences.GetAsync(db, current.UserId, HttpContext.RequestAborted)).Compare;
        }

        var title = $"Vergleich: {document.Title}";
        Head(title, $"Fassung {From} → Fassung {To}", ("Projekte", "/Projects"), (document.Project!.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={document.Id}"), ("Vergleich", null));
        ViewData["ActiveHref"] = "/Projects";
        ViewData["Width"] = "wide";

        VersionOptions = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == Id).OrderBy(v => v.VersionNo)
            .Select(v => new SelectListItem("Fassung " + v.VersionNo + (v.Label != null && v.Label != "" ? " – " + v.Label : ""), v.VersionNo.ToString())).ToListAsync();

        var (diff, stats) = await documents.CompareAsync(document, From, To, HttpContext.RequestAborted);
        Stats = stats;
        if ((PageView || PagesView) && stats.HasChanges)
        {
            // The page is longer than the paragraphs it holds (pictures, tables): fewer blocks to a page.
            Sheet = await layouts.ForChangesAsync(document, From, To, HttpContext.RequestAborted);
            var blocks = LayoutView.Items(Sheet.Layout.Blocks, Changes);
            PageItems = Slice(blocks, PageBlocks, out var pageNo);
            Pager = new PagerModel { Page = pageNo, PageSize = PageBlocks, Total = blocks.Count, Url = p => Url.Page("/Documents/Compare", new { id = Id, from = From, to = To, mode = Mode, changes = Changes, page = p })! };
            return Page();
        }

        var all = DiffView.Items(diff, Changes);
        Items = Slice(all, PageSize, out var number);
        Pager = new PagerModel { Page = number, PageSize = PageSize, Total = all.Count, Url = p => Url.Page("/Documents/Compare", new { id = Id, from = From, to = To, mode = Mode, changes = Changes, page = p })! };
        return Page();
    }
}
