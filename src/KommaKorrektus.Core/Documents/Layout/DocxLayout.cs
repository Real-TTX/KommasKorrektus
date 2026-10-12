using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace KommaKorrektus.Core.Documents.Layout;

public enum LayoutMode
{
    /// <summary>The text as it is: earlier tracked changes are taken as accepted (the same reading the editor works on).</summary>
    Clean,
    /// <summary>The tracked changes of the document are drawn: insertions underlined, deletions struck through.</summary>
    Changes,
}

public sealed class LayoutOptions
{
    public LayoutMode Mode { get; init; } = LayoutMode.Clean;

    /// <summary>In the changes view: only the changes by this author are drawn as changes (the others are taken as accepted, as in the clean view). Null: all of them.</summary>
    public string? ChangesBy { get; init; }

    /// <summary>For the editor: a link is not a link there (a click puts the cursor in it), and a paragraph says what a new paragraph after it looks like.</summary>
    public bool Editable { get; init; }

    public long MaxImageBytes { get; init; } = 4L * 1024 * 1024;

    public long MaxTotalImageBytes { get; init; } = 40L * 1024 * 1024;
}

/// <summary>A paragraph or a table of the page; <see cref="FirstParagraph"/> and <see cref="LastParagraph"/> are the numbers (from 0) of the paragraphs of the text in it, -1 when none.</summary>
public sealed record LayoutBlock(string Html, int FirstParagraph, int LastParagraph, bool Changed);

public sealed class DocxLayoutResult
{
    public required IReadOnlyList<LayoutBlock> Blocks { get; init; }

    /// <summary>The footnotes and endnotes the text refers to, as one block ("" when there are none).</summary>
    public required string Notes { get; init; }

    /// <summary>Rules for every class the markup uses.</summary>
    public required string Css { get; init; }

    /// <summary>The class of the sheet: page width, margins and tab width of the document.</summary>
    public required string PageClass { get; init; }

    public required double PageWidthPx { get; init; }

    public required double PageHeightPx { get; init; }

    /// <summary>How many paragraphs of the text were drawn. Only when it is the number of paragraphs of the text can the page be edited (every paragraph has its place).</summary>
    public required int ParagraphCount { get; init; }

    public required int TextParagraphCount { get; init; }

    public bool Complete => ParagraphCount == TextParagraphCount;

    public string Html => string.Concat(Blocks.Select(b => b.Html)) + Notes;
}

/// <summary>
/// Draws a Word document as a page: the paragraphs with their styles (headings, alignment, indents, spacing), the formatting of the words (bold, italic,
/// colour, size, font), lists with their numbers, tables, pictures and footnotes. The text of every paragraph in the markup is exactly the text the editor
/// works on (<see cref="DocxReader"/>), so that the page can be written in and read back.
/// </summary>
public static class DocxLayout
{
    public static DocxLayoutResult Render(byte[] docx, LayoutOptions? options = null)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("Das Word-Dokument hat keinen Text.");
        return new LayoutRenderer(main, options ?? new LayoutOptions()).Render();
    }
}

/// <summary>What one block (a paragraph, or a table with its cells) contained.</summary>
internal sealed class BlockInfo
{
    public int First { get; private set; } = -1;

    public int Last { get; private set; } = -1;

    /// <summary>How many insertions and deletions were drawn.</summary>
    public int Changes { get; set; }

    public bool Changed => Changes > 0;

    public void Seen(int index)
    {
        First = First < 0 ? index : Math.Min(First, index);
        Last = Math.Max(Last, index);
    }
}

internal sealed partial class LayoutRenderer
{
    private const int MaxTableDepth = 5;

    private readonly MainDocumentPart _main;

    private readonly LayoutOptions _options;

    private readonly ThemeBook _theme;

    private readonly StyleBook _styles;

    private readonly NumberingBook _numbering;

    private readonly CssRegistry _css = new();

    private readonly Dictionary<Paragraph, int> _index = new(ReferenceEqualityComparer.Instance);

    private readonly List<(string Label, long Id, bool Endnote)> _notes = [];

    private int _rendered;

    private int _shown;

    private long _imageBytes;

    public LayoutRenderer(MainDocumentPart main, LayoutOptions options)
    {
        _main = main;
        _options = options;
        _theme = ThemeBook.Read(main);
        _styles = new StyleBook(main, _theme);
        _numbering = new NumberingBook(main, _theme);
    }

