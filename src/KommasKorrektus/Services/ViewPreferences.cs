using KommasKorrektus.Data;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

/// <summary>
/// How a person likes to look at a text: the view of the editor and of a version (text or page), and of a comparison. It is kept with the account, so that a
/// new browser or another device opens the way the person left it. Written without touching the audit columns: a view is not an edit of the account.
/// </summary>
public static class ViewPreferences
{
    public static readonly string[] EditorViews = ["text", "page"];

    public static readonly string[] CompareViews = ["side", "inline", "page", "pages"];

    public static async Task<(string Editor, string Compare)> GetAsync(KkDbContext db, long? userId, CancellationToken ct = default)
    {
        var row = userId is null ? null : await db.User.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.EditorView, u.CompareView }).FirstOrDefaultAsync(ct);
        return (row is not null && EditorViews.Contains(row.EditorView) ? row.EditorView : "text", row is not null && CompareViews.Contains(row.CompareView) ? row.CompareView : "side");
    }

    /// <summary>Remembers the view of the text; anything that is not a view is ignored. Returns whether it was written.</summary>
    public static async Task<bool> SetEditorAsync(KkDbContext db, long? userId, string? view, CancellationToken ct = default)
    {
        if (userId is null || view is null || !EditorViews.Contains(view))
        {
            return false;
        }

        await db.User.Where(u => u.Id == userId && u.EditorView != view).ExecuteUpdateAsync(s => s.SetProperty(u => u.EditorView, view), ct);
        return true;
    }

    public static async Task<bool> SetCompareAsync(KkDbContext db, long? userId, string? mode, CancellationToken ct = default)
    {
        if (userId is null || mode is null || !CompareViews.Contains(mode))
        {
            return false;
        }

        await db.User.Where(u => u.Id == userId && u.CompareView != mode).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompareView, mode), ct);
        return true;
    }
}
