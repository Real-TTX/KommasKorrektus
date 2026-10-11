using KommaKorrektus.Core.Writing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Documents;

/// <summary>One version of a document to read, without the editor.</summary>
public sealed class VersionModel(KkDbContext db, DocumentService documents) : PageModelBase
{
    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string[] Paragraphs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, int no)
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == id);
        var version = document is null ? null : await documents.GetVersionAsync(id, no, HttpContext.RequestAborted);
        if (document is null || version is null)
        {
            return NotFound();
        }

        Document = document;
        Version = version;
        Paragraphs = DocumentService.ParagraphsOf(version);
        Head($"{document.Title}, Fassung {no}", $"{Fmt.Moment(version.CreateDate)} · {Fmt.Integer(version.WordCount)} Wörter", ("Projekte", "/Projects"), (document.Project!.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={id}"), ($"Fassung {no}", null));
        ViewData["ActiveHref"] = "/Projects";
        return Page();
    }

    public static string Html(string paragraph) => System.Net.WebUtility.HtmlEncode(paragraph).Replace(TextMetrics.LineBreak.ToString(), "<br>");
}
