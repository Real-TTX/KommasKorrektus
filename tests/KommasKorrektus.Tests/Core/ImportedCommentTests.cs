using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2013.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Documents;

namespace KommasKorrektus.Tests.Core;

/// <summary>The remarks that are already in a file the author or the customer sent (comments of Word, notes of a PDF) are read with the text.</summary>
public sealed class ImportedCommentTests
{
    private static Run Run(string text) => new(new DocumentFormat.OpenXml.Wordprocessing.Text(text) { Space = SpaceProcessingModeValues.Preserve });

    private static DocumentFormat.OpenXml.Wordprocessing.Comment Remark(string id, string author, string text, string paraId, DateTime? date = null)
        => new(new Paragraph(new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(text))) { ParagraphId = paraId }) { Id = id, Author = author, Initials = "XX", Date = date };

    public static byte[] WordWithComments()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(Run("Erster Absatz ohne Kommentar.")),
                // a comment on a passage, and an answer to it that has no place of its own
                new Paragraph(Run("Zweiter Absatz mit "), new CommentRangeStart { Id = "0" }, Run("einem markierten Satz"), new CommentRangeEnd { Id = "0" }, new Run(new CommentReference { Id = "0" }), Run(", der weitergeht.")),
                // a range that goes over a paragraph break: the passage is what stands in the first one
                new Paragraph(new CommentRangeStart { Id = "2" }, Run("Dritter Absatz, der weitergeht")),
                new Paragraph(Run("und im vierten endet."), new CommentRangeEnd { Id = "2" }, new Run(new CommentReference { Id = "2" })),
                // a mark without a range, and a comment without words
                new Paragraph(Run("Fünfter Absatz mit einer Marke."), new Run(new CommentReference { Id = "3" }), new Run(new CommentReference { Id = "4" }))));

            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new Comments(
                Remark("0", "Maria Autorin", "Hier fehlt ein Komma.", "10000001", new DateTime(2026, 10, 12, 14, 30, 0, DateTimeKind.Utc)),
                Remark("1", "Lektor", "Ist ergänzt.", "10000002"),
                Remark("2", "Maria Autorin", "Bitte kürzen.", "10000003"),
                Remark("3", "Lektor", "Nur eine Marke.", "10000004"),
                Remark("4", "Lektor", "", "10000005"));

            var extended = main.AddNewPart<WordprocessingCommentsExPart>();
            extended.CommentsEx = new CommentsEx(
                new CommentEx { ParaId = "10000002", ParaIdParent = "10000001" },
                new CommentEx { ParaId = "10000003", Done = true });
            main.Document.Save();
        }

        return stream.ToArray();
    }

    [Fact]
    public void Comments_of_a_Word_file_come_with_their_paragraph_passage_author_and_state()
    {
        var docx = WordWithComments();
        var paragraphs = DocxReader.ReadParagraphs(docx);
        var comments = DocxComments.Read(docx);

        Assert.Equal(5, paragraphs.Count);
        // the comment without words is left out; the others are in the order of the text, an answer after the comment it answers
        Assert.Equal(["Hier fehlt ein Komma.", "Ist ergänzt.", "Bitte kürzen.", "Nur eine Marke."], comments.Select(c => c.Text));

        var first = comments[0];
        Assert.Equal(1, first.ParagraphIndex);
        Assert.Equal("einem markierten Satz", first.Quote);
        Assert.Equal("Maria Autorin", first.Author);
        Assert.Equal(new DateTime(2026, 10, 12, 14, 30, 0, DateTimeKind.Utc), first.Written);
        Assert.False(first.Resolved);

        // an answer without a place of its own stands where the comment stands that it answers
        Assert.Equal(1, comments[1].ParagraphIndex);
        Assert.Equal("einem markierten Satz", comments[1].Quote);
        Assert.Equal("Lektor", comments[1].Author);

        // over a paragraph break: the paragraph where it begins, the words that are there; marked as done
        Assert.Equal(2, comments[2].ParagraphIndex);
        Assert.Equal("Dritter Absatz, der weitergeht", comments[2].Quote);
        Assert.True(comments[2].Resolved);

        Assert.Equal(4, comments[3].ParagraphIndex);
        Assert.Null(comments[3].Quote);
    }

    [Fact]
    public void A_Word_file_without_comments_has_none()
    {
        Assert.Empty(DocxComments.Read(DocxBuilder.FromParagraphs(["Ein Absatz.", "Noch einer."])));
    }

    // ------------------------------------------------------------------------------------------------ PDF

    private static string Hex(string text) => "<FEFF" + string.Concat(text.Select(c => ((int)c).ToString("X4"))) + ">";

    /// <summary>A PDF made by hand: two paragraphs of text, a marked passage with a note, a mark without a note, and a sticky note by the second paragraph.</summary>
    public static byte[] PdfWithNotes()
    {
        string[] first = ["Der Nebel kam ueber die Wiesen und legte sich auf die Daecher", "des kleinen Dorfes und alle Menschen blieben lange in ihren", "Haeusern bis die Sonne den Dunst endlich wieder aufloeste und"];
        string[] second = ["Am naechsten Morgen klopfte es an der Tuer und draussen stand", "ein Rabe mit einem Brief im Schnabel und sah sehr ernst aus"];
        var content = new StringBuilder();
        var y = 700;
        foreach (var line in first)
        {
            content.Append($"BT /F1 12 Tf 72 {y} Td ({line}) Tj ET\n");
            y -= 14;
        }

        y = 640;
        foreach (var line in second)
        {
            content.Append($"BT /F1 12 Tf 72 {y} Td ({line}) Tj ET\n");
            y -= 14;
        }

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 7 0 R >> >> /Annots [5 0 R 6 0 R 8 0 R] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
            $"<< /Type /Annot /Subtype /Highlight /Rect [72 698 200 712] /QuadPoints [72 712 200 712 72 698 200 698] /Contents {Hex("Prüfe die Schreibweise.")} /T (Maria Autorin) /M (D:20261012143000+02'00') /F 4 >>",
            $"<< /Type /Annot /Subtype /Text /Rect [530 628 550 648] /Contents {Hex("Hier fehlt ein Absatz davor.")} /T (Lektor) /F 4 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Annot /Subtype /Highlight /Rect [72 684 200 698] /QuadPoints [72 698 200 698 72 684 200 684] /F 4 >>",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    [Fact]
    public void Notes_of_a_PDF_come_with_the_marked_words_and_the_paragraph_they_are_next_to()
    {
        var import = PdfTextReader.Read(PdfWithNotes());

        Assert.Equal(2, import.Paragraphs.Count);
        // the mark without a note says nothing and is left out
        Assert.Equal(2, import.Comments.Count);

        var marked = import.Comments[0];
        Assert.Equal("Prüfe die Schreibweise.", marked.Text);
        Assert.Equal(0, marked.ParagraphIndex);
        Assert.StartsWith("Der Nebel kam", marked.Quote);
        Assert.DoesNotContain("Wiesen", marked.Quote);
        Assert.Equal("Maria Autorin", marked.Author);
        Assert.Equal(new DateTime(2026, 10, 12, 14, 30, 0, DateTimeKind.Utc), marked.Written);

        var sticky = import.Comments[1];
        Assert.Equal("Hier fehlt ein Absatz davor.", sticky.Text);
        Assert.Equal(1, sticky.ParagraphIndex);
        Assert.Null(sticky.Quote);
        Assert.Equal("Lektor", sticky.Author);
    }

    [Fact]
    public void A_PDF_without_notes_has_no_comments()
    {
        Assert.Empty(PdfTextReader.Read(PdfWithoutNotes()).Comments);
    }

    private static byte[] PdfWithoutNotes()
    {
        var text = Encoding.Latin1.GetString(PdfWithNotes()).Replace(" /Annots [5 0 R 6 0 R 8 0 R]", string.Empty);
        return Encoding.Latin1.GetBytes(text);
    }
}
