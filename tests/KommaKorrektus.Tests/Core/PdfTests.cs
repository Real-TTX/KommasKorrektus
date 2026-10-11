using KommaKorrektus.Core.Diff;
using KommaKorrektus.Core.Documents;
using QuestPDF.Fluent;

namespace KommaKorrektus.Tests.Core;

public class PdfTests
{
    private static readonly string[] Paragraphs =
    [
        "Es war einmal eine kleine Hexe, die wohnte am Rand eines dunklen Waldes und braute jeden Abend einen Tee aus Kräutern, die sie selbst gesammelt hatte.",
        "Am nächsten Morgen klopfte es an der Tür. Draußen stand ein Rabe mit einem Brief im Schnabel, und er sah sehr ernst aus.",
        "„Ein Komma,“ sagte die Hexe, „kann Leben retten.“",
    ];

    private static PdfHeader Header => new("Probe", "Testdokument", "KommaKorrektus");

    [Fact]
    public void A_pdf_made_from_text_can_be_read_back_as_the_same_paragraphs()
    {
        var pdf = DocumentPdf.RenderClean(Header, Paragraphs);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));

        var read = PdfTextReader.Read(pdf);
        foreach (var paragraph in Paragraphs)
        {
            Assert.True(read.Paragraphs.Any(r => r.Contains(paragraph)), "fehlt: " + paragraph + " in " + string.Join(" || ", read.Paragraphs));
        }

        var all = string.Join(' ', read.Paragraphs);
        Assert.Contains("kleine Hexe", all);
        Assert.Contains("Rabe mit einem Brief", all);
        Assert.Contains("Ein Komma,", all);
    }

    [Fact]
    public void A_changed_text_can_be_drawn_as_changes_clean_and_side_by_side()
    {
        var old = new[] { "Der Hund bellt laut.", "Dieser Absatz fällt weg.", "Der Schluss bleibt." };
        var changed = new[] { "Der Hund bellte laut.", "Der Schluss bleibt.", "Ein neuer Absatz." };
        var diff = DocumentDiff.Compare(old, changed);

        foreach (var layout in Enum.GetValues<PdfLayout>())
        {
            var pdf = DocumentPdf.Render(Header, diff, layout);
            var text = string.Join(' ', PdfTextReader.Read(pdf).Paragraphs);
            Assert.Contains("Schluss", text);
            if (layout == PdfLayout.Clean)
            {
                Assert.DoesNotContain("fällt weg", text);
                Assert.Contains("neuer Absatz", text);
            }
            else if (layout == PdfLayout.Changes)
            {
                Assert.Contains("fällt weg", text);
            }
        }
    }

    [Fact]
    public void A_text_without_letters_is_reported_as_a_scan()
    {
        var empty = QuestPDF.Fluent.Document.Create(c => c.Page(p => { p.Size(QuestPDF.Helpers.PageSizes.A4); p.Content().Height(100).Background("#cccccc"); })).GeneratePdf();
        var read = PdfTextReader.Read(empty);
        Assert.Contains(read.Notes, n => n.Contains("keinen Text"));
    }

    [Fact]
    public void A_plain_text_with_blank_lines_becomes_paragraphs_and_one_without_becomes_lines()
    {
        Assert.Equal(["Eins zwei", "Drei"], PlainTextReader.Read("Eins\nzwei\n\nDrei").Paragraphs);
        Assert.Equal(["Eins", "Zwei", "Drei"], PlainTextReader.Read("Eins\nZwei\nDrei").Paragraphs);
    }
}
