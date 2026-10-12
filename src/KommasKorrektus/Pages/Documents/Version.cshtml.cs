using KommasKorrektus.Core.Writing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>One version of a document to read, without the editor: as text, or as a page in the layout of the original.</summary>
public sealed class VersionModel(KkDbContext db, DocumentService documents, LayoutService layouts, ICurrentUser current) : PageModelBase
{
    private const int PageBlocks = 40;

    /// <summary>"page" for the layout of the original, "text" for the plain text; without one the view the person used last (kept with the account, like the editor's).</summary>
    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string[] Paragraphs { get; private set; } = [];

    public bool PageView => View == "page";

    public PagePayload? Sheet { get; private set; }

    public List<LayoutItem> PageItems { get; private set; } = [];

    public PagerModel Pager { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, int no)
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == id);
        var version = document is null ? null : await documents.GetVersionAsync(id, no, HttpContext.RequestAborted);
        if (document is null || version is null)
        {
            return NotFound();
        }

        if (View is "page" or "text")
        {
            await ViewPreferences.SetEditorAsync(db, current.UserId, View, HttpContext.RequestAborted);
        }
        else
        {
            View = (await ViewPreferences.GetAsync(db, current.UserId, HttpContext.RequestAborted)).Editor;
        }

        Document = document;
        Version = version;
        Paragraphs = DocumentService.ParagraphsOf(version);
        Head($"{document.Title}, Fassung {no}", $"{Fmt.Moment(version.CreateDate)} · {Fmt.Integer(version.WordCount)} Wörter", ("Projekte", "/Projects"), (document.Project!.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={id}"), ($"Fassung {no}", null));
        ViewData["ActiveHref"] = "/Projects";
        ViewData["Width"] = PageView ? "wide" : "narrow";

        if (PageView)
        {
            Sheet = await layouts.ForVersionAsync(document, version, editable: false, HttpContext.RequestAborted);
            var blocks = LayoutView.Items(Sheet.Layout.Blocks, onlyChanges: false);
            var page = Math.Max(1, ListQuery.Page(Request));
            page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(blocks.Count / (double)PageBlocks)));
            PageItems = blocks.Skip((page - 1) * PageBlocks).Take(PageBlocks).ToList();
            Pager = new PagerModel { Page = page, PageSize = PageBlocks, Total = blocks.Count, Url = p => Url.Page("/Documents/Version", new { id, no, view = "page", page = p })! };
        }

        return Page();
    }

    public static string Html(string paragraph) => System.Net.WebUtility.HtmlEncode(paragraph).Replace(TextMetrics.LineBreak.ToString(), "<br>");
}
