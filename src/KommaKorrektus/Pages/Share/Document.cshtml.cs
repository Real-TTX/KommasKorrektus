using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Core.Diff;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Share;

/// <summary>One document behind a share link: the text, the changes (old and new, or in one text), remarks and the approval, as far as the link allows.</summary>
public sealed class DocumentModel(ShareService shares, ICurrentUser current, KkDbContext db, DocumentService documents, TimeProvider clock) : ShareModelBase(shares, current)
{
    private const int MaxComments = 200;

    private const int PageSize = 80;

    public sealed class CommentInput
    {
        [Display(Name = "Dein Name")]
        [Required(ErrorMessage = "Bitte sag, von wem die Anmerkung ist.")]
        [StringLength(100)]
        public string Name { get; set; } = "";

        [Display(Name = "Absatz")]
        [Range(1, 100000, ErrorMessage = "Die Nummer des Absatzes stimmt nicht.")]
        public int? Paragraph { get; set; }

        [Display(Name = "Anmerkung")]
        [Required(ErrorMessage = "Bitte schreibe eine Anmerkung.")]
        [StringLength(2000, ErrorMessage = "Die Anmerkung ist zu lang (höchstens 2000 Zeichen).")]
        public string Text { get; set; } = "";
    }

    public sealed class ApprovalInput
    {
        [Display(Name = "Dein Name")]
        [Required(ErrorMessage = "Bitte sag, wer freigibt.")]
        [StringLength(100)]
        public string Name { get; set; } = "";
    }

    private readonly ShareService _shares = shares;

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    /// <summary>"text" (clean), "changes" (one text with the changes) or "side" (old and new next to each other).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }

    public CommentInput Input { get; set; } = new();

    public ApprovalInput Approval { get; set; } = new();

    public Document Document { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string[] Paragraphs { get; private set; } = [];

    public IReadOnlyList<ParagraphDiff> Diff { get; private set; } = [];

    public DiffStats Stats { get; private set; } = DiffStats.None;

    /// <summary>The lines of the changes view on this page (a paragraph, or unchanged paragraphs folded away).</summary>
    public List<DiffItem> Items { get; private set; } = [];

    /// <summary>The paragraphs of the clean text on this page, and the number of the first one.</summary>
    public string[] PageParagraphs { get; private set; } = [];

    public int FirstParagraph { get; private set; }

    public PagerModel Pager { get; private set; } = null!;

    public List<DocumentComment> Comments { get; private set; } = [];

    /// <summary>The version the changes are counted from: the original (1). A document that has no second version has no changes to show.</summary>
    public int BaseVersionNo => 1;

    public bool HasChanges => Link.ShowChanges && Version.VersionNo > BaseVersionNo;

    public string ViewMode => !HasChanges ? "text" : Mode is "side" or "text" ? Mode : "changes";

    public bool CanComment => Link.AllowComments && Comments.Count < MaxComments;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await InitAsync())
        {
            return Page();
        }

        if (!await LoadAsync())
        {
            return NotFound();
        }

