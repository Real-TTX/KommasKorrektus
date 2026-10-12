using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Documents.Layout;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Tests.Core;

/// <summary>A Word file written the way Word writes it (styles, numbering, notes, a table, a picture), from the XML up, so that the page view is tested on real structure.</summary>
internal static class SampleDocx
{
    public const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    // A 1 x 1 pixel PNG.
    public const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public const string Styles = $@"<w:styles {W}>
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=""Calibri"" w:hAnsi=""Calibri""/><w:sz w:val=""22""/></w:rPr></w:rPrDefault>
<w:pPrDefault><w:pPr><w:spacing w:after=""160"" w:line=""259"" w:lineRule=""auto""/></w:pPr></w:pPrDefault></w:docDefaults>
<w:style w:type=""paragraph"" w:default=""1"" w:styleId=""Normal""><w:name w:val=""Normal""/></w:style>
<w:style w:type=""paragraph"" w:styleId=""Heading1""><w:name w:val=""heading 1""/><w:basedOn w:val=""Normal""/><w:pPr><w:keepNext/><w:spacing w:before=""360"" w:after=""80""/><w:outlineLvl w:val=""0""/></w:pPr><w:rPr><w:rFonts w:ascii=""Georgia"" w:hAnsi=""Georgia""/><w:b/><w:color w:val=""2F5496""/><w:sz w:val=""32""/></w:rPr></w:style>
<w:style w:type=""paragraph"" w:styleId=""Zitat""><w:name w:val=""Quote""/><w:basedOn w:val=""Normal""/><w:pPr><w:ind w:left=""720"" w:right=""720""/><w:jc w:val=""center""/></w:pPr><w:rPr><w:i/></w:rPr></w:style>
<w:style w:type=""paragraph"" w:styleId=""ListParagraph""><w:name w:val=""List Paragraph""/><w:basedOn w:val=""Normal""/><w:pPr><w:ind w:left=""720""/><w:contextualSpacing/></w:pPr></w:style>
<w:style w:type=""character"" w:default=""1"" w:styleId=""DefaultParagraphFont""><w:name w:val=""Default Paragraph Font""/></w:style>
<w:style w:type=""character"" w:styleId=""Hyperlink""><w:name w:val=""Hyperlink""/><w:rPr><w:color w:val=""0563C1""/><w:u w:val=""single""/></w:rPr></w:style>
<w:style w:type=""table"" w:default=""1"" w:styleId=""TableNormal""><w:name w:val=""Normal Table""/><w:tblPr><w:tblCellMar><w:left w:w=""108"" w:type=""dxa""/><w:right w:w=""108"" w:type=""dxa""/></w:tblCellMar></w:tblPr></w:style>
<w:style w:type=""table"" w:styleId=""TableGrid""><w:name w:val=""Table Grid""/><w:basedOn w:val=""TableNormal""/><w:pPr><w:spacing w:after=""0"" w:line=""240"" w:lineRule=""auto""/></w:pPr><w:tblPr><w:tblBorders><w:top w:val=""single"" w:sz=""4"" w:color=""auto""/><w:left w:val=""single"" w:sz=""4"" w:color=""auto""/><w:bottom w:val=""single"" w:sz=""4"" w:color=""auto""/><w:right w:val=""single"" w:sz=""4"" w:color=""auto""/><w:insideH w:val=""single"" w:sz=""4"" w:color=""auto""/><w:insideV w:val=""single"" w:sz=""4"" w:color=""auto""/></w:tblBorders></w:tblPr><w:tblStylePr w:type=""firstRow""><w:rPr><w:b/></w:rPr><w:tcPr><w:shd w:val=""clear"" w:color=""auto"" w:fill=""D9E2F3""/></w:tcPr></w:tblStylePr></w:style>
</w:styles>";

