using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Documents.Layout;

// Everything in a paragraph that is not text (pictures, notes, page breaks, deleted words), the notes at the end, and tables.
internal sealed partial class LayoutRenderer
{
    private const double EmuPerPixel = 9525;

    /// <summary>A part of a paragraph without text, as markup the cursor skips (it is <c>contenteditable=false</c> and marked <c>data-opaque</c>, so that it is never part of the text).</summary>
    private string OpaqueHtml(OpenXmlElement element, BlockInfo info, RunFmt baseRun)
    {
        if (element is DeletedRun or MoveFromRun)
        {
            return Shows(element) ? DeletedHtml(element, info, baseRun) : string.Empty;
        }

        if (element.Descendants<Drawing>().FirstOrDefault() is { } drawing)
        {
            return ImageHtml(drawing);
        }

        if (element.Descendants<FootnoteReference>().FirstOrDefault() is { Id.Value: var footnoteId })
        {
            return NoteMark(footnoteId, endnote: false);
        }

        if (element.Descendants<EndnoteReference>().FirstOrDefault() is { Id.Value: var endnoteId })
        {
            return NoteMark(endnoteId, endnote: true);
        }

        if (element.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
        {
            return "<span class=\"kk-pb\" data-opaque contenteditable=\"false\"></span>";
        }

        return element.Descendants<Picture>().Any() || element.Descendants<EmbeddedObject>().Any() ? Placeholder("Objekt", 0, 0) : string.Empty;
    }

    private string Placeholder(string what, double width, double height)
    {
        var size = width > 0 && height > 0 ? _css.Add("ph", $"width:{LayoutXml.Px(Math.Min(width, 700))};height:{LayoutXml.Px(Math.Min(height, 700))};") : string.Empty;
        return $"<span class=\"kk-obj {size}\" data-opaque contenteditable=\"false\" title=\"{Esc(what)}\"></span>";
    }

    /// <summary>Words that a tracked change took away: shown struck through, in the format they had.</summary>
    private string DeletedHtml(OpenXmlElement element, BlockInfo info, RunFmt baseRun)
    {
        var sb = new StringBuilder();
        foreach (var run in element.Descendants<Run>())
        {
            var text = new StringBuilder();
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case DeletedText deleted: text.Append(deleted.Text); break;
                    case Text plain: text.Append(plain.Text); break;
                    case TabChar: text.Append('\t'); break;
                    case Break b when b.Type is null || b.Type.Value == BreakValues.TextWrapping:
                        text.Append(TextMetrics.LineBreak);
                        break;
                }
            }

            if (text.Length == 0)
            {
                continue;
            }

            var css = Resolve(run.RunProperties, baseRun).Css(baseRun);
            var cssClass = css.Length == 0 ? string.Empty : _css.Add("r", css);
            sb.Append(cssClass.Length > 0 ? $"<span class=\"{cssClass}\">{Esc(text.ToString())}</span>" : Esc(text.ToString()));
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        info.Changes++;
        return $"<del class=\"kk-del\">{sb}</del>";
    }

