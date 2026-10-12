using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Configuration;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Portal;

/// <summary>A document of a client: the text, what was changed (one text, or old and new next to each other), the files to download, remarks to the editor, and (when it is allowed) a file sent back.</summary>
[RequestSizeLimit(DocumentService.MaxUploadBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentService.MaxUploadBytes + 1024 * 1024)]
public sealed class DocumentModel(KkDbContext db, ICurrentUser current, DocumentService documents, LayoutService layouts, ActivityLogger log, ConfigStore config, ProjectService projects) : PortalModelBase(db, current)
{
    private const int PageSize = 80;

    private const int PageBlocks = 40;

    public sealed class CommentInput
    {
        [Display(Name = "Absatz")]
        [Range(1, 100000, ErrorMessage = "Die Nummer des Absatzes stimmt nicht.")]
        public int? Paragraph { get; set; }

        [Display(Name = "Anmerkung")]
        [Required(ErrorMessage = "Bitte schreibe eine Anmerkung.")]
        [StringLength(2000, ErrorMessage = "Die Anmerkung ist zu lang (höchstens 2000 Zeichen).")]
        public string Text { get; set; } = "";
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    /// <summary>"changes" (one text with the changes), "side" (old and new) or "text" (the clean text).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }

    public CommentInput Input { get; set; } = new();

    /// <summary>The file the client sends back, and a word to the editor.</summary>
    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    [BindProperty]
    public string? UploadNote { get; set; }

    /// <summary>Whether the administrator lets clients send a file back (<see cref="SecurityConfig.AllowPortalUploads"/>).</summary>
    public bool UploadsAllowed => config.Current.Security.AllowPortalUploads;

    public Document Document { get; private set; } = null!;

    public Project Project { get; private set; } = null!;

    public DocumentVersion Version { get; private set; } = null!;

    public string[] Paragraphs { get; private set; } = [];

    public IReadOnlyList<ParagraphDiff> Diff { get; private set; } = [];

    public DiffStats Stats { get; private set; } = DiffStats.None;

    public List<DiffItem> Items { get; private set; } = [];

    public string[] PageParagraphs { get; private set; } = [];

    public int FirstParagraph { get; private set; }

    public PagerModel Pager { get; private set; } = null!;

    public List<DocumentComment> Comments { get; private set; } = [];

    public bool HasChanges => Version.VersionNo > 1;

    /// <summary>"page" (the layout of the original, with the changes in it when there are some), "text", "changes" or "side".</summary>
    // The page is the standard; the plain text, the one text with the changes and the two texts next to each other are asked for by name.
    public string ViewMode => Mode is "text" or "changes" or "side" ? (Mode != "text" && !HasChanges ? "text" : Mode) : "page";

    /// <summary>Whether the view is about changes (so it has the numbers and the steps from change to change).</summary>
    public bool ShowsChanges => ViewMode is "changes" or "side" || (ViewMode == "page" && HasChanges);

    public PagePayload? Sheet { get; private set; }

