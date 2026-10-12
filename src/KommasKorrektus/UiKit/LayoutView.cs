using System.Net;
using System.Text;
using KommasKorrektus.Core.Documents.Layout;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Html;

namespace KommasKorrektus.UiKit;

/// <summary>A line of the page view: a block (a paragraph or a table), or a run of blocks without changes folded away.</summary>
public sealed record LayoutItem(LayoutBlock? Block, IReadOnlyList<LayoutBlock>? Folded);

/// <summary>
/// The page of a document as markup for the pages that only read it (the comparison, one version, the pages behind a share link and in the portal). The
/// editor builds its own page from the same markup in the browser.
/// </summary>
public static class LayoutView
{
    /// <summary>
    /// The lines of the view. With <paramref name="onlyChanges"/> the blocks without changes are folded into runs (one block of context stays around a change);
    /// without it every block is a line of its own.
    /// </summary>
    public static List<LayoutItem> Items(IReadOnlyList<LayoutBlock> blocks, bool onlyChanges)
    {
        var items = new List<LayoutItem>();
        if (!onlyChanges)
        {
            items.AddRange(blocks.Select(b => new LayoutItem(b, null)));
            return items;
        }

        var show = new bool[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].Changed)
            {
                continue;
            }

            for (var j = Math.Max(0, i - 1); j <= Math.Min(blocks.Count - 1, i + 1); j++)
            {
                show[j] = true;
            }
        }

        var run = new List<LayoutBlock>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!show[i])
            {
                run.Add(blocks[i]);
                continue;
            }

            if (run.Count > 0)
            {
                items.Add(new LayoutItem(null, [.. run]));
                run.Clear();
            }

            items.Add(new LayoutItem(blocks[i], null));
        }

        if (run.Count > 0)
        {
            items.Add(new LayoutItem(null, [.. run]));
        }

        return items;
    }

    /// <summary>
    /// One of two pages next to each other (the comparison): the page of a version as it was, nothing marked. What differs is found in the browser from the two texts
    /// (sidebyside.js), the same way as in the editor with an earlier version beside the text, so that the two look alike. <paramref name="side"/> is "old" or "new",
    /// <paramref name="zoom"/> the size of the writing in percent (the person's choice).
    /// </summary>
    public static IHtmlContent Plain(PagePayload page, string side, int zoom = 100)
    {
        var sb = new StringBuilder();
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(page.CssUrl)).Append("\">");
        sb.Append("<div class=\"kk-desk kk-desk--read kk-desk--").Append(side).Append("\" data-fit data-scale=\"").Append(zoom / 100d == 1 ? "1" : (zoom / 100d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("\"><div class=\"kk-sheet kk-sheet--read ").Append(page.Layout.PageClass).Append("\">");
        sb.Append(page.Html).Append("</div></div>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>The stylesheet and the page. <paramref name="withNotes"/> puts the footnotes at the end (on the last page of a long text).</summary>
    public static IHtmlContent Render(PagePayload page, IEnumerable<LayoutItem> items, bool withNotes, bool diff = false)
    {
        var sb = new StringBuilder();
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(page.CssUrl)).Append("\">");
        sb.Append("<div class=\"kk-desk kk-desk--read\" data-fit");
        if (diff)
        {
            sb.Append(" id=\"diff\" data-diff");
        }

        sb.Append("><div class=\"kk-sheet kk-sheet--read ").Append(page.Layout.PageClass).Append("\">");
        foreach (var item in items)
        {
            if (item.Block is { } block)
            {
                sb.Append(block.Html);
                continue;
            }

            var folded = item.Folded!;
            sb.Append("<details class=\"kk-diff__gap\"><summary>").Append(folded.Count).Append(folded.Count == 1 ? " unveränderter Absatz" : " unveränderte Absätze").Append("</summary>");
            foreach (var hidden in folded)
            {
                sb.Append(hidden.Html);
            }

            sb.Append("</details>");
        }

        if (withNotes)
        {
            sb.Append(page.Layout.Notes);
        }

        sb.Append("</div></div>");
        return new HtmlString(sb.ToString());
    }
}
