using System.Net;
using System.Text;
using KommaKorrektus.Core.Diff;
using KommaKorrektus.Core.Writing;
using Microsoft.AspNetCore.Html;

namespace KommaKorrektus.UiKit;

/// <summary>A line of the comparison: a paragraph, or a run of unchanged paragraphs folded away.</summary>
public sealed record DiffItem(ParagraphDiff? Paragraph, IReadOnlyList<ParagraphDiff>? Folded);

/// <summary>
/// The markup of a comparison, as the compare page of the editor and the page behind a share link draw it: next to each other or as one document, the words
/// as &lt;ins&gt; and &lt;del&gt;. Everything is encoded.
/// </summary>
public static class DiffView
{
    public static IHtmlContent Words(ParagraphDiff p, bool oldSide, bool both)
    {
        var sb = new StringBuilder();
        foreach (var s in p.Segments)
        {
            if (!both && ((oldSide && s.Kind == EditKind.Insert) || (!oldSide && s.Kind == EditKind.Delete)))
            {
                continue;
            }

            var html = WebUtility.HtmlEncode(s.Text).Replace(TextMetrics.LineBreak.ToString(), "<br>");
            sb.Append(s.Kind switch { EditKind.Insert => $"<ins>{html}</ins>", EditKind.Delete => $"<del>{html}</del>", _ => html });
        }

        return new HtmlString(sb.Length == 0 ? "&nbsp;" : sb.ToString());
    }

    private static string RowClass(ParagraphChange c) => c switch
    {
        ParagraphChange.Inserted => "kk-diff__row is-inserted kk-change",
        ParagraphChange.Deleted => "kk-diff__row is-deleted kk-change",
        ParagraphChange.Modified => "kk-diff__row is-modified kk-change",
        _ => "kk-diff__row",
    };

    private static void Row(StringBuilder sb, ParagraphDiff p, bool inline)
    {
        var number = (p.NewIndex ?? p.OldIndex ?? 0) + 1;
        if (inline)
        {
            sb.Append("<div class=\"").Append(RowClass(p.Change)).Append("\" id=\"p").Append(number).Append("\"><span class=\"kk-diff__no\">").Append(number).Append("</span><p class=\"kk-diff__text\">");
            sb.Append(Html(Words(p, false, true))).Append("</p></div>");
            return;
        }

        sb.Append("<div class=\"").Append(RowClass(p.Change)).Append("\" id=\"p").Append(number).Append("\">");
        sb.Append("<div class=\"kk-diff__cell\"><span class=\"kk-diff__no\">").Append(p.OldIndex is { } oi ? (oi + 1).ToString() : string.Empty).Append("</span>");
        if (p.Change != ParagraphChange.Inserted)
        {
            sb.Append("<p class=\"kk-diff__text\">").Append(Html(Words(p, true, false))).Append("</p>");
        }

        sb.Append("</div><div class=\"kk-diff__cell\"><span class=\"kk-diff__no\">").Append(p.NewIndex is { } ni ? (ni + 1).ToString() : string.Empty).Append("</span>");
        if (p.Change != ParagraphChange.Deleted)
        {
            sb.Append("<p class=\"kk-diff__text\">").Append(Html(Words(p, false, false))).Append("</p>");
        }

        sb.Append("</div></div>");
    }

    /// <summary>
    /// The lines of the view. With <paramref name="onlyChanges"/> the paragraphs that did not change are folded into runs (one paragraph of context stays
    /// around a change); without it every paragraph is a line of its own.
    /// </summary>
    public static List<DiffItem> Items(IReadOnlyList<ParagraphDiff> diff, bool onlyChanges)
    {
        var items = new List<DiffItem>();
        if (!onlyChanges)
        {
            items.AddRange(diff.Select(p => new DiffItem(p, null)));
            return items;
        }

        var show = new bool[diff.Count];
        for (var i = 0; i < diff.Count; i++)
        {
            if (diff[i].Change == ParagraphChange.Unchanged)
            {
                continue;
            }

            for (var j = Math.Max(0, i - 1); j <= Math.Min(diff.Count - 1, i + 1); j++)
            {
                show[j] = true;
            }
        }

        var run = new List<ParagraphDiff>();
        for (var i = 0; i < diff.Count; i++)
        {
            if (!show[i])
            {
                run.Add(diff[i]);
                continue;
            }

            if (run.Count > 0)
            {
                items.Add(new DiffItem(null, [.. run]));
                run.Clear();
            }

            items.Add(new DiffItem(diff[i], null));
        }

        if (run.Count > 0)
        {
            items.Add(new DiffItem(null, [.. run]));
        }

        return items;
    }

    /// <summary>The comparison as markup: next to each other (<paramref name="inline"/> false) or as one text.</summary>
    public static IHtmlContent Render(IEnumerable<DiffItem> items, bool inline, string leftTitle = "Alt", string rightTitle = "Neu")
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"kk-diff ").Append(inline ? "kk-diff--inline" : "kk-diff--side").Append("\" id=\"diff\" data-diff>");
        if (!inline)
        {
            sb.Append("<div class=\"kk-diff__head\" aria-hidden=\"true\"><div>").Append(WebUtility.HtmlEncode(leftTitle)).Append("</div><div>").Append(WebUtility.HtmlEncode(rightTitle)).Append("</div></div>");
        }

        foreach (var item in items)
        {
            if (item.Paragraph is { } p)
            {
                Row(sb, p, inline);
                continue;
            }

            var folded = item.Folded!;
            sb.Append("<details class=\"kk-diff__gap\"><summary>").Append(folded.Count).Append(folded.Count == 1 ? " unveränderter Absatz" : " unveränderte Absätze").Append("</summary>");
            foreach (var q in folded)
            {
                Row(sb, q, inline);
            }

            sb.Append("</details>");
        }

        sb.Append("</div>");
        return new HtmlString(sb.ToString());
    }

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }
}
