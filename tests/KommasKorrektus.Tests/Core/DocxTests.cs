using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Tests.Core;

/// <summary>What Word would show after "accept all changes" and after "reject all changes", worked out from the XML.</summary>
internal static class RevisionView
{
    public static List<string> Accepted(byte[] docx) => Read(docx, accept: true);

    public static List<string> Rejected(byte[] docx) => Read(docx, accept: false);

    private static List<string> Read(byte[] docx, bool accept)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        var result = new List<string>();
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            var markInserted = p.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Inserted>() is not null;
            var markDeleted = p.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Deleted>() is not null;
            if ((accept && markDeleted) || (!accept && markInserted))
            {
                continue;
            }

            var text = new System.Text.StringBuilder();
            foreach (var element in p.Descendants())
            {
                var insideInsertion = element.Ancestors<InsertedRun>().Any();
                var insideDeletion = element.Ancestors<DeletedRun>().Any();
                if ((accept && insideDeletion) || (!accept && insideInsertion))
                {
                    continue;
                }

                switch (element)
                {
                    case Text t: text.Append(t.Text); break;
                    case DeletedText dt: text.Append(dt.Text); break;
                    case TabChar: text.Append('\t'); break;
                    case Break b when b.Type is null: text.Append(TextMetrics.LineBreak); break;
                }
            }

            result.Add(text.ToString());
        }

        return result;
    }

    public static IReadOnlyList<ValidationErrorInfo> Validate(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
    }
}

public class DocxTests
{
    private static readonly DateTime When = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static string[] Old => ["Kapitel 1", "Die Katze saß auf dem Dach und sah zum Mond.", "", "Dieser Absatz fällt weg, weil er nicht passt.", "Der letzte Absatz bleibt, wie er ist."];

    private static string[] New => ["Kapitel 1", "Die Katze saß auf dem Dach und blickte zum hellen Mond.", "", "Ein ganz neuer Absatz, der vorher fehlte.", "Der letzte Absatz bleibt, wie er ist."];

    [Fact]
    public void A_built_document_reads_back_with_the_same_paragraphs()
    {
        var docx = DocxBuilder.FromParagraphs(Old);
        Assert.Equal(Old, DocxReader.ReadParagraphs(docx));
        Assert.Empty(RevisionView.Validate(docx));
    }

    [Fact]
    public void Tracked_changes_accept_to_the_new_text_and_reject_to_the_old_text()
    {
        var docx = DocxWriter.Write(null, Old, New, RevisionMode.Tracked, "Miriam", When);
        Assert.Equal(New, RevisionView.Accepted(docx));
        Assert.Equal(Old, RevisionView.Rejected(docx));
    }

