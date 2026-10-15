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
    /// <param name="comments">Remarks that are written into the file as comments of Word, on the words they are about; the comments the file already has stay.</param>
    public static byte[] Write(byte[]? baseDocx, IReadOnlyList<string> oldParagraphs, IReadOnlyList<string> newParagraphs, RevisionMode mode, string author, DateTime dateUtc, IReadOnlyList<DocxComment>? comments = null)
    {
        var source = baseDocx is not null && DocxReader.Matches(baseDocx, oldParagraphs) ? baseDocx : DocxBuilder.FromParagraphs(oldParagraphs);
        using var stream = new MemoryStream();
        stream.Write(source);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            var written = Apply(doc, DocumentDiff.Compare(oldParagraphs, newParagraphs), oldParagraphs, mode, author, dateUtc);
            if (comments is { Count: > 0 })
            {
                DocxCommentWriter.Add(doc, written, newParagraphs, comments);
            }

            doc.MainDocumentPart!.Document!.Save();
        }

        return stream.ToArray();
    }

    private sealed record Ctx(bool Tracked, string Author, string Date, Counter Ids)
    {
        public Ctx(RevisionMode mode, string author, DateTime date, long firstId)
            : this(mode == RevisionMode.Tracked, author, date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), new Counter(firstId))
        {
        }

        public string NextId() => Ids.Next().ToString(CultureInfo.InvariantCulture);
    }

    private sealed class Counter(long start)
    {
        private long _value = start;

        public long Next() => ++_value;
    }

    /// <summary>
    /// Writes the changes into the document. A row of the comparison is a paragraph of Word (see <see cref="ParagraphDiff"/>): a paragraph that was split is written as the old
    /// paragraph with a paragraph mark that was put in and a new paragraph with the rest of its text; paragraphs that were joined keep their paragraphs with the mark between them
    /// taken away (tracked), or are one paragraph (the clean version: the text joins the first one and takes its look, as on the page of the editor).
    /// Returns which paragraph of the document is which paragraph of the new text (a deleted paragraph is none of them; a joined one is its first part).
    /// </summary>
    private static Dictionary<int, Paragraph> Apply(WordprocessingDocument doc, IReadOnlyList<ParagraphDiff> diff, IReadOnlyList<string> oldParagraphs, RevisionMode mode, string author, DateTime dateUtc)
    {
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = DocxReader.EnumerateParagraphs(body);
        var firstId = body.Descendants().OfType<OpenXmlElement>()
            .Select(e => e.GetAttributes().FirstOrDefault(a => a.LocalName == "id" && a.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main").Value)
            .Where(v => long.TryParse(v, out _)).Select(v => long.Parse(v!)).DefaultIfEmpty(0).Max();
        var ctx = new Ctx(mode, author, dateUtc, Math.Max(firstId, 1000));
        var written = new Dictionary<int, Paragraph>();
        var rowsOf = diff.Where(d => d.OldIndex is not null).GroupBy(d => d.OldIndex!.Value).ToDictionary(g => g.Key, g => g.Count());
        var open = new Dictionary<int, OldParagraph>();   // old paragraphs that are written over several rows (split), and how far
        var targets = new Paragraph?[diff.Count];          // the paragraph of the document each row went to

        Paragraph? previous = null;
        for (var i = 0; i < diff.Count; i++)
        {
            var d = diff[i];
            if (d.Change == ParagraphChange.Unchanged)
            {
                previous = paragraphs[d.OldIndex!.Value];
                targets[i] = previous;
                written.TryAdd(d.NewIndex!.Value, previous);
                continue;
            }

            if (d.Change == ParagraphChange.Deleted && rowsOf[d.OldIndex!.Value] == 1)
            {
                // a paragraph that is gone as a whole
                var deleted = paragraphs[d.OldIndex.Value];
                if (ctx.Tracked)
                {
                    MarkDeleted(deleted, ctx);
                    previous = deleted;
                    targets[i] = deleted;
                }
                else
                {
                    RemoveParagraph(deleted);
                }

                continue;
            }

            if (d.OldIndex is null)
            {
                // A paragraph that is new: it goes in front of the next paragraph that exists, or after the one before when there is none.
                var next = NextExisting(diff, i, paragraphs);
                var inserted = NewParagraph(d.NewText!, previous ?? next, ctx);
                Place(body, inserted, previous, next);
                previous = inserted;
                targets[i] = inserted;
                written.TryAdd(d.NewIndex!.Value, inserted);
                continue;
            }

            // (a part of) an old paragraph: the first part stays in the paragraph of the document, a part after a break that was put in is a new paragraph after the one before
            var oldIndex = d.OldIndex.Value;
            if (!open.TryGetValue(oldIndex, out var source))
            {
                source = new OldParagraph(paragraphs[oldIndex], oldParagraphs[oldIndex], rowsOf[oldIndex]);
                open[oldIndex] = source;
            }

            Paragraph target;
            if (source.Done == 0)
            {
                target = source.Element;
            }
            else
            {
                target = NewParagraph(string.Empty, source.Element, ctx with { Tracked = false });
                (previous ?? source.Element).InsertAfterSelf(target);
            }

            source.Write(target, d.Segments, ctx);
            if (ctx.Tracked && d.Mark == EditKind.Insert)
            {
                MarkParagraphMark(target, new Inserted { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) });
            }
            else if (ctx.Tracked && d.Mark == EditKind.Delete)
            {
                MarkParagraphMark(target, new Deleted { Id = ctx.NextId(), Author = ctx.Author, Date = ParseDate(ctx.Date) });
            }

            previous = target;
            targets[i] = target;
            if (d.NewIndex is { } newIndex)
            {
                written.TryAdd(newIndex, target);
            }
        }

        if (!ctx.Tracked)
        {
            // The clean version: a break that was taken away joins the row with the next one (from the end, so that three joined paragraphs become one).
            for (var i = diff.Count - 1; i >= 0; i--)
            {
                if (diff[i].Mark != EditKind.Delete || diff[i].Change == ParagraphChange.Unchanged || targets[i] is not { } joined)
                {
                    continue;
                }

                if (diff[i].NewIndex is null)
                {
                    RemoveParagraph(joined);
                    continue;
                }

                var following = targets.Skip(i + 1).FirstOrDefault(t => t is not null);
                if (following is null || following.Parent != joined.Parent)
                {
                    continue;
                }

                foreach (var child in following.ChildElements.Where(c => c is not ParagraphProperties).ToList())
                {
                    child.Remove();
                    joined.AppendChild(child);
                }

                following.Remove();
                for (var k = i + 1; k < diff.Count; k++)
                {
                    if (targets[k] == following) { targets[k] = joined; }
                }

                foreach (var key in written.Where(w => w.Value == following).Select(w => w.Key).ToList())
                {
                    written[key] = joined;
                }
            }
        }

        return written;
    }

    /// <summary>A new paragraph goes in front of the next paragraph that exists when the one before is somewhere else (another table cell), else after the one before.</summary>
    private static void Place(Body body, Paragraph paragraph, Paragraph? previous, Paragraph? next)
    {
        if (next is not null && (previous is null || previous.Parent != next.Parent))
        {
            next.InsertBeforeSelf(paragraph);
        }
        else if (previous is not null)
        {
            previous.InsertAfterSelf(paragraph);
        }
        else
        {
            body.PrependChild(paragraph);
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
    /// An old paragraph of the document that gets the compared text, in one row or (when it was split) in several, one after the other: the text that stays gets the formatting
    /// of the run it was in, deleted text stays as deleted runs, inserted text takes the formatting of the text in front of it. Pictures, bookmarks and note marks go where
    /// their place in the text is.
    /// </summary>
    private sealed class OldParagraph
    {
        private readonly ParagraphItems _items;
        private readonly List<int> _starts = [];
        private readonly List<ParagraphItems.OpaqueItem> _opaque;
        private readonly int _rows;
        private int _opaqueIndex;
        private int _offset;
        private bool _cleared;

        public OldParagraph(Paragraph element, string expected, int rows)
        {
            Element = element;
            _items = ParagraphItems.Of(element);
            if (!string.Equals(_items.Text, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Der Absatz im Word-Dokument passt nicht zum Text der alten Fassung.");
            }

            // where each piece of text starts: the formatting at a position is found by walking this list
            var position = 0;
            foreach (var t in _items.Texts)
            {
                _starts.Add(position);
                position += t.Text.Length;
            }

            _opaque = [.. _items.Opaque.OrderBy(o => o.Offset)];
            _rows = rows;
        }

        public Paragraph Element { get; }

        /// <summary>How many rows of this paragraph are written.</summary>
        public int Done { get; private set; }

        /// <summary>Writes the next row of this paragraph into a paragraph of the document (the old one for the first row, a new one after it for the next ones).</summary>
        public void Write(Paragraph target, IReadOnlyList<DiffSegment> segments, Ctx ctx)
        {
            if (!_cleared)
            {
                foreach (var child in Element.ChildElements.Where(c => c is not ParagraphProperties).ToList())
                {
                    child.Remove();
                }

                _cleared = true;
            }

            var content = new List<OpenXmlElement>();
            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case EditKind.Equal:
                        EmitOld(content, segment.Text, deleted: false, ctx);
                        break;

                    case EditKind.Delete:
                        if (ctx.Tracked)
                        {
                            EmitOld(content, segment.Text, deleted: true, ctx);
                        }
                        else
                        {
                            // Gone in the clean version: the position moves on, what sat in between (a bookmark) stays.
                            EmitOpaqueUpTo(content, _offset + segment.Text.Length - 1);
                            _offset += segment.Text.Length;
                        }

                        break;

                    default:
                        var formatIndex = ItemAt(Math.Max(_offset - 1, 0));
                        var format = formatIndex >= 0 ? _items.Texts[formatIndex].Format : null;
                        EmitOpaqueUpTo(content, _offset - 1);
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

            Done++;
            if (Done == _rows)
            {
                EmitOpaqueUpTo(content, int.MaxValue);
            }

            foreach (var element in content)
            {
                target.AppendChild(element);
            }
        }

        private void EmitOpaqueUpTo(List<OpenXmlElement> content, int limit)
        {
            while (_opaqueIndex < _opaque.Count && _opaque[_opaqueIndex].Offset <= limit)
            {
                content.Add(_opaque[_opaqueIndex].Element.CloneNode(true));
                _opaqueIndex++;
            }
        }

        /// <summary>The text item that holds a position (the one before it for the end of the text).</summary>
        private int ItemAt(int pos)
        {
            for (var i = _items.Texts.Count - 1; i >= 0; i--)
            {
                if (_starts[i] <= pos)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EmitOld(List<OpenXmlElement> content, string text, bool deleted, Ctx ctx)
        {
            var remaining = text;
            while (remaining.Length > 0)
            {
                var index = ItemAt(_offset);
                var item = _items.Texts[index];
                var take = Math.Min(remaining.Length, _starts[index] + item.Text.Length - _offset);
                var piece = remaining[..take];
                EmitOpaqueUpTo(content, _offset);
                content.Add(Wrap(MakeRun(piece, item.Format, deleted), item.Container, deleted, ctx));
                remaining = remaining[take..];
                _offset += take;
            }
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