    public DocxLayoutResult Render()
    {
        var body = _main.Document?.Body ?? throw new InvalidDataException("Das Word-Dokument hat keinen Text.");
        var all = DocxReader.EnumerateParagraphs(body);
        for (var i = 0; i < all.Count; i++)
        {
            _index[all[i]] = i;
        }

        var (pageClass, width, height) = Page(body);
        var blocks = new List<LayoutBlock>();
        foreach (var element in Blocks(body))
        {
            var info = new BlockInfo();
            var sb = new StringBuilder();
            if (element is Paragraph paragraph)
            {
                RenderParagraph(sb, paragraph, info, null);
            }
            else if (element is Table table)
            {
                RenderTable(sb, table, info, 0);
            }

            blocks.Add(new LayoutBlock(sb.ToString(), info.First, info.Last, info.Changed));
        }

        var notes = NotesHtml();
        return new DocxLayoutResult
        {
            Blocks = blocks,
            Notes = notes,
            Css = _css.ToCss(),
            PageClass = pageClass,
            PageWidthPx = width,
            PageHeightPx = height,
            ParagraphCount = _rendered,
            TextParagraphCount = all.Count,
        };
    }

    /// <summary>The paragraphs and tables a container holds, in order, also the ones inside content controls.</summary>
    private static IEnumerable<OpenXmlElement> Blocks(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph or Table:
                    yield return child;
                    break;
                case SdtBlock sdt when sdt.SdtContentBlock is { } content:
                    foreach (var inner in Blocks(content))
                    {
                        yield return inner;
                    }

                    break;
                case CustomXmlBlock custom:
                    foreach (var inner in Blocks(custom))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    /// <summary>The page of the first section: its size and margins (a twentieth of a point is 1/15 of a pixel on screen) and the width of a tab stop.</summary>
    private (string Class, double Width, double Height) Page(Body body)
    {
        var section = body.Descendants<SectionProperties>().FirstOrDefault();
        var size = section.Child("pgSz");
        var margin = section.Child("pgMar");
        static double Px(double? twips, double fallback) => Math.Round(Math.Abs(twips ?? fallback) / 15, 1);
        var width = Math.Clamp(Px(size.Attr("w").Number(), 11906), 300, 2400);
        var height = Math.Clamp(Px(size.Attr("h").Number(), 16838), 300, 3600);
        var top = Px(margin.Attr("top").Number(), 1417);
        var right = Px(margin.Attr("right").Number(), 1417);
        var bottom = Px(margin.Attr("bottom").Number(), 1134);
        var left = Px(margin.Attr("left").Number(), 1417);
        // The margins may not eat the page (a damaged file): at least a third of the width stays for the text.
        if (left + right > width * 0.66)
        {
            left = right = width * 0.1;
        }

        var tab = _main.DocumentSettingsPart?.Settings.Child("defaultTabStop").Val().Number() / 20 ?? 36;
        var css = $"width:{LayoutXml.Px(width)};min-height:{LayoutXml.Px(height)};padding:{LayoutXml.Px(top)} {LayoutXml.Px(right)} {LayoutXml.Px(bottom)} {LayoutXml.Px(left)};tab-size:{LayoutXml.Pt(tab)};--kk-page-top:{LayoutXml.Px(top)};--kk-page-inner-h:{LayoutXml.Px(Math.Max(100, height - top - bottom))};";
        return (_css.Add("pg", css), width, height);
    }

    // ------------------------------------------------------------------------------------------------ paragraphs

    /// <summary>Whether a tracked change is to be drawn as a change: in the changes view, and when it is by the author the view is for (all, when none is named).</summary>
    private bool Shows(OpenXmlElement? revision)
        => revision is not null && _options.Mode == LayoutMode.Changes
            && (_options.ChangesBy is null || string.Equals(revision.Attr("author"), _options.ChangesBy, StringComparison.OrdinalIgnoreCase));

    private static OpenXmlElement? Neighbour(OpenXmlElement element, bool next)
    {
        var other = next ? element.NextSibling() : element.PreviousSibling();
        while (other is not null && other.LocalName is "bookmarkStart" or "bookmarkEnd" or "proofErr" or "permStart" or "permEnd" or "commentRangeStart" or "commentRangeEnd")
        {
            other = next ? other.NextSibling() : other.PreviousSibling();
        }

        return other;
    }

    private void RenderParagraph(StringBuilder sb, Paragraph paragraph, BlockInfo info, CellFmt? cell, bool note = false)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var (fmt, baseRun) = _styles.Paragraph(styleId, cell);
        var direct = ParaFmt.Read(properties, _theme);

        // Order of precedence: defaults, the table, the style, the list level, the paragraph itself.
        ListMarker? marker = null;
        var numId = direct.NumId ?? fmt.NumId;
        if (numId is > 0)
        {
            var level = direct.NumLevel ?? fmt.NumLevel ?? _numbering.LevelOfStyle(numId.Value, _styles.ResolveParagraphStyleId(styleId)) ?? 0;
            marker = _numbering.Next(numId.Value, level);
        }

        if (marker is not null)
        {
            fmt.Apply(marker.Para);
        }

        fmt.Apply(direct);

        var extra = marker is null ? string.Empty : MarkerRules(marker, fmt, baseRun);
        var cssClass = _css.Add("p", fmt.Css() + baseRun.Css(null), extra);
        var classes = new StringBuilder("kk-lp");
        if (cssClass.Length > 0)
        {
            classes.Append(' ').Append(cssClass);
        }

        if (fmt.PageBreakBefore == true)
        {
            classes.Append(" kk-pbb");
        }

        if (note)
        {
            classes.Append(" kk-lp--note");
        }

        if (fmt.Contextual == true)
        {
            var own = _styles.ResolveParagraphStyleId(styleId);
            if (Neighbour(paragraph, next: false) is Paragraph before && _styles.ResolveParagraphStyleId(before.ParagraphProperties?.ParagraphStyleId?.Val?.Value) == own)
            {
                classes.Append(" kk-cs-b");
            }

            if (Neighbour(paragraph, next: true) is Paragraph after && _styles.ResolveParagraphStyleId(after.ParagraphProperties?.ParagraphStyleId?.Val?.Value) == own)
            {
                classes.Append(" kk-cs-a");
            }
        }

        // A paragraph that was put in or taken out as a whole carries the change on its paragraph mark.
        var mark = properties?.ParagraphMarkRunProperties;
        var markChange = (OpenXmlElement?)mark?.GetFirstChild<Inserted>() ?? mark?.GetFirstChild<Deleted>();
        var deleted = false;
        if (Shows(markChange))
        {
            deleted = markChange is Deleted;
            classes.Append(deleted ? " is-deleted kk-change" : " is-inserted kk-change");
            info.Changes++;
        }

        // The words first: whether the paragraph has changes in it decides its class.
        var content = new StringBuilder();
        var changesBefore = info.Changes;
        if (marker is { Text.Length: > 0 })
        {
            content.Append("<span class=\"kk-lm\" data-opaque contenteditable=\"false\" data-m=\"").Append(Esc(marker.Text)).Append("\"></span>");
        }

        if (!Inline(content, paragraph, baseRun, info))
        {
            // An empty paragraph keeps its height (and a place for the cursor) with a line break the editor ignores.
            content.Append("<br>");
        }

        if (info.Changes > changesBefore && !classes.ToString().Contains("kk-change", StringComparison.Ordinal))
        {
            classes.Append(" kk-change");
        }

        var indexed = !note && _index.TryGetValue(paragraph, out _);
        var index = indexed ? _index[paragraph] : -1;
        sb.Append("<p class=\"").Append(classes).Append('"');
        if (indexed)
        {
            sb.Append(" id=\"p").Append(index + 1).Append("\" data-n=\"").Append(index + 1).Append('"');
            // The number a reader sees: in the changes view a deleted paragraph has none, so that the numbers are those of the new text.
            if (!deleted)
            {
                sb.Append(" data-no=\"").Append(++_shown).Append('"');
            }

            _rendered++;
            info.Seen(index);
        }

        if (_options.Editable && indexed)
        {
            var next = NextParagraphClass(direct, cell, cssClass);
            if (next is not null)
            {
                sb.Append(" data-nx=\"").Append(next).Append('"');
            }
        }

        sb.Append('>').Append(content).Append("</p>");
    }

