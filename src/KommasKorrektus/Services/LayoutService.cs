using System.Security.Cryptography;
using System.Text;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Documents.Layout;
using KommasKorrektus.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace KommasKorrektus.Services;

/// <summary>A document drawn as a page: the markup of its paragraphs, tables and pictures, and the address of the stylesheet that goes with it.</summary>
public sealed class PagePayload
{
    public required DocxLayoutResult Layout { get; init; }

    /// <summary>The key of the stylesheet (a hash of its content), kept for a day; the browser fetches it from <see cref="CssUrl"/>.</summary>
    public required string CssKey { get; init; }

    public string CssUrl => $"/layout/{CssKey}.css";

    /// <summary>True when the page has the formatting of the uploaded Word file; false when it is a plain page made from the text (a PDF or a text file has no layout to keep).</summary>
    public required bool Formatted { get; init; }

    public string Html => Layout.Html;
}

/// <summary>
/// Draws documents as pages for the editor, the comparison and the pages of authors and clients. The page is made the way the export is made (the uploaded
/// Word file with the new text written into it), so what the page shows is what the Word export contains.
/// </summary>
public sealed class LayoutService(DocumentService documents, IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan PageLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan CssLifetime = TimeSpan.FromHours(24);

    private const string CssKeyPrefix = "kk:layout-css:";

    /// <summary>The name the changes of the page are written under (never shown): it tells the changes between two versions from the revisions of the author's file.</summary>
    private const string ChangesAuthor = "KommasKorrektus-Seite";

    /// <summary>The stylesheet of a page that was drawn lately (see <see cref="PagePayload.CssUrl"/>); the key is a hash of its content.</summary>
    public bool TryCss(string key, out string css)
    {
        if (cache.TryGetValue(CssKeyPrefix + key, out string? found) && found is not null)
        {
            css = found;
            return true;
        }

        css = string.Empty;
        return false;
    }

    /// <summary>
    /// A text (the editor's, not yet saved) drawn on the page of the document, in the formatting of the round the version <paramref name="versionNo"/> belongs to
    /// (the file of the import, or of the latest file of the customer before it).
    /// </summary>
    public async Task<PagePayload> ForTextAsync(Document document, IReadOnlyList<string> paragraphs, bool editable, int versionNo, CancellationToken ct = default)
    {
        var round = await documents.RoundOfAsync(document, versionNo, ct) ?? throw new InvalidOperationException("Die erste Fassung fehlt.");
        var original = await FormattedOriginalAsync(document, round, ct);
        var docx = DocxWriter.Write(original, round.Paragraphs, paragraphs, RevisionMode.Accepted, "KommasKorrektus", clock.GetUtcNow().UtcDateTime);
        return Wrap(DocxLayout.Render(docx, new LayoutOptions { Editable = editable }), original is not null);
    }

    /// <summary>A saved version on the page; kept for a few minutes (a long text takes a while to draw, and the pages of the reader come one after the other).</summary>
    public async Task<PagePayload> ForVersionAsync(Document document, DocumentVersion version, bool editable, CancellationToken ct = default)
    {
        var key = $"kk:page:{document.Id}:{document.OriginalFileId}:{version.VersionNo}:{Hash(version.Text)}:{editable}";
        var page = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(3);
            entry.AbsoluteExpirationRelativeToNow = PageLifetime;
            return await ForTextAsync(document, DocumentService.ParagraphsOf(version), editable, version.VersionNo, ct);
        });
        return Fresh(page!);
    }

    /// <summary>Two versions as one page with the changes in it: what was added underlined, what was taken away struck through, in the formatting of the text.</summary>
    public async Task<PagePayload> ForChangesAsync(Document document, int fromNo, int toNo, CancellationToken ct = default)
    {
        var from = await documents.GetVersionAsync(document.Id, fromNo, ct) ?? throw new InvalidOperationException("Die Ausgangsfassung gibt es nicht.");
        var to = fromNo == toNo ? from : await documents.GetVersionAsync(document.Id, toNo, ct) ?? throw new InvalidOperationException("Die Zielfassung gibt es nicht.");
        var key = $"kk:changes:{document.Id}:{document.OriginalFileId}:{fromNo}:{Hash(from.Text)}:{toNo}:{Hash(to.Text)}";
        var page = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(3);
            entry.AbsoluteExpirationRelativeToNow = PageLifetime;
            var export = await documents.ExportAsync(document, fromNo, toNo, ExportFormat.DocxTracked, ChangesAuthor, ct);
            var round = await documents.RoundOfAsync(document, toNo, ct);
            var formatted = round is not null && await FormattedOriginalAsync(document, round, ct) is not null;
            // Only the changes between the two versions are changes here: revision marks the author's own file came with are taken as accepted.
            return Wrap(DocxLayout.Render(export.Bytes, new LayoutOptions { Mode = LayoutMode.Changes, ChangesBy = ChangesAuthor }), formatted);
        });
        return Fresh(page!);
    }

    /// <summary>
    /// The Word file of the round when the page can take its formatting (its text is the text of the version the round began with), else null. Whether the text
    /// matches is found out once and remembered: it needs the whole file read.
    /// </summary>
    private async Task<byte[]?> FormattedOriginalAsync(Document document, DocumentService.Round round, CancellationToken ct)
    {
        if (round.Docx is null)
        {
            return null;
        }

        var key = $"kk:formatted:{document.Id}:{round.StartNo}:{document.OriginalFileId}";
        if (!cache.TryGetValue(key, out bool matches))
        {
            matches = DocxReader.Matches(round.Docx, round.Paragraphs);
            cache.Set(key, matches, CssLifetime);
        }

        return matches ? round.Docx : null;
    }

    private PagePayload Wrap(DocxLayoutResult layout, bool formatted)
    {
        var key = Hash(layout.Css);
        cache.Set(CssKeyPrefix + key, layout.Css, new MemoryCacheEntryOptions { SlidingExpiration = CssLifetime });
        return new PagePayload { Layout = layout, CssKey = key, Formatted = formatted };
    }

    /// <summary>A page from the cache gets its stylesheet's lifetime renewed: the page that was just handed out must find it when the browser asks.</summary>
    private PagePayload Fresh(PagePayload page)
    {
        var key = page.CssKey;
        if (!cache.TryGetValue(CssKeyPrefix + key, out string? _))
        {
            cache.Set(CssKeyPrefix + key, page.Layout.Css, new MemoryCacheEntryOptions { SlidingExpiration = CssLifetime });
        }

        return page;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant();
}
