using System.Text.Json;
using KommasKorrektus.Configuration;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Writing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

/// <summary>
/// The editor: the text of a document as paragraphs that can be written in. The browser saves it as an autosave while somebody types and as a version
/// when asked; it also reports how long it was used. Comments on paragraphs are kept here too.
/// </summary>
public sealed class EditorModel(KkDbContext db, DocumentService documents, LayoutService layouts, TimeTrackingService tracking, ConfigStore config, ICurrentUser current, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed record SaveRequest(string Text, string Kind, string? Label, string? Comment, int ActiveSeconds, int Keystrokes, int BaseVersion, bool Force);

    public sealed record TrackRequest(int ActiveSeconds, int Keystrokes);

    public sealed record CommentRequest(int ParagraphIndex, string? Quote, string Text);

    public sealed record LayoutRequest(string[] Paragraphs);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string DataJson { get; private set; } = "{}";

    public int AutosaveSeconds => config.Current.Tracking.AutosaveSeconds;

    public int IdleMinutes => config.Current.Tracking.IdleMinutes;

    public async Task<IActionResult> OnGetAsync()
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).ThenInclude(p => p!.Customer).FirstOrDefaultAsync(d => d.Id == Id);
        if (document?.Project is null)
        {
            return NotFound();
        }

        Document = document;
        var version = await documents.GetVersionAsync(Id, document.CurrentVersionNo, HttpContext.RequestAborted);
        if (version is null)
        {
            return NotFound();
        }

        Version = version;
        var comments = await db.DocumentComment.AsNoTracking().Where(c => c.DocumentId == Id).OrderBy(c => c.ParagraphIndex).ThenBy(c => c.CreateDate).ToListAsync();
        var styles = await db.StyleEntry.AsNoTracking().Where(s => s.ProjectId == document.ProjectId).OrderBy(s => s.Category).ThenBy(s => s.Term).ToListAsync();
        DataJson = JsonSerializer.Serialize(new
        {
            documentId = document.Id,
            kind = document.Kind.ToString(),
            versionNo = version.VersionNo,
            paragraphs = DocumentService.ParagraphsOf(version),
            autosaveSeconds = AutosaveSeconds,
            idleMinutes = IdleMinutes,
            comments = comments.Select(c => new { c.Id, c.ParagraphIndex, c.Quote, c.Text, author = c.AuthorName ?? "Lektorat", c.IsResolved, created = Fmt.Moment(c.CreateDate) }),
            styles = styles.Select(s => new { s.Term, category = UiKit.Labels.Of(s.Category), s.Decision }),
        });

        Head($"Editor: {document.Title}", null, ("Projekte", "/Projects"), (document.Project.Title, $"/Projects/Details?id={document.ProjectId}&tab=documents"), (document.Title, $"/Documents/Details?id={document.Id}"), ("Editor", null));
        ViewData["ActiveHref"] = "/Projects";
        ViewData["Width"] = "wide";
        ViewData["OwnHead"] = true;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync([FromBody] SaveRequest request)
    {
        var document = await db.Document.FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var userId = current.UserId!.Value;
        var latest = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == Id && v.VersionNo == document.CurrentVersionNo)
            .Select(v => new { v.VersionNo, v.CreateUserId, v.Origin, v.CreateDate }).FirstAsync();

        // Somebody else saved a newer version while this window was open: overwriting it would lose their work.
        if (!request.Force && request.BaseVersion < latest.VersionNo && latest.CreateUserId != userId)
        {
            var other = await db.User.IgnoreQueryFilters().Where(u => u.Id == latest.CreateUserId).Select(u => u.DisplayName).FirstOrDefaultAsync();
            return new JsonResult(new { ok = false, conflict = true, message = $"{other ?? "Jemand anderes"} hat inzwischen Fassung {latest.VersionNo} gespeichert. Lade die Seite neu, oder speichere trotzdem." });
        }

        var kind = request.Kind == "manual" ? VersionOrigin.Manual : VersionOrigin.Autosave;
        var result = await documents.SaveAsync(document, request.Text ?? string.Empty, kind, request.Label, request.Comment, HttpContext.RequestAborted);
        await tracking.RecordAsync(userId, document, request.ActiveSeconds, request.Keystrokes, result.Delta, HttpContext.RequestAborted);
        if (kind == VersionOrigin.Manual && result.Created)
        {
            await log.LogAsync("Fassung gespeichert", "Document", document.Id, $"Fassung {result.Version.VersionNo}");
        }

        return new JsonResult(new
        {
            ok = true,
            versionNo = result.Version.VersionNo,
            created = result.Created,
            changed = result.Delta.HasChanges,
            wordCount = result.Version.WordCount,
            added = result.Delta.WordsAdded,
            removed = result.Delta.WordsRemoved,
            replaced = result.Delta.WordsChanged,
            savedAt = Fmt.Time(clock.GetUtcNow().UtcDateTime),
        });
    }

    /// <summary>
    /// The text of the editor drawn as a page: the formatting of the uploaded Word file around the text as it is now (saved or not). The page can only be written
    /// in when every paragraph of the text has its place on it; a text for which that is not so is refused and stays in the text view.
    /// </summary>
    public async Task<IActionResult> OnPostLayoutAsync([FromBody] LayoutRequest request)
    {
        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var paragraphs = request.Paragraphs is { Length: > 0 } ? request.Paragraphs : [string.Empty];
        var page = await layouts.ForTextAsync(document, paragraphs, editable: true, HttpContext.RequestAborted);
        if (page.Layout.ParagraphCount != paragraphs.Length)
        {
            return new JsonResult(new { ok = false, reason = "Diese Fassung lässt sich nicht als Seite zeigen (eine Tabellenzelle ohne Absatz). Bitte im Textmodus weiterarbeiten." });
        }

        return new JsonResult(new
        {
            ok = true,
            html = page.Html,
            css = page.CssUrl,
            pageClass = page.Layout.PageClass,
            width = page.Layout.PageWidthPx,
            formatted = page.Formatted,
        });
    }

    public async Task<IActionResult> OnPostTrackAsync([FromBody] TrackRequest request)
    {
        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        await tracking.RecordAsync(current.UserId!.Value, document, request.ActiveSeconds, request.Keystrokes, DiffStats.None, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostCommentAsync([FromBody] CommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest();
        }

        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var comment = new DocumentComment
        {
            DocumentId = Id,
            VersionNo = document.CurrentVersionNo,
            ParagraphIndex = Math.Max(0, request.ParagraphIndex),
            Quote = string.IsNullOrWhiteSpace(request.Quote) ? null : request.Quote.Trim()[..Math.Min(request.Quote.Trim().Length, 120)],
            Text = request.Text.Trim(),
            AuthorUserId = current.UserId,
            AuthorName = current.Name,
        };
        db.DocumentComment.Add(comment);
        await db.SaveChangesAsync();
        return new JsonResult(new { ok = true, comment.Id, comment.ParagraphIndex, comment.Quote, comment.Text, author = comment.AuthorName, isResolved = false, created = Fmt.Moment(comment.CreateDate) });
    }

    public async Task<IActionResult> OnPostResolveAsync(long commentId, bool resolved)
    {
        var comment = await db.DocumentComment.FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == Id);
        if (comment is null)
        {
            return NotFound();
        }

        comment.IsResolved = resolved;
        comment.ResolvedDate = resolved ? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync();
        return new JsonResult(new { ok = true });
    }
}