    public const string Numbering = $@"<w:numbering {W}>
<w:abstractNum w:abstractNumId=""0""><w:multiLevelType w:val=""multilevel""/>
<w:lvl w:ilvl=""0""><w:start w:val=""1""/><w:numFmt w:val=""decimal""/><w:lvlText w:val=""%1.""/><w:pPr><w:ind w:left=""720"" w:hanging=""360""/></w:pPr></w:lvl>
<w:lvl w:ilvl=""1""><w:start w:val=""1""/><w:numFmt w:val=""lowerLetter""/><w:lvlText w:val=""%2)""/><w:pPr><w:ind w:left=""1440"" w:hanging=""360""/></w:pPr></w:lvl>
</w:abstractNum>
<w:abstractNum w:abstractNumId=""1""><w:multiLevelType w:val=""hybridMultilevel""/>
<w:lvl w:ilvl=""0""><w:start w:val=""1""/><w:numFmt w:val=""bullet""/><w:lvlText w:val=""&#xF0B7;""/><w:pPr><w:ind w:left=""720"" w:hanging=""360""/></w:pPr><w:rPr><w:rFonts w:ascii=""Symbol"" w:hAnsi=""Symbol"" w:hint=""default""/></w:rPr></w:lvl>
</w:abstractNum>
<w:num w:numId=""1""><w:abstractNumId w:val=""0""/></w:num>
<w:num w:numId=""2""><w:abstractNumId w:val=""1""/></w:num>
</w:numbering>";

    public const string Footnotes = $@"<w:footnotes {W}>
<w:footnote w:type=""separator"" w:id=""-1""><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
<w:footnote w:type=""continuationSeparator"" w:id=""0""><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>
<w:footnote w:id=""2""><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space=""preserve""> Der Rabe heißt auch Corvus.</w:t></w:r></w:p></w:footnote>
</w:footnotes>";

    public const string Body = $@"<w:document {W}><w:body>
