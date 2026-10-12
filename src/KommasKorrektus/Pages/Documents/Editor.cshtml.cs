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
/// The editor: the text of a document as paragraphs that can be written in. The browser saves it while somebody types and when asked, always into the
/// version being worked on; "close" ends the pass so that the next change begins a new version. It also reports how long it was used. Comments on
/// paragraphs are kept here too.
/// </summary>
public sealed class EditorModel(KkDbContext db, DocumentService documents, LayoutService layouts, TimeTrackingService tracking, ConfigStore config, ICurrentUser current, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed record SaveRequest(string Text, string Kind, string? Label, string? Comment, int ActiveSeconds, int Keystrokes, int BaseVersion, bool Force);

    public sealed record TrackRequest(int ActiveSeconds, int Keystrokes);

    public sealed record CommentRequest(int ParagraphIndex, string? Quote, string Text);

    public sealed record LayoutRequest(string[] Paragraphs);

    public sealed record ViewRequest(string? View);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string DataJson { get; private set; } = "{}";

    public int AutosaveSeconds => config.Current.Tracking.AutosaveSeconds;

    public int AutosaveMinChars => config.Current.Tracking.AutosaveMinChars;

    public int IdleMinutes => config.Current.Tracking.IdleMinutes;

    /// <summary>What the bar says about the version: "working" (saves go into it), "closed" (the next change begins a new one), or nothing (the original, a restored one).</summary>
    public static string StateOf(DocumentVersion version) => version.Origin switch
    {
        VersionOrigin.Working => "working",
        VersionOrigin.Manual => "closed",
        _ => "fixed",
    };

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
        var views = await ViewPreferences.GetAsync(db, current.UserId, HttpContext.RequestAborted);
        DataJson = JsonSerializer.Serialize(new
        {
            documentId = document.Id,
            view = views.Editor,
            kind = document.Kind.ToString(),
            versionNo = version.VersionNo,
            state = StateOf(version),
            paragraphs = DocumentService.ParagraphsOf(version),
            autosaveSeconds = AutosaveSeconds,
            autosaveMinChars = AutosaveMinChars,
            idleMinutes = IdleMinutes,
            // The names are the ones editor.js reads (camelCase): the serializer would keep the names of the properties.
            comments = comments.Select(c => new { id = c.Id, paragraphIndex = c.ParagraphIndex, quote = c.Quote, text = c.Text, author = c.AuthorName ?? "Lektorat", isResolved = c.IsResolved, created = Fmt.Moment(c.CreateDate) }),
            styles = styles.Select(s => new { term = s.Term, category = UiKit.Labels.Of(s.Category), decision = s.Decision }),
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

        // Saving by hand and the autosave both go into the version being worked on; only "close" ends the pass.
        var closing = request.Kind == "close";
        var result = await documents.SaveAsync(document, request.Text ?? string.Empty, closing ? VersionOrigin.Manual : VersionOrigin.Working, request.Label, request.Comment, HttpContext.RequestAborted);
        await tracking.RecordAsync(userId, document, request.ActiveSeconds, request.Keystrokes, result.Delta, HttpContext.RequestAborted);
        if (result.Created)
        {
            await log.LogAsync("Fassung begonnen", "Document", document.Id, $"Fassung {result.Version.VersionNo}");
        }

        if (closing)
        {
            await log.LogAsync("Durchgang abgeschlossen", "Document", document.Id, $"Fassung {result.Version.VersionNo}");
        }

        return new JsonResult(new
        {
            ok = true,
            versionNo = result.Version.VersionNo,
            state = StateOf(result.Version),
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

    /// <summary>The person changed the view of the text: it is kept with the account, so that the next browser opens the same way.</summary>
    public async Task<IActionResult> OnPostViewAsync([FromBody] ViewRequest request)
    {
        var saved = await ViewPreferences.SetEditorAsync(db, current.UserId, request.View, HttpContext.RequestAborted);
        return saved ? new JsonResult(new { ok = true }) : BadRequest();
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
