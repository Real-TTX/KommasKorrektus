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

    /// <summary>The sizes of the writing (percent) the editor and the comparison offer; 100 is the size the page and the text were made for.</summary>
    public static readonly int[] Zooms = [70, 80, 90, 100, 110, 125, 150, 175, 200];

    public const int DefaultZoom = 100;

    public static int CleanZoom(int? value) => value is { } z && Zooms.Contains(z) ? z : DefaultZoom;

    /// <summary>
    /// What the editor has open: the earlier version beside the text (and which: "previous" the version before, "round" the beginning of the round, "original", or a number),
    /// the side panel with the comments and the style sheet, and the size of the writing (<see cref="Zooms"/>), which the comparison of two pages uses too.
    /// </summary>
    public sealed record EditorOptions(bool Reference = false, bool Side = false, string ReferenceFrom = "previous", int Zoom = DefaultZoom);

    /// <summary>"previous", "round", "original" or the number of a version; anything else is "previous".</summary>
    public static string CleanReferenceFrom(string? value)
        => value is "previous" or "round" or "original" || (value is not null && value.Length is > 0 and < 7 && value.All(char.IsAsciiDigit)) ? value : "previous";

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
    public static async Task<bool> SetOptionsAsync(KkDbContext db, long? userId, bool? reference, bool? side, string? referenceFrom = null, int? zoom = null, CancellationToken ct = default)
    {
        zoom = zoom is { } size && Zooms.Contains(size) ? size : null;   // what is not one of the sizes is no change
        if (userId is null || (reference is null && side is null && referenceFrom is null && zoom is null))
        {
            return false;
        }

        var current = ParseOptions(await db.User.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EditorOptions).FirstOrDefaultAsync(ct));
        var next = current with { Reference = reference ?? current.Reference, Side = side ?? current.Side, ReferenceFrom = referenceFrom is null ? CleanReferenceFrom(current.ReferenceFrom) : CleanReferenceFrom(referenceFrom), Zoom = zoom is null ? CleanZoom(current.Zoom) : CleanZoom(zoom) };
        var json = JsonSerializer.Serialize(next, JsonOptions);
        await db.User.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EditorOptions, json), ct);
        return true;
    }
}