<w:p><w:pPr><w:pStyle w:val=""Heading1""/></w:pPr><w:r><w:t>Kapitel 1</w:t></w:r></w:p>
<w:p><w:r><w:t xml:space=""preserve"">Ein </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>fetter</w:t></w:r><w:r><w:t xml:space=""preserve""> und ein </w:t></w:r><w:r><w:rPr><w:i/><w:color w:val=""FF0000""/></w:rPr><w:t>kursiver roter</w:t></w:r><w:r><w:t xml:space=""preserve""> Text mit </w:t></w:r><w:r><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr><w:t>hoch</w:t></w:r><w:r><w:t>.</w:t></w:r></w:p>
<w:p><w:pPr><w:pStyle w:val=""Zitat""/></w:pPr><w:r><w:t>Ein Zitat in der Mitte.</w:t></w:r></w:p>
<w:p><w:pPr><w:pStyle w:val=""ListParagraph""/><w:numPr><w:ilvl w:val=""0""/><w:numId w:val=""1""/></w:numPr></w:pPr><w:r><w:t>Erster Punkt</w:t></w:r></w:p>
<w:p><w:pPr><w:pStyle w:val=""ListParagraph""/><w:numPr><w:ilvl w:val=""1""/><w:numId w:val=""1""/></w:numPr></w:pPr><w:r><w:t>Unterpunkt</w:t></w:r></w:p>
<w:p><w:pPr><w:pStyle w:val=""ListParagraph""/><w:numPr><w:ilvl w:val=""0""/><w:numId w:val=""1""/></w:numPr></w:pPr><w:r><w:t>Zweiter Punkt</w:t></w:r></w:p>
<w:p><w:pPr><w:pStyle w:val=""ListParagraph""/><w:numPr><w:ilvl w:val=""0""/><w:numId w:val=""2""/></w:numPr></w:pPr><w:r><w:t>Ein Aufzählungspunkt</w:t></w:r></w:p>
<w:p><w:r><w:t>Spalte</w:t></w:r><w:r><w:tab/><w:t>Wert</w:t></w:r><w:r><w:br/><w:t>nach dem Umbruch</w:t></w:r></w:p>
<w:p><w:r><w:t xml:space=""preserve"">Ein </w:t></w:r><w:hyperlink r:id=""rId9""><w:r><w:rPr><w:rStyle w:val=""Hyperlink""/></w:rPr><w:t>Link</w:t></w:r></w:hyperlink><w:r><w:t xml:space=""preserve""> und </w:t></w:r><w:hyperlink w:anchor=""ziel""><w:r><w:t>Sprung</w:t></w:r></w:hyperlink><w:bookmarkStart w:id=""7"" w:name=""ziel""/><w:bookmarkEnd w:id=""7""/></w:p>
<w:p><w:r><w:t xml:space=""preserve"">Behalten </w:t></w:r><w:del w:id=""1"" w:author=""X"" w:date=""2026-01-01T00:00:00Z""><w:r><w:delText>gestrichen</w:delText></w:r></w:del><w:ins w:id=""2"" w:author=""X"" w:date=""2026-01-01T00:00:00Z""><w:r><w:t xml:space=""preserve"">eingefügt </w:t></w:r></w:ins><w:r><w:t>Ende.</w:t></w:r></w:p>
<w:p><w:r><w:t>Mit Fußnote</w:t></w:r><w:r><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr><w:footnoteReference w:id=""2""/></w:r><w:r><w:t>.</w:t></w:r></w:p>
<w:p><w:r><w:t xml:space=""preserve"">Bild: </w:t></w:r><w:r><w:drawing><wp:inline distT=""0"" distB=""0"" distL=""0"" distR=""0""><wp:extent cx=""952500"" cy=""476250""/><wp:docPr id=""1"" name=""Bild 1"" descr=""Ein kleines Bild""/><a:graphic><a:graphicData uri=""http://schemas.openxmlformats.org/drawingml/2006/picture""><pic:pic><pic:nvPicPr><pic:cNvPr id=""0"" name=""x.png""/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=""rId10""/></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r><w:r><w:t xml:space=""preserve""> und weiter.</w:t></w:r></w:p>
<w:p/>
<w:tbl><w:tblPr><w:tblStyle w:val=""TableGrid""/><w:tblW w:w=""0"" w:type=""auto""/><w:tblLook w:val=""04A0"" w:firstRow=""1"" w:lastRow=""0"" w:firstColumn=""1"" w:lastColumn=""0"" w:noHBand=""0"" w:noVBand=""1""/></w:tblPr>
<w:tblGrid><w:gridCol w:w=""3000""/><w:gridCol w:w=""3000""/><w:gridCol w:w=""3000""/></w:tblGrid>
<w:tr><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/></w:tcPr><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=""6000"" w:type=""dxa""/><w:gridSpan w:val=""2""/></w:tcPr><w:p><w:r><w:t>Angaben</w:t></w:r></w:p></w:tc></w:tr>
<w:tr><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/><w:vMerge w:val=""restart""/></w:tcPr><w:p><w:r><w:t>Hexe</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/></w:tcPr><w:p><w:r><w:t>Alter</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/><w:shd w:val=""clear"" w:fill=""FFF2CC""/></w:tcPr><w:p><w:r><w:t>unbekannt</w:t></w:r></w:p></w:tc></w:tr>
<w:tr><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/><w:vMerge/></w:tcPr><w:p/></w:tc><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/></w:tcPr><w:p><w:r><w:t>Besen</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=""3000"" w:type=""dxa""/></w:tcPr><w:p><w:r><w:t>Birke</w:t></w:r></w:p></w:tc></w:tr>
</w:tbl>
<w:p><w:r><w:t>Nach der Tabelle.</w:t></w:r></w:p>
<w:sectPr><w:pgSz w:w=""11906"" w:h=""16838""/><w:pgMar w:top=""1417"" w:right=""1417"" w:bottom=""1134"" w:left=""1417"" w:header=""708"" w:footer=""708"" w:gutter=""0""/></w:sectPr>
</w:body></w:document>";

    public static byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content) => AddBytes(zip, name, Encoding.UTF8.GetBytes(content));
            Add("[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
                <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/></Types>
                """);
            Add("_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add("word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/>
                <Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.org/seite" TargetMode="External"/>
                <Relationship Id="rId10" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                </Relationships>
                """);
            Add("word/document.xml", Body);
            Add("word/styles.xml", Styles);
            Add("word/numbering.xml", Numbering);
            Add("word/footnotes.xml", Footnotes);
            AddBytes(zip, "word/media/image1.png", Convert.FromBase64String(PngBase64));
        }

        return stream.ToArray();
    }

    private static void AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(bytes);
    }
}

