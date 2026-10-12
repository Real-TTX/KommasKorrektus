using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Documents;

public enum RevisionMode
{
    /// <summary>The changes are marked as revisions (insertions and deletions) that can be accepted or rejected in Word.</summary>
    Tracked,
    /// <summary>The new text is written as it is, without revisions: the clean version.</summary>
    Accepted,
}

/// <summary>
/// Writes a Word document with the changes between two versions of the text. When the original document is at hand the paragraphs that did not change
/// stay exactly as they were (styles, numbering, pictures, tables); a changed paragraph keeps the formatting of its runs, with insertions and deletions
/// as Word's own tracked changes (<c>w:ins</c>, <c>w:del</c>) so that the author can accept them one by one in Word.
/// </summary>
public static class DocxWriter
{
    /// <param name="baseDocx">The document the old text came from, or null: then a plain document is made from the old paragraphs.</param>
    public static byte[] Write(byte[]? baseDocx, IReadOnlyList<string> oldParagraphs, IReadOnlyList<string> newParagraphs, RevisionMode mode, string author, DateTime dateUtc)
    {
        var source = baseDocx is not null && DocxReader.Matches(baseDocx, oldParagraphs) ? baseDocx : DocxBuilder.FromParagraphs(oldParagraphs);
        using var stream = new MemoryStream();
        stream.Write(source);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            Apply(doc, DocumentDiff.Compare(oldParagraphs, newParagraphs), mode, author, dateUtc);
            doc.MainDocumentPart!.Document!.Save();
        }