    /// <summary>
    /// The class a paragraph inserted behind this one gets from the export: the properties of this paragraph, without the style, the list and a page break.
    /// The editor gives a paragraph made with the Enter key this class, so that what it shows is what the export will write.
    /// </summary>
    private string? NextParagraphClass(ParaFmt direct, CellFmt? cell, string ownClass)
    {
        var (fmt, run) = _styles.Paragraph(null, cell);
        var copy = direct.Clone();
        copy.NumId = null;
        copy.NumLevel = null;
        copy.PageBreakBefore = null;
        fmt.Apply(copy);
        var css = _css.Add("p", fmt.Css() + run.Css(null));
        return css == ownClass ? null : css;
    }

    private string MarkerRules(ListMarker marker, ParaFmt fmt, RunFmt baseRun)
    {
        var markerRun = marker.Run.Clone();
        // The symbol fonts of bullets are not on the screen of a reader: the bullet is drawn in the font of the text.
        markerRun.Font = null;
        var sb = new StringBuilder("{c} .kk-lm{");
        if (marker.Suffix == "tab")
        {
            var hanging = fmt.FirstLinePt is < 0 ? -fmt.FirstLinePt.Value : 0;
            sb.Append("min-width:").Append(LayoutXml.Pt(hanging > 0 ? hanging : 36)).Append(';');
        }
        else if (marker.Suffix == "space")
        {
            sb.Append("padding-right:.3em;");
        }

        sb.Append(markerRun.Css(baseRun)).Append('}');
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------ the words of a paragraph

    private RunFmt Resolve(RunProperties? properties, RunFmt baseRun)
    {
        var fmt = baseRun.Clone();
        var direct = RunFmt.Read(properties, _theme);
        if (direct.CharStyle is { } style)
        {
            fmt.Apply(_styles.Character(style));
        }

        fmt.Apply(direct);
        return fmt;
    }

    /// <summary>
    /// The text and the things between the text of a paragraph, from the same reading the editor and the export use (<see cref="ParagraphItems"/>): what is text
    /// there is text here, and everything else (pictures, notes, page breaks) is a part the page shows but a cursor skips.
    /// </summary>
    private bool Inline(StringBuilder sb, Paragraph paragraph, RunFmt baseRun, BlockInfo info)
    {
        var items = ParagraphItems.Of(paragraph);
        var opaque = items.Opaque;
        var next = 0;
        var position = 0;
        var wrote = false;
        var endsWithBreak = false;
        OpenXmlElement? open = null;
        var closing = string.Empty;

        void Close()
        {
            if (open is null)
            {
                return;
            }

            sb.Append(closing);
            open = null;
        }

        void Enter(OpenXmlElement? container)
        {
            if (ReferenceEquals(container, open))
            {
                return;
            }

            Close();
            if (container is null)
            {
                return;
            }

            var (tag, end) = OpenContainer(container, info);
            if (tag.Length > 0)
            {
                sb.Append(tag);
                open = container;
                closing = end;
            }
        }

        void Objects(int upTo)
        {
            while (next < opaque.Count && opaque[next].Offset <= upTo)
            {
                var html = OpaqueHtml(opaque[next].Element, info, baseRun);
                next++;
                if (html.Length > 0)
                {
                    Close();
                    sb.Append(html);
                    wrote = true;
                    endsWithBreak = false;
                }
            }
        }

        foreach (var item in items.Texts)
        {
            Objects(position);
            Enter(item.Container);
            var css = Resolve(item.Format, baseRun).Css(baseRun);
            var cssClass = css.Length == 0 ? string.Empty : _css.Add("r", css);
            if (cssClass.Length > 0)
            {
                sb.Append("<span class=\"").Append(cssClass).Append("\">").Append(Esc(item.Text)).Append("</span>");
            }
            else
            {
                sb.Append(Esc(item.Text));
            }

            wrote = true;
            endsWithBreak = item.Text.EndsWith(Writing.TextMetrics.LineBreak);
            position += item.Text.Length;
        }

        Objects(int.MaxValue);
        Close();
        if (endsWithBreak)
        {
            // A line break at the very end of a block is only drawn when another one follows it (and the browser keeps one there for the cursor): the editor reads the last one as that, not as a break.
            sb.Append('\n');
        }

        return wrote;
    }

    /// <summary>The tags a container of runs (a link, an insertion) is drawn with, the opening and the closing one; both empty for a container that draws nothing.</summary>
    private (string Open, string Close) OpenContainer(OpenXmlElement container, BlockInfo info)
    {
        switch (container)
        {
            case Hyperlink link:
                var url = Url(link);
                return url is null || _options.Editable
                    ? ("<span class=\"kk-a\">", "</span>")
                    : ($"<a class=\"kk-a\" href=\"{Esc(url)}\" target=\"_blank\" rel=\"noopener noreferrer\">", "</a>");
            case InsertedRun or MoveToRun when Shows(container):
                info.Changes++;
                return ("<ins class=\"kk-ins\">", "</ins>");
            default:
                return (string.Empty, string.Empty);
        }
    }

    /// <summary>The address of a link when it is one a reader may follow: a web address or a mail address, nothing a document could smuggle in (script, file).</summary>
    private string? Url(Hyperlink link)
    {
        if (link.Id?.Value is not { Length: > 0 } id)
        {
            return null;
        }

        var relationship = _main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id);
        var uri = relationship?.Uri;
        return uri is not null && uri.IsAbsoluteUri && uri.Scheme is "http" or "https" or "mailto" ? uri.AbsoluteUri : null;
    }

    /// <summary>Text as markup. A line break inside a paragraph is drawn as a line feed (the paragraph keeps white space as it is), never as an element: it is also how the editor holds it.</summary>
    internal static string Esc(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case Writing.TextMetrics.LineBreak: sb.Append('\n'); break;
                case < ' ' when c is not ('\t' or '\n'): break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
