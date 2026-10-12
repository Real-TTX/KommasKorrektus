using KommasKorrektus.Configuration;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Writing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Storage;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

public enum ExportFormat
{
    /// <summary>Word with the changes as tracked changes that can be accepted in Word.</summary>
    DocxTracked,
    /// <summary>Word with the new text, no changes shown.</summary>
    DocxClean,
    /// <summary>PDF proof sheet: deleted words struck through, new words underlined.</summary>
    PdfChanges,
    PdfClean,
    PdfSideBySide,
    Text,
}

public sealed record ExportResult(byte[] Bytes, string FileName, string ContentType);

public sealed record ImportResult(Document Document, IReadOnlyList<string> Notes);

public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Documents and their versions. Everything the editor does to a text goes through here: the import of a file (Word, PDF, text), saving (every save is a
/// version; an autosave of the same person updates the autosave before it instead of piling up), putting an old version back, and the exports.
/// </summary>
public sealed class DocumentService(KkDbContext db, FileStoreService files, ConfigStore config, ICurrentUser current, TimeProvider clock)
{
    public const long MaxUploadBytes = 150L * 1024 * 1024;

    public static DocumentKind DetectKind(string fileName, byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            return DocumentKind.Docx;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            return DocumentKind.Pdf;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".txt" or ".md" or ".text" ? DocumentKind.Text : throw new ImportException("Dieses Dateiformat kann ich nicht lesen. Bitte lade eine Word-Datei (.docx), ein PDF oder eine Textdatei (.txt) hoch.");
    }

    /// <summary>Reads a file and makes a document with its first version (the text as the author delivered it). Saved by the caller's SaveChanges.</summary>
    public async Task<ImportResult> ImportAsync(Project project, string title, string fileName, byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length == 0)
        {
            throw new ImportException("Die Datei ist leer.");
        }

        var kind = DetectKind(fileName, bytes);
        IReadOnlyList<string> paragraphs;
        var notes = new List<string>();
        try
        {
            switch (kind)
            {
                case DocumentKind.Docx:
                    paragraphs = DocxReader.ReadParagraphs(bytes);
                    if (!Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add("Die Datei ist ein Word-Dokument, hat aber eine andere Endung.");
                    }

                    break;
                case DocumentKind.Pdf:
                    var pdf = PdfTextReader.Read(bytes);
                    paragraphs = pdf.Paragraphs;
                    notes.AddRange(pdf.Notes);
                    break;
                default:
                    paragraphs = PlainTextReader.Read(DecodeText(bytes)).Paragraphs;
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or IOException or InvalidOperationException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            throw new ImportException("Die Datei lässt sich nicht lesen. Ist sie beschädigt oder mit einem Kennwort geschützt?");
        }

        if (paragraphs.Count == 0 || paragraphs.All(string.IsNullOrWhiteSpace))
        {
            throw new ImportException(notes.FirstOrDefault(n => n.Contains("keinen Text")) ?? "In der Datei steht kein Text.");
        }

        var contentType = kind switch
        {
            DocumentKind.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            DocumentKind.Pdf => "application/pdf",
            _ => "text/plain; charset=utf-8",
        };

        var original = await files.SaveAsync(bytes, fileName, contentType, FileKind.Original, projectId: project.Id, customerId: project.CustomerId, ct: ct);
        var document = new Document
        {
            Project = project,
            ProjectId = project.Id,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
            FileName = FileStoreService.SafeFileName(fileName),
            Kind = kind,
            OriginalFile = original,
            CurrentVersionNo = 1,
            ImportNotes = notes.Count == 0 ? null : string.Join(' ', notes),
        };
        document.Versions.Add(NewVersion(1, paragraphs, VersionOrigin.Import, "Original", null, DiffStats.None));
        db.Document.Add(document);
        return new ImportResult(document, notes);
    }

    private static string DecodeText(byte[] bytes)
    {
        // UTF-8 with or without a mark; a file that is not valid UTF-8 is taken as Windows-1252 (an old text file from a German word processor).
        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private DocumentVersion NewVersion(int no, IReadOnlyList<string> paragraphs, VersionOrigin origin, string? label, string? comment, DiffStats stats) => new()
    {
        VersionNo = no,
        Origin = origin,
        Label = label,
        Comment = comment,
        Text = TextMetrics.JoinParagraphs(paragraphs),
        ParagraphCount = paragraphs.Count,
        WordCount = TextMetrics.CountWords(paragraphs),
        CharCount = TextMetrics.CountCharacters(paragraphs),
        WordsAdded = stats.WordsAdded,
        WordsRemoved = stats.WordsRemoved,
        WordsChanged = stats.WordsChanged,
    };

    public Task<DocumentVersion?> GetVersionAsync(long documentId, int versionNo, CancellationToken ct = default)
        => db.DocumentVersion.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNo == versionNo, ct);

    public static string[] ParagraphsOf(DocumentVersion version) => TextMetrics.Paragraphs(version.Text);

    /// <summary>What a save did.</summary>
    public sealed record SaveResult(DocumentVersion Version, bool Created, DiffStats Delta);

    /// <summary>
    /// Saves a text as a version. No change, no new version. An autosave after an autosave of the same person updates that one; an explicit save
    /// after such an autosave turns it into the saved version (so one working session leaves one version, not twenty).
    /// </summary>
    public async Task<SaveResult> SaveAsync(Document document, string text, VersionOrigin origin, string? label, string? comment, CancellationToken ct = default)
    {
        var latest = await db.DocumentVersion.FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == document.CurrentVersionNo, ct);
        var paragraphs = TextMetrics.Paragraphs(text);
        var oldParagraphs = TextMetrics.Paragraphs(latest.Text);

        var delta = DocumentDiff.Stats(DocumentDiff.Compare(oldParagraphs, paragraphs));
        if (!delta.HasChanges)
        {
            if (origin == VersionOrigin.Manual && (latest.Origin == VersionOrigin.Autosave || !string.IsNullOrWhiteSpace(label)))
            {
                // Nothing changed since, but the editor says "this is a version": the autosave becomes one.
                latest.Origin = VersionOrigin.Manual;
                latest.Label = string.IsNullOrWhiteSpace(label) ? latest.Label : label.Trim();
                latest.Comment = string.IsNullOrWhiteSpace(comment) ? latest.Comment : comment.Trim();
                await db.SaveChangesAsync(ct);
            }

            return new SaveResult(latest, false, DiffStats.None);
        }

        var sameAuthorAutosave = latest.Origin == VersionOrigin.Autosave && latest.CreateUserId == current.UserId && latest.VersionNo > 1;
        if (sameAuthorAutosave)
        {
            // The statistics of the version are against the version before it, not against itself.
            var before = await db.DocumentVersion.AsNoTracking().FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == latest.VersionNo - 1, ct);
            var against = DocumentDiff.Stats(DocumentDiff.Compare(TextMetrics.Paragraphs(before.Text), paragraphs));
            latest.Text = TextMetrics.JoinParagraphs(paragraphs);
            latest.ParagraphCount = paragraphs.Length;
            latest.WordCount = TextMetrics.CountWords(paragraphs);
            latest.CharCount = TextMetrics.CountCharacters(paragraphs);
            latest.WordsAdded = against.WordsAdded;
            latest.WordsRemoved = against.WordsRemoved;
            latest.WordsChanged = against.WordsChanged;
            if (origin != VersionOrigin.Autosave)
            {
                latest.Origin = origin;
                latest.Label = string.IsNullOrWhiteSpace(label) ? latest.Label : label.Trim();
                latest.Comment = string.IsNullOrWhiteSpace(comment) ? latest.Comment : comment.Trim();
            }

            await db.SaveChangesAsync(ct);
            return new SaveResult(latest, false, delta);
        }

        var version = NewVersion(latest.VersionNo + 1, paragraphs, origin, string.IsNullOrWhiteSpace(label) ? null : label.Trim(), string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), delta);
        version.DocumentId = document.Id;
        db.DocumentVersion.Add(version);
        document.CurrentVersionNo = version.VersionNo;
        await db.SaveChangesAsync(ct);
        return new SaveResult(version, true, delta);
    }

    /// <summary>Puts an old version back as a new one: nothing is lost, the versions in between stay.</summary>
    public async Task<SaveResult> RestoreAsync(Document document, int versionNo, CancellationToken ct = default)
    {
        var old = await GetVersionAsync(document.Id, versionNo, ct) ?? throw new InvalidOperationException("Diese Fassung gibt es nicht.");
        var latest = await db.DocumentVersion.AsNoTracking().FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == document.CurrentVersionNo, ct);
        var delta = DocumentDiff.Stats(DocumentDiff.Compare(TextMetrics.Paragraphs(latest.Text), TextMetrics.Paragraphs(old.Text)));
        var version = NewVersion(latest.VersionNo + 1, TextMetrics.Paragraphs(old.Text), VersionOrigin.Restore, $"Zurück auf Fassung {versionNo}", null, delta);
        version.DocumentId = document.Id;
        db.DocumentVersion.Add(version);
        document.CurrentVersionNo = version.VersionNo;
        await db.SaveChangesAsync(ct);
        return new SaveResult(version, true, delta);
    }

    public async Task<(IReadOnlyList<ParagraphDiff> Diff, DiffStats Stats)> CompareAsync(Document document, int fromNo, int toNo, CancellationToken ct = default)
    {
        var from = await GetVersionAsync(document.Id, fromNo, ct) ?? throw new InvalidOperationException("Die Ausgangsfassung gibt es nicht.");
        var to = fromNo == toNo ? from : await GetVersionAsync(document.Id, toNo, ct) ?? throw new InvalidOperationException("Die Zielfassung gibt es nicht.");
        var diff = DocumentDiff.Compare(ParagraphsOf(from), ParagraphsOf(to));
        return (diff, DocumentDiff.Stats(diff));
    }

    public async Task<ExportResult> ExportAsync(Document document, int fromNo, int toNo, ExportFormat format, string author, CancellationToken ct = default)
    {
        var from = await GetVersionAsync(document.Id, fromNo, ct) ?? throw new InvalidOperationException("Die Ausgangsfassung gibt es nicht.");
        var to = fromNo == toNo ? from : await GetVersionAsync(document.Id, toNo, ct) ?? throw new InvalidOperationException("Die Zielfassung gibt es nicht.");
        var oldParagraphs = ParagraphsOf(from);
        var newParagraphs = ParagraphsOf(to);
        var stem = Path.GetFileNameWithoutExtension(document.FileName);
        var date = clock.GetUtcNow().UtcDateTime;
        var customer = await db.Project.AsNoTracking().Where(p => p.Id == document.ProjectId).Select(p => p.Customer!.Company ?? p.Customer.LastName).FirstOrDefaultAsync(ct);
        var header = new PdfHeader(document.Title, $"{customer} · Fassung {fromNo} → {toNo} · {Fmt.Date(date)}".TrimStart(' ', '·'), config.Current.Company.Name);

        switch (format)
        {
            case ExportFormat.DocxTracked:
            case ExportFormat.DocxClean:
                var baseDocx = await BaseDocxAsync(document, fromNo, oldParagraphs, ct);
                var mode = format == ExportFormat.DocxTracked ? RevisionMode.Tracked : RevisionMode.Accepted;
                var docx = DocxWriter.Write(baseDocx, oldParagraphs, newParagraphs, mode, author, date);
                var suffix = format == ExportFormat.DocxTracked ? "mit Änderungen" : "bereinigt";
                return new ExportResult(docx, $"{stem} ({suffix}).docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            case ExportFormat.PdfChanges:
                return new ExportResult(DocumentPdf.Render(header, DocumentDiff.Compare(oldParagraphs, newParagraphs), PdfLayout.Changes), $"{stem} (Korrekturfahne).pdf", "application/pdf");

            case ExportFormat.PdfSideBySide:
                return new ExportResult(DocumentPdf.Render(header, DocumentDiff.Compare(oldParagraphs, newParagraphs), PdfLayout.SideBySide), $"{stem} (alt und neu).pdf", "application/pdf");

            case ExportFormat.PdfClean:
                return new ExportResult(DocumentPdf.RenderClean(header with { Subtitle = $"{customer} · Fassung {toNo} · {Fmt.Date(date)}".TrimStart(' ', '·') }, newParagraphs), $"{stem} (bereinigt).pdf", "application/pdf");

            default:
                return new ExportResult(System.Text.Encoding.UTF8.GetBytes(string.Join("\n\n", newParagraphs.Where(p => p.Length > 0)).Replace(TextMetrics.LineBreak, '\n')), $"{stem}.txt", "text/plain; charset=utf-8");
        }
    }

    /// <summary>The Word file as it was uploaded; null when the document did not come as Word or the file cannot be read (the page and the export then make a plain one).</summary>
    public async Task<byte[]?> OriginalDocxAsync(Document document, CancellationToken ct = default)
    {
        if (document.Kind != DocumentKind.Docx || document.OriginalFileId is null)
        {
            return null;
        }

        var stored = await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == document.OriginalFileId, ct);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return await files.ReadAllAsync(stored, ct);
        }
        catch (StorageException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Word file the export starts from: the uploaded original, brought to the starting version. A document that was not uploaded as Word has no
    /// original to keep the formatting of, so the export makes a plain one (the caller passes null).
    /// </summary>
    private async Task<byte[]?> BaseDocxAsync(Document document, int fromNo, IReadOnlyList<string> fromParagraphs, CancellationToken ct)
    {
        var original = await OriginalDocxAsync(document, ct);
        if (original is null)
        {
            return null;
        }

        if (fromNo == 1)
        {
            return original;
        }

        // The original has the text of version 1; the starting version is that with the changes accepted, formatting kept.
        var first = await GetVersionAsync(document.Id, 1, ct);
        return first is null ? null : DocxWriter.Write(original, ParagraphsOf(first), fromParagraphs, RevisionMode.Accepted, "KommasKorrektus", clock.GetUtcNow().UtcDateTime);
    }
}
