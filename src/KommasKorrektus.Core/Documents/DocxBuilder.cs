using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace KommasKorrektus.Core.Documents;

/// <summary>Makes a plain Word document from paragraphs: the base for an export when the manuscript did not come as a Word file (a PDF or a text).</summary>
public static class DocxBuilder
{
    public static byte[] FromParagraphs(IReadOnlyList<string> paragraphs, string fontName = "Calibri", int halfPoints = 22)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);

            foreach (var text in paragraphs)
            {
                var paragraph = new Paragraph();
                if (text.Length > 0)
                {
                    paragraph.AppendChild(DocxWriter.MakeRun(text, null, deleted: false));
                }

                body.AppendChild(paragraph);
            }

            // A4 with margins of 2.5 cm.
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1417, Right = 1417, Bottom = 1134, Left = 1417, Header = 708, Footer = 708, Gutter = 0 }));

            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new DocDefaults(
                    new RunPropertiesDefault(new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = fontName, HighAnsi = fontName, EastAsia = fontName, ComplexScript = fontName },
                        new FontSize { Val = halfPoints.ToString() },
                        new Languages { Val = "de-DE" })),
                    new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }))),
                new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