public class LayoutTests
{
    private static readonly DateTime When = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Regex ParagraphMarkup = new(@"<p class=""[^""]*""[^>]*?data-n=""(?<n>\d+)""[^>]*>(?<body>.*?)</p>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex OpaqueMarkup = new(@"<(?<tag>span|sup)\b[^>]*data-opaque[^>]*></\k<tag>>|<img\b[^>]*>|<br>", RegexOptions.Compiled);

    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// The text of every paragraph in the markup, by number: what the editor reads from the paragraph (the parts the cursor skips taken out). A line break at the
    /// very end is the place holder the browser keeps there for the cursor, not a break (the markup writes a real one at the end with one more).
    /// </summary>
    internal static List<string> TextsOf(string html)
        => [.. ParagraphMarkup.Matches(html).OrderBy(m => int.Parse(m.Groups["n"].Value)).Select(m =>
        {
            var text = WebUtility.HtmlDecode(AnyTag.Replace(OpaqueMarkup.Replace(m.Groups["body"].Value, string.Empty), string.Empty));
            return (text.EndsWith('\n') ? text[..^1] : text).Replace('\n', TextMetrics.LineBreak);
        })];

    [Fact]
    public void A_line_break_at_the_end_of_a_paragraph_is_written_with_the_place_holder_the_editor_reads_it_by()
    {
        var text = new[] { "abc" + TextMetrics.LineBreak, "def", "x" + TextMetrics.LineBreak + TextMetrics.LineBreak };
        var result = DocxLayout.Render(DocxBuilder.FromParagraphs(text));
        Assert.Contains(">abc\n\n</p>", result.Html);
        Assert.Contains(">def</p>", result.Html);
        Assert.Contains(">x\n\n\n</p>", result.Html);
        Assert.Equal(text, TextsOf(result.Html));
    }

    [Fact]
    public void The_text_of_every_paragraph_on_the_page_is_exactly_the_text_the_editor_works_on()
    {
        var docx = SampleDocx.Build();
        var result = DocxLayout.Render(docx);
        var expected = DocxReader.ReadParagraphs(docx);
        Assert.True(result.Complete, $"{result.ParagraphCount} of {result.TextParagraphCount} paragraphs were drawn");
        Assert.Equal(expected, TextsOf(result.Html));
    }

    [Fact]
    public void Headings_styles_and_the_formatting_of_words_are_drawn()
    {
        var result = DocxLayout.Render(SampleDocx.Build());
        // The heading: a style with a font, a size, bold and a colour (Georgia, 16 pt, #2f5496).
        Assert.Matches(@"font-family:'Georgia',serif", result.Css);
        Assert.Contains("font-size:16pt", result.Css);
        Assert.Contains("color:#2f5496", result.Css);
        // Words: bold, italic with a colour, superscript.
        Assert.Contains("font-weight:bold", result.Css);
        Assert.Contains("font-style:italic", result.Css);
        Assert.Contains("color:#ff0000", result.Css);
        Assert.Contains("vertical-align:super", result.Css);
        // A quote: indented on both sides and centred.
        Assert.Contains("text-align:center", result.Css);
        Assert.Contains("margin-left:36pt", result.Css);
        // The body text: Calibri 11 pt with the line spacing and the space after the document says.
        Assert.Contains("font-size:11pt", result.Css);
        Assert.Contains("line-height:1.079", result.Css);
        Assert.Contains("padding-bottom:8pt", result.Css);
    }

    [Fact]
    public void The_page_has_the_size_and_margins_of_the_document()
    {
        var result = DocxLayout.Render(SampleDocx.Build());
        Assert.Equal(793.7, result.PageWidthPx, 1);
        Assert.Equal(1122.5, result.PageHeightPx, 1);
        Assert.Matches(@"\.pg-[0-9a-f]+\{width:793\.7px;min-height:1122\.5px;padding:94\.5px 94\.5px 75\.6px 94\.5px;tab-size:36pt;--kk-page-top:94\.5px;--kk-page-inner-h:952\.4px;", result.Css);
        Assert.Contains(result.PageClass, result.Css);
    }

    [Fact]
    public void Lists_are_numbered_and_bullets_are_drawn_as_text_a_browser_has()
    {
        var html = DocxLayout.Render(SampleDocx.Build()).Html;
        Assert.Contains("data-m=\"1.\"", html);
        Assert.Contains("data-m=\"a)\"", html);
        Assert.Contains("data-m=\"2.\"", html);
        Assert.Contains("data-m=\"•\"", html);
        // A marker is not text: nothing of it is in the text of the paragraph.
        Assert.DoesNotContain(">1.<", html);
    }

    [Fact]
    public void Tabs_and_line_breaks_are_kept_as_text_characters()
    {
        var html = DocxLayout.Render(SampleDocx.Build()).Html;
        Assert.Contains("Spalte\tWert\nnach dem Umbruch", html);
    }

    [Fact]
    public void Links_are_followed_only_to_web_and_mail_addresses_and_not_in_the_editor()
    {
        var docx = SampleDocx.Build();
        var reading = DocxLayout.Render(docx).Html;
        Assert.Contains("<a class=\"kk-a\" href=\"https://example.org/seite\" target=\"_blank\" rel=\"noopener noreferrer\">", reading);
        Assert.Contains("<span class=\"kk-a\">Sprung</span>", reading);

        var editing = DocxLayout.Render(docx, new LayoutOptions { Editable = true }).Html;
        Assert.DoesNotContain("<a ", editing);
    }

    [Fact]
    public void Earlier_tracked_changes_are_taken_as_accepted_in_the_clean_view_and_drawn_in_the_changes_view()
    {
        var docx = SampleDocx.Build();
        var clean = DocxLayout.Render(docx).Html;
        Assert.Contains("Behalten eingefügt Ende.", clean);
        Assert.DoesNotContain("gestrichen", clean);

        var changes = DocxLayout.Render(docx, new LayoutOptions { Mode = LayoutMode.Changes });
        Assert.Contains("<del class=\"kk-del\">gestrichen</del>", changes.Html);
        Assert.Contains("<ins class=\"kk-ins\">eingefügt </ins>", changes.Html);
        Assert.Contains(changes.Blocks, b => b.Changed);
    }

    [Fact]
    public void Footnotes_are_marked_in_the_text_and_listed_at_the_end()
    {
        var result = DocxLayout.Render(SampleDocx.Build());
        Assert.Contains("<sup class=\"kk-fnr\" data-opaque contenteditable=\"false\" data-m=\"1\"></sup>", result.Html);
        Assert.Contains("Der Rabe heißt auch Corvus.", result.Notes);
        // The notes are not paragraphs of the text.
        Assert.DoesNotContain("Corvus", string.Join('\n', TextsOf(result.Html)));
    }

    [Fact]
    public void A_picture_is_carried_by_the_page_and_has_its_size_and_text()
    {
        var html = DocxLayout.Render(SampleDocx.Build()).Html;
        Assert.Matches("<img class=\"kk-img im-[0-9a-f]+\" data-opaque contenteditable=\"false\" src=\"data:image/png;base64,", html);
        Assert.Contains("width=\"100\" height=\"50\" alt=\"Ein kleines Bild\"", html);
    }

    [Fact]
    public void A_picture_that_is_too_large_is_a_frame_of_the_same_size()
    {
        var html = DocxLayout.Render(SampleDocx.Build(), new LayoutOptions { MaxImageBytes = 10 }).Html;
        Assert.DoesNotContain("<img", html);
        Assert.Contains("class=\"kk-obj ph-", html);
    }

    [Fact]
    public void A_table_keeps_its_grid_its_borders_its_shading_and_a_paragraph_in_every_cell_even_in_a_merged_one()
    {
        var result = DocxLayout.Render(SampleDocx.Build());
        var html = result.Html;
        Assert.Equal(3, Regex.Matches(html, "<tr>").Count);
        Assert.Contains("colspan=\"2\"", html);
        Assert.Equal(3, Regex.Matches(html, "<col class=").Count);
        Assert.Contains("border-top:1px solid #000", result.Css);
        Assert.Contains("background-color:#d9e2f3", result.Css);
        Assert.Contains("background-color:#fff2cc", result.Css);
        // The merged cell goes on in the row below without a line between: its paragraph (empty) is still there, in the order of the text.
        Assert.True(result.Complete);
        var texts = TextsOf(html);
        Assert.Equal(["Name", "Angaben", "Hexe", "Alter", "unbekannt", "", "Besen", "Birke"], texts.Skip(texts.IndexOf("Name")).Take(8));
        // The style of the table: no space after a paragraph inside it.
        Assert.Contains("padding-bottom:0pt", result.Css);
    }

    [Fact]
    public void The_markup_has_no_style_attributes_and_no_script_so_the_content_security_policy_holds()
    {
        var result = DocxLayout.Render(SampleDocx.Build(), new LayoutOptions { Editable = true });
        Assert.DoesNotContain(" style=", result.Html);
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nothing_a_document_says_can_get_into_the_stylesheet_but_the_values_it_is_made_of()
    {
        var evil = Rewrite(SampleDocx.Build(), "w:val=\"FF0000\"", "w:val=\"red;}body{display:none}\"", "Calibri", "Evil';}</style><script>alert(1)</script>");
        var result = DocxLayout.Render(evil);
        Assert.DoesNotContain("display:none", result.Css);
        Assert.DoesNotContain("<script", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</style", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result.Html);
    }

    [Fact]
    public void A_page_of_a_document_that_was_edited_by_the_export_shows_the_new_text_with_the_old_formatting()
    {
        var original = SampleDocx.Build();
        var oldText = DocxReader.ReadParagraphs(original);
        var newText = oldText.ToList();
        newText[1] = newText[1].Replace("fetter", "dicker");
        newText.Insert(2, "Ein ganz neuer Absatz.");
        newText.RemoveAt(newText.IndexOf("Nach der Tabelle."));
        newText.Add("Zum Schluss.");

        var edited = DocxWriter.Write(original, oldText, newText, RevisionMode.Accepted, "Lektorat", When);
        var result = DocxLayout.Render(edited, new LayoutOptions { Editable = true });
        Assert.True(result.Complete);
        Assert.Equal(newText, TextsOf(result.Html));
        // "dicker" kept the bold of "fetter"; the new paragraph was made from its neighbour.
        Assert.Matches(@"<span class=""r-[0-9a-f]+"">dicker</span>", result.Html);
        // A paragraph made with Enter after a heading is a plain paragraph in the export: the page says so.
        Assert.Contains("data-nx=", result.Html);
    }

    [Fact]
    public void The_changes_view_of_a_tracked_export_draws_insertions_and_deletions_in_the_formatted_text()
    {
        var original = SampleDocx.Build();
        var oldText = DocxReader.ReadParagraphs(original);
        var newText = oldText.ToList();
        newText[1] = newText[1].Replace("fetter", "dicker");
        newText.Insert(2, "Ein ganz neuer Absatz.");

        var tracked = DocxWriter.Write(original, oldText, newText, RevisionMode.Tracked, "Lektorat", When);
        var result = DocxLayout.Render(tracked, new LayoutOptions { Mode = LayoutMode.Changes });
        Assert.Contains("<del class=\"kk-del\">", result.Html);
        Assert.Contains("<ins class=\"kk-ins\">", result.Html);
        Assert.Contains("is-inserted", result.Html);
        Assert.Contains(result.Blocks, b => b.Changed);
        Assert.Contains(result.Blocks, b => !b.Changed);
    }

    [Fact]
    public void Only_the_changes_of_the_named_author_are_drawn_as_changes()
    {
        var original = SampleDocx.Build();
        var oldText = DocxReader.ReadParagraphs(original);
        var newText = oldText.ToList();
        newText[1] = newText[1].Replace("fetter", "dicker");

        var tracked = DocxWriter.Write(original, oldText, newText, RevisionMode.Tracked, "Lektorat", When);
        var result = DocxLayout.Render(tracked, new LayoutOptions { Mode = LayoutMode.Changes, ChangesBy = "Lektorat" });
        // The change of the editor is drawn; the revision marks the author's file came with (by "X") are taken as accepted.
        Assert.Matches("<del class=\"kk-del\">(<span[^>]*>)?fetter(</span>)?</del>", result.Html);
        Assert.Matches("<ins class=\"kk-ins\">(<span[^>]*>)?dicker(</span>)?</ins>", result.Html);
        Assert.DoesNotContain("gestrichen", result.Html);
        Assert.DoesNotContain("<ins class=\"kk-ins\">eingefügt", result.Html);
        Assert.Contains("Behalten eingefügt Ende.", result.Html);
        Assert.Equal(1, result.Blocks.Count(b => b.Changed));
    }

    [Fact]
    public void The_numbers_in_the_changes_view_are_those_of_the_new_text()
    {
        var original = DocxBuilder.FromParagraphs(["Eins", "Zwei", "Drei"]);
        var tracked = DocxWriter.Write(original, ["Eins", "Zwei", "Drei"], ["Eins", "Drei", "Vier"], RevisionMode.Tracked, "Lektorat", When);
        var html = DocxLayout.Render(tracked, new LayoutOptions { Mode = LayoutMode.Changes, ChangesBy = "Lektorat" }).Html;
        var numbers = Regex.Matches(html, @"<p class=""[^""]*"" id=""p\d+"" data-n=""\d+""( data-no=""(?<no>\d+)"")?[^>]*>").Select(m => m.Groups["no"].Success ? m.Groups["no"].Value : "-").ToList();
        // "Zwei" is gone and has no number; "Drei" and "Vier" are paragraphs 2 and 3 of the new text.
        Assert.Equal(["1", "-", "2", "3"], numbers);
    }

    [Fact]
    public void A_plain_document_made_from_paragraphs_has_a_page_too()
    {
        var docx = DocxBuilder.FromParagraphs(["Eins", "", "Zwei" + TextMetrics.LineBreak + "drei"]);
        var result = DocxLayout.Render(docx);
        Assert.True(result.Complete);
        Assert.Equal(["Eins", "", "Zwei" + TextMetrics.LineBreak + "drei"], TextsOf(result.Html));
    }

    [Fact]
    public void A_deleted_paragraph_in_a_cell_with_only_that_paragraph_leaves_the_page_with_one_more_paragraph_than_the_text()
    {
        // The writer must leave an empty paragraph in the cell, which the text no longer has: whoever draws the page for editing compares the numbers.
        var original = SampleDocx.Build();
        var oldText = DocxReader.ReadParagraphs(original);
        var newText = oldText.ToList();
        newText.RemoveAt(newText.IndexOf("Angaben"));
        var edited = DocxWriter.Write(original, oldText, newText, RevisionMode.Accepted, "Lektorat", When);
        var result = DocxLayout.Render(edited);
        Assert.Equal(newText.Count + 1, result.ParagraphCount);
        Assert.False(DocxReader.Matches(edited, newText));
    }

    /// <summary>The same document with some text of the XML changed (to try what a hostile file does).</summary>
    private static byte[] Rewrite(byte[] docx, string findColor, string replaceColor, string findFont, string replaceFont)
    {
        using var input = new MemoryStream(docx);
        using var output = new MemoryStream();
        using (var source = new ZipArchive(input, ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = entry.Open();
                using var buffer = new MemoryStream();
                read.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (entry.FullName is "word/document.xml" or "word/styles.xml")
                {
                    var text = Encoding.UTF8.GetString(bytes).Replace(findColor, replaceColor).Replace(findFont, WebUtility.HtmlEncode(replaceFont).Replace("&#39;", "&apos;"));
                    bytes = Encoding.UTF8.GetBytes(text);
                }

                using var write = target.CreateEntry(entry.FullName).Open();
                write.Write(bytes);
            }
        }

        return output.ToArray();
    }
}
