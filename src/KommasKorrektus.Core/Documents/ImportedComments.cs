using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Documents;

/// <summary>
/// A remark that was already in the file the author (or the customer) sent: a comment of Word, a note or a marked passage of a PDF. <see cref="ParagraphIndex"/> counts the
/// paragraphs of the text that was read from the file, <see cref="Quote"/> is the passage it was written on (so that it is found again when the text has moved).
/// </summary>
public sealed record ImportedComment(int ParagraphIndex, string? Quote, string Text, string? Author, DateTime? Written, bool Resolved);

/// <summary>The comments of a Word document, with the passage each is anchored to and who wrote it when. Replies are comments of their own at the place of the one they answer.</summary>
public static partial class DocxComments
{
    private const int MaxQuote = 200;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Blanks();

    private sealed class Anchor(int paragraph)
    {
        public int Paragraph { get; } = paragraph;

        public StringBuilder Quote { get; } = new();

    }

    public static IReadOnlyList<ImportedComment> Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        var comments = main?.WordprocessingCommentsPart?.Comments?.Elements<Comment>().ToList();
        var body = main?.Document?.Body;
        if (comments is null || comments.Count == 0 || body is null)
        {
            return [];
        }

        // Where each comment sits: the paragraph its range starts in (or, without a range, its mark), and the words inside the range.
        var anchors = new Dictionary<string, Anchor>(StringComparer.Ordinal);
        var open = new List<Anchor>();   // the ranges that are open at this point of the text (a few, not all of them: a text can have thousands of comments)
        var paragraphs = DocxReader.EnumerateParagraphs(body);
        for (var index = 0; index < paragraphs.Count; index++)
        {
            foreach (var node in paragraphs[index].Descendants())
            {
                switch (node)
                {
                    case CommentRangeStart start when start.Id?.Value is { } id:
                        open.Remove(anchors.GetValueOrDefault(id)!);
                        anchors[id] = new Anchor(index);
                        open.Add(anchors[id]);
                        break;
                    case CommentRangeEnd end when end.Id?.Value is { } id && anchors.TryGetValue(id, out var closing):
                        open.Remove(closing);
                        break;
                    case CommentReference mark when mark.Id?.Value is { } id && !anchors.ContainsKey(id):
                        anchors[id] = new Anchor(index);
                        break;
                    case Text text:
                        Collect(open, index, text.Text);
                        break;
                    case TabChar:
                        Collect(open, index, " ");
                        break;
                }
            }
        }

        // The answers (Word 2013 and later): which comment answers which, and which are done.
        var extended = main!.WordprocessingCommentsExPart?.CommentsEx?.Elements<DocumentFormat.OpenXml.Office2013.Word.CommentEx>().ToList() ?? [];
        var byParagraph = comments
            .Select(c => (Comment: c, ParaId: c.Elements<Paragraph>().LastOrDefault()?.ParagraphId?.Value))
            .Where(x => x.ParaId is not null)
            .ToDictionary(x => x.ParaId!, x => x.Comment, StringComparer.OrdinalIgnoreCase);
        var done = extended.Where(e => e.Done?.Value == true && e.ParaId?.Value is not null).Select(e => e.ParaId!.Value!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parentOf = extended
            .Where(e => e.ParaId?.Value is not null && e.ParaIdParent?.Value is not null && byParagraph.ContainsKey(e.ParaId.Value) && byParagraph.ContainsKey(e.ParaIdParent.Value))
            .ToDictionary(e => byParagraph[e.ParaId!.Value!].Id!.Value!, e => byParagraph[e.ParaIdParent!.Value!].Id!.Value!, StringComparer.Ordinal);

        var result = new List<(int Order, ImportedComment Comment)>();
        for (var order = 0; order < comments.Count; order++)
        {
            var comment = comments[order];
            var id = comment.Id?.Value;
            var text = TextOf(comment);
            if (id is null || text.Length == 0)
            {
                continue;
            }

            var anchor = anchors.GetValueOrDefault(id);
            for (var up = id; anchor is null && parentOf.TryGetValue(up, out var parent); up = parent)
            {
                anchor = anchors.GetValueOrDefault(parent);
            }

            var paraId = comment.Elements<Paragraph>().LastOrDefault()?.ParagraphId?.Value;
            var quote = anchor is null ? null : Blanks().Replace(anchor.Quote.ToString(), " ").Trim();
            if (quote is { Length: > MaxQuote })
            {
                quote = quote[..MaxQuote].TrimEnd() + "…";
            }

            DateTime? written = comment.Date?.Value is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
            result.Add((order, new ImportedComment(anchor?.Paragraph ?? 0, string.IsNullOrEmpty(quote) ? null : quote, text, comment.Author?.Value, written, paraId is not null && done.Contains(paraId))));
        }

        return [.. result.OrderBy(r => r.Comment.ParagraphIndex).ThenBy(r => r.Order).Select(r => r.Comment)];
    }

    private static void Collect(List<Anchor> open, int paragraph, string text)
    {
        foreach (var anchor in open)
        {
            if (anchor.Paragraph == paragraph && anchor.Quote.Length < MaxQuote)
            {
                anchor.Quote.Append(text);
            }
        }
    }

    private static string TextOf(Comment comment)
    {
        var lines = comment.Descendants<Paragraph>().Select(p =>
        {
            var sb = new StringBuilder();
            foreach (var node in p.Descendants())
            {
                switch (node)
                {
                    case Text t:
                        sb.Append(t.Text);
                        break;
                    case TabChar:
                        sb.Append(' ');
                        break;
                    case Break or CarriageReturn:
                        sb.Append(' ');
                        break;
                }
            }

            return sb.ToString().Trim();
        });
        return string.Join('\n', lines.Where(l => l.Length > 0)).Replace(TextMetrics.LineBreak, ' ').Trim();
    }
}
