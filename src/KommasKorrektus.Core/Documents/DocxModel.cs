using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Documents;

/// <summary>
/// What the program takes from a paragraph of a Word document, and what it leaves alone. A paragraph is a list of items in reading order: pieces of text
/// that share their formatting, and "opaque" things (a picture, a footnote mark, a bookmark, an earlier deletion) that have no text and are kept where they are.
/// Reading and writing use the same list, so the text that was read and the places that are rewritten always agree.
/// </summary>
internal sealed class ParagraphItems
{
    /// <summary>Text with one format. <see cref="Container"/> is the element around the run (a hyperlink, an earlier insertion) that is put around the new runs too.</summary>
    public sealed record TextItem(RunProperties? Format, OpenXmlElement? Container, string Text);

    /// <summary>Something without text. <see cref="Offset"/> is the position in the text where it sits.</summary>
    public sealed record OpaqueItem(OpenXmlElement Element, int Offset);

    public List<TextItem> Texts { get; } = [];

    public List<OpaqueItem> Opaque { get; } = [];

    public string Text => string.Concat(Texts.Select(t => t.Text));

    private int _offset;

    public static ParagraphItems Of(Paragraph paragraph)
    {
        var items = new ParagraphItems();
        items.Walk(paragraph, null);
        return items;
    }

    private void Walk(OpenXmlElement parent, OpenXmlElement? container)
    {
        foreach (var child in parent.ChildElements)
        {
            switch (child)
            {
                case ParagraphProperties:
                    break;
                case Run run:
                    AddRun(run, container);
                    break;
                case DeletedRun or MoveFromRun or SdtRun:
                    AddOpaque(child);
                    break;
                case Hyperlink or InsertedRun or MoveToRun or SimpleField or CustomXmlRun:
                    Walk(child, child);
                    break;
                default:
                    AddOpaque(child);
                    break;
            }
        }
    }

    private void AddOpaque(OpenXmlElement element) => Opaque.Add(new OpaqueItem(element, _offset));

    private void AddRun(Run run, OpenXmlElement? container)
    {
        var format = run.RunProperties;
        var text = new System.Text.StringBuilder();

        void FlushText()
        {
            if (text.Length > 0)
            {
                Texts.Add(new TextItem(format, container, text.ToString()));
                _offset += text.Length;
                text.Clear();
            }
        }

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case RunProperties:
                    break;
                case Text t:
                    text.Append(t.Text);
                    break;
                case TabChar:
                    text.Append('\t');
                    break;
                case CarriageReturn:
                    text.Append(TextMetrics.LineBreak);
                    break;
                case Break b when b.Type is null || b.Type.Value == BreakValues.TextWrapping:
                    text.Append(TextMetrics.LineBreak);
                    break;
                case NoBreakHyphen:
                    text.Append(NoBreakHyphenChar);
                    break;
                case SoftHyphen:
                    text.Append(SoftHyphenChar);
                    break;
                default:
                    FlushText();
                    var wrapper = new Run();
                    if (format is not null)
                    {
                        wrapper.AppendChild((RunProperties)format.CloneNode(true));
                    }

                    wrapper.AppendChild(child.CloneNode(true));
                    Opaque.Add(new OpaqueItem(WrapContainer(wrapper, container), _offset));
                    break;
            }
        }

        FlushText();
    }

    private static OpenXmlElement WrapContainer(OpenXmlElement run, OpenXmlElement? container)
    {
        if (container is null)
        {
            return run;
        }

        var wrapper = container.CloneNode(false);
        wrapper.AppendChild(run);
        return wrapper;
    }

    public const char NoBreakHyphenChar = (char)0x2011;

    public const char SoftHyphenChar = (char)0x00AD;
}

/// <summary>Reads the text of a Word document: the paragraphs of the body in reading order, table cells included.</summary>
public static class DocxReader
{
    /// <summary>The paragraphs a document has for the program. Paragraphs in text boxes are left out (they are drawn objects, not part of the text flow).</summary>
    internal static List<Paragraph> EnumerateParagraphs(Body body)
        => [.. body.Descendants<Paragraph>().Where(p => !p.Ancestors<TextBoxContent>().Any())];

    public static IReadOnlyList<string> ReadParagraphs(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("Das Word-Dokument hat keinen Text.");
        return [.. EnumerateParagraphs(body).Select(p => ParagraphItems.Of(p).Text)];
    }

    /// <summary>True when the document has exactly these paragraphs (the condition for rewriting it in place).</summary>
    public static bool Matches(byte[] docx, IReadOnlyList<string> paragraphs)
    {
        try
        {
            return ReadParagraphs(docx).SequenceEqual(paragraphs, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidDataException or OpenXmlPackageException or FileFormatException)
        {
            return false;
        }
    }
}