    private string NoteMark(long id, bool endnote)
    {
        var label = endnote ? NumberingBook.Format(_notes.Count(n => n.Endnote) + 1, "lowerRoman") : (_notes.Count(n => !n.Endnote) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _notes.Add((label, id, endnote));
        return $"<sup class=\"kk-fnr\" data-opaque contenteditable=\"false\" data-m=\"{label}\"></sup>";
    }

    /// <summary>A picture as an image the page carries itself (a data address), when the browser can show the format and the size is reasonable; else a frame of the same size.</summary>
    private string ImageHtml(Drawing drawing)
    {
        var shape = drawing.Descendants().FirstOrDefault(e => e.LocalName is "inline" or "anchor");
        var extent = shape.Child("extent");
        var width = Math.Round((extent.Attr("cx").Number() ?? 0) / EmuPerPixel);
        var height = Math.Round((extent.Attr("cy").Number() ?? 0) / EmuPerPixel);
        var properties = drawing.Descendants().FirstOrDefault(e => e.LocalName == "docPr");
        var alt = properties.Attr("descr") ?? properties.Attr("title") ?? properties.Attr("name") ?? "Bild";
        var blip = drawing.Descendants().FirstOrDefault(e => e.LocalName == "blip");
        var embed = blip.Attr("embed");
        if (embed is null)
        {
            return Placeholder(alt, width, height);
        }

        ImagePart? part;
        try
        {
            part = _main.GetPartById(embed) as ImagePart;
        }
        catch (ArgumentOutOfRangeException)
        {
            part = null;
        }

        if (part is null || part.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/webp" or "image/svg+xml"))
        {
            return Placeholder(alt + (part is null ? string.Empty : " (" + part.ContentType + ")"), width, height);
        }

        var bytes = ReadLimited(part);
        if (bytes is null || _imageBytes + bytes.Length > _options.MaxTotalImageBytes)
        {
            return Placeholder(alt + " (zu groß für die Seitenansicht)", width, height);
        }

        _imageBytes += bytes.Length;
        var side = shape is { LocalName: "anchor" } ? Side(shape) : string.Empty;
        // The size the document gives, kept in proportion when the page is narrower: the ratio is the document's, not the one of the picture (Word may stretch a picture).
        var ratio = width > 0 && height > 0 ? " " + _css.Add("im", $"aspect-ratio:{width:0}/{height:0};") : string.Empty;
        var size = width > 0 && height > 0 ? $" width=\"{width:0}\" height=\"{height:0}\"" : string.Empty;
        return $"<img class=\"kk-img{side}{ratio}\" data-opaque contenteditable=\"false\" src=\"data:{part.ContentType};base64,{Convert.ToBase64String(bytes)}\"{size} alt=\"{Esc(alt)}\">";
    }

    /// <summary>Where a floating picture sits: to the left or right of the text, or on a line of its own.</summary>
    private static string Side(OpenXmlElement anchor)
    {
        if (anchor.Child("wrapTopAndBottom") is not null)
        {
            return " kk-img--block";
        }

        return anchor.Child("positionH").Child("align")?.InnerText switch
        {
            "left" => " kk-img--left",
            "right" => " kk-img--right",
            _ => string.Empty,
        };
    }

    private byte[]? ReadLimited(ImagePart part)
    {
        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > _options.MaxImageBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    // ------------------------------------------------------------------------------------------------ notes

    private string NotesHtml()
    {
        if (_notes.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var any = false;
        foreach (var (label, id, endnote) in _notes)
        {
            OpenXmlElement? note = endnote
                ? _main.EndnotesPart?.Endnotes?.Elements<Endnote>().FirstOrDefault(e => e.Id?.Value == id)
                : _main.FootnotesPart?.Footnotes?.Elements<Footnote>().FirstOrDefault(e => e.Id?.Value == id);
            if (note is null)
            {
                continue;
            }

            any = true;
            sb.Append("<div class=\"kk-note\"><span class=\"kk-note__no\">").Append(label).Append("</span><div class=\"kk-note__body\">");
            foreach (var paragraph in note.Elements<Paragraph>())
            {
                RenderParagraph(sb, paragraph, new BlockInfo(), null, note: true);
            }

            sb.Append("</div></div>");
        }

        if (!any)
        {
            return string.Empty;
        }

        var title = _notes.Any(n => n.Endnote) ? (_notes.Any(n => !n.Endnote) ? "Fußnoten und Endnoten" : "Endnoten") : "Fußnoten";
        return $"<section class=\"kk-notes\" data-opaque contenteditable=\"false\"><h2 class=\"kk-notes__title\">{title}</h2>{sb}</section>";
    }

    // ------------------------------------------------------------------------------------------------ tables

    /// <summary>A cell of the grid of a table: where it starts, how many columns it spans, and whether it only continues the cell above (a vertical merge).</summary>
    private sealed record CellPlan(TableCell Cell, int Row, int Column, int Span, bool Continues, bool ContinuedBelow);

    /// <summary>
    /// A table. A vertically merged cell is drawn as the cells it is made of, without the line between them: every cell keeps its paragraph, in the order of the
    /// text, so that the page can be written in without losing track of which paragraph is which.
    /// </summary>
    private void RenderTable(StringBuilder sb, Table table, BlockInfo info, int depth)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var props = table.GetFirstChild<TableProperties>();
        var style = _styles.Table(props.Child("tblStyle").Val());
        var look = TableLook.Read(props.Child("tblLook"));
        var plan = Plan(rows);
        var columns = plan.Max(r => r.Count == 0 ? 0 : r[^1].Column + r[^1].Span);
        var grid = table.Child("tblGrid").Children("gridCol").Select(g => g.Attr("w").Number() ?? 0).ToList();
        if (grid.Count < columns)
        {
            var known = grid.Where(w => w > 0).DefaultIfEmpty(2000).Average();
            while (grid.Count < columns)
            {
                grid.Add(known);
            }
        }

        var borders = style.Borders.Clone();
        borders.Apply(BorderSet.Read(props.Child("tblBorders")));
        var margins = style.Margins;
        if (props.Child("tblCellMar") is { } direct)
        {
            margins = (
                direct.Child("top").Points("w") ?? margins.Top,
                (direct.Child("left") ?? direct.Child("start")).Points("w") ?? margins.Left,
                direct.Child("bottom").Points("w") ?? margins.Bottom,
                (direct.Child("right") ?? direct.Child("end")).Points("w") ?? margins.Right);
        }

        // Word's default cell padding when nothing says otherwise.
        var pad = (Top: margins.Top ?? 0, Left: margins.Left ?? 5.4, Bottom: margins.Bottom ?? 0, Right: margins.Right ?? 5.4);

        var totalPx = Math.Round(grid.Sum() / 15, 1);
        var declared = props.Child("tblW");
        var tableCss = new StringBuilder();
        if (declared.Attr("type") == "pct" && declared.Attr("w").Number() is { } percent)
        {
            tableCss.Append("width:").Append(LayoutXml.Num(Math.Min(percent / 50, 100))).Append("%;");
        }
        else
        {
            tableCss.Append("width:").Append(LayoutXml.Px(Math.Max(totalPx, 20))).Append(";");
        }

        switch (props.Child("jc").Val())
        {
            case "center":
                tableCss.Append("margin-left:auto;margin-right:auto;");
                break;
            case "right" or "end":
                tableCss.Append("margin-left:auto;");
                break;
            default:
                if (props.Child("tblInd").Attr("w").Number() is { } indent && indent > 0)
                {
                    tableCss.Append("margin-left:").Append(LayoutXml.Px(indent / 15)).Append(';');
                }

                break;
        }

        sb.Append("<table class=\"kk-lt ").Append(_css.Add("tb", tableCss.ToString())).Append("\"><colgroup>");
        foreach (var width in grid)
        {
            sb.Append("<col class=\"").Append(_css.Add("cw", $"width:{LayoutXml.Px(Math.Round(width / 15, 1))};")).Append("\">");
        }

        sb.Append("</colgroup><tbody>");
        for (var r = 0; r < rows.Count; r++)
        {
            sb.Append("<tr>");
            var before = rows[r].Child("trPr").Child("gridBefore").Val().Integer() ?? 0;
            if (before > 0)
            {
                sb.Append("<td class=\"kk-gap\" colspan=\"").Append(before).Append("\"></td>");
            }

            foreach (var cell in plan[r])
            {
                RenderCell(sb, cell, rows.Count, columns, style, look, borders, pad, info, depth);
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static List<List<CellPlan>> Plan(List<TableRow> rows)
    {
        var plan = new List<List<CellPlan>>();
        for (var r = 0; r < rows.Count; r++)
        {
            var list = new List<CellPlan>();
            var column = rows[r].Child("trPr").Child("gridBefore").Val().Integer() ?? 0;
            foreach (var cell in rows[r].Elements<TableCell>())
            {
                var cellProps = cell.TableCellProperties;
                var span = Math.Max(1, cellProps.Child("gridSpan").Val().Integer() ?? 1);
                var merge = cellProps.Child("vMerge");
                list.Add(new CellPlan(cell, r, column, span, merge is not null && merge.Val() is null or "continue", false));
                column += span;
            }

            plan.Add(list);
        }

        // A cell is "continued below" when the cell under it at the same place continues it.
        for (var r = 0; r + 1 < plan.Count; r++)
        {
            for (var i = 0; i < plan[r].Count; i++)
            {
                var cell = plan[r][i];
                if (plan[r + 1].Any(below => below.Continues && below.Column == cell.Column))
                {
                    plan[r][i] = cell with { ContinuedBelow = true };
                }
            }
        }

        return plan;
    }

    private void RenderCell(StringBuilder sb, CellPlan plan, int rowCount, int columns, TableStyle style, TableLook look, BorderSet tableBorders, (double Top, double Left, double Bottom, double Right) pad, BlockInfo info, int depth)
    {
        var props = plan.Cell.TableCellProperties;
        var fmt = style.For(plan.Row, plan.Column, rowCount, columns, look);

        var sides = new BorderSet
        {
            Top = plan.Row == 0 ? tableBorders.Top : tableBorders.InsideH,
            Bottom = plan.Row == rowCount - 1 ? tableBorders.Bottom : tableBorders.InsideH,
            Left = plan.Column == 0 ? tableBorders.Left : tableBorders.InsideV,
            Right = plan.Column + plan.Span >= columns ? tableBorders.Right : tableBorders.InsideV,
        };
        sides.Apply(fmt.Borders);
        sides.Apply(BorderSet.Read(props.Child("tcBorders")));
        var none = new Border("none", 0, null);
        if (plan.Continues)
        {
            sides.Top = none;
        }

        if (plan.ContinuedBelow)
        {
            sides.Bottom = none;
        }

        var css = new StringBuilder();
        var margin = props.Child("tcMar");
        var top = margin.Child("top").Points("w") ?? pad.Top;
        var left = (margin.Child("left") ?? margin.Child("start")).Points("w") ?? pad.Left;
        var bottom = margin.Child("bottom").Points("w") ?? pad.Bottom;
        var right = (margin.Child("right") ?? margin.Child("end")).Points("w") ?? pad.Right;
        css.Append("padding:").Append(LayoutXml.Pt(top)).Append(' ').Append(LayoutXml.Pt(right)).Append(' ').Append(LayoutXml.Pt(bottom)).Append(' ').Append(LayoutXml.Pt(left)).Append(';');
        if ((LayoutXml.HexColor(props.Child("shd").Attr("fill")) ?? fmt.Shade) is { } shade)
        {
            css.Append("background-color:").Append(shade).Append(';');
        }

        css.Append(props.Child("vAlign").Val() switch { "center" => "vertical-align:middle;", "bottom" => "vertical-align:bottom;", _ => string.Empty });
        AppendSide(css, "top", sides.Top);
        AppendSide(css, "right", sides.Right);
        AppendSide(css, "bottom", sides.Bottom);
        AppendSide(css, "left", sides.Left);

        sb.Append("<td class=\"").Append(_css.Add("td", css.ToString())).Append('"');
        if (plan.Span > 1)
        {
            sb.Append(" colspan=\"").Append(plan.Span).Append('"');
        }

        sb.Append('>');
        foreach (var element in Blocks(plan.Cell))
        {
            if (element is Paragraph paragraph)
            {
                RenderParagraph(sb, paragraph, info, fmt);
            }
            else if (element is Table nested)
            {
                RenderTable(sb, nested, info, depth + 1);
            }
        }

        sb.Append("</td>");
    }

    private static void AppendSide(StringBuilder css, string side, Border? border)
    {
        if (border is not null)
        {
            css.Append("border-").Append(side).Append(':').Append(border.Css).Append(';');
        }
    }
}