        await CountViewAsync();
        Input.Name = Link.RecipientName ?? string.Empty;
        Approval.Name = Link.RecipientName ?? string.Empty;
        return Page();
    }

    // The two forms are bound by the handler that needs them (not as page properties): the form that was not sent must not fail validation.
    public async Task<IActionResult> OnPostCommentAsync()
    {
        if (!await InitAsync())
        {
            return Page();
        }

        if (!await LoadAsync() || !Link.AllowComments)
        {
            return NotFound();
        }

        await TryUpdateModelAsync(Input, nameof(Input));
        if (Comments.Count >= MaxComments)
        {
            ModelState.AddModelError("Input.Text", "Zu diesem Link gibt es schon sehr viele Anmerkungen.");
        }

        var paragraphs = Paragraphs;
        if (Input.Paragraph is { } number && number > paragraphs.Length)
        {
            ModelState.AddModelError("Input.Paragraph", $"Der Text hat nur {paragraphs.Length} Absätze.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var index = Input.Paragraph is { } n ? n - 1 : 0;
        var quote = Input.Paragraph is null ? null : Excerpt(paragraphs[index]);
        db.DocumentComment.Add(new DocumentComment
        {
            DocumentId = Document.Id,
            VersionNo = Version.VersionNo,
            ParagraphIndex = index,
            Quote = quote,
            Text = Input.Text.Trim(),
            AuthorName = Input.Name.Trim(),
            ShareLinkId = Link.Id,
        });
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        await _shares.LogAsync(Link, ShareAction.Comment, HttpContext, Input.Name.Trim(), HttpContext.RequestAborted);
        Flash("Danke, deine Anmerkung ist angekommen.");
        return RedirectToPage(new { token = Token, id = Id, mode = Mode, handler = (string?)null });
    }

    public async Task<IActionResult> OnPostApproveAsync()
    {
        if (!await InitAsync())
        {
            return Page();
        }

        if (!await LoadAsync() || !Link.AllowApproval)
        {
            return NotFound();
        }

        await TryUpdateModelAsync(Approval, nameof(Approval));
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Approval.Name.Trim();
        var tracked = await db.ShareLink.FirstAsync(l => l.Id == Link.Id, HttpContext.RequestAborted);
        tracked.ApprovedDate = clock.GetUtcNow().UtcDateTime;
        tracked.ApprovedByName = name;
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        await _shares.LogAsync(tracked, ShareAction.Approve, HttpContext, $"{name}, Fassung {Version.VersionNo}", HttpContext.RequestAborted);
        Flash("Danke, die Änderungen sind freigegeben.");
        return RedirectToPage(new { token = Token, id = Id, mode = Mode, handler = (string?)null });
    }

    private async Task<bool> LoadAsync()
    {
        var document = await db.Document.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id && d.ProjectId == Link.ProjectId, HttpContext.RequestAborted);
        if (document is null || (Link.DocumentId is { } only && only != document.Id))
        {
            return false;
        }

        var versionNo = ShareService.VersionFor(Link, document);
        var version = await documents.GetVersionAsync(document.Id, versionNo, HttpContext.RequestAborted);
        if (version is null)
        {
            return false;
        }

        Document = document;
        Version = version;
        Paragraphs = DocumentService.ParagraphsOf(version);
        ViewData["Title"] = $"{document.Title} – {Link.Title}";
        if (HasChanges && ViewMode != "text")
        {
            (Diff, Stats) = await documents.CompareAsync(document, BaseVersionNo, version.VersionNo, HttpContext.RequestAborted);
        }

        SlicePage();

        Comments = await db.DocumentComment.AsNoTracking().Where(c => c.DocumentId == document.Id && c.ShareLinkId == Link.Id).OrderBy(c => c.CreateDate).ToListAsync(HttpContext.RequestAborted);
        return true;
    }

    /// <summary>Cuts the view into pages: a long text is read page by page, and only the paragraphs of the page go into the markup.</summary>
    private void SlicePage()
    {
        var all = ViewMode == "text" ? null : DiffView.Items(Diff, ViewMode == "side");
        var total = all?.Count ?? Paragraphs.Length;
        var page = Math.Min(Math.Max(1, ListQuery.Page(Request)), Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        FirstParagraph = (page - 1) * PageSize;
        Items = all?.Skip(FirstParagraph).Take(PageSize).ToList() ?? [];
        PageParagraphs = all is null ? Paragraphs.Skip(FirstParagraph).Take(PageSize).ToArray() : [];
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = total, Url = n => Url.Page("/Share/Document", new { token = Token, id = Id, mode = ViewMode, page = n })! };
    }

    private static string Excerpt(string paragraph)
    {
        var text = paragraph.Replace(KommaKorrektus.Core.Writing.TextMetrics.LineBreak, ' ').Trim();
        return text.Length <= 120 ? text : text[..120];
    }
}