    [Fact]
    public void Tracked_changes_are_valid_open_xml_with_real_revision_elements()
    {
        var docx = DocxWriter.Write(null, Old, New, RevisionMode.Tracked, "Miriam", When);
        var errors = RevisionView.Validate(docx);
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => e.Description + " @" + e.Path?.XPath)));

        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        Assert.NotEmpty(body.Descendants<InsertedRun>());
        Assert.NotEmpty(body.Descendants<DeletedRun>());
        Assert.All(body.Descendants<InsertedRun>(), i => Assert.Equal("Miriam", i.Author!.Value));
        var ids = body.Descendants<InsertedRun>().Select(i => i.Id!.Value).Concat(body.Descendants<DeletedRun>().Select(d => d.Id!.Value)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void The_clean_export_has_the_new_text_and_no_revisions()
    {
        var docx = DocxWriter.Write(null, Old, New, RevisionMode.Accepted, "Miriam", When);
        Assert.Equal(New, DocxReader.ReadParagraphs(docx));
        Assert.Equal(New, RevisionView.Accepted(docx));
        Assert.Empty(RevisionView.Validate(docx));
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        Assert.Empty(doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>());
        Assert.Empty(doc.MainDocumentPart.Document.Body.Descendants<DeletedRun>());
    }

    [Fact]
    public void The_formatting_of_the_words_that_stay_is_kept_and_new_words_take_the_formatting_of_their_neighbour()
    {
        var original = BuildFormatted();
        var oldText = DocxReader.ReadParagraphs(original);
        Assert.Equal(["Ein fetter Anfang und ein kursives Ende."], oldText);

        var docx = DocxWriter.Write(original, oldText, ["Ein fetter Beginn und ein kursives Ende."], RevisionMode.Tracked, "Miriam", When);
        Assert.Empty(RevisionView.Validate(docx));

        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        var runs = doc.MainDocumentPart!.Document!.Body!.Descendants<Run>().ToList();
        var bold = runs.Where(r => r.RunProperties?.Bold is not null).Select(r => string.Concat(r.Descendants<Text>().Select(t => t.Text)) + string.Concat(r.Descendants<DeletedText>().Select(t => t.Text))).ToList();
        var italic = runs.Where(r => r.RunProperties?.Italic is not null).Select(r => string.Concat(r.Descendants<Text>().Select(t => t.Text))).ToList();
        Assert.Contains(bold, s => s.Contains("fetter"));
        Assert.Contains(bold, s => s.Contains("Anfang"));
        Assert.Contains(bold, s => s.Contains("Beginn"));
        Assert.Contains(italic, s => s.Contains("kursives"));
        Assert.Equal(["Ein fetter Beginn und ein kursives Ende."], RevisionView.Accepted(docx));
        Assert.Equal(["Ein fetter Anfang und ein kursives Ende."], RevisionView.Rejected(docx));
    }

    [Fact]
    public void Paragraphs_that_did_not_change_are_left_exactly_as_they_were()
    {
        var original = DocxBuilder.FromParagraphs(Old);
        var docx = DocxWriter.Write(original, Old, New, RevisionMode.Tracked, "Miriam", When);
        using var before = new MemoryStream(original);
        using var after = new MemoryStream(docx);
        using var d1 = WordprocessingDocument.Open(before, false);
        using var d2 = WordprocessingDocument.Open(after, false);
        var p1 = d1.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
        var p2 = d2.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
        Assert.Equal(p1[0].OuterXml, p2[0].OuterXml);
        Assert.Equal(p1[4].OuterXml, p2.Last().OuterXml);
    }

    [Fact]
    public void Paragraphs_in_table_cells_are_part_of_the_text_and_can_be_changed()
    {
        var original = BuildTable();
        var oldText = DocxReader.ReadParagraphs(original);
        Assert.Equal(["Vor der Tabelle.", "Zelle eins", "Zelle zwei", "Nach der Tabelle."], oldText);

        var newText = new[] { "Vor der Tabelle.", "Zelle eins, korrigiert", "Zelle zwei", "Nach der Tabelle." };
        var docx = DocxWriter.Write(original, oldText, newText, RevisionMode.Tracked, "Miriam", When);
        Assert.Empty(RevisionView.Validate(docx));
        Assert.Equal(newText, RevisionView.Accepted(docx));
        Assert.Equal(oldText, RevisionView.Rejected(docx));
    }

    [Fact]
    public void A_deleted_paragraph_in_the_clean_export_is_gone_and_a_table_cell_keeps_its_paragraph()
    {
        var original = BuildTable();
        var oldText = DocxReader.ReadParagraphs(original);
        var docx = DocxWriter.Write(original, oldText, ["Vor der Tabelle.", "", "Zelle zwei", "Nach der Tabelle."], RevisionMode.Accepted, "Miriam", When);
        Assert.Empty(RevisionView.Validate(docx));
        Assert.Equal(["Vor der Tabelle.", "", "Zelle zwei", "Nach der Tabelle."], DocxReader.ReadParagraphs(docx));
    }

    [Fact]
    public void Tabs_and_line_breaks_survive_a_round_trip()
    {
        var text = new[] { "Name:\tMüller" + TextMetrics.LineBreak + "Ort:\tBerlin" };
        var docx = DocxBuilder.FromParagraphs(text);
        Assert.Equal(text, DocxReader.ReadParagraphs(docx));
        var changed = DocxWriter.Write(docx, text, ["Name:\tMüller" + TextMetrics.LineBreak + "Ort:\tHamburg"], RevisionMode.Tracked, "Miriam", When);
        Assert.Equal(["Name:\tMüller" + TextMetrics.LineBreak + "Ort:\tHamburg"], RevisionView.Accepted(changed));
        Assert.Equal(text, RevisionView.Rejected(changed));
        Assert.Empty(RevisionView.Validate(changed));
    }

    [Fact]
    public void A_document_that_does_not_match_the_old_text_is_not_rewritten_in_place_but_rebuilt()
    {
        var other = DocxBuilder.FromParagraphs(["etwas ganz anderes"]);
        var docx = DocxWriter.Write(other, Old, New, RevisionMode.Tracked, "Miriam", When);
        Assert.Equal(New, RevisionView.Accepted(docx));
    }

    [Fact]
    public void A_text_with_many_changes_across_a_long_manuscript_stays_consistent()
    {
        var random = new Random(7);
        var old = Enumerable.Range(0, 400).Select(i => $"Absatz {i}: " + string.Join(' ', Enumerable.Range(0, 20).Select(_ => "wort" + random.Next(50)))).ToList();
        var next = old.ToList();
        for (var i = 0; i < 60; i++)
        {
            var at = random.Next(next.Count);
            switch (random.Next(3))
            {
                case 0: next[at] = next[at].Replace("wort1", "geändert"); break;
                case 1: next.RemoveAt(at); break;
                default: next.Insert(at, "Neuer Absatz " + i + " mit einigen Wörtern darin."); break;
            }
        }

        var docx = DocxWriter.Write(null, old, next, RevisionMode.Tracked, "Miriam", When);
        Assert.Equal(next, RevisionView.Accepted(docx));
        Assert.Equal(old, RevisionView.Rejected(docx));
        Assert.Empty(RevisionView.Validate(docx));
    }

    private static byte[] BuildFormatted()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(
                new Run(new Text("Ein ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new RunProperties(new Bold()), new Text("fetter Anfang") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new Text(" und ein ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new RunProperties(new Italic()), new Text("kursives") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new Text(" Ende.") { Space = SpaceProcessingModeValues.Preserve }))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildTable()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Vor der Tabelle."))),
                new Table(new TableProperties(), new TableGrid(new GridColumn { Width = "4000" }, new GridColumn { Width = "4000" }), new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Zelle eins")))),
                    new TableCell(new Paragraph(new Run(new Text("Zelle zwei")))))),
                new Paragraph(new Run(new Text("Nach der Tabelle.")))));
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
