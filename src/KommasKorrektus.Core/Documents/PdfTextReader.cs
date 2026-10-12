using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace KommasKorrektus.Core.Documents;

public sealed record TextImport(IReadOnlyList<string> Paragraphs, int Pages, IReadOnlyList<string> Notes)
{
    /// <summary>The remarks that were in the file (notes and marked passages of a PDF); a text file has none.</summary>
    public IReadOnlyList<ImportedComment> Comments { get; init; } = [];
}

/// <summary>
/// The text of a PDF, as paragraphs. A PDF knows letters at places, not paragraphs, so lines are put together from the words and the paragraphs are
/// guessed from the gaps between lines, first-line indents, short last lines and hyphens at the end of a line. It is good for a manuscript that was
/// exported from a word processor; the layout (columns, tables, footnotes) is not kept. The import says so.
/// </summary>
public static partial class PdfTextReader
{
    private sealed record Line(double Left, double Right, double Top, double Bottom, double Baseline, double FontSize, string Text);

    private const int MaxQuote = 200;

    [GeneratedRegex(@"^(?:[-–—]\s*)?(?:Seite\s+)?\d{1,4}(?:\s*[-–—])?$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();

    [GeneratedRegex(@"[.!?…:“”»«""'’)\]]$")]
    private static partial Regex EndsSentence();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ManySpaces();

    public static TextImport Read(byte[] pdf)
    {
        var notes = new List<string>();
        var paragraphs = new List<string>();
        var comments = new List<ImportedComment>();
        var current = new StringBuilder();
        Line? previous = null;
        var pageCount = 0;

        void Close()
        {
            var text = ManySpaces().Replace(current.ToString().Trim(), " ");
            if (text.Length > 0)
            {
                paragraphs.Add(text);
            }

            current.Clear();
        }

        using var document = PdfDocument.Open(pdf);
        foreach (var page in document.GetPages())
        {
            pageCount++;
            var lines = BuildLines(page);
            if (lines.Count == 0)
            {
                continue;
            }

            var sizes = lines.Select(l => l.FontSize).Order().ToList();
            var medianHeight = sizes[sizes.Count / 2];
            var pitches = lines.Zip(lines.Skip(1), (a, b) => a.Baseline - b.Baseline).Where(p => p > 0).Order().ToList();
            var medianPitch = pitches.Count == 0 ? medianHeight * 1.3 : pitches[pitches.Count / 2];
            var leftMargin = lines.Select(l => Math.Round(l.Left)).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
            var maxRight = lines.Max(l => l.Right);

            Line? before = null;
            var placed = new List<(Line Line, int Paragraph)>();   // the paragraph each line of this page ended up in: where a remark on the page belongs
            foreach (var line in lines)
            {
                var isFirstOnPage = before is null;
                bool startNew;
                if (previous is null)
                {
                    startNew = true;
                }
                else if (isFirstOnPage)
                {
                    // A paragraph goes on over the page break when it was cut in the middle of a sentence.
                    startNew = EndsSentence().IsMatch(current.ToString().TrimEnd()) || !StartsLower(line.Text);
                }
                else
                {
                    var pitch = previous.Baseline - line.Baseline;
                    var indent = line.Left - leftMargin > 0.8 * medianHeight;
                    var shortLine = previous.Right < leftMargin + 0.75 * (maxRight - leftMargin) && EndsSentence().IsMatch(previous.Text);
                    var sizeChange = Math.Abs(line.FontSize - previous.FontSize) > 0.12 * medianHeight;
                    startNew = pitch > 1.35 * medianPitch || indent || shortLine || sizeChange;
                }

                if (startNew)
                {
                    Close();
                    current.Append(line.Text);
                }
                else if (current.Length > 0 && current[^1] == '-' && current.Length > 1 && char.IsLetter(current[^2]) && StartsLower(line.Text))
                {
                    // "Beschrei-" + "bung": the hyphen was only a line break. (A hyphen before a capital stays: "Müller-" + "Schmidt".)
                    current.Length--;
                    current.Append(line.Text);
                }
                else
                {
                    current.Append(' ').Append(line.Text);
                }

                previous = line;
                before = line;
                placed.Add((line, paragraphs.Count));
            }

            comments.AddRange(Annotations(page, placed, notes));
        }

        Close();

        if (paragraphs.Count == 0)
        {
            notes.Add("Das PDF enthält keinen Text, vermutlich ist es ein Scan. Bitte lade eine Word-Datei hoch oder lasse das PDF vorher texterkennen (OCR).");
        }
        else
        {
            notes.Add("Aus einem PDF wird nur der Text übernommen: Seitenlayout, Spalten, Tabellen und Fußnoten gehen verloren. Absätze wurden aus dem Zeilenabstand erkannt. Prüfe die Absatzgrenzen im Editor.");
        }

        return new TextImport(paragraphs, pageCount, notes) { Comments = comments };
    }

    private static readonly HashSet<AnnotationType> Remarks = [AnnotationType.Text, AnnotationType.FreeText, AnnotationType.Highlight, AnnotationType.Underline, AnnotationType.Squiggly, AnnotationType.StrikeOut, AnnotationType.Caret];

    private static readonly HashSet<AnnotationType> Markings = [AnnotationType.Highlight, AnnotationType.Underline, AnnotationType.Squiggly, AnnotationType.StrikeOut];

    private sealed record Box(double Left, double Right, double Bottom, double Top);

    private static Box BoxOf(QuadPointsQuadrilateral quad)
    {
        double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
        foreach (var p in quad.Points)
        {
            left = Math.Min(left, p.X);
            right = Math.Max(right, p.X);
            bottom = Math.Min(bottom, p.Y);
            top = Math.Max(top, p.Y);
        }

        return new Box(left, right, bottom, top);
    }

    /// <summary>
    /// The remarks written into a page of the PDF (sticky notes, text boxes, and notes on marked passages): the paragraph they are next to, the marked words as the quote,
    /// who wrote them and when. A mark without a note says nothing and is left out. Whatever cannot be read is left out too, the import goes on.
    /// </summary>
    private static List<ImportedComment> Annotations(Page page, List<(Line Line, int Paragraph)> lines, List<string> notes)
    {
        var found = new List<ImportedComment>();
        if (lines.Count == 0)
        {
            return found;
        }

        try
        {
            var annotations = page.GetAnnotations().Where(a => Remarks.Contains(a.Type) && !string.IsNullOrWhiteSpace(a.Content)).ToList();
            if (annotations.Count == 0)
            {
                return found;
            }

            var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            foreach (var annotation in annotations)
            {
                // an answer stands where the remark it answers stands
                var where = annotation.InReplyTo ?? annotation;
                var boxes = where.QuadPoints is { Count: > 0 } quads
                    ? quads.Select(BoxOf).ToList()
                    : [new Box(where.Rectangle.Left, where.Rectangle.Right, where.Rectangle.Bottom, where.Rectangle.Top)];

                string? quote = null;
                if (Markings.Contains(where.Type))
                {
                    var marked = words.Where(w => boxes.Any(b =>
                    {
                        var x = (w.BoundingBox.Left + w.BoundingBox.Right) / 2;
                        var y = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2;
                        return x >= b.Left && x <= b.Right && y >= b.Bottom && y <= b.Top;
                    })).OrderByDescending(w => Math.Round((w.BoundingBox.Top + w.BoundingBox.Bottom) / 2)).ThenBy(w => w.BoundingBox.Left).Select(w => w.Text);
                    quote = string.Join(' ', marked);
                    quote = quote.Length > MaxQuote ? quote[..MaxQuote].TrimEnd() + "…" : quote;
                }

                var top = boxes.Max(b => b.Top);
                var bottom = boxes.Min(b => b.Bottom);
                var left = boxes.Min(b => b.Left);
                var best = lines
                    .Select(l => (l.Paragraph, Overlap: Math.Min(top, l.Line.Top) - Math.Max(bottom, l.Line.Bottom), Gap: Math.Abs((top + bottom) / 2 - (l.Line.Top + l.Line.Bottom) / 2), Side: Math.Abs(left - l.Line.Left)))
                    .OrderByDescending(l => l.Overlap > 0)
                    .ThenBy(l => l.Overlap > 0 ? l.Side : l.Gap)
                    .First();

                var author = Entry(annotation, "T");
                found.Add(new ImportedComment(best.Paragraph, string.IsNullOrWhiteSpace(quote) ? null : quote, annotation.Content!.Trim(), string.IsNullOrWhiteSpace(author) ? null : author.Trim(), PdfDate(annotation.ModifiedDate) ?? PdfDate(Entry(annotation, "M")), false));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            notes.Add($"Die Anmerkungen auf Seite {page.Number} des PDFs ließen sich nicht lesen.");
        }

        return found;
    }

    /// <summary>A text entry of the dictionary of an annotation (the author is /T, the date /M).</summary>
    private static string? Entry(Annotation annotation, string key)
        => annotation.AnnotationDictionary.Data.TryGetValue(key, out var token) ? (token as StringToken)?.Data ?? (token as HexToken)?.Data : null;

    [GeneratedRegex(@"^D?:?(?<y>\d{4})(?<m>\d{2})?(?<d>\d{2})?(?<h>\d{2})?(?<n>\d{2})?(?<s>\d{2})?")]
    private static partial Regex PdfDateParts();

    /// <summary>A date of a PDF ("D:20261012143000+02'00'"); the time zone is not applied: the moment is shown as it was written.</summary>
    private static DateTime? PdfDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || PdfDateParts().Match(value.Trim()) is not { Success: true } m)
        {
            return null;
        }

        int Part(string name, int fallback) => m.Groups[name].Success ? int.Parse(m.Groups[name].Value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        try
        {
            return new DateTime(Part("y", 1), Part("m", 1), Part("d", 1), Part("h", 0), Part("n", 0), Part("s", 0), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool StartsLower(string text) => text.Length > 0 && char.IsLower(text[0]);

    private static List<Line> BuildLines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var pageHeight = page.Height;
        var rows = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2).ThenBy(w => w.BoundingBox.Left))
        {
            var centre = (word.BoundingBox.Top + word.BoundingBox.Bottom) / 2;
            var row = rows.LastOrDefault();
            if (row is not null)
            {
                var rowCentre = row.Average(w => (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2);
                var height = Math.Max(row.Average(w => w.BoundingBox.Height), word.BoundingBox.Height);
                if (Math.Abs(centre - rowCentre) < 0.5 * height)
                {
                    row.Add(word);
                    continue;
                }
            }

            rows.Add([word]);
        }

        var lines = new List<Line>();
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(w => w.BoundingBox.Left).ToList();
            var text = string.Join(' ', ordered.Select(w => w.Text));
            var top = ordered.Max(w => w.BoundingBox.Top);
            var bottom = ordered.Min(w => w.BoundingBox.Bottom);
            var letters = ordered.SelectMany(w => w.Letters).ToList();
            var fontSize = letters.Count == 0 ? ordered.Average(w => w.BoundingBox.Height) : letters.Average(l => l.PointSize);
            var baseline = letters.Count == 0 ? bottom : letters.Average(l => l.StartBaseLine.Y);
            var line = new Line(ordered[0].BoundingBox.Left, ordered[^1].BoundingBox.Right, top, bottom, baseline, fontSize, text);

            // Page numbers and the like at the very top or bottom are no part of the text.
            var nearEdge = bottom < pageHeight * 0.08 || top > pageHeight * 0.94;
            if (nearEdge && PageNumber().IsMatch(text.Trim()))
            {
                continue;
            }

            lines.Add(line);
        }

        return lines;
    }
}

/// <summary>A plain text file as paragraphs: separated by blank lines when there are any, else one paragraph per line.</summary>
public static class PlainTextReader
{
    public static TextImport Read(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var hasBlankLines = lines.Any(l => l.Trim().Length == 0) && lines.Count(l => l.Trim().Length > 0) > 1;
        var paragraphs = new List<string>();
        if (hasBlankLines)
        {
            var block = new List<string>();
            foreach (var line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    if (block.Count > 0)
                    {
                        paragraphs.Add(string.Join(' ', block));
                        block.Clear();
                    }
                }
                else
                {
                    block.Add(line.Trim());
                }
            }

            if (block.Count > 0)
            {
                paragraphs.Add(string.Join(' ', block));
            }
        }
        else
        {
            paragraphs.AddRange(lines.Select(l => l.TrimEnd()).Where(l => l.Length > 0));
        }

        return new TextImport(paragraphs, 0, []);
    }
}
