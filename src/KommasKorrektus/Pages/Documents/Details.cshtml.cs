using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Documents;

public sealed class DetailsModel(KkDbContext db, DocumentService documents, ConfigStore config, ActivityLogger log) : PageModelBase
{
    public sealed record VersionRow(DocumentVersion Version, string? Author);

    public sealed record DayRow(DateOnly Day, int Seconds, int WordsEdited);

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public Document Document { get; private set; } = null!;

    public Project Project { get; private set; } = null!;

    public List<VersionRow> Versions { get; private set; } = [];

    public List<DocumentComment> Comments { get; private set; } = [];

    public List<DayRow> Days { get; private set; } = [];

    public DocumentVersion Current { get; private set; } = null!;

    /// <summary>The version the newest round began with (the original, or the latest file of the customer): the starting point of "what did I change".</summary>
    public int RoundStart { get; private set; } = 1;

    public int TotalSeconds { get; private set; }

    public int TotalWordsEdited { get; private set; }

    public decimal NormPages => config.Current.Company.NormPageCharacters <= 0 ? 0 : (decimal)Current.CharCount / config.Current.Company.NormPageCharacters;

    /// <summary>The names of the versions: "Original", "Änderung 1" ... "Datei vom Kunden v2", "Änderung 1" ... and the round each belongs to.</summary>
    public Dictionary<int, VersionName> Names { get; private set; } = [];

    public List<SelectListItem> VersionOptions => [.. Versions.Select(v => new SelectListItem(Names[v.Version.VersionNo].Option(v.Version.VersionNo), v.Version.VersionNo.ToString()))];

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostRestoreAsync(int version)
    {
        var document = await db.Document.FirstOrDefaultAsync(d => d.Id == Id);
        if (document is null)
        {
            return NotFound();
        }

        var result = await documents.RestoreAsync(document, version);
        await log.LogAsync("Fassung wiederhergestellt", "Document", document.Id, $"Fassung {version} → {result.Version.VersionNo}");
        Flash($"Fassung {version} ist als Fassung {result.Version.VersionNo} wiederhergestellt.");
        return RedirectToPage(new { id = Id });
    }

    private async Task<bool> LoadAsync()
    {
        var document = await db.Document.AsNoTracking().Include(d => d.Project).ThenInclude(p => p!.Customer).FirstOrDefaultAsync(d => d.Id == Id);
        if (document?.Project is null)
        {
            return false;
        }

        Document = document;
        Project = document.Project;
        Head(document.Title, $"{Project.Title} · {Project.Customer?.DisplayName}", ("Projekte", "/Projects"), (Project.Title, $"/Projects/Details?id={Project.Id}&tab=documents"), (document.Title, null));
        ViewData["ActiveHref"] = "/Projects";

        // The texts are large: the list needs only their numbers.
        var rows = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == Id).OrderByDescending(v => v.VersionNo)
            .Select(v => new
            {
                Version = new DocumentVersion
                {
                    Id = v.Id, DocumentId = v.DocumentId, VersionNo = v.VersionNo, Label = v.Label, Origin = v.Origin, ParagraphCount = v.ParagraphCount, WordCount = v.WordCount,
                    CharCount = v.CharCount, SourceFileId = v.SourceFileId, WordsAdded = v.WordsAdded, WordsRemoved = v.WordsRemoved, WordsChanged = v.WordsChanged, Comment = v.Comment, CreateDate = v.CreateDate, CreateUserId = v.CreateUserId,
                },
                Author = db.User.IgnoreQueryFilters().Where(u => u.Id == v.CreateUserId).Select(u => u.DisplayName).FirstOrDefault(),
            }).ToListAsync();
        Versions = [.. rows.Select(r => new VersionRow(r.Version, r.Author))];
        Names = VersionNames.Of(Versions.Select(v => (v.Version.VersionNo, v.Version.Origin, v.Version.Label)));
        Current = Versions.First(v => v.Version.VersionNo == document.CurrentVersionNo).Version;
        RoundStart = await documents.RoundStartAsync(document, document.CurrentVersionNo, HttpContext.RequestAborted);

        Comments = await db.DocumentComment.AsNoTracking().Where(c => c.DocumentId == Id).OrderBy(c => c.IsResolved).ThenBy(c => c.ParagraphIndex).ThenBy(c => c.CreateDate).ToListAsync();

        var activity = await db.WorkActivity.AsNoTracking().Where(a => a.DocumentId == Id).Select(a => new { a.At, a.ActiveSeconds, Edited = a.WordsAdded + a.WordsChanged }).ToListAsync();
        TotalSeconds = activity.Sum(a => a.ActiveSeconds);
        TotalWordsEdited = activity.Sum(a => a.Edited);
        Days = [.. activity.GroupBy(a => DateOnly.FromDateTime(Fmt.Local(a.At))).OrderByDescending(g => g.Key).Take(14).Select(g => new DayRow(g.Key, g.Sum(a => a.ActiveSeconds), g.Sum(a => a.Edited)))];
        return true;
    }
}
