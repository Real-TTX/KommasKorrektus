using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Share;

/// <summary>The page behind a share link: the project (its released documents), or straight to the document when the link is for one.</summary>
public sealed class IndexModel(ShareService shares, ICurrentUser current, KkDbContext db) : ShareModelBase(shares, current)
{
    public sealed record DocumentRow(Document Document, int VersionNo, int Words, DateTime Changed);

    private readonly ShareService _shares = shares;

    [BindProperty]
    public string? Password { get; set; }

    public List<DocumentRow> Documents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (await InitAsync())
        {
            await CountViewAsync();
            if (Link.DocumentId is { } documentId)
            {
                return RedirectToPage("/Share/Document", new { token = Token, id = documentId });
            }

            await LoadAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        await InitAsync();
        if (State != ShareState.Active)
        {
            return Page();
        }

        if (!NeedsPassword || await _shares.TryPasswordAsync(Link, Password, HttpContext))
        {
            return RedirectToPage(new { token = Token });
        }

        ModelState.AddModelError(nameof(Password), Locked || await _shares.IsLockedAsync(Link) ? "Zu viele falsche Versuche. Bitte warte einige Minuten." : "Das Passwort stimmt nicht.");
        return Page();
    }

    private async Task LoadAsync()
    {
        var documents = await _shares.DocumentsAsync(Link, HttpContext.RequestAborted);
        var rows = new List<DocumentRow>();
        foreach (var d in documents)
        {
            var versionNo = ShareService.VersionFor(Link, d);
            var version = await db.DocumentVersion.AsNoTracking().Where(v => v.DocumentId == d.Id && v.VersionNo == versionNo).Select(v => new { v.WordCount, v.CreateDate }).FirstOrDefaultAsync();
            rows.Add(new DocumentRow(d, versionNo, version?.WordCount ?? 0, version?.CreateDate ?? d.UpdateDate));
        }

        Documents = rows;
    }
}
