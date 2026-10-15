using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Tests.Core;

/// <summary>The remarks of the editor go into the Word file as comments of Word, on the words they are about; the file's own comments stay.</summary>
public class DocxCommentExportTests
{
    private static readonly DateTime When = new(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc);

    private static byte[] Write(byte[]? baseDocx, string[] old, string[] next, RevisionMode mode, params DocxComment[] comments)
        => DocxWriter.Write(baseDocx, old, next, mode, "Lektorat", When, comments);

    private static DocxComment Remark(int paragraph, string? quote, string text, string author = "Anna Lektorin") => new(paragraph, quote, text, author, When);

    private static List<Comment> CommentsOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        return [.. doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Select(c => (Comment)c.CloneNode(true))];
    }

    /// <summary>The words between the start and the end of the range of a comment, as Word marks them.</summary>
    private static string Anchored(byte[] docx, string id)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = new System.Text.StringBuilder();
        var inside = false;
        foreach (var element in doc.MainDocumentPart!.Document!.Body!.Descendants())
        {
            switch (element)
            {
                case CommentRangeStart start when start.Id?.Value == id: inside = true; break;
                case CommentRangeEnd end when end.Id?.Value == id: inside = false; break;
                case Text t when inside && !t.Ancestors<DeletedRun>().Any(): text.Append(t.Text); break;
                case TabChar when inside: text.Append('\t'); break;
            }
        }

        return text.ToString();
    }

    private static int References(byte[] docx, string id)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<CommentReference>().Count(r => r.Id?.Value == id);
    }

    [Fact]
    public void A_remark_is_a_comment_of_word_on_the_words_it_is_about_and_the_changes_are_still_there()
    {
        string[] old = ["Der Hund lief schnell nach Hause.", "Zweiter Absatz bleibt."];
        string[] next = ["Der Hund lief sehr schnell nach Hause.", "Zweiter Absatz bleibt."];
        var docx = Write(DocxBuilder.FromParagraphs(old), old, next, RevisionMode.Tracked, Remark(0, "nach Hause", "Heim oder Haus?"), Remark(1, null, "Passt so."));

        var comments = CommentsOf(docx);
        Assert.Equal(2, comments.Count);
        Assert.Equal("Anna Lektorin", comments[0].Author?.Value);
        Assert.Equal("AL", comments[0].Initials?.Value);
        Assert.Contains("Heim oder Haus?", comments[0].InnerText);
        Assert.Contains("Passt so.", comments[1].InnerText);
        Assert.NotEqual(comments[0].Id?.Value, comments[1].Id?.Value);

        // the words that are meant, and the whole paragraph when nothing is named
        Assert.Equal("nach Hause", Anchored(docx, comments[0].Id!.Value!));
        Assert.Equal("Zweiter Absatz bleibt.", Anchored(docx, comments[1].Id!.Value!));
        Assert.Equal(1, References(docx, comments[0].Id!.Value!));
        Assert.Equal(1, References(docx, comments[1].Id!.Value!));

        // the text and the tracked changes are what they were without the comments
        Assert.Equal(next, RevisionView.Accepted(docx).Take(2));
        Assert.Contains("sehr", DocxText(docx));
        Assert.Empty(RevisionView.Validate(docx));
    }

    private static string DocxText(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().Select(r => r.InnerText));
    }

    [Fact]
    public void A_remark_finds_its_paragraph_by_its_words_when_the_text_has_moved()
    {
        string[] text = ["Erster Absatz.", "Der Rabe krächzte laut.", "Dritter Absatz."];
        var docx = Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, Remark(0, "Rabe krächzte", "Welcher Rabe?"));

        var id = CommentsOf(docx).Single().Id!.Value!;
        Assert.Equal("Rabe krächzte", Anchored(docx, id));
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void Words_that_are_gone_from_the_text_leave_the_remark_on_the_paragraph_it_was_written_on()
    {
        string[] text = ["Erster Absatz.", "Zweiter Absatz."];
        var docx = Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, Remark(1, "es gibt diese Worte nicht mehr", "Trotzdem da."));

        var id = CommentsOf(docx).Single().Id!.Value!;
        Assert.Equal("Zweiter Absatz.", Anchored(docx, id));
    }

    [Fact]
    public void A_range_across_runs_with_different_formatting_is_cut_at_the_words()
    {
        // "Der Hund" is bold, " lief schnell." is not: the words "Hund lief" are in two runs
        byte[] built;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                var bold = new Run(new RunProperties(new Bold()), new Text("Der Hund") { Space = SpaceProcessingModeValues.Preserve });
                var plain = new Run(new Text(" lief schnell.") { Space = SpaceProcessingModeValues.Preserve });
                main.Document = new Document(new Body(new Paragraph(bold, plain)));
                main.Document.Save();
            }

            built = stream.ToArray();
        }

        string[] text = ["Der Hund lief schnell."];
        var docx = Write(built, text, text, RevisionMode.Tracked, Remark(0, "Hund lief", "Hund und Lauf passen nicht zusammen."));
        var id = CommentsOf(docx).Single().Id!.Value!;
        Assert.Equal("Hund lief", Anchored(docx, id));
        // the bold word is still bold on both sides of the cut
        using var read = new MemoryStream(docx);
        using var opened = WordprocessingDocument.Open(read, false);
        var runs = opened.MainDocumentPart!.Document!.Body!.Descendants<Run>().Where(r => r.Elements<Text>().Any()).ToList();
        Assert.Equal(["Der ", "Hund", " lief", " schnell."], runs.Select(r => r.InnerText).ToArray());
        Assert.Equal([true, true, false, false], runs.Select(r => r.RunProperties?.Bold is not null).ToArray());
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void A_run_that_holds_more_than_one_text_is_not_cut_and_the_range_takes_it_whole()
    {
        string[] text = ["Vorher\tNachher bleibt."];   // MakeRun writes the tab into the run: not one text
        var docx = Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, Remark(0, "Nachher", "Nach der Tabulatortaste."));
        var id = CommentsOf(docx).Single().Id!.Value!;
        Assert.Contains("Nachher", Anchored(docx, id));
        Assert.Equal(1, References(docx, id));
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void The_comments_a_file_already_has_stay_and_new_ones_get_ids_above_them()
    {
        string[] text = ["Erster Absatz.", "Zweiter Absatz."];
        byte[] built = DocxBuilder.FromParagraphs(text);
        using (var stream = new MemoryStream())
        {
            stream.Write(built);
            stream.Position = 0;
            using (var doc = WordprocessingDocument.Open(stream, true))
            {
                var part = doc.MainDocumentPart!.AddNewPart<WordprocessingCommentsPart>();
                part.Comments = new Comments(new Comment(new Paragraph(new Run(new Text("Schon da")))) { Id = "5", Author = "Der Autor", Date = When });
                part.Comments.Save();
                var first = doc.MainDocumentPart.Document!.Body!.Elements<Paragraph>().First();
                first.InsertAt(new CommentRangeStart { Id = "5" }, 0);
                first.AppendChild(new CommentRangeEnd { Id = "5" });
                first.AppendChild(new Run(new CommentReference { Id = "5" }));
                doc.MainDocumentPart.Document.Save();
            }

            built = stream.ToArray();
        }

        var docx = Write(built, text, text, RevisionMode.Tracked, Remark(1, null, "Neu dazu."));
        var comments = CommentsOf(docx);
        Assert.Equal(2, comments.Count);
        Assert.Equal("5", comments[0].Id?.Value);
        Assert.Contains("Schon da", comments[0].InnerText);
        Assert.Equal("6", comments[1].Id?.Value);
        Assert.Equal("Erster Absatz.", Anchored(docx, "5"));
        Assert.Equal("Zweiter Absatz.", Anchored(docx, "6"));
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void A_remark_on_a_paragraph_that_is_new_and_one_with_several_lines_work()
    {
        string[] old = ["Erster Absatz."];
        string[] next = ["Erster Absatz.", "Ein ganz neuer Absatz."];
        var docx = Write(DocxBuilder.FromParagraphs(old), old, next, RevisionMode.Tracked, Remark(1, "neuer", "Zeile eins\nZeile zwei"));

        var comment = CommentsOf(docx).Single();
        Assert.Equal(2, comment.Elements<Paragraph>().Count());
        Assert.Equal("neuer", Anchored(docx, comment.Id!.Value!));
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void Without_remarks_nothing_changes_in_the_file()
    {
        string[] text = ["Erster Absatz.", "Zweiter Absatz."];
        var without = DocxWriter.Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, "Lektorat", When);
        var empty = DocxWriter.Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, "Lektorat", When, []);
        using var a = WordprocessingDocument.Open(new MemoryStream(without), false);
        using var b = WordprocessingDocument.Open(new MemoryStream(empty), false);
        Assert.Null(a.MainDocumentPart!.WordprocessingCommentsPart);
        Assert.Null(b.MainDocumentPart!.WordprocessingCommentsPart);
    }

    [Fact]
    public void A_line_break_inside_a_paragraph_can_be_the_words_of_a_remark()
    {
        string[] text = ["Erste Zeile" + TextMetrics.LineBreak + "zweite Zeile bleibt."];
        var docx = Write(DocxBuilder.FromParagraphs(text), text, text, RevisionMode.Tracked, Remark(0, "Erste Zeile\nzweite", "Über den Zeilenumbruch hinweg."));
        var id = CommentsOf(docx).Single().Id!.Value!;
        Assert.Contains("Erste Zeile", Anchored(docx, id));
        Assert.Empty(RevisionView.Validate(docx));
    }
}