    public List<LayoutItem> PageItems { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostCommentAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        await TryUpdateModelAsync(Input, nameof(Input));
        if (Input.Paragraph is { } number && number > Paragraphs.Length)
        {
            ModelState.AddModelError("Input.Paragraph", $"Der Text hat nur {Paragraphs.Length} Absätze.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var index = Input.Paragraph is { } n ? n - 1 : 0;
        var quote = Input.Paragraph is null ? null : Excerpt(Paragraphs[index]);
        Db.DocumentComment.Add(new DocumentComment
        {
            DocumentId = Document.Id,
            VersionNo = Version.VersionNo,
            ParagraphIndex = index,
            Quote = quote,
            Text = Input.Text.Trim(),
            AuthorUserId = Current.UserId,
            AuthorName = Current.Name,
        });
        await Db.SaveChangesAsync();
        await log.LogAsync("Anmerkung des Kunden", "Document", Document.Id, $"{Current.Name}: {Document.Title}");
        Flash("Danke, deine Anmerkung ist angekommen.");
        return RedirectToPage(new { id = Id, mode = Mode, handler = (string?)null });
    }

    /// <summary>The client sends a file back: it becomes the next version of the document, with its comments, so the editor can compare it with the one before.</summary>
    public async Task<IActionResult> OnPostUploadAsync()
    {
        // not allowed is "not found", as for everything a client may not do
        if (!UploadsAllowed)
        {
            return NotFound();
        }

        var document = await Db.Document.Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id && MyCustomerIds.Contains(d.Project!.CustomerId));
        if (document is null)
        {
            return NotFound();
        }

        if (UploadedFile is null || UploadedFile.Length == 0 || UploadedFile.Length > DocumentService.MaxUploadBytes)
        {
            FlashError(UploadedFile is { Length: > DocumentService.MaxUploadBytes } ? $"Die Datei ist größer als {DocumentService.MaxUploadBytes / 1024 / 1024} MB." : "Bitte wähle eine Datei aus.");
            return RedirectToPage(new { id = Id, mode = Mode, handler = (string?)null });
        }

        byte[] bytes;
        await using (var stream = UploadedFile.OpenReadStream())
        using (var buffer = new MemoryStream((int)UploadedFile.Length))
        {
            await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            bytes = buffer.ToArray();
        }

        try
        {
            var note = string.IsNullOrWhiteSpace(UploadNote) ? null : UploadNote.Trim();
            var result = await documents.AddFileVersionAsync(document, UploadedFile.FileName, bytes, null, note is null ? Current.Name : $"{Current.Name}: {note}", importComments: true, HttpContext.RequestAborted);
            projects.AddHistory(document.Project!.CustomerId, $"Datei von {Current.Name} zu „{document.Title}“", $"Fassung {result.Version.VersionNo}: +{result.Delta.WordsAdded} −{result.Delta.WordsRemoved} Wörter", document.ProjectId, "Document", document.Id);
            await Db.SaveChangesAsync();
            await log.LogAsync("Datei vom Kunden im Portal", "Document", document.Id, $"{Current.Name}: Fassung {result.Version.VersionNo}");
            Flash($"Danke, deine Datei ist angekommen (Fassung {result.Version.VersionNo}). Dein Lektorat sieht sie sofort.");
        }
        catch (ImportException ex)
        {
            FlashError(ex.Message);
        }
        catch (Storage.StorageException)
        {
            FlashError("Die Datei konnte gerade nicht abgelegt werden. Bitte versuche es später noch einmal.");
        }

        return RedirectToPage(new { id = Id, mode = Mode, handler = (string?)null });
    }

    private async Task<bool> LoadAsync()
    {
        var document = await Db.Document.AsNoTracking().Include(d => d.Project).FirstOrDefaultAsync(d => d.Id == Id && MyCustomerIds.Contains(d.Project!.CustomerId));
        if (document is null)
        {
            return false;
        }

        var version = await documents.GetVersionAsync(document.Id, document.CurrentVersionNo, HttpContext.RequestAborted);
        if (version is null)
        {
            return false;
        }

        Document = document;
        Project = document.Project!;
        Version = version;
        Paragraphs = DocumentService.ParagraphsOf(version);
        PortalHead(document.Title, $"Fassung {version.VersionNo} vom {Fmt.Date(version.CreateDate)} · {Fmt.Integer(version.WordCount)} Wörter", ("Meine Projekte", "/Portal"), (Project.Title, $"/Portal/Project?id={Project.Id}"), (document.Title, null));
        ViewData["Width"] = "wide";

        if (HasChanges && ViewMode != "text")
        {
            (Diff, Stats) = await documents.CompareAsync(document, 1, version.VersionNo, HttpContext.RequestAborted);
        }

        if (ViewMode == "page")
        {
            Sheet = HasChanges && Stats.HasChanges
                ? await layouts.ForChangesAsync(document, 1, version.VersionNo, HttpContext.RequestAborted)
                : await layouts.ForVersionAsync(document, version, editable: false, HttpContext.RequestAborted);
            var blocks = LayoutView.Items(Sheet.Layout.Blocks, onlyChanges: false);
            var number = Math.Min(Math.Max(1, ListQuery.Page(Request)), Math.Max(1, (int)Math.Ceiling(blocks.Count / (double)PageBlocks)));
            PageItems = blocks.Skip((number - 1) * PageBlocks).Take(PageBlocks).ToList();
            Pager = new PagerModel { Page = number, PageSize = PageBlocks, Total = blocks.Count, Url = p => Url.Page("/Portal/Document", new { id = Id, mode = ViewMode, page = p })! };
            await LoadCommentsAsync(document);
            return true;
        }

        var all = ViewMode == "text" ? null : DiffView.Items(Diff, ViewMode == "side");
        var total = all?.Count ?? Paragraphs.Length;
        var page = Math.Min(Math.Max(1, ListQuery.Page(Request)), Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        FirstParagraph = (page - 1) * PageSize;
        Items = all?.Skip(FirstParagraph).Take(PageSize).ToList() ?? [];
        PageParagraphs = all is null ? Paragraphs.Skip(FirstParagraph).Take(PageSize).ToArray() : [];
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = total, Url = p => Url.Page("/Portal/Document", new { id = Id, mode = ViewMode, page = p })! };

        await LoadCommentsAsync(document);
        return true;
    }

    private async Task LoadCommentsAsync(Document document)
    {
        var userId = Current.UserId;
        Comments = await Db.DocumentComment.AsNoTracking().Where(c => c.DocumentId == document.Id && c.AuthorUserId == userId).OrderBy(c => c.CreateDate).ToListAsync();
    }

    private static string Excerpt(string paragraph)
    {
        var text = paragraph.Replace(KommasKorrektus.Core.Writing.TextMetrics.LineBreak, ' ').Trim();
        return text.Length <= 120 ? text : text[..120];
    }
}
