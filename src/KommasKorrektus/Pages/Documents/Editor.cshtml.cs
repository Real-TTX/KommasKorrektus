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

    public sealed record ViewRequest(string? View, bool? Reference, bool? Side);

    public sealed record StyleRequest(string? Category, string? Term, string? Decision, string? Note);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string DataJson { get; private set; } = "{}";

    public int AutosaveSeconds => config.Current.Tracking.AutosaveSeconds;

    public int AutosaveMinChars => config.Current.Tracking.AutosaveMinChars;

    /// <summary>How the version in the editor is called: "Änderung 2", "Datei vom Kunden v2" ...</summary>
    public string VersionTitle { get; private set; } = "";

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
        VersionTitle = (await NameOfAsync(document.Id, version.VersionNo)).Title;
        var views = await ViewPreferences.GetAsync(db, current.UserId, HttpContext.RequestAborted);
        DataJson = JsonSerializer.Serialize(new
        {
            documentId = document.Id,
            view = views.Editor,
            options = new { reference = views.Options.Reference, side = views.Options.Side },
            kind = document.Kind.ToString(),
            versionNo = version.VersionNo,
            versionName = VersionTitle,
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
            name = (await NameOfAsync(document.Id, result.Version.VersionNo)).Title,
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
        var page = await layouts.ForTextAsync(document, paragraphs, editable: true, document.CurrentVersionNo, HttpContext.RequestAborted);
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

    /// <summary>What the version is called in the lists: "Änderung 2" of the round, "Datei vom Kunden v2" ...</summary>
    private async Task<VersionName> NameOfAsync(long documentId, int versionNo)
    {
        var rows = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == documentId).Select(v => new { v.VersionNo, v.Origin, v.Label }).ToListAsync();
        return VersionNames.Of(rows.Select(r => (r.VersionNo, r.Origin, r.Label)))[versionNo];
    }

    /// <summary>
    /// The earlier version beside the text ("Änderungsmodus"): the changes between <c>from</c> and the saved newest version as one page, of which the editor shows the
    /// side that has what was taken away (the markup is the one of the comparison). Without a starting version it is the one before the newest.
    /// </summary>
    public async Task<IActionResult> OnGetReferenceAsync(int from)
    {
        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var to = document.CurrentVersionNo;
        var rows = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == Id).Select(v => new { v.VersionNo, v.Origin, v.Label }).ToListAsync();
        var names = VersionNames.Of(rows.Select(r => (r.VersionNo, r.Origin, r.Label)));
        var options = rows.Where(r => r.VersionNo < to).OrderByDescending(r => r.VersionNo).Select(r => new { no = r.VersionNo, text = names[r.VersionNo].Option(r.VersionNo) }).ToList();
        if (to <= 1)
        {
            return new JsonResult(new { ok = true, empty = true, to, options });
        }

        if (from <= 0 || from >= to)
        {
            from = to - 1;
        }

        var page = await layouts.ForChangesAsync(document, from, to, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true, empty = false, from, to, options, html = page.Html, link = page.CssUrl, pageClass = page.Layout.PageClass, formatted = page.Formatted });
    }

    /// <summary>The person changed the view of the text: it is kept with the account, so that the next browser opens the same way.</summary>
    public async Task<IActionResult> OnPostViewAsync([FromBody] ViewRequest request)
    {
        var saved = await ViewPreferences.SetEditorAsync(db, current.UserId, request.View, HttpContext.RequestAborted);
        saved |= await ViewPreferences.SetOptionsAsync(db, current.UserId, request.Reference, request.Side, HttpContext.RequestAborted);
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

    /// <summary>An entry for the style sheet of the project, made while writing: the word that is marked, and what is decided about it.</summary>
    public async Task<IActionResult> OnPostStyleAsync([FromBody] StyleRequest request)
    {
        var term = request.Term?.Trim() ?? string.Empty;
        if (term.Length == 0 || term.Length > 200)
        {
            return BadRequest(new { ok = false, message = "Bitte gib den Eintrag ein (bis 200 Zeichen)." });
        }

        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var category = Enum.TryParse<StyleCategory>(request.Category, out var parsed) ? parsed : StyleCategory.Spelling;
        var decision = string.IsNullOrWhiteSpace(request.Decision) ? null : request.Decision.Trim();
        var existing = await db.StyleEntry.FirstOrDefaultAsync(s => s.ProjectId == document.ProjectId && s.Term.ToLower() == term.ToLower());
        var created = existing is null;
        var entry = existing ?? new StyleEntry { ProjectId = document.ProjectId, Term = term };
        entry.Category = category;
        entry.Decision = decision ?? entry.Decision;
        entry.Note = string.IsNullOrWhiteSpace(request.Note) ? entry.Note : request.Note.Trim();
        if (created)
        {
            db.StyleEntry.Add(entry);
        }

        await db.SaveChangesAsync();
        await log.LogAsync(created ? "Stilblatt-Eintrag angelegt" : "Stilblatt-Eintrag geändert", "StyleEntry", entry.Id, term);
        return new JsonResult(new { ok = true, created, term = entry.Term, category = UiKit.Labels.Of(entry.Category), decision = entry.Decision });
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
