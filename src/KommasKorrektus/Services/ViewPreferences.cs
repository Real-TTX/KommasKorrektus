using System.Text.Json;
using KommasKorrektus.Data;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

/// <summary>
/// How a person likes to look at a text: the view of the editor and of a version (the page is the standard, or the text), the mode of a comparison (two pages), and
/// what the editor has open (the earlier version beside the text, the comments). It is kept with the account, so that a new browser or another device opens the way the
/// person left it. Written without touching the audit columns: a view is not an edit of the account.
/// </summary>
public static class ViewPreferences
{
    public static readonly string[] EditorViews = ["page", "text"];

    public static readonly string[] CompareViews = ["pages", "page", "side", "inline"];

    public const string DefaultEditorView = "page";

    public const string DefaultCompareView = "pages";

    /// <summary>What the editor has open: the earlier version beside the text, the side panel with the comments and the style sheet.</summary>
    public sealed record EditorOptions(bool Reference = false, bool Side = false);

    public static async Task<(string Editor, string Compare, EditorOptions Options)> GetAsync(KkDbContext db, long? userId, CancellationToken ct = default)
    {
        var row = userId is null ? null : await db.User.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.EditorView, u.CompareView, u.EditorOptions }).FirstOrDefaultAsync(ct);
        return (
            row is not null && EditorViews.Contains(row.EditorView) ? row.EditorView : DefaultEditorView,
            row is not null && CompareViews.Contains(row.CompareView) ? row.CompareView : DefaultCompareView,
            row is null ? new EditorOptions() : ParseOptions(row.EditorOptions));
    }

    private static EditorOptions ParseOptions(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? new EditorOptions() : JsonSerializer.Deserialize<EditorOptions>(json, JsonOptions) ?? new EditorOptions();
        }
        catch (JsonException)
        {
            return new EditorOptions();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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

    /// <summary>Remembers what the editor has open; what is null stays as it was.</summary>
    public static async Task<bool> SetOptionsAsync(KkDbContext db, long? userId, bool? reference, bool? side, CancellationToken ct = default)
    {
        if (userId is null || (reference is null && side is null))
        {
            return false;
        }

        var current = ParseOptions(await db.User.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EditorOptions).FirstOrDefaultAsync(ct));
        var next = current with { Reference = reference ?? current.Reference, Side = side ?? current.Side };
        var json = JsonSerializer.Serialize(next, JsonOptions);
        await db.User.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EditorOptions, json), ct);
        return true;
    }
}
