using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Documents;

/// <summary>
/// A remark of the editor that goes into the Word file as a comment of Word: on the words it is about (<see cref="Quote"/>), or on the paragraph when those words are not
/// to be found any more. <see cref="ParagraphIndex"/> counts the paragraphs of the new text.
/// </summary>
public sealed record DocxComment(int ParagraphIndex, string? Quote, string Text, string Author, DateTime DateUtc);

/// <summary>
/// Writes comments into a Word document the way Word does: the part <c>comments.xml</c>, and in the text a range (<c>commentRangeStart</c>, <c>commentRangeEnd</c>) with a run that
/// holds the <c>commentReference</c>. The range begins and ends inside the runs: a run that holds more than the words is cut in two for it (a run that has anything but one text,
/// a tab or a line break for instance, is not cut: the range then takes the whole run). Comments the file already has stay as they are; the new ones get ids above them.
/// </summary>
internal static class DocxCommentWriter
{
    /// <summary>Puts the comments on the paragraphs they belong to. <paramref name="paragraphs"/> maps the number of a paragraph of the new text to its paragraph in the document.</summary>
    public static void Add(WordprocessingDocument doc, IReadOnlyDictionary<int, Paragraph> paragraphs, IReadOnlyList<string> newText, IReadOnlyList<DocxComment> comments)
    {
        var usable = comments.Where(c => !string.IsNullOrWhiteSpace(c.Text) && newText.Count > 0).ToList();
        if (usable.Count == 0)
        {
            return;
        }

        var main = doc.MainDocumentPart!;
        var part = main.WordprocessingCommentsPart ?? main.AddNewPart<WordprocessingCommentsPart>();
        part.Comments ??= new Comments();
        var next = part.Comments.Elements<Comment>().Select(c => long.TryParse(c.Id?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1).DefaultIfEmpty(-1).Max() + 1;

        // in the order of the text, so that the comments of one paragraph keep the order they were written in
        foreach (var comment in usable.Select((c, order) => (Comment: c, Order: order, Target: Locate(newText, c))).OrderBy(x => x.Target).ThenBy(x => x.Order))
        {
            if (!paragraphs.TryGetValue(comment.Target, out var paragraph))
            {
                continue;
            }

            var id = next.ToString(CultureInfo.InvariantCulture);
            next++;
            part.Comments.AppendChild(MakeComment(id, comment.Comment));
            Anchor(paragraph, Normalize(comment.Comment.Quote), id);
        }

        part.Comments.Save();
    }

    /// <summary>The paragraph a comment belongs to: where it was written, or the one that has its words when the text has moved.</summary>
    internal static int Locate(IReadOnlyList<string> text, DocxComment comment)
    {
        var index = Math.Clamp(comment.ParagraphIndex, 0, text.Count - 1);
        var quote = Normalize(comment.Quote);
        if (quote is null || text[index].Contains(quote, StringComparison.Ordinal))
        {
            return index;
        }

        for (var i = 0; i < text.Count; i++)
        {
            if (text[i].Contains(quote, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return index;
    }

    /// <summary>The words of a remark as the text has them (the editor writes a line break inside a paragraph as a new line).</summary>
    private static string? Normalize(string? quote)
        => string.IsNullOrWhiteSpace(quote) ? null : quote.Replace("\r\n", "\n").Replace('\n', TextMetrics.LineBreak);

    private static Comment MakeComment(string id, DocxComment source)
    {
        var comment = new Comment { Id = id, Author = source.Author, Initials = Initials(source.Author), Date = source.DateUtc };
        var lines = source.Text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var paragraph = new Paragraph();
            if (i == 0)
            {
                paragraph.AppendChild(new Run(new AnnotationReferenceMark()));
            }

            paragraph.AppendChild(new Run(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve }));
            comment.AppendChild(paragraph);
        }

        return comment;
    }

    private static string Initials(string author)
    {
        var letters = author.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray();
        return letters.Length == 0 ? "L" : new string(letters);
    }

    // ------------------------------------------------------------------------------------------------ where the range goes
    /// <summary>
    /// The runs of a paragraph that hold text, in reading order, each with where its text begins: the runs <see cref="ParagraphItems"/> reads text from (what is deleted, or sits in
    /// a content control, is not text of the paragraph), so that the positions are the positions of its text.
    /// </summary>
    private static List<(Run Run, int Start, int Length)> TextRuns(Paragraph paragraph)
    {
        var list = new List<(Run, int, int)>();
        var position = 0;
        foreach (var run in paragraph.Descendants<Run>().Where(r => r.Ancestors().TakeWhile(a => !ReferenceEquals(a, paragraph)).All(a => a is Hyperlink or InsertedRun or MoveToRun or SimpleField or CustomXmlRun)))
        {
            var length = LengthOf(run);
            if (length == 0)
            {
                continue;
            }

            list.Add((run, position, length));
            position += length;
        }

        return list;
    }

    /// <summary>How many characters of the text of the paragraph a run holds (the same count as <see cref="ParagraphItems"/> makes).</summary>
    private static int LengthOf(Run run)
    {
        var length = 0;
        foreach (var child in run.ChildElements)
        {
            length += child switch
            {
                Text t => t.Text.Length,
                TabChar or CarriageReturn or NoBreakHyphen or SoftHyphen => 1,
                Break b when b.Type is null || b.Type.Value == BreakValues.TextWrapping => 1,
                _ => 0,
            };
        }

        return length;
    }

    /// <summary>Cuts a run that is one text in two at a position of its text; the part after the cut is returned. Null when the run is more than that (it is left whole).</summary>
    private static Run? Split(Run run, int offset)
    {
        var texts = run.Elements<Text>().ToList();
        if (texts.Count != 1 || run.ChildElements.Any(c => c is not RunProperties and not Text))
        {
            return null;
        }

        var text = texts[0].Text;
        if (offset <= 0 || offset >= text.Length)
        {
            return null;
        }

        var right = (Run)run.CloneNode(true);
        var rightText = right.Elements<Text>().Single();
        rightText.Text = text[offset..];
        rightText.Space = SpaceProcessingModeValues.Preserve;
        texts[0].Text = text[..offset];
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        run.InsertAfterSelf(right);
        return right;
    }

    /// <summary>Marks the words of the quote (or the whole paragraph) as the range of the comment, and puts the reference where the range ends.</summary>
    private static void Anchor(Paragraph paragraph, string? quote, string id)
    {
        var runs = TextRuns(paragraph);
        var found = quote is null ? -1 : ParagraphItems.Of(paragraph).Text.IndexOf(quote, StringComparison.Ordinal);

        Run? first = null;
        Run? last = null;
        if (found >= 0 && runs.Count > 0)
        {
            var from = found;
            var to = found + quote!.Length;
            var startIndex = runs.FindIndex(r => from >= r.Start && from < r.Start + r.Length);
            var endIndex = runs.FindIndex(r => to > r.Start && to <= r.Start + r.Length);
            if (startIndex >= 0 && endIndex >= startIndex)
            {
                first = runs[startIndex].Run;
                last = runs[endIndex].Run;
                // the end first: cutting the start afterwards cannot move it
                if (to < runs[endIndex].Start + runs[endIndex].Length)
                {
                    Split(last, to - runs[endIndex].Start);
                }

                if (from > runs[startIndex].Start)
                {
                    var right = Split(first, from - runs[startIndex].Start);
                    if (right is not null)
                    {
                        if (ReferenceEquals(first, last))
                        {
                            last = right;
                        }

                        first = right;
                    }
                }
            }
        }

        var start = new CommentRangeStart { Id = id };
        var end = new CommentRangeEnd { Id = id };
        var reference = new Run(new CommentReference { Id = id });
        if (first is not null && last is not null)
        {
            first.InsertBeforeSelf(start);
            last.InsertAfterSelf(end);
            end.InsertAfterSelf(reference);
            return;
        }

        // the whole paragraph (after its properties)
        if (paragraph.ParagraphProperties is { } properties)
        {
            properties.InsertAfterSelf(start);
        }
        else
        {
            paragraph.PrependChild(start);
        }

        paragraph.AppendChild(end);
        paragraph.AppendChild(reference);
    }
}