        return stream.ToArray();
    }

    private sealed class Ctx(RevisionMode mode, string author, DateTime date, long firstId)
    {
        private long _next = firstId;

        public bool Tracked => mode == RevisionMode.Tracked;

        public string Author => author;

        public string Date => date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        public string NextId() => (++_next).ToString(CultureInfo.InvariantCulture);
    }

    private static void Apply(WordprocessingDocument doc, IReadOnlyList<ParagraphDiff> diff, RevisionMode mode, string author, DateTime dateUtc)
    {
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = DocxReader.EnumerateParagraphs(body);
        var firstId = body.Descendants().OfType<OpenXmlElement>()
            .Select(e => e.GetAttributes().FirstOrDefault(a => a.LocalName == "id" && a.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main").Value)
            .Where(v => long.TryParse(v, out _)).Select(v => long.Parse(v!)).DefaultIfEmpty(0).Max();
        var ctx = new Ctx(mode, author, dateUtc, Math.Max(firstId, 1000));

        Paragraph? previous = null;
        for (var i = 0; i < diff.Count; i++)
        {
            var d = diff[i];
            switch (d.Change)
            {
                case ParagraphChange.Unchanged:
                    previous = paragraphs[d.OldIndex!.Value];
                    break;

                case ParagraphChange.Modified:
                    var modified = paragraphs[d.OldIndex!.Value];
                    Rewrite(modified, d.Segments, ctx);
                    previous = modified;
                    break;

                case ParagraphChange.Deleted:
                    var deleted = paragraphs[d.OldIndex!.Value];
                    if (ctx.Tracked)
                    {
                        MarkDeleted(deleted, ctx);
                        previous = deleted;
                    }
                    else
                    {
                        RemoveParagraph(deleted);
                    }

                    break;

                default:
                    // A run of inserted paragraphs goes in front of the next paragraph that exists, or after the one before when there is none.
                    var next = NextExisting(diff, i, paragraphs);
                    var inserted = NewParagraph(d.NewText!, previous ?? next, ctx);
                    if (next is not null && (previous is null || previous.Parent != next.Parent))
                    {
                        next.InsertBeforeSelf(inserted);
                    }
                    else if (previous is not null)
                    {
                        previous.InsertAfterSelf(inserted);
                    }
                    else
                    {
                        body.PrependChild(inserted);
                    }

                    previous = inserted;
                    break;
            }
        }
    }

    private static Paragraph? NextExisting(IReadOnlyList<ParagraphDiff> diff, int from, List<Paragraph> paragraphs)
    {
        for (var j = from + 1; j < diff.Count; j++)
        {
            if (diff[j].Change != ParagraphChange.Inserted && diff[j].OldIndex is { } index)
            {
                return paragraphs[index];
            }
        }

        return null;
    }

    private static void RemoveParagraph(Paragraph paragraph)
    {
        // A table cell must keep one paragraph: an emptied one stays.
        if (paragraph.Parent is TableCell cell && cell.Elements<Paragraph>().Count() == 1)
        {
            foreach (var child in paragraph.ChildElements.Where(c => c is not ParagraphProperties).ToList())
            {
                child.Remove();
            }

            return;
        }

        paragraph.Remove();
    }

    /// <summary>A deleted paragraph as a tracked change: every run becomes a deleted run and the paragraph mark is deleted too.</summary>
    private static void MarkDeleted(Paragraph paragraph, Ctx ctx)
    {
        foreach (var run in paragraph.Descendants<Run>().Where(r => !r.Ancestors<DeletedRun>().Any() && r.Ancestors<Paragraph>().First() == paragraph).ToList())
        {
            foreach (var text in run.Elements<Text>().ToList())
            {
                run.ReplaceChild(new DeletedText(text.Text) { Space = SpaceProcessingModeValues.Preserve }, text);
            }

            var wrapper = new DeletedRun { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) };
            run.InsertBeforeSelf(wrapper);
            run.Remove();
            wrapper.AppendChild(run);
        }

        MarkParagraphMark(paragraph, new Deleted { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) });
    }

    private static DateTime ParseDate(string iso) => DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>Puts an insertion or deletion mark into the run properties of the paragraph mark, where the schema wants it (before a section break or a change record).</summary>
    private static void MarkParagraphMark(Paragraph paragraph, OpenXmlElement mark)
    {
        var properties = paragraph.ParagraphProperties ?? paragraph.PrependChild(new ParagraphProperties());
        var markProperties = properties.GetFirstChild<ParagraphMarkRunProperties>();
        if (markProperties is null)
        {
            markProperties = new ParagraphMarkRunProperties();
            var anchor = properties.ChildElements.FirstOrDefault(c => c is SectionProperties or ParagraphPropertiesChange);
            if (anchor is not null)
            {
                anchor.InsertBeforeSelf(markProperties);
            }
            else
            {
                properties.AppendChild(markProperties);
            }
        }

        markProperties.PrependChild(mark);
    }

    private static Paragraph NewParagraph(string text, Paragraph? neighbour, Ctx ctx)
    {
        var paragraph = new Paragraph();
        if (neighbour?.ParagraphProperties is { } source)
        {
            var properties = (ParagraphProperties)source.CloneNode(true);
            // A new paragraph is not a page break, a section end, a list item or a heading just because the paragraph next to it is.
            foreach (var drop in properties.ChildElements.Where(c => c is PageBreakBefore or SectionProperties or NumberingProperties or ParagraphMarkRunProperties or ParagraphPropertiesChange or ParagraphStyleId).ToList())
            {
                drop.Remove();
            }

            paragraph.AppendChild(properties);
        }

        var format = neighbour is null ? null : ParagraphItems.Of(neighbour).Texts.LastOrDefault()?.Format;
        if (ctx.Tracked)
        {
            MarkParagraphMark(paragraph, new Inserted { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) });
        }

        if (text.Length > 0)
        {
            AppendInserted(paragraph, text, format, ctx);
        }

        return paragraph;
    }

    private static void AppendInserted(OpenXmlElement parent, string text, RunProperties? format, Ctx ctx)
    {
        var run = MakeRun(text, format, deleted: false);
        if (ctx.Tracked)
        {
            var wrapper = new InsertedRun { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) };
            wrapper.AppendChild(run);
            parent.AppendChild(wrapper);
        }
        else
        {
            parent.AppendChild(run);
        }
    }

    /// <summary>A run with this text: tabs, line breaks and hyphens become their Word elements.</summary>
    internal static Run MakeRun(string text, RunProperties? format, bool deleted)
    {
        var run = new Run();
        if (format is not null)
        {
            run.AppendChild((RunProperties)format.CloneNode(true));
        }

        var buffer = new System.Text.StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            if (deleted)
            {
                run.AppendChild(new DeletedText(buffer.ToString()) { Space = SpaceProcessingModeValues.Preserve });
            }
            else
            {
                run.AppendChild(new Text(buffer.ToString()) { Space = SpaceProcessingModeValues.Preserve });
            }

            buffer.Clear();
        }

        foreach (var c in text)
        {
            if (c == '\t')
            {
                Flush();
                run.AppendChild(new TabChar());
            }
            else if (c == TextMetrics.LineBreak)
            {
                Flush();
                run.AppendChild(new Break());
            }
            else if (c == ParagraphItems.NoBreakHyphenChar)
            {
                Flush();
                run.AppendChild(new NoBreakHyphen());
            }
            else if (c == ParagraphItems.SoftHyphenChar)
            {
                Flush();
                run.AppendChild(new SoftHyphen());
            }
            else
            {
                buffer.Append(c);
            }
        }

        Flush();
        return run;
    }

    /// <summary>
    /// Replaces the content of a paragraph by the compared words. The words that stay get the formatting of the run they were in; deleted words stay as
    /// deleted runs; inserted words take the formatting of the word in front of them.
    /// </summary>
    private static void Rewrite(Paragraph paragraph, IReadOnlyList<DiffSegment> segments, Ctx ctx)
    {
        var items = ParagraphItems.Of(paragraph);
        var oldText = items.Text;
        var expected = string.Concat(segments.Where(s => s.Kind != EditKind.Insert).Select(s => s.Text));
        if (!string.Equals(oldText, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Der Absatz im Word-Dokument passt nicht zum Text der alten Fassung.");
        }

        // Where each piece of text starts: the formatting at a position is found by walking this list.
        var starts = new List<int>();
        var position = 0;
        foreach (var t in items.Texts)
        {
            starts.Add(position);
            position += t.Text.Length;
        }

        var content = new List<OpenXmlElement>();
        var opaque = items.Opaque.OrderBy(o => o.Offset).ToList();
        var opaqueIndex = 0;
        var offset = 0;

        void EmitOpaqueUpTo(int limit)
        {
            while (opaqueIndex < opaque.Count && opaque[opaqueIndex].Offset <= limit)
            {
                content.Add(opaque[opaqueIndex].Element.CloneNode(true));
                opaqueIndex++;
            }
        }

        // The text item that holds a position (the one before it for the end of the text).
        int ItemAt(int pos)
        {
            for (var i = items.Texts.Count - 1; i >= 0; i--)
            {
                if (starts[i] <= pos)
                {
                    return i;
                }
            }

            return -1;
        }

        void EmitOld(string text, bool deleted)
        {
            var remaining = text;
            while (remaining.Length > 0)
            {
                var index = ItemAt(offset);
                var item = items.Texts[index];
                var take = Math.Min(remaining.Length, starts[index] + item.Text.Length - offset);
                var piece = remaining[..take];
                EmitOpaqueUpTo(offset);
                content.Add(Wrap(MakeRun(piece, item.Format, deleted), item.Container, deleted, ctx));
                remaining = remaining[take..];
                offset += take;
            }
        }

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case EditKind.Equal:
                    EmitOld(segment.Text, deleted: false);
                    break;

                case EditKind.Delete:
                    if (ctx.Tracked)
                    {
                        EmitOld(segment.Text, deleted: true);
                    }
                    else
                    {
                        // Gone in the clean version: the position moves on, what sat in between (a bookmark) stays.
                        EmitOpaqueUpTo(offset + segment.Text.Length - 1);
                        offset += segment.Text.Length;
                    }

                    break;

                default:
                    var formatIndex = ItemAt(Math.Max(offset - 1, 0));
                    var format = formatIndex >= 0 ? items.Texts[formatIndex].Format : null;
                    EmitOpaqueUpTo(offset - 1);
                    var run = MakeRun(segment.Text, format, deleted: false);
                    if (ctx.Tracked)
                    {
                        var wrapper = new InsertedRun { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) };
                        wrapper.AppendChild(run);
                        content.Add(wrapper);
                    }
                    else
                    {
                        content.Add(run);
                    }

                    break;
            }
        }

        EmitOpaqueUpTo(int.MaxValue);

        foreach (var child in paragraph.ChildElements.Where(c => c is not ParagraphProperties).ToList())
        {
            child.Remove();
        }

        foreach (var element in content)
        {
            paragraph.AppendChild(element);
        }
    }

    /// <summary>Puts a run back into the element it was in (a hyperlink); a deleted run goes into a deletion mark.</summary>
    private static OpenXmlElement Wrap(Run run, OpenXmlElement? container, bool deleted, Ctx ctx)
    {
        OpenXmlElement result = run;
        if (deleted)
        {
            var mark = new DeletedRun { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) };
            mark.AppendChild(run);
            result = mark;
        }

        if (container is null or InsertedRun or MoveToRun)
        {
            // An earlier insertion by somebody else is kept as the plain text it is now: this program's own marks are the only revisions it writes.
            return result;
        }

        var wrapper = container.CloneNode(false);
        wrapper.AppendChild(result);
        return wrapper;
    }
}
