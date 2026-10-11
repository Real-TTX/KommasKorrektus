using KommaKorrektus.Core.Diff;
using KommaKorrektus.Core.Writing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KommaKorrektus.Core.Documents;

/// <summary>How the changes are shown in a PDF.</summary>
public enum PdfLayout
{
    /// <summary>One text, deleted words struck through in red, new words underlined in green: the proof sheet (Korrekturfahne).</summary>
    Changes,
    /// <summary>The new text as it is.</summary>
    Clean,
    /// <summary>Old and new text next to each other, paragraph by paragraph.</summary>
    SideBySide,
}

public sealed record PdfHeader(string Title, string? Subtitle, string? Footer);

/// <summary>Draws the text of a manuscript, with or without the changes, as a PDF (QuestPDF).</summary>
public static class DocumentPdf
{
    private const string Deleted = "#b3261e";
    private const string Inserted = "#1b6e3c";

    static DocumentPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(PdfHeader header, IReadOnlyList<ParagraphDiff> diff, PdfLayout layout)
    {
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2.2f, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(10.5f).LineHeight(1.35f));

            page.Header().PaddingBottom(10).Column(column =>
            {
                column.Item().Text(header.Title).FontSize(15).Bold();
                if (!string.IsNullOrWhiteSpace(header.Subtitle))
                {
                    column.Item().Text(header.Subtitle).FontSize(9.5f).FontColor(Colors.Grey.Darken1);
                }

                if (layout == PdfLayout.Changes)
                {
                    column.Item().PaddingTop(4).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f));
                        t.Span("Gelöscht: ").FontColor(Colors.Grey.Darken1);
                        t.Span("durchgestrichen").FontColor(Deleted).Strikethrough();
                        t.Span("   Neu: ").FontColor(Colors.Grey.Darken1);
                        t.Span("unterstrichen").FontColor(Inserted).Underline();
                    });
                }
            });

            page.Content().Column(column =>
            {
                column.Spacing(5);
                if (layout == PdfLayout.SideBySide)
                {
                    foreach (var p in diff)
                    {
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().PaddingRight(6).Element(c => Side(c, p, oldSide: true));
                            row.RelativeItem().PaddingLeft(6).Element(c => Side(c, p, oldSide: false));
                        });
                    }
                }
                else
                {
                    foreach (var p in diff)
                    {
                        if (layout == PdfLayout.Clean && p.Change == ParagraphChange.Deleted)
                        {
                            continue;
                        }

                        column.Item().Element(c => Paragraph(c, p, layout));
                    }
                }
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(header.Footer ?? string.Empty).FontSize(8).FontColor(Colors.Grey.Darken1);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" von ");
                    t.TotalPages();
                });
            });
        }));

        return document.GeneratePdf();
    }

    /// <summary>The clean text of a list of paragraphs: no comparison needed.</summary>
    public static byte[] RenderClean(PdfHeader header, IReadOnlyList<string> paragraphs)
        => Render(header, [.. paragraphs.Select((p, i) => new ParagraphDiff { Change = ParagraphChange.Unchanged, NewIndex = i, OldIndex = i, OldText = p, NewText = p, Segments = [new DiffSegment(EditKind.Equal, p)] })], PdfLayout.Clean);

    private static string Printable(string text) => text.Replace(TextMetrics.LineBreak, '\n');

    private static void Paragraph(IContainer container, ParagraphDiff p, PdfLayout layout)
    {
        if (p.Segments.Count == 0 || p.Segments.All(s => s.Text.Length == 0))
        {
            container.Height(8);
            return;
        }

        container.Text(t =>
        {
            foreach (var s in p.Segments)
            {
                if (layout == PdfLayout.Clean)
                {
                    if (s.Kind != EditKind.Delete)
                    {
                        t.Span(Printable(s.Text));
                    }

                    continue;
                }

                switch (s.Kind)
                {
                    case EditKind.Delete:
                        t.Span(Printable(s.Text)).FontColor(Deleted).Strikethrough();
                        break;
                    case EditKind.Insert:
                        t.Span(Printable(s.Text)).FontColor(Inserted).Underline();
                        break;
                    default:
                        t.Span(Printable(s.Text));
                        break;
                }
            }
        });
    }

    private static void Side(IContainer container, ParagraphDiff p, bool oldSide)
    {
        var exists = oldSide ? p.Change != ParagraphChange.Inserted : p.Change != ParagraphChange.Deleted;
        if (!exists)
        {
            container.Height(1);
            return;
        }

        container.Text(t =>
        {
            foreach (var s in p.Segments)
            {
                if (oldSide && s.Kind == EditKind.Insert || !oldSide && s.Kind == EditKind.Delete)
                {
                    continue;
                }

                var span = t.Span(Printable(s.Text));
                if (s.Kind == EditKind.Delete)
                {
                    span.FontColor(Deleted).Strikethrough();
                }
                else if (s.Kind == EditKind.Insert)
                {
                    span.FontColor(Inserted).Underline();
                }
            }
        });
    }
}
